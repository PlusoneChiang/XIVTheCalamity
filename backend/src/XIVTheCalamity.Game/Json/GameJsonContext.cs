using System.Text.Json;
using System.Text.Json.Serialization;

namespace XIVTheCalamity.Game.Json;

/// <summary>
/// JSON Source Generator context for Game project
/// Required for NativeAOT compatibility
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(LoginPayload))]
[JsonSerializable(typeof(SessionPayload))]
public partial class GameJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Login API request payload
/// </summary>
public class LoginPayload
{
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    
    [JsonPropertyName("password")]
    public string Password { get; set; } = string.Empty;
    
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
    
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}

/// <summary>
/// Session API request payload
/// </summary>
public class SessionPayload
{
    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;
}
