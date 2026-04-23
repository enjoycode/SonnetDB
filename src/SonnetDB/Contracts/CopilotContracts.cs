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
