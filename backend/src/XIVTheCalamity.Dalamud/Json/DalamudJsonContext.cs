using System.Text.Json.Serialization;
using XIVTheCalamity.Dalamud.Models;

namespace XIVTheCalamity.Dalamud.Json;

/// <summary>
/// JSON Source Generator context for Dalamud project
/// Required for NativeAOT compatibility
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DalamudVersionInfo))]
[JsonSerializable(typeof(DalamudAssetManifest))]
[JsonSerializable(typeof(DalamudAssetEntry))]
[JsonSerializable(typeof(DalamudStatus))]
public partial class DalamudJsonContext : JsonSerializerContext
{
}
