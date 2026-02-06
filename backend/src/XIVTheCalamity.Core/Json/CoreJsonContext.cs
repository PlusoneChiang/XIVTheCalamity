using System.Text.Json.Serialization;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Core.Json;

/// <summary>
/// JSON Source Generator context for Core project
/// Required for NativeAOT compatibility
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(GameConfig))]
[JsonSerializable(typeof(DalamudConfig))]
[JsonSerializable(typeof(LauncherConfig))]
[JsonSerializable(typeof(WineConfig))]
[JsonSerializable(typeof(WineXIVConfig))]
[JsonSerializable(typeof(LoginResult))]
public partial class CoreJsonContext : JsonSerializerContext
{
}
