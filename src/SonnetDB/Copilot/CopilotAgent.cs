using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SonnetDB.Contracts;
using SonnetDB.Engine;
using SonnetDB.Json;
using SonnetDB.Mcp;
using SonnetDB.Sql;
using SonnetDB.Sql.Ast;
using SonnetDB.Sql.Execution;

namespace SonnetDB.Copilot;

/// <summary>
/// PR #67：单轮 Copilot 问答编排器。
/// </summary>
internal sealed class CopilotAgent
{
    private const int DefaultDocsK = 5;
    private const int MaxDocsK = 10;
    private const int DefaultSkillsK = 3;
    private const int MaxSkillsK = 8;
    private const int MaxLoadedSkills = 3;
    private const int MaxPlannedTools = 3;

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
        string message,
        int? docsK = null,
        int? skillsK = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        var normalizedMessage = message.Trim();
        var effectiveDocsK = NormalizeLimit(docsK, DefaultDocsK, MaxDocsK);
        var effectiveSkillsK = NormalizeLimit(skillsK, DefaultSkillsK, MaxSkillsK);

        yield return new CopilotChatEvent(
            Type: "start",
            Message: $"开始处理数据库 '{context.DatabaseName}' 上的问题。");

        var docs = effectiveDocsK > 0
            ? await _docsSearchService.SearchAsync(normalizedMessage, effectiveDocsK, cancellationToken).ConfigureAwait(false)
            : [];

        var skillHits = effectiveSkillsK > 0
            ? await _skillSearchService.SearchAsync(normalizedMessage, effectiveSkillsK, cancellationToken).ConfigureAwait(false)
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

        var plan = await PlanToolsAsync(context, normalizedMessage, docs, loadedSkills, cancellationToken).ConfigureAwait(false);
        var observations = new List<CopilotToolObservation>(plan.Count);

        foreach (var tool in plan)
        {
            var toolArguments = FormatToolArguments(tool);
            yield return new CopilotChatEvent(
                Type: "tool_call",
                Message: $"执行工具 {tool.Name}。",
                ToolName: tool.Name,
                ToolArguments: toolArguments);

            var observation = ExecuteTool(context, tool);
            var citation = BuildToolCitation(tool, observation, ref nextCitationNumber);
            var captured = new CopilotToolObservation(tool.Name, toolArguments, observation, citation);
            observations.Add(captured);

            yield return new CopilotChatEvent(
                Type: "tool_result",
                Message: $"工具 {tool.Name} 已返回结果。",
                ToolName: tool.Name,
                ToolArguments: toolArguments,
                ToolResult: observation,
                Citations: [citation]);
        }

        var allCitations = new List<CopilotCitation>(retrievalCitations.Count + observations.Count);
        allCitations.AddRange(retrievalCitations);
        allCitations.AddRange(observations.Select(static item => item.Citation));

        var answer = await GenerateAnswerAsync(
            context,
            normalizedMessage,
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
        string message,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        CancellationToken cancellationToken)
    {
        var measurements = _schemaCache.GetMeasurements(context.DatabaseName, context.Database);
        var plannerPrompt = BuildPlannerPrompt(context, message, docs, loadedSkills);

        try
        {
            var response = await _chatProvider.CompleteAsync(
                [
                    new AiMessage("system", PlannerSystemPrompt),
                    new AiMessage("user", plannerPrompt),
                ],
                cancellationToken).ConfigureAwait(false);

            if (TryParsePlan(response, out var plan) && plan is not null)
                return SanitizePlan(plan.Tools, measurements, message);

            _logger.LogWarning("Copilot planner returned non-JSON content: {Response}", response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot planner failed, falling back to heuristics.");
        }

        return BuildHeuristicPlan(message, measurements);
    }

    private async Task<string> GenerateAnswerAsync(
        CopilotAgentContext context,
        string message,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        IReadOnlyList<CopilotToolObservation> observations,
        IReadOnlyList<CopilotCitation> citations,
        CancellationToken cancellationToken)
    {
        var prompt = BuildAnswerPrompt(context, message, docs, loadedSkills, observations, citations);

        try
        {
            var answer = await _chatProvider.CompleteAsync(
                [
                    new AiMessage("system", AnswerSystemPrompt),
                    new AiMessage("user", prompt),
                ],
                cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(answer))
                return answer.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Copilot final answer generation failed, using deterministic fallback.");
        }

        return BuildFallbackAnswer(context, observations, citations);
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

            tools.Add(new CopilotToolInvocation(
                "query_sql",
                SonnetDbMcpResults.DefaultToolRowLimit,
                null,
                null,
                sql));
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

    private string ExecuteTool(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        return tool.Name switch
        {
            "list_databases" => SerializeToolResult(
                new McpDatabaseListResult(context.DatabaseName, context.VisibleDatabases),
                ServerJsonContext.Default.McpDatabaseListResult),
            "list_measurements" => ExecuteListMeasurements(context, tool),
            "describe_measurement" => ExecuteDescribeMeasurement(context, tool),
            "sample_rows" => ExecuteSampleRows(context, tool),
            "explain_sql" => ExecuteExplainSql(context, tool),
            "query_sql" => ExecuteQuerySql(context, tool),
            _ => throw new InvalidOperationException($"不支持的 Copilot 工具 '{tool.Name}'。"),
        };
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

    private string ExecuteQuerySql(CopilotAgentContext context, CopilotToolInvocation tool)
    {
        var sql = tool.Sql
            ?? throw new InvalidOperationException("query_sql 缺少 sql 参数。");
        var maxRows = tool.MaxRows ?? SonnetDbMcpResults.DefaultToolRowLimit;
        var statement = SqlParser.Parse(sql);
        if (!IsReadOnlyStatement(statement))
        {
            throw new InvalidOperationException(
                "query_sql 仅支持 SELECT、SHOW MEASUREMENTS / SHOW TABLES 与 DESCRIBE [MEASUREMENT]。");
        }

        SqlStatement executable = statement;
        var canTruncate = false;
        if (statement is SelectStatement select)
            executable = SonnetDbMcpResults.ApplyToolRowLimit(select, maxRows, out canTruncate);

        var executionResult = SqlExecutor.ExecuteStatement(context.Database, executable);
        if (executionResult is not SelectExecutionResult selectResult)
            throw new InvalidOperationException("只读 SQL 未返回结果集。");

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
        string message,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前数据库：{context.DatabaseName}");
        builder.AppendLine($"当前可见数据库：{string.Join(", ", context.VisibleDatabases)}");
        builder.AppendLine();
        builder.AppendLine("用户问题：");
        builder.AppendLine(message);
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
        string message,
        IReadOnlyList<DocsSearchResult> docs,
        IReadOnlyList<SkillLoadResult> loadedSkills,
        IReadOnlyList<CopilotToolObservation> observations,
        IReadOnlyList<CopilotCitation> citations)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"当前数据库：{context.DatabaseName}");
        builder.AppendLine($"当前可见数据库：{string.Join(", ", context.VisibleDatabases)}");
        builder.AppendLine();
        builder.AppendLine("用户问题：");
        builder.AppendLine(message);
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

    private static string BuildFallbackAnswer(
        CopilotAgentContext context,
        IReadOnlyList<CopilotToolObservation> observations,
        IReadOnlyList<CopilotCitation> citations)
    {
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

        return null;
    }

    private static bool LooksLikeSql(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        return text.StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("SHOW ", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("DESCRIBE ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReadOnlyStatement(SqlStatement statement)
        => statement is SelectStatement or ShowMeasurementsStatement or DescribeMeasurementStatement;

    private static string GetStatementType(SqlStatement statement) => statement switch
    {
        SelectStatement => "select",
        ShowMeasurementsStatement => "show_measurements",
        DescribeMeasurementStatement => "describe_measurement",
        _ => "unknown",
    };

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
        你的任务是只从下面 6 个工具中选择最少必要的工具调用，最多 3 个：
        - list_databases()
        - list_measurements(maxRows?)
        - describe_measurement(measurement)
        - sample_rows(measurement, n?)
        - explain_sql(sql)
        - query_sql(sql, maxRows?)

        输出必须是严格 JSON，格式如下：
        {"tools":[{"name":"describe_measurement","measurement":"cpu"}]}

        规则：
        - 只能输出 JSON，不要附加解释、Markdown 或代码块。
        - 如果已有上下文足够回答，可以返回 {"tools":[]}
        - 询问 schema/字段/列结构时，优先 describe_measurement 或 list_measurements。
        - 用户给出只读 SQL 并询问结果时，优先 query_sql；询问扫描/成本/解释时优先 explain_sql。
        - 不要编造不存在的 measurement 名称。
        """;

    private const string AnswerSystemPrompt =
        """
        你是 SonnetDB Copilot 的最终回答器。
        请严格基于给定的文档、技能与工具结果作答，不要编造数据库结构、数据或 SQL 结果。
        要求：
        - 使用中文回答。
        - 优先给出直接结论，再补充必要说明。
        - 如果给定了 citations，请尽量在对应句子末尾用 [C1] 这样的编号引用。
        - 若证据不足，请明确说明“不确定”或“当前结果不足以确认”。
        """;
}

/// <summary>
/// Copilot 单轮执行上下文。
/// </summary>
/// <param name="DatabaseName">当前数据库名。</param>
/// <param name="Database">当前数据库实例。</param>
/// <param name="VisibleDatabases">当前凭据可见的数据库集合。</param>
internal sealed record CopilotAgentContext(
    string DatabaseName,
    Tsdb Database,
    IReadOnlyList<string> VisibleDatabases);

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
