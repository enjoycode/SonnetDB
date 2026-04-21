using System.Text.Json.Serialization;

namespace SonnetDB.Cli;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(CliProfilesDocument))]
[JsonSerializable(typeof(List<CliRemoteProfile>))]
[JsonSerializable(typeof(CliLocalProfile))]
[JsonSerializable(typeof(List<CliLocalProfile>))]
internal sealed partial class CliJsonContext : JsonSerializerContext;
