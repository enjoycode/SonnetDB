namespace SonnetDB.Contracts;

/// <summary>
/// Copilot 文档摄入请求体（PR #64）。
/// </summary>
/// <param name="Roots">可选的覆盖根目录列表。为空时使用配置中的 <c>Copilot:Docs:Roots</c>。</param>
/// <param name="Force">是否忽略 mtime/fingerprint 强制重新嵌入。</param>
/// <param name="DryRun">仅扫描与切片，不实际写入向量库。</param>
public sealed record CopilotIngestRequest(
    IReadOnlyList<string>? Roots = null,
    bool Force = false,
    bool DryRun = false);

/// <summary>
/// Copilot 文档摄入响应体（PR #64）。
/// </summary>
public sealed record CopilotIngestResponse(
    int ScannedFiles,
    int IndexedFiles,
    int SkippedFiles,
    int DeletedFiles,
    int WrittenChunks,
    bool DryRun,
    double ElapsedMilliseconds);

/// <summary>
/// Copilot 文档检索请求体（PR #64）。
/// </summary>
public sealed record CopilotSearchRequest(string Query, int? K = null);

/// <summary>
/// Copilot 文档检索单条命中（PR #64）。
/// </summary>
public sealed record CopilotSearchHit(
    string Source,
    string Title,
    string Section,
    string Content,
    double Score);

/// <summary>
/// Copilot 文档检索响应体（PR #64）。
/// </summary>
public sealed record CopilotSearchResponse(
    string Query,
    int Requested,
    IReadOnlyList<CopilotSearchHit> Hits,
    double ElapsedMilliseconds);

/// <summary>
/// Copilot 技能库摄入请求体（PR #65）。
/// </summary>
/// <param name="Root">可选根目录，覆盖配置中的 <c>Copilot:Skills:Root</c>。</param>
/// <param name="Force">是否忽略 mtime/fingerprint 强制重新嵌入。</param>
/// <param name="DryRun">仅扫描，不写入向量库。</param>
public sealed record CopilotSkillsIngestRequest(
    string? Root = null,
    bool Force = false,
    bool DryRun = false);

/// <summary>
/// Copilot 技能库摄入响应体（PR #65）。
/// </summary>
public sealed record CopilotSkillsIngestResponse(
    int ScannedSkills,
    int IndexedSkills,
    int SkippedSkills,
    int DeletedSkills,
    bool DryRun,
    double ElapsedMilliseconds);

/// <summary>
/// Copilot 技能库检索请求体（PR #65）。
/// </summary>
public sealed record CopilotSkillsSearchRequest(string Query, int? K = null);

/// <summary>
/// Copilot 技能库检索单条命中（PR #65）。
/// </summary>
public sealed record CopilotSkillsSearchHit(
    string Name,
    string Description,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> RequiresTools,
    double Score);

/// <summary>
/// Copilot 技能库检索响应体（PR #65）。
/// </summary>
public sealed record CopilotSkillsSearchResponse(
    string Query,
    int Requested,
    IReadOnlyList<CopilotSkillsSearchHit> Hits,
    double ElapsedMilliseconds);

/// <summary>
/// Copilot 技能 load 响应体（PR #65）。
/// </summary>
public sealed record CopilotSkillLoadResponse(
    string Name,
    string Description,
    IReadOnlyList<string> Triggers,
    IReadOnlyList<string> RequiresTools,
    string Body,
    string Source);

/// <summary>
/// Copilot 技能 list 响应体（PR #65）。
/// </summary>
public sealed record CopilotSkillsListResponse(IReadOnlyList<CopilotSkillsSearchHit> Skills);

/// <summary>
/// Copilot 单轮聊天请求体（PR #67）。
/// </summary>
/// <param name="Db">目标数据库名。</param>
/// <param name="Message">用户问题。</param>
/// <param name="DocsK">文档召回条数；为空时使用服务端默认值。</param>
/// <param name="SkillsK">技能召回条数；为空时使用服务端默认值。</param>
public sealed record CopilotChatRequest(
    string Db,
    string Message,
    int? DocsK = null,
    int? SkillsK = null);

/// <summary>
/// Copilot 回答中附带的一条 citation（PR #67）。
/// </summary>
/// <param name="Id">引用编号，例如 <c>C1</c>。</param>
/// <param name="Kind">引用类别：<c>doc</c> / <c>skill</c> / <c>tool</c>。</param>
/// <param name="Title">引用标题。</param>
/// <param name="Source">引用来源。</param>
/// <param name="Snippet">引用摘要片段。</param>
public sealed record CopilotCitation(
    string Id,
    string Kind,
    string Title,
    string Source,
    string Snippet);

/// <summary>
/// Copilot 聊天流中的单条事件（PR #67）。
/// </summary>
/// <param name="Type">事件类型：<c>start</c> / <c>retrieval</c> / <c>tool_call</c> / <c>tool_result</c> / <c>final</c> / <c>error</c> / <c>done</c>。</param>
/// <param name="Message">阶段性说明或错误消息。</param>
/// <param name="Answer">最终回答文本，仅 <c>final</c> 事件使用。</param>
/// <param name="ToolName">工具名，仅工具相关事件使用。</param>
/// <param name="ToolArguments">工具参数的 JSON 文本。</param>
/// <param name="ToolResult">工具结果的 JSON 文本。</param>
/// <param name="SkillNames">召回到的技能名列表。</param>
/// <param name="ToolNames">召回技能建议使用的工具名列表。</param>
/// <param name="Citations">当前事件附带的 citations。</param>
public sealed record CopilotChatEvent(
    string Type,
    string? Message = null,
    string? Answer = null,
    string? ToolName = null,
    string? ToolArguments = null,
    string? ToolResult = null,
    IReadOnlyList<string>? SkillNames = null,
    IReadOnlyList<string>? ToolNames = null,
    IReadOnlyList<CopilotCitation>? Citations = null);
