using System.Text.Json.Serialization;

namespace TSLite.Server.Auth;

/// <summary>
/// AOT-friendly <see cref="System.Text.Json"/> 源生成 context，专用于 <c>users.json</c>
/// 与 <c>grants.json</c> 的反/序列化。
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(UserFile))]
[JsonSerializable(typeof(GrantsFile))]
internal sealed partial class AuthJsonContext : JsonSerializerContext;
