using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SonnetDB.Catalog;
using SonnetDB.Contracts;
using SonnetDB.Engine;
using SonnetDB.Exceptions;
using SonnetDB.Json;
using SonnetDB.Mcp;
using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Copilot;

/// <summary>
/// PR #67 / #68：Copilot 问答编排器。
/// </summary>
internal sealed class CopilotAgent
{
    private const int DefaultDocsK = 5;
    private const int MaxDocsK = 10;
    private const int DefaultSkillsK = 3;
    private const int MaxSkillsK = 8;
    private const int MaxLoadedSkills = 3;
    private const int MaxPlannedTools = 3;
    private const int HistoryTokenBudget = 1200;
    private const int MaxSqlRepairAttempts = 3;

    private readonly DocsSearchService _docsSearchService;
    private readonly SkillSearchService _skillSearchService;
    private readonly SkillRegistry _skillRegistry;
    private readonly IChatProvider _chatProvider;
    private readonly SonnetDbMcpSchemaCache _schemaCache;
    private readonly SonnetDbMcpExplainSqlService _explainSqlService;
    private readonly ILogger<CopilotAgent> _logger;

    public CopilotAgent(
        DocsSearchService docsSearchService,
        SkillSearchService skillSearchService,
        SkillRegistry skillRegistry,
        IChatProvider chatProvider,
        SonnetDbMcpSchemaCache schemaCache,
        SonnetDbMcpExplainSqlService explainSqlService,
        ILogger<CopilotAgent> logger)
    {
        _docsSearchService = docsSearchService;
        _skillSearchService = skillSearchService;
        _skillRegistry = skillRegistry;
        _chatProvider = chatProvider;
        _schemaCache = schemaCache;
        _explainSqlService = explainSqlService;
        _logger = logger;
    }

    public async IAsyncEnumerable<CopilotChatEvent> RunAsync(
        CopilotAgentContext context,
        IReadOnlyList<AiMessage> messages,
        int? docsK = null,
        int? skillsK = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(messages);

        var conversation = PrepareConversation(messages);
        var effectiveDocsK = NormalizeLimit(docsK, DefaultDocsK, MaxDocsK);
        var effectiveSkillsK = NormalizeLimit(skillsK, DefaultSkillsK, MaxSkillsK);

        yield return new CopilotChatEvent(
            Type: "start",
            Message: conversation.WasTrimmed
                ? $"开始处理数据库 '{context.DatabaseName}' 上的问题，历史消息已按 token 预算裁剪为 {conversation.Messages.Count} 条。"
                : $"开始处理数据库 '{context.DatabaseName}' 上的问题。");

        var docs = effectiveDocsK > 0
            ? await _docsSearchService.SearchAsync(conversation.RetrievalQuery, effectiveDocsK, cancellationToken).ConfigureAwait(false)
            : [];

        var skillHits = effectiveSkillsK > 0
            ? await _skillSearchService.SearchAsync(conversation.RetrievalQuery, effectiveSkillsK, cancellationToken).ConfigureAwait(false)
            : [];

        var loadedSkills = LoadTopSkills(skillHits);
        var nextCitationNumber = 1;
        var retrievalCitations = BuildRetrievalCitations(docs, loadedSkills, ref nextCitationNumber);
        var suggestedToolNames = loadedSkills
            .SelectMany(static skill => skill.RequiresTools)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        yield return new CopilotChatEvent(
            Type: "retrieval",
            Message: $"已召回 {loadedSkills.Count} 个技能、{docs.Count} 条文档。",
            SkillNames: loadedSkills.Count > 0 ? loadedSkills.Select(static skill => skill.Name).ToArray() : null,
            ToolNames: suggestedToolNames.Length > 0 ? suggestedToolNames : null,
            Citations: retrievalCitations.Count > 0 ? retrievalCitations : null);

        var plan = await PlanToolsAsync(context, conversation, docs, loadedSkills, cancellationToken).ConfigureAwait(false);
        var observations = new List<CopilotToolObservation>(plan.Count);

        foreach (var tool in plan)
        {
            var toolArguments = FormatToolArguments(tool);
            yield return new CopilotChatEvent(
                Type: "tool_call",
                Message: $"执行工具 {tool.Name}。",
                ToolName: tool.Name,
                ToolArguments: toolArguments);

            var execution = await ExecuteToolAsync(
                context,
                conversation,
                docs,
                loadedSkills,
                tool,
                cancellationToken).ConfigureAwait(false);

            foreach (var evt in execution.Events)
                yield return evt;

            var finalToolArguments = FormatToolArguments(execution.Tool);
            var citation = BuildToolCitation(execution.Tool, execution.ResultJson, ref nextCitationNumber);
            var captured = new CopilotToolObservation(execution.Tool.Name, finalToolArguments, execution.ResultJson, citation);
            observations.Add(captured);

            yield return new CopilotChatEvent(
                Type: "tool_result",
                Message: $"工具 {execution.Tool.Name} 已返回结果。",
                ToolName: execution.Tool.Name,
                ToolArguments: finalToolArguments,
                ToolResult: execution.ResultJson,
                Citations: [citation]);
        }

        var allCitations = new List<CopilotCitation>(retrievalCitations.Count + observations.Count);
        allCitations.AddRange(retrievalCitations);
        allCitations.AddRange(observations.Select(static item => item.Citation));

        var answer = await GenerateAnswerAsync(
            context,
            conversation,
            docs,
            loadedSkills,
            observations,
            allCitations,
            cancellationToken).ConfigureAwait(false);

        yield return new CopilotChatEvent(
            Type: "final",
            Message: "已生成最终回答。",
            Answer: answer,
            Citations: allCitations.Count > 0 ? allCitations : null);

        yield return new CopilotChatEvent(
            Type: "done",
            Message: "completed");
    }

    private static CopilotConversation PrepareConversation(IReadOnlyList<AiMessage> messages)
    {
        var normalized = NormalizeMessages(messages);
        if (normalized.Count == 0)
            throw new ArgumentException("Copilot messages cannot be empty.", nameof(messages));

        var trimmed = TrimConversation(normalized);
        var latestUserIndex = FindLatestUserMessageIndex(trimmed);
        if (latestUserIndex < 0)
            throw new ArgumentException("Copilot messages must contain at least one user message.", nameof(messages));

        var activeMessages = trimmed.Take(latestUserIndex + 1).ToArray();
        var history = latestUserIndex == 0
            ? []
            : activeMessages[..latestUserIndex];
        var latestUserMessage = activeMessages[latestUserIndex].Content;

        return new CopilotConversation(
            Messages: activeMessages,
            History: history,
            LatestUserMessage: latestUserMessage,
            RetrievalQuery: BuildRetrievalQuery(activeMessages),
            WasTrimmed: trimmed.Count != normalized.Count || activeMessages.Length != normalized.Count);
    }

    private IReadOnlyList<SkillLoadResult> LoadTopSkills(IReadOnlyList<SkillSearchHit> skillHits)
    {
        if (skillHits.Count == 0)
            return [];

        var loaded = new List<SkillLoadResult>(Math.Min(skillHits.Count, MaxLoadedSkills));
        foreach (var hit in skillHits.Take(MaxLoadedSkills))
        {
            var full = _skillRegistry.Load(hit.Name);
            if (full is not null)
                loaded.Add(full);
        }

        return loaded;
    }

    private async Task<IReadOnlyList<CopilotToolInvocation>> PlanToolsAsync(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        CancellationToken cancellationToken)
    {
        var measurements = _schemaCache.GetMeasurements(context.DatabaseName, context.Database);
        var plannerPrompt = BuildPlannerPrompt(context, conversation, docs, loadedSkills);

        try
        {
            var response = await _chatProvider.CompleteAsync(
                [
                    new AiMessage("system", PlannerSystemPrompt),
                    new AiMessage("user", plannerPrompt),
                ],
                context.ModelOverride,
                cancellationToken).ConfigureAwait(false);

            if (TryParsePlan(response, out var plan) && plan is not null)
            {
                var sanitized = SanitizePlan(plan.Tools, measurements, conversation.LatestUserMessage);
                return EnsureWriteDraftPlan(sanitized, conversation.LatestUserMessage);
            }

            _logger.LogWarning("Copilot planner returned non-JSON content: {Response}", response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot planner failed, falling back to heuristics.");
        }

        return EnsureWriteDraftPlan(BuildHeuristicPlan(conversation.LatestUserMessage, measurements), conversation.LatestUserMessage);
    }

    private async Task<string> GenerateAnswerAsync(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        IReadOnlyList<CopilotToolObservation> observations,
        IReadOnlyList<CopilotCitation> citations,
        CancellationToken cancellationToken)
    {
        var prompt = BuildAnswerPrompt(context, conversation, docs, loadedSkills, observations, citations);

        try
        {
            var answer = await _chatProvider.CompleteAsync(
                [
                    new AiMessage("system", AnswerSystemPrompt),
                    new AiMessage("user", prompt),
                ],
                context.ModelOverride,
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(answer))
                return answer.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot final answer generation failed, using deterministic fallback.");
        }

        return BuildFallbackAnswer(context, conversation, observations, citations);
    }

    private static bool TryParsePlan(string response, out CopilotToolPlan? plan)
    {
        plan = null;
        if (string.IsNullOrWhiteSpace(response))
            return false;

        if (TryDeserializePlan(response, out plan))
            return true;

        var json = ExtractJsonObject(response);
        return json is not null && TryDeserializePlan(json, out plan);
    }

    private static bool TryDeserializePlan(string json, out CopilotToolPlan? plan)
    {
        try
        {
            plan = JsonSerializer.Deserialize(json, ServerJsonContext.Default.CopilotToolPlan);
            return plan is not null;
        }
        catch (JsonException)
        {
            plan = null;
            return false;
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        return text[start..(end + 1)];
    }

    private static IReadOnlyList<CopilotToolInvocation> SanitizePlan(
        IReadOnlyList<CopilotPlannedTool>? plannedTools,
        IReadOnlyList<string> measurements,
        string userMessage)
    {
        if (plannedTools is null || plannedTools.Count == 0)
            return [];

        var tools = new List<CopilotToolInvocation>(Math.Min(plannedTools.Count, MaxPlannedTools));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var planned in plannedTools)
        {
            if (tools.Count >= MaxPlannedTools)
                break;
            if (string.IsNullOrWhiteSpace(planned.Name))
                continue;

            var normalizedName = planned.Name.Trim();
            CopilotToolInvocation? tool = normalizedName switch
            {
                "list_databases" => new CopilotToolInvocation(normalizedName, MaxRows: null, N: null, Measurement: null, Sql: null),
                "list_measurements" => new CopilotToolInvocation(
                    normalizedName,
                    MaxRows: NormalizeLimit(planned.MaxRows, SonnetDbMcpResults.DefaultToolRowLimit, SonnetDbMcpResults.MaxToolRowLimit),
                    N: null,
                    Measurement: null,
                    Sql: null),
                "describe_measurement" => TryResolveMeasurement(planned.Measurement, measurements, userMessage) is { } describeMeasurement
                    ? new CopilotToolInvocation(normalizedName, null, null, describeMeasurement, null)
                    : null,
                "sample_rows" => TryResolveMeasurement(planned.Measurement, measurements, userMessage) is { } sampleMeasurement
                    ? new CopilotToolInvocation(
                        normalizedName,
                        MaxRows: null,
                        N: NormalizeLimit(planned.N, SonnetDbMcpResults.DefaultSampleRowLimit, SonnetDbMcpResults.MaxSampleRowLimit),
                        Measurement: sampleMeasurement,
                        Sql: null)
                    : null,
                "explain_sql" when !string.IsNullOrWhiteSpace(planned.Sql)
                    => new CopilotToolInvocation(normalizedName, null, null, null, planned.Sql.Trim()),
                "query_sql" when !string.IsNullOrWhiteSpace(planned.Sql)
                    => new CopilotToolInvocation(
                        normalizedName,
                        NormalizeLimit(planned.MaxRows, SonnetDbMcpResults.DefaultToolRowLimit, SonnetDbMcpResults.MaxToolRowLimit),
                        null,
                        null,
                        planned.Sql.Trim()),
                "draft_sql" when !string.IsNullOrWhiteSpace(planned.Sql)
                    => new CopilotToolInvocation(normalizedName, null, null, null, planned.Sql.Trim()),
                "execute_sql" when !string.IsNullOrWhiteSpace(planned.Sql)
                    => new CopilotToolInvocation(
                        normalizedName,
                        NormalizeLimit(planned.MaxRows, SonnetDbMcpResults.DefaultToolRowLimit, SonnetDbMcpResults.MaxToolRowLimit),
                        null,
                        null,
                        planned.Sql.Trim()),
                _ => null,
            };

            if (tool is null)
                continue;

            var key = $"{tool.Name}|{tool.Measurement}|{tool.Sql}|{tool.MaxRows}|{tool.N}";
            if (seen.Add(key))
                tools.Add(tool);
        }

        return tools;
    }

    private static IReadOnlyList<CopilotToolInvocation> BuildHeuristicPlan(string message, IReadOnlyList<string> measurements)
    {
        var lowered = message.ToLowerInvariant();
        var tools = new List<CopilotToolInvocation>(2);
        var sql = TryExtractSql(message);
        var measurement = TryResolveMeasurement(null, measurements, message);

        if (!string.IsNullOrWhiteSpace(sql))
        {
            if (lowered.Contains("解释", StringComparison.Ordinal)
                || lowered.Contains("扫描", StringComparison.Ordinal)
                || lowered.Contains("explain", StringComparison.Ordinal))
            {
                tools.Add(new CopilotToolInvocation("explain_sql", null, null, null, sql));
                return tools;
            }

            if (LooksLikeWriteSql(sql))
            {
                tools.Add(new CopilotToolInvocation("draft_sql", null, null, null, sql));
                return tools;
            }

            tools.Add(new CopilotToolInvocation(
                "query_sql",
                SonnetDbMcpResults.DefaultToolRowLimit,
                null,
                null,
                sql));
            return tools;
        }

        if (LooksLikeWriteIntent(lowered))
        {
            // 当用户只描述需求（建表 / 插入数据）但没给 SQL 时，先把已有 measurement 列表喂给 LLM，
            // 让最终回答阶段据此生成可执行的 CREATE MEASUREMENT / INSERT 语句。
            tools.Add(new CopilotToolInvocation(
                "list_measurements",
                SonnetDbMcpResults.DefaultToolRowLimit,
                null,
                null,
                null));
            if (measurement is not null)
                tools.Add(new CopilotToolInvocation("describe_measurement", null, null, measurement, null));
            return tools;
        }

        if ((lowered.Contains("字段", StringComparison.Ordinal)
                || lowered.Contains("列", StringComparison.Ordinal)
                || lowered.Contains("schema", StringComparison.Ordinal)
                || lowered.Contains("结构", StringComparison.Ordinal))
            && measurement is not null)
        {
            tools.Add(new CopilotToolInvocation("describe_measurement", null, null, measurement, null));
            return tools;
        }

        if ((lowered.Contains("样例", StringComparison.Ordinal)
                || lowered.Contains("示例", StringComparison.Ordinal)
                || lowered.Contains("sample", StringComparison.Ordinal)
                || lowered.Contains("几行", StringComparison.Ordinal))
            && measurement is not null)
        {
            tools.Add(new CopilotToolInvocation(
                "sample_rows",
                null,
                SonnetDbMcpResults.DefaultSampleRowLimit,
                measurement,
                null));
            return tools;
        }

        if ((lowered.Contains("数据库", StringComparison.Ordinal) || lowered.Contains("db", StringComparison.Ordinal))
            && (lowered.Contains("哪些", StringComparison.Ordinal)
                || lowered.Contains("列表", StringComparison.Ordinal)
                || lowered.Contains("list", StringComparison.Ordinal)))
        {
            tools.Add(new CopilotToolInvocation("list_databases", null, null, null, null));
            return tools;
        }

        if (lowered.Contains("measurement", StringComparison.Ordinal)
            || lowered.Contains("表", StringComparison.Ordinal)
            || lowered.Contains("有哪些", StringComparison.Ordinal)
            || lowered.Contains("列表", StringComparison.Ordinal))
        {
            if (measurement is not null
                && (lowered.Contains("字段", StringComparison.Ordinal) || lowered.Contains("列", StringComparison.Ordinal)))
            {
                tools.Add(new CopilotToolInvocation("describe_measurement", null, null, measurement, null));
            }
            else
            {
                tools.Add(new CopilotToolInvocation(
                    "list_measurements",
                    SonnetDbMcpResults.DefaultToolRowLimit,
                    null,
                    null,
                    null));
            }
        }

        return tools.Count > 0
            ? tools
            : [new CopilotToolInvocation("list_measurements", SonnetDbMcpResults.DefaultToolRowLimit, null, null, null)];
    }

    private static IReadOnlyList<CopilotToolInvocation> EnsureWriteDraftPlan(
        IReadOnlyList<CopilotToolInvocation> plan,
        string userMessage)
    {
        if (!LooksLikeCreateMeasurementIntent(userMessage.ToLowerInvariant()))
            return plan;

        if (plan.Any(static tool =>
                string.Equals(tool.Name, "draft_sql", StringComparison.OrdinalIgnoreCase)
                || string.Equals(tool.Name, "execute_sql", StringComparison.OrdinalIgnoreCase)))
        {
            return plan;
        }

        var sql = TryBuildCreateMeasurementSql(userMessage);
        if (sql is null)
            return plan;

        var draft = new CopilotToolInvocation("draft_sql", MaxRows: null, N: null, Measurement: null, Sql: sql);
        if (plan.Count == 0)
            return [draft];

        var augmented = new List<CopilotToolInvocation>(Math.Min(MaxPlannedTools, plan.Count + 1));
        foreach (var tool in plan)
        {
            if (augmented.Count >= MaxPlannedTools - 1)
                break;
            augmented.Add(tool);
        }

        augmented.Add(draft);
        return augmented;
    }

    private async Task<CopilotToolExecutionResult> ExecuteToolAsync(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        CopilotToolInvocation tool,
        CancellationToken cancellationToken)
    {
        switch (tool.Name)
        {
            case "list_databases":
                return new CopilotToolExecutionResult(
                    tool,
                    SerializeToolResult(
                        new McpDatabaseListResult(context.DatabaseName, context.VisibleDatabases),
                        ServerJsonContext.Default.McpDatabaseListResult),
                    []);
            case "list_measurements":
                return new CopilotToolExecutionResult(tool, ExecuteListMeasurements(context, tool), []);
            case "describe_measurement":
                return new CopilotToolExecutionResult(tool, ExecuteDescribeMeasurement(context, tool), []);
            case "sample_rows":
                return new CopilotToolExecutionResult(tool, ExecuteSampleRows(context, tool), []);
            case "explain_sql":
                return new CopilotToolExecutionResult(tool, ExecuteExplainSql(context, tool), []);
            case "draft_sql":
                return new CopilotToolExecutionResult(tool, ExecuteDraftSql(context, tool), []);
            case "execute_sql":
                return new CopilotToolExecutionResult(tool, ExecuteExecuteSql(context, tool), []);
            case "query_sql":
                return await ExecuteQuerySqlWithRepairAsync(
                    context,
                    conversation,
                    docs,
                    loadedSkills,
                    tool,
                    cancellationToken).ConfigureAwait(false);
            default:
                throw new InvalidOperationException($"不支持的 Copilot 工具 '{tool.Name}'。");
        }
    }

    private string ExecuteListMeasurements(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var maxRows = tool.MaxRows ?? SonnetDbMcpResults.DefaultToolRowLimit;
        var measurements = _schemaCache.GetMeasurements(context.DatabaseName, context.Database);
        var names = new List<string>(Math.Min(measurements.Count, maxRows));
        for (var i = 0; i < measurements.Count && i < maxRows; i++)
            names.Add(measurements[i]);

        var payload = new McpMeasurementListResult(
            context.DatabaseName,
            names,
            Truncated: measurements.Count > maxRows);
        return SerializeToolResult(payload, ServerJsonContext.Default.McpMeasurementListResult);
    }

    private string ExecuteDescribeMeasurement(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var measurement = tool.Measurement
            ?? throw new InvalidOperationException("describe_measurement 缺少 measurement 参数。");
        var payload = _schemaCache.GetMeasurementSchema(context.DatabaseName, measurement, context.Database);
        return SerializeToolResult(payload, ServerJsonContext.Default.McpMeasurementSchemaResult);
    }

    private string ExecuteSampleRows(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var measurement = tool.Measurement
            ?? throw new InvalidOperationException("sample_rows 缺少 measurement 参数。");
        var rows = tool.N ?? SonnetDbMcpResults.DefaultSampleRowLimit;

        var statement = new SelectStatement(
            Projections: [new SelectItem(StarExpression.Instance, Alias: null)],
            Measurement: measurement,
            Where: null,
            GroupBy: [],
            TableValuedFunction: null,
            Pagination: new PaginationSpec(0, checked(rows + 1)));

        var executionResult = SqlExecutor.ExecuteStatement(context.Database, statement);
        if (executionResult is not SelectExecutionResult selectResult)
            throw new InvalidOperationException("sample_rows 未返回结果集。");

        var (resultRows, truncated) = SonnetDbMcpResults.SliceRows(selectResult, rows, canTruncate: true);
        var payload = new McpSampleRowsResult(
            Database: context.DatabaseName,
            Measurement: measurement,
            RequestedRows: rows,
            Columns: selectResult.Columns,
            Rows: resultRows,
            ReturnedRows: resultRows.Count,
            Truncated: truncated);

        return SerializeToolResult(payload, ServerJsonContext.Default.McpSampleRowsResult);
    }

    private string ExecuteExplainSql(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var sql = tool.Sql
            ?? throw new InvalidOperationException("explain_sql 缺少 sql 参数。");
        var statement = SqlParser.Parse(sql);
        if (!IsReadOnlyStatement(statement))
        {
            throw new InvalidOperationException(
                "explain_sql 仅支持 SELECT、SHOW MEASUREMENTS / SHOW TABLES 与 DESCRIBE [MEASUREMENT]。");
        }

        var payload = _explainSqlService.Explain(context.DatabaseName, context.Database, statement);
        return SerializeToolResult(payload, ServerJsonContext.Default.McpExplainSqlResult);
    }

    private string ExecuteDraftSql(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var sql = tool.Sql
            ?? throw new InvalidOperationException("draft_sql 缺少 sql 参数。");

        SqlStatement statement;
        try
        {
            statement = SqlParser.Parse(sql);
        }
        catch (SqlParseException ex)
        {
            throw new SqlExecutionException(sql, "parse", ex.Message, ex);
        }

        if (!IsDraftableStatement(statement))
        {
            throw new InvalidOperationException(
                "draft_sql 仅支持 CREATE MEASUREMENT、INSERT、DELETE、SELECT、SHOW MEASUREMENTS 与 DESCRIBE [MEASUREMENT]。");
        }

        var (statementType, measurement, isWrite) = DescribeStatement(statement);
        bool? exists = null;
        var notes = new List<string>(2);

        if (isWrite && measurement is not null)
        {
            var existing = context.Database.Measurements.TryGet(measurement);
            exists = existing is not null;
            switch (statement)
            {
                case CreateMeasurementStatement when exists is true:
                    notes.Add($"measurement '{measurement}' 已经存在；如需追加列，请改用 INSERT 而不是 CREATE。");
                    break;
                case CreateMeasurementStatement when exists is false:
                    notes.Add($"measurement '{measurement}' 当前不存在，可以执行该 CREATE 语句创建。");
                    break;
                case InsertStatement when exists is false:
                    notes.Add($"measurement '{measurement}' 尚未创建，执行 INSERT 之前需要先 CREATE MEASUREMENT。");
                    break;
                case DeleteStatement when exists is false:
                    notes.Add($"measurement '{measurement}' 不存在，DELETE 不会影响任何数据。");
                    break;
            }
        }

        if (isWrite)
        {
            notes.Add(context.CanWrite
                ? "当前凭据具备写权限，可以调用 execute_sql 直接执行。"
                : "当前凭据没有写权限。您可以：① 请管理员执行 GRANT WRITE ON DATABASE <db> TO <user> 为您授权（授权后在当前会话中即可生效）；② 将上方 SQL 复制后，切换到 SQL Console 选项卡，以具备写权限的账号粘贴执行。");
        }

        var payload = new McpDraftSqlResult(
            Database: context.DatabaseName,
            StatementType: statementType,
            Sql: sql.Trim(),
            Measurement: measurement,
            IsWrite: isWrite,
            MeasurementExists: exists,
            Notes: notes);
        return SerializeToolResult(payload, ServerJsonContext.Default.McpDraftSqlResult);
    }

    private string ExecuteExecuteSql(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var sql = tool.Sql
            ?? throw new InvalidOperationException("execute_sql 缺少 sql 参数。");

        SqlStatement statement;
        try
        {
            statement = SqlParser.Parse(sql);
        }
        catch (SqlParseException ex)
        {
            throw new SqlExecutionException(sql, "parse", ex.Message, ex);
        }

        if (!IsDraftableStatement(statement))
        {
            throw new SqlExecutionException(
                sql,
                "validate",
                "execute_sql 仅支持 CREATE MEASUREMENT、INSERT、DELETE、SELECT、SHOW MEASUREMENTS 与 DESCRIBE [MEASUREMENT]。");
        }

        var (statementType, measurement, isWrite) = DescribeStatement(statement);
        if (isWrite && !context.CanWrite)
        {
            throw new SqlExecutionException(
                sql,
                "permission",
                $"当前凭据对数据库 '{context.DatabaseName}' 没有写权限，无法执行 {statementType.ToUpperInvariant()} 语句。");
        }

        var maxRows = tool.MaxRows ?? SonnetDbMcpResults.DefaultToolRowLimit;
        SqlStatement executable = statement;
        var canTruncate = false;
        if (statement is SelectStatement selectStatement)
            executable = SonnetDbMcpResults.ApplyToolRowLimit(selectStatement, maxRows, out canTruncate);

        object? executionResult;
        try
        {
            executionResult = SqlExecutor.ExecuteStatement(context.Database, executable);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new SqlExecutionException(sql, "execute", ex.Message, ex);
        }

        IReadOnlyList<string>? columns = null;
        IReadOnlyList<IReadOnlyList<JsonElementValue>>? rows = null;
        int? returnedRows = null;
        int? rowsAffected = null;
        var truncated = false;

        switch (executionResult)
        {
            case SelectExecutionResult selectResult:
                var (rowList, isTruncated) = SonnetDbMcpResults.SliceRows(selectResult, maxRows, canTruncate);
                columns = selectResult.Columns;
                rows = rowList;
                returnedRows = rowList.Count;
                truncated = isTruncated;
                break;
            case InsertExecutionResult insertResult:
                rowsAffected = insertResult.RowsInserted;
                break;
            case DeleteExecutionResult deleteResult:
                rowsAffected = deleteResult.TombstonesAdded;
                break;
            case MeasurementSchema schema:
                rowsAffected = schema.Columns.Count;
                break;
        }

        var payload = new McpExecuteSqlResult(
            Database: context.DatabaseName,
            StatementType: statementType,
            Sql: sql.Trim(),
            Measurement: measurement,
            RowsAffected: rowsAffected,
            Columns: columns,
            Rows: rows,
            ReturnedRows: returnedRows,
            Truncated: truncated);
        return SerializeToolResult(payload, ServerJsonContext.Default.McpExecuteSqlResult);
    }

    private static (string StatementType, string? Measurement, bool IsWrite) DescribeStatement(SqlStatement statement)
        => statement switch
        {
            CreateMeasurementStatement create => ("create_measurement", create.Name, true),
            InsertStatement insert => ("insert", insert.Measurement, true),
            DeleteStatement delete => ("delete", delete.Measurement, true),
            SelectStatement select => ("select", select.Measurement, false),
            ShowMeasurementsStatement => ("show_measurements", null, false),
            DescribeMeasurementStatement describe => ("describe_measurement", describe.Name, false),
            _ => ("unknown", null, false),
        };

    private static bool IsDraftableStatement(SqlStatement statement)
        => statement is CreateMeasurementStatement
            or InsertStatement
            or DeleteStatement
            or SelectStatement
            or ShowMeasurementsStatement
            or DescribeMeasurementStatement;

    private async Task<CopilotToolExecutionResult> ExecuteQuerySqlWithRepairAsync(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        CopilotToolInvocation tool,
        CancellationToken cancellationToken)
    {
        var currentTool = tool;
        var events = new List<CopilotChatEvent>();

        for (var attempt = 1; attempt <= MaxSqlRepairAttempts; attempt++)
        {
            try
            {
                return new CopilotToolExecutionResult(currentTool, TryExecuteQuerySql(context, currentTool), events);
            }
            catch (SqlExecutionException ex) when (attempt < MaxSqlRepairAttempts)
            {
                var rewrittenSql = await RepairSqlAsync(
                    context,
                    conversation,
                    docs,
                    loadedSkills,
                    currentTool,
                    ex,
                    cancellationToken).ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(rewrittenSql)
                    || string.Equals(
                        CollapseWhitespace(rewrittenSql),
                        CollapseWhitespace(currentTool.Sql ?? string.Empty),
                        StringComparison.OrdinalIgnoreCase))
                {
                    var errorPayload = BuildSqlErrorPayload(ex, attempt, final: true);
                    events.Add(new CopilotChatEvent(
                        Type: "tool_retry",
                        Message: $"query_sql 第 {attempt} 次失败后未得到可用的改写 SQL。",
                        ToolName: currentTool.Name,
                        ToolArguments: FormatToolArguments(currentTool),
                        ToolResult: errorPayload,
                        Attempt: attempt));
                    return new CopilotToolExecutionResult(currentTool, errorPayload, events);
                }

                currentTool = currentTool with { Sql = rewrittenSql };
                events.Add(new CopilotChatEvent(
                    Type: "tool_retry",
                    Message: $"query_sql 第 {attempt} 次执行失败，已依据错误信息改写 SQL 并重试。",
                    ToolName: currentTool.Name,
                    ToolArguments: FormatToolArguments(currentTool),
                    ToolResult: BuildSqlErrorPayload(ex, attempt, final: false),
                    Attempt: attempt));
            }
            catch (SqlExecutionException ex)
            {
                return new CopilotToolExecutionResult(currentTool, BuildSqlErrorPayload(ex, attempt, final: true), events);
            }
        }

        throw new InvalidOperationException("query_sql 修复循环意外结束。");
    }

    private async Task<string?> RepairSqlAsync(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        CopilotToolInvocation tool,
        SqlExecutionException exception,
        CancellationToken cancellationToken)
    {
        try
        {
            var prompt = BuildSqlRepairPrompt(context, conversation, docs, loadedSkills, tool, exception);
            var response = await _chatProvider.CompleteAsync(
                [
                    new AiMessage("system", SqlRepairSystemPrompt),
                    new AiMessage("user", prompt),
                ],
                context.ModelOverride,
                cancellationToken).ConfigureAwait(false);
            return TryExtractSql(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot SQL repair failed for sql={Sql}.", tool.Sql);
            return null;
        }
    }

    private static string TryExecuteQuerySql(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var sql = tool.Sql
            ?? throw new InvalidOperationException("query_sql 缺少 sql 参数。");
        var maxRows = tool.MaxRows ?? SonnetDbMcpResults.DefaultToolRowLimit;

        SqlStatement statement;
        try
        {
            statement = SqlParser.Parse(sql);
        }
        catch (SqlParseException ex)
        {
            throw new SqlExecutionException(sql, "parse", ex.Message, ex);
        }

        if (!IsReadOnlyStatement(statement))
        {
            throw new SqlExecutionException(
                sql,
                "validate",
                "query_sql 仅支持 SELECT、SHOW MEASUREMENTS / SHOW TABLES 与 DESCRIBE [MEASUREMENT]。");
        }

        SqlStatement executable = statement;
        var canTruncate = false;
        if (statement is SelectStatement select)
            executable = SonnetDbMcpResults.ApplyToolRowLimit(select, maxRows, out canTruncate);

        object? executionResult;
        try
        {
            executionResult = SqlExecutor.ExecuteStatement(context.Database, executable);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or ArgumentException)
        {
            throw new SqlExecutionException(sql, "execute", ex.Message, ex);
        }

        if (executionResult is not SelectExecutionResult selectResult)
            throw new SqlExecutionException(sql, "execute", "只读 SQL 未返回结果集。");

        var (rows, truncated) = SonnetDbMcpResults.SliceRows(selectResult, maxRows, canTruncate);
        var payload = new McpSqlQueryResult(
            context.DatabaseName,
            StatementType: GetStatementType(statement),
            Columns: selectResult.Columns,
            Rows: rows,
            ReturnedRows: rows.Count,
            Truncated: truncated);

        return SerializeToolResult(payload, ServerJsonContext.Default.McpSqlQueryResult);
    }

    private static string BuildPlannerPrompt(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前数据库：{context.DatabaseName}");
        builder.AppendLine($"当前可见数据库：{string.Join(", ", context.VisibleDatabases)}");
        builder.AppendLine();
        AppendConversationHistory(builder, conversation.History);
        builder.AppendLine("当前用户问题：");
        builder.AppendLine(conversation.LatestUserMessage);
        builder.AppendLine();

        if (loadedSkills.Count > 0)
        {
            builder.AppendLine("已召回技能：");
            foreach (var skill in loadedSkills)
            {
                builder.Append("- ");
                builder.Append(skill.Name);
                if (!string.IsNullOrWhiteSpace(skill.Description))
                {
                    builder.Append("：");
                    builder.Append(skill.Description);
                }
                if (skill.RequiresTools.Count > 0)
                {
                    builder.Append("；建议工具=");
                    builder.Append(string.Join(", ", skill.RequiresTools));
                }
                builder.AppendLine();
            }
            builder.AppendLine();
        }

        if (docs.Count > 0)
        {
            builder.AppendLine("已召回文档摘要：");
            foreach (var doc in docs.Take(3))
            {
                builder.Append("- ");
                builder.Append(string.IsNullOrWhiteSpace(doc.Title) ? doc.Source : doc.Title);
                builder.Append("：");
                builder.AppendLine(Truncate(CollapseWhitespace(doc.Content), 240));
            }
        }

        return builder.ToString().Trim();
    }

    private static string BuildAnswerPrompt(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        IReadOnlyList<CopilotToolObservation> observations,
        IReadOnlyList<CopilotCitation> citations)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前数据库：{context.DatabaseName}");
        builder.AppendLine($"当前可见数据库：{string.Join(", ", context.VisibleDatabases)}");
        builder.AppendLine();
        AppendConversationHistory(builder, conversation.History);
        builder.AppendLine("当前用户问题：");
        builder.AppendLine(conversation.LatestUserMessage);
        builder.AppendLine();

        if (loadedSkills.Count > 0)
        {
            builder.AppendLine("已加载技能：");
            foreach (var skill in loadedSkills)
            {
                builder.Append("- ");
                builder.Append(skill.Name);
                builder.Append("：");
                builder.AppendLine(Truncate(CollapseWhitespace($"{skill.Description} {skill.Body}"), 600));
            }
            builder.AppendLine();
        }

        if (docs.Count > 0)
        {
            builder.AppendLine("文档上下文：");
            foreach (var doc in docs)
            {
                builder.Append("- ");
                builder.Append(doc.Source);
                builder.Append(" / ");
                builder.Append(string.IsNullOrWhiteSpace(doc.Section) ? doc.Title : doc.Section);
                builder.Append("：");
                builder.AppendLine(Truncate(CollapseWhitespace(doc.Content), 400));
            }
            builder.AppendLine();
        }

        if (observations.Count > 0)
        {
            builder.AppendLine("工具结果：");
            foreach (var observation in observations)
            {
                builder.Append("- tool=");
                builder.Append(observation.Name);
                builder.Append(" args=");
                builder.Append(observation.ArgumentsJson);
                builder.Append(" result=");
                builder.AppendLine(observation.ResultJson);
            }
            builder.AppendLine();
        }

        if (citations.Count > 0)
        {
            builder.AppendLine("可用 citations：");
            foreach (var citation in citations)
            {
                builder.Append('[');
                builder.Append(citation.Id);
                builder.Append("] kind=");
                builder.Append(citation.Kind);
                builder.Append("; title=");
                builder.Append(citation.Title);
                builder.Append("; source=");
                builder.Append(citation.Source);
                builder.Append("; snippet=");
                builder.AppendLine(citation.Snippet);
            }
        }

        return builder.ToString().Trim();
    }

    private string BuildSqlRepairPrompt(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        CopilotToolInvocation tool,
        SqlExecutionException exception)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前数据库：{context.DatabaseName}");
        builder.AppendLine($"当前可见数据库：{string.Join(", ", context.VisibleDatabases)}");
        builder.AppendLine($"已知 measurements：{string.Join(", ", _schemaCache.GetMeasurements(context.DatabaseName, context.Database))}");
        builder.AppendLine();
        AppendConversationHistory(builder, conversation.History);
        builder.AppendLine("当前用户问题：");
        builder.AppendLine(conversation.LatestUserMessage);
        builder.AppendLine();
        builder.AppendLine("失败 SQL：");
        builder.AppendLine(tool.Sql);
        builder.AppendLine();
        builder.AppendLine($"失败阶段：{exception.Phase}");
        builder.AppendLine("错误消息：");
        builder.AppendLine(exception.Message);
        builder.AppendLine();

        if (loadedSkills.Count > 0)
        {
            builder.AppendLine("已加载技能摘要：");
            foreach (var skill in loadedSkills)
            {
                builder.Append("- ");
                builder.Append(skill.Name);
                builder.Append("：");
                builder.AppendLine(Truncate(CollapseWhitespace($"{skill.Description} {skill.Body}"), 320));
            }
            builder.AppendLine();
        }

        if (docs.Count > 0)
        {
            builder.AppendLine("文档摘要：");
            foreach (var doc in docs.Take(3))
            {
                builder.Append("- ");
                builder.Append(doc.Source);
                builder.Append("：");
                builder.AppendLine(Truncate(CollapseWhitespace(doc.Content), 240));
            }
            builder.AppendLine();
        }

        builder.AppendLine("请返回修正后的只读 SQL。不要解释，不要 Markdown，不要 JSON。");
        return builder.ToString().Trim();
    }

    private static string BuildFallbackAnswer(
        CopilotAgentContext context,
        CopilotConversation conversation,
        IReadOnlyList<CopilotToolObservation> observations,
        IReadOnlyList<CopilotCitation> citations)
    {
        if (TryBuildSqlFallbackAnswer(conversation, observations, out var sqlAnswer))
            return sqlAnswer;

        if (observations.Count == 0)
        {
            return citations.Count > 0
                ? $"我已经完成文档与技能召回，但当前没有额外工具结果可补充；你可以结合已有引用继续追问更具体的字段、SQL 或抽样需求。[{citations[0].Id}]"
                : $"我已经检查了数据库 '{context.DatabaseName}' 的可用上下文，但当前没有足够证据给出更具体的回答。";
        }

        var summary = string.Join("、", observations.Select(static item => item.Name));
        var citationSuffix = citations.Count > 0
            ? string.Concat(citations.Select(static item => $"[{item.Id}]"))
            : string.Empty;
        return $"我已经执行了这些工具：{summary}。请结合返回的结构化结果继续确认或缩小问题范围。{citationSuffix}".Trim();
    }

    private static bool TryBuildSqlFallbackAnswer(
        CopilotConversation conversation,
        IReadOnlyList<CopilotToolObservation> observations,
        out string answer)
    {
        foreach (var observation in observations)
        {
            if (string.Equals(observation.Name, "draft_sql", StringComparison.OrdinalIgnoreCase)
                && TryDeserializeToolResult(observation.ResultJson, ServerJsonContext.Default.McpDraftSqlResult) is { } draft)
            {
                answer = BuildDraftSqlFallbackAnswer(draft, observation.Citation.Id);
                return true;
            }

            if (string.Equals(observation.Name, "execute_sql", StringComparison.OrdinalIgnoreCase)
                && TryDeserializeToolResult(observation.ResultJson, ServerJsonContext.Default.McpExecuteSqlResult) is { } executed)
            {
                answer = BuildExecuteSqlFallbackAnswer(executed, observation.Citation.Id);
                return true;
            }
        }

        var createSql = TryBuildCreateMeasurementSql(conversation.LatestUserMessage);
        if (createSql is not null)
        {
            answer = BuildInferredCreateSqlFallbackAnswer(createSql);
            return true;
        }

        answer = string.Empty;
        return false;
    }

    private static T? TryDeserializeToolResult<T>(
        string json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static string BuildDraftSqlFallbackAnswer(McpDraftSqlResult draft, string citationId)
    {
        var builder = new StringBuilder();
        builder.AppendLine(draft.StatementType switch
        {
            "create_measurement" => "已经为你起草建表 SQL：",
            "insert" => "已经为你起草写入 SQL：",
            "delete" => "已经为你起草删除 SQL：",
            _ => "已经为你起草 SQL：",
        });
        AppendSqlBlock(builder, draft.Sql);
        AppendNotes(builder, draft.Notes);
        AppendCitation(builder, citationId);
        return builder.ToString().Trim();
    }

    private static string BuildExecuteSqlFallbackAnswer(McpExecuteSqlResult executed, string citationId)
    {
        var builder = new StringBuilder();
        builder.AppendLine(executed.StatementType switch
        {
            "create_measurement" => "SQL 已执行，measurement 已创建：",
            "insert" => "SQL 已执行，数据已写入：",
            "delete" => "SQL 已执行，删除标记已写入：",
            _ => "SQL 已执行：",
        });
        AppendSqlBlock(builder, executed.Sql);
        if (executed.RowsAffected is not null)
            builder.AppendLine($"影响行数/列数：{executed.RowsAffected.Value}。");
        if (executed.ReturnedRows is not null)
            builder.AppendLine($"返回行数：{executed.ReturnedRows.Value}。");
        AppendCitation(builder, citationId);
        return builder.ToString().Trim();
    }

    private static string BuildInferredCreateSqlFallbackAnswer(string sql)
    {
        var builder = new StringBuilder();
        builder.AppendLine("可以用这条 SonnetDB SQL 创建温湿度监测 measurement：");
        AppendSqlBlock(builder, sql);
        builder.AppendLine("说明：`time` 是写入数据时提供的毫秒时间戳；TAG 用于按设备或位置过滤，温度和湿度作为 FLOAT FIELD 存储。");
        return builder.ToString().Trim();
    }

    private static void AppendSqlBlock(StringBuilder builder, string sql)
    {
        builder.AppendLine("```sql");
        builder.AppendLine(sql.Trim());
        builder.AppendLine("```");
    }

    private static void AppendNotes(StringBuilder builder, IReadOnlyList<string> notes)
    {
        foreach (var note in notes)
        {
            if (!string.IsNullOrWhiteSpace(note))
                builder.AppendLine($"- {note}");
        }
    }

    private static void AppendCitation(StringBuilder builder, string citationId)
    {
        if (!string.IsNullOrWhiteSpace(citationId))
            builder.Append('[').Append(citationId).Append(']');
    }

    private static List<CopilotCitation> BuildRetrievalCitations(
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        ref int nextCitationNumber)
    {
        var citations = new List<CopilotCitation>(docs.Count + loadedSkills.Count);
        foreach (var doc in docs)
        {
            citations.Add(new CopilotCitation(
                Id: $"C{nextCitationNumber++}",
                Kind: "doc",
                Title: string.IsNullOrWhiteSpace(doc.Title) ? doc.Source : doc.Title,
                Source: doc.Source,
                Snippet: Truncate(CollapseWhitespace(doc.Content), 220)));
        }

        foreach (var skill in loadedSkills)
        {
            citations.Add(new CopilotCitation(
                Id: $"C{nextCitationNumber++}",
                Kind: "skill",
                Title: skill.Name,
                Source: skill.Source,
                Snippet: Truncate(CollapseWhitespace($"{skill.Description} {skill.Body}"), 220)));
        }

        return citations;
    }

    private static CopilotCitation BuildToolCitation(
        CopilotToolInvocation tool,
        string resultJson,
        ref int nextCitationNumber)
    {
        var title = tool.Measurement is not null
            ? $"{tool.Name}({tool.Measurement})"
            : tool.Sql is not null
                ? $"{tool.Name}({Truncate(CollapseWhitespace(tool.Sql), 48)})"
                : tool.Name;

        return new CopilotCitation(
            Id: $"C{nextCitationNumber++}",
            Kind: "tool",
            Title: title,
            Source: $"tool:{tool.Name}",
            Snippet: Truncate(CollapseWhitespace(resultJson), 220));
    }

    private static string SerializeToolResult<T>(T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        => JsonSerializer.Serialize(payload, typeInfo);

    private static int NormalizeLimit(int? requested, int defaultValue, int maxValue)
    {
        if (requested is null || requested <= 0)
            return defaultValue;

        return Math.Min(requested.Value, maxValue);
    }

    private static string? TryResolveMeasurement(string? requested, IReadOnlyList<string> measurements, string message)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var exact = measurements.FirstOrDefault(item =>
                string.Equals(item, requested.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
                return exact;
        }

        foreach (var measurement in measurements)
        {
            if (message.Contains(measurement, StringComparison.OrdinalIgnoreCase))
                return measurement;
        }

        return null;
    }

    private static string? TryExtractSql(string message)
    {
        var trimmed = message.Trim();
        var fencedStart = trimmed.IndexOf("```", StringComparison.Ordinal);
        if (fencedStart >= 0)
        {
            var fencedEnd = trimmed.IndexOf("```", fencedStart + 3, StringComparison.Ordinal);
            if (fencedEnd > fencedStart)
            {
                var payload = trimmed[(fencedStart + 3)..fencedEnd].Trim();
                var newline = payload.IndexOf('\n');
                if (newline >= 0 && payload[..newline].All(static ch => char.IsLetter(ch)))
                    payload = payload[(newline + 1)..].Trim();
                if (LooksLikeSql(payload))
                    return payload;
            }
        }

        if (LooksLikeSql(trimmed))
            return trimmed;

        var selectIndex = message.IndexOf("SELECT ", StringComparison.OrdinalIgnoreCase);
        if (selectIndex >= 0)
            return message[selectIndex..].Trim();

        var showIndex = message.IndexOf("SHOW ", StringComparison.OrdinalIgnoreCase);
        if (showIndex >= 0)
            return message[showIndex..].Trim();

        var describeIndex = message.IndexOf("DESCRIBE ", StringComparison.OrdinalIgnoreCase);
        if (describeIndex >= 0)
            return message[describeIndex..].Trim();

        var createIndex = message.IndexOf("CREATE ", StringComparison.OrdinalIgnoreCase);
        if (createIndex >= 0)
            return message[createIndex..].Trim();

        var insertIndex = message.IndexOf("INSERT ", StringComparison.OrdinalIgnoreCase);
        if (insertIndex >= 0)
            return message[insertIndex..].Trim();

        var deleteIndex = message.IndexOf("DELETE ", StringComparison.OrdinalIgnoreCase);
        if (deleteIndex >= 0)
            return message[deleteIndex..].Trim();

        return null;
    }

    private static bool LooksLikeSql(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("SHOW ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DESCRIBE ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("INSERT ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWriteSql(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.TrimStart();
        return trimmed.StartsWith("CREATE ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("INSERT ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("DELETE ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeWriteIntent(string lowered)
    {
        // 中英文常见的“建表 / 创建 measurement / 插入 / 写入 / 删除”意图关键词。
        return lowered.Contains("建表", StringComparison.Ordinal)
            || lowered.Contains("建一个表", StringComparison.Ordinal)
            || lowered.Contains("建一张表", StringComparison.Ordinal)
            || lowered.Contains("新建表", StringComparison.Ordinal)
            || lowered.Contains("创建表", StringComparison.Ordinal)
            || lowered.Contains("创建 measurement", StringComparison.Ordinal)
            || lowered.Contains("create table", StringComparison.Ordinal)
            || lowered.Contains("create measurement", StringComparison.Ordinal)
            || lowered.Contains("插入", StringComparison.Ordinal)
            || lowered.Contains("写入数据", StringComparison.Ordinal)
            || lowered.Contains("写入一条", StringComparison.Ordinal)
            || lowered.Contains("insert into", StringComparison.Ordinal)
            || lowered.Contains("删除数据", StringComparison.Ordinal)
            || lowered.Contains("delete from", StringComparison.Ordinal);
    }

    private static bool LooksLikeCreateMeasurementIntent(string lowered)
    {
        return lowered.Contains("建表", StringComparison.Ordinal)
            || lowered.Contains("建一个表", StringComparison.Ordinal)
            || lowered.Contains("建一张表", StringComparison.Ordinal)
            || lowered.Contains("新建表", StringComparison.Ordinal)
            || lowered.Contains("创建表", StringComparison.Ordinal)
            || lowered.Contains("创建 measurement", StringComparison.Ordinal)
            || lowered.Contains("create table", StringComparison.Ordinal)
            || lowered.Contains("create measurement", StringComparison.Ordinal)
            || (lowered.Contains("建", StringComparison.Ordinal)
                && (lowered.Contains("表", StringComparison.Ordinal)
                    || lowered.Contains("measurement", StringComparison.Ordinal)));
    }

    private static string? TryBuildCreateMeasurementSql(string message)
    {
        var lowered = message.ToLowerInvariant();
        if (!LooksLikeCreateMeasurementIntent(lowered))
            return null;

        var hasTemperature = lowered.Contains("温度", StringComparison.Ordinal)
            || ContainsIdentifierToken(message, "temperature")
            || ContainsIdentifierToken(message, "temp");
        var hasHumidity = lowered.Contains("湿度", StringComparison.Ordinal)
            || ContainsIdentifierToken(message, "humidity");

        if (!hasTemperature && !hasHumidity)
            return null;

        var measurement = InferCreateMeasurementName(message, hasTemperature, hasHumidity);
        var columns = new List<string>(4);
        AddTagColumns(message, columns);

        if (hasTemperature)
        {
            var name = ContainsIdentifierToken(message, "temperature") && !ContainsIdentifierToken(message, "temp")
                ? "temperature"
                : ContainsIdentifierToken(message, "temp")
                    ? "temp"
                    : "temperature";
            AddUniqueColumn(columns, $"{name} FIELD FLOAT");
        }

        if (hasHumidity)
            AddUniqueColumn(columns, "humidity FIELD FLOAT");

        return $"CREATE MEASUREMENT {measurement} ({string.Join(", ", columns)})";
    }

    private static string InferCreateMeasurementName(string message, bool hasTemperature, bool hasHumidity)
    {
        var explicitName = TryFindIdentifierAfterAny(
                message,
                "名为",
                "命名为",
                "叫做",
                "叫",
                "表名",
                "measurement",
                "table")
            ?? TryFindIdentifierBefore(message, "表")
            ?? TryFindIdentifierBefore(message, "measurement");

        if (explicitName is not null && !IsWeakInferredName(explicitName))
            return explicitName;

        return hasTemperature && hasHumidity
            ? "sensor_temperature"
            : "sensor_data";
    }

    private static void AddTagColumns(string message, List<string> columns)
    {
        if (ContainsIdentifierToken(message, "host"))
            AddUniqueColumn(columns, "host TAG");
        if (ContainsIdentifierToken(message, "device_id") || message.Contains("设备", StringComparison.Ordinal))
            AddUniqueColumn(columns, "device_id TAG");
        if (ContainsIdentifierToken(message, "sensor_id") || message.Contains("传感器", StringComparison.Ordinal))
            AddUniqueColumn(columns, "sensor_id TAG");
        if (ContainsIdentifierToken(message, "location") || message.Contains("位置", StringComparison.Ordinal))
            AddUniqueColumn(columns, "location TAG");

        if (columns.Count == 0)
        {
            AddUniqueColumn(columns, "device_id TAG");
            AddUniqueColumn(columns, "location TAG");
        }
    }

    private static void AddUniqueColumn(List<string> columns, string column)
    {
        var columnNameEnd = column.IndexOf(' ', StringComparison.Ordinal);
        var columnName = columnNameEnd > 0 ? column[..columnNameEnd] : column;
        if (!columns.Any(item => item.StartsWith(columnName + " ", StringComparison.OrdinalIgnoreCase)))
            columns.Add(column);
    }

    private static string? TryFindIdentifierAfterAny(string text, params string[] markers)
    {
        foreach (var marker in markers)
        {
            var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            while (index >= 0)
            {
                var identifier = TryReadIdentifierForward(text, index + marker.Length);
                if (identifier is not null)
                    return identifier;

                index = text.IndexOf(marker, index + marker.Length, StringComparison.OrdinalIgnoreCase);
            }
        }

        return null;
    }

    private static string? TryFindIdentifierBefore(string text, string marker)
    {
        var index = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var identifier = TryReadIdentifierBackward(text, index - 1);
            if (identifier is not null)
                return identifier;

            index = text.IndexOf(marker, index + marker.Length, StringComparison.OrdinalIgnoreCase);
        }

        return null;
    }

    private static string? TryReadIdentifierForward(string text, int start)
    {
        var index = start;
        while (index < text.Length && IsForwardIdentifierSeparator(text[index]))
            index++;

        return TryReadIdentifierAt(text, index);
    }

    private static bool IsForwardIdentifierSeparator(char ch)
        => char.IsWhiteSpace(ch)
            || ch is ':' or '：' or '=' or '"' or '`' or '\'' or '“' or '”';

    private static string? TryReadIdentifierBackward(string text, int start)
    {
        var end = start;
        while (end >= 0 && char.IsWhiteSpace(text[end]))
            end--;
        if (end < 0 || !IsIdentifierPart(text[end]))
            return null;

        var begin = end;
        while (begin >= 0 && IsIdentifierPart(text[begin]))
            begin--;
        begin++;

        if (begin > end || !IsIdentifierStart(text[begin]))
            return null;

        return text[begin..(end + 1)];
    }

    private static string? TryReadIdentifierAt(string text, int index)
    {
        if (index < 0 || index >= text.Length || !IsIdentifierStart(text[index]))
            return null;

        var end = index + 1;
        while (end < text.Length && IsIdentifierPart(text[end]))
            end++;

        return text[index..end];
    }

    private static bool ContainsIdentifierToken(string text, string token)
    {
        var index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            var beforeOk = index == 0 || !IsIdentifierPart(text[index - 1]);
            var after = index + token.Length;
            var afterOk = after >= text.Length || !IsIdentifierPart(text[after]);
            if (beforeOk && afterOk)
                return true;

            index = text.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool IsIdentifierStart(char ch)
        => ch == '_' || (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z');

    private static bool IsIdentifierPart(char ch)
        => IsIdentifierStart(ch) || (ch >= '0' && ch <= '9');

    private static bool IsWeakInferredName(string identifier)
        => identifier.Equals("for", StringComparison.OrdinalIgnoreCase)
            || identifier.Equals("with", StringComparison.OrdinalIgnoreCase)
            || identifier.Equals("need", StringComparison.OrdinalIgnoreCase)
            || identifier.Equals("needs", StringComparison.OrdinalIgnoreCase);

    private static bool IsReadOnlyStatement(SqlStatement statement)
        => statement is SelectStatement or ShowMeasurementsStatement or DescribeMeasurementStatement;

    private static string GetStatementType(SqlStatement statement) => statement switch
    {
        SelectStatement => "select",
        ShowMeasurementsStatement => "show_measurements",
        DescribeMeasurementStatement => "describe_measurement",
        _ => "unknown",
    };

    private static List<AiMessage> NormalizeMessages(IReadOnlyList<AiMessage> messages)
    {
        var normalized = new List<AiMessage>(messages.Count);
        foreach (var message in messages)
        {
            if (message is null || string.IsNullOrWhiteSpace(message.Content))
                continue;

            var role = NormalizeRole(message.Role);
            if (role is null)
                continue;

            normalized.Add(new AiMessage(role, message.Content.Trim()));
        }

        return normalized;
    }

    private static IReadOnlyList<AiMessage> TrimConversation(IReadOnlyList<AiMessage> messages)
    {
        if (messages.Count == 0)
            return [];

        var remaining = HistoryTokenBudget;
        var reversed = new List<AiMessage>(messages.Count);
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            var message = messages[i];
            var content = reversed.Count == 0
                ? TruncateToTokenBudget(message.Content, Math.Max(64, remaining))
                : message.Content;
            var estimatedTokens = EstimateTokens(content) + 4;

            if (reversed.Count > 0 && estimatedTokens > remaining)
                break;

            reversed.Add(new AiMessage(message.Role, content));
            remaining = Math.Max(0, remaining - estimatedTokens);
        }

        reversed.Reverse();
        return reversed;
    }

    private static int FindLatestUserMessageIndex(IReadOnlyList<AiMessage> messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(messages[i].Role, "user", StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string BuildRetrievalQuery(IReadOnlyList<AiMessage> messages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages.TakeLast(4))
        {
            builder.Append(message.Role);
            builder.Append(": ");
            builder.AppendLine(Truncate(CollapseWhitespace(message.Content), 220));
        }

        return builder.ToString().Trim();
    }

    private static string? NormalizeRole(string? role)
    {
        if (string.IsNullOrWhiteSpace(role))
            return "user";

        return role.Trim().ToLowerInvariant() switch
        {
            "user" => "user",
            "assistant" => "assistant",
            "system" => "system",
            _ => null,
        };
    }

    private static int EstimateTokens(string text)
        => string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (text.Length + 3) / 4);

    private static string TruncateToTokenBudget(string text, int tokenBudget)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var maxChars = Math.Max(32, tokenBudget * 4);
        return text.Length <= maxChars
            ? text
            : text[..maxChars].TrimEnd() + "...";
    }

    private static string FormatToolArguments(CopilotToolInvocation tool)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();

        if (tool.Measurement is not null)
            writer.WriteString("measurement", tool.Measurement);
        if (tool.Sql is not null)
            writer.WriteString("sql", tool.Sql);
        if (tool.MaxRows is not null)
            writer.WriteNumber("maxRows", tool.MaxRows.Value);
        if (tool.N is not null)
            writer.WriteNumber("n", tool.N.Value);

        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string BuildSqlErrorPayload(SqlExecutionException exception, int attempt, bool final)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        writer.WriteStartObject();
        writer.WriteString("error", "sql_error");
        writer.WriteString("phase", exception.Phase);
        writer.WriteString("message", exception.Message);
        writer.WriteString("sql", exception.Sql);
        writer.WriteNumber("attempt", attempt);
        writer.WriteBoolean("final", final);
        writer.WriteEndObject();
        writer.Flush();
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void AppendConversationHistory(StringBuilder builder, IReadOnlyList<AiMessage> history)
    {
        if (history.Count == 0)
            return;

        builder.AppendLine("最近对话历史：");
        foreach (var message in history)
        {
            builder.Append("- ");
            builder.Append(message.Role switch
            {
                "assistant" => "assistant",
                "system" => "system",
                _ => "user",
            });
            builder.Append("：");
            builder.AppendLine(Truncate(CollapseWhitespace(message.Content), 320));
        }
        builder.AppendLine();
    }

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var builder = new StringBuilder(text.Length);
        var previousWhitespace = false;
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                    builder.Append(' ');
                previousWhitespace = true;
            }
            else
            {
                builder.Append(ch);
                previousWhitespace = false;
            }
        }

        return builder.ToString().Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
            return text;

        return text[..Math.Max(0, maxLength - 3)].TrimEnd() + "...";
    }

    private const string PlannerSystemPrompt =
        """
        你是 SonnetDB Copilot 的工具规划器。
        你的任务是只从下面 8 个工具中选择最少必要的工具调用，最多 3 个：
        - list_databases()
        - list_measurements(maxRows?)
        - describe_measurement(measurement)
        - sample_rows(measurement, n?)
        - explain_sql(sql)
        - query_sql(sql, maxRows?)              // 仅 SELECT/SHOW/DESCRIBE
        - draft_sql(sql)                        // 起草 / 校验 CREATE MEASUREMENT、INSERT、DELETE、SELECT 等 SQL，但不会改写数据
        - execute_sql(sql, maxRows?)            // 真正执行 CREATE MEASUREMENT / INSERT / DELETE / SELECT；写入需调用方具备写权限

        输出必须是严格 JSON，格式如下（示例：用户要求建一个保存电脑性能数据的仓库）：
        {"tools":[{"name":"list_databases"},{"name":"draft_sql","sql":"CREATE MEASUREMENT host_perf (host TAG, region TAG, cpu_pct FIELD FLOAT, mem_pct FIELD FLOAT, cpu_temp_celsius FIELD FLOAT, disk_read_mbps FIELD FLOAT, disk_write_mbps FIELD FLOAT, net_rx_mbps FIELD FLOAT, net_tx_mbps FIELD FLOAT)"}]}

        规则：
        - 只能输出 JSON，不要附加解释、Markdown 或代码块。
        - 如果已有上下文足够回答，可以返回 {"tools":[]}
        - 询问 schema/字段/列结构时，优先 describe_measurement 或 list_measurements。
        - 用户给出只读 SQL 并询问结果时，优先 query_sql；询问扫描/成本/解释时优先 explain_sql。
        - 用户描述“建表 / 创建 measurement / 插入数据 / 写入 / 删除数据”等需求时：
            * 先用 list_measurements 或 describe_measurement 获取已有结构（如尚未掌握）。
            * 再用 draft_sql 给出可执行的 CREATE MEASUREMENT / INSERT / DELETE 语句。
            * 仅当用户在最近一次消息中明确说“执行 / 立即建表 / 直接写入 / 帮我跑一下”等含义时，才追加 execute_sql。
            * 不要在没有 draft_sql 验证的情况下直接调用 execute_sql。
        - 不要编造不存在的 measurement 名称、列名或函数。
        - SonnetDB 的 CREATE MEASUREMENT 语法：CREATE MEASUREMENT name (col TAG, col FIELD type, ...)，FIELD 类型只接受 FLOAT / INT / BOOL / STRING / VECTOR(N)，TAG 列固定为 STRING。
        - SonnetDB 的 INSERT 语法：INSERT INTO measurement (time, tag1, field1, ...) VALUES (1700000000000, 'host-1', 0.42, ...)，time 为毫秒时间戳。
        """;

    private const string SqlRepairSystemPrompt =
        """
        你是 SonnetDB Copilot 的 SQL 纠错器。
        请根据失败 SQL、错误消息、对话上下文和文档/技能摘要，把 SQL 改写成可执行的只读 SQL。
        规则：
        - 只允许输出一条 SELECT、SHOW MEASUREMENTS / SHOW TABLES 或 DESCRIBE [MEASUREMENT]。
        - 只能输出 SQL 本身，不要解释、Markdown、代码块或 JSON。
        - 不要编造不存在的 measurement、列名或函数。
        """;

    private const string AnswerSystemPrompt =
        """
        你是 SonnetDB Copilot 的最终回答器。
        请严格基于给定的文档、技能与工具结果作答，不要编造数据库结构、数据或 SQL 结果。
        要求：
        - 使用中文回答。
        - 优先给出直接结论，再补充必要说明。
        - 如果给定了 citations，请尽量在对应句子末尾用 [C1] 这样的编号引用。
        - 若证据不足，请明确说明不确定或当前结果不足以确认。
        - 当用户的意图是建表 / 写入 / 删除 / 改 schema 时，必须给出可直接复制执行的 SQL：
            * 把每条 SQL 单独放在 ```sql 代码块中。
            * 优先使用 draft_sql / execute_sql 工具返回的 SQL，不要自行改写列名或类型。
            * 如果工具返回了 notes（例如缺权限、measurement 已存在），请把这些注意事项明确转述给用户。
            * 生成建表 SQL 时，必须覆盖用户提到的所有指标字段，不要只写一两个示例字段就省略其余。例如用户说 CPU 使用率、内存使用率、温度，就必须把三者都建成独立的 FIELD 列。
            * 如果当前数据库不存在（list_databases 结果为空或不含目标库），必须在建表 SQL 之前先给出 CREATE DATABASE 语句，并说明需要先创建数据库。
            * 当 SQL 已准备好且界面提供了 SQL Console 选项卡时，请在回答末尾提示用户：可以点击页面上方的 SQL Console 按鈕，将上方 SQL 粘贴进去直接执行。
        - 当用户给出只是描述而工具没生成 SQL 时，根据已有 measurement 列表与字段，自己起草一条最贴近需求的 CREATE MEASUREMENT / INSERT 语句，同样放进 ```sql 代码块。
        """;
}

/// <summary>
/// Copilot 执行上下文。
/// </summary>
/// <param name="DatabaseName">当前数据库名。</param>
/// <param name="Database">当前数据库实例。</param>
/// <param name="VisibleDatabases">当前凭据可见的数据库集合。</param>
/// <param name="CanWrite">当前调用方对该数据库是否拥有写权限（控制 <c>execute_sql</c> 是否可对 DDL/DML 生效）。</param>
/// <param name="ModelOverride">可选模型覆盖（M8）：如果不为空，会传递给 chat provider 作为本次调用的模型名。</param>
internal sealed record CopilotAgentContext(
    string DatabaseName,
    Tsdb Database,
    IReadOnlyList<string> VisibleDatabases,
    bool CanWrite = false,
    string? ModelOverride = null);

/// <summary>
/// 多轮对话的规范化结果。
/// </summary>
internal sealed record CopilotConversation(
    IReadOnlyList<AiMessage> Messages,
    IReadOnlyList<AiMessage> History,
    string LatestUserMessage,
    string RetrievalQuery,
    bool WasTrimmed);

/// <summary>
/// 工具规划结果。
/// </summary>
internal sealed record CopilotToolPlan(IReadOnlyList<CopilotPlannedTool> Tools);

/// <summary>
/// 单个规划出来的工具调用。
/// </summary>
internal sealed record CopilotPlannedTool(
    string Name,
    string? Measurement = null,
    string? Sql = null,
    int? MaxRows = null,
    int? N = null);

/// <summary>
/// 规范化后的工具调用。
/// </summary>
internal sealed record CopilotToolInvocation(
    string Name,
    int? MaxRows,
    int? N,
    string? Measurement,
    string? Sql);

/// <summary>
/// 已执行工具的观测结果。
/// </summary>
internal sealed record CopilotToolObservation(
    string Name,
    string ArgumentsJson,
    string ResultJson,
    CopilotCitation Citation);

/// <summary>
/// 工具执行与修复后的最终结果。
/// </summary>
internal sealed record CopilotToolExecutionResult(
    CopilotToolInvocation Tool,
    string ResultJson,
    IReadOnlyList<CopilotChatEvent> Events);
