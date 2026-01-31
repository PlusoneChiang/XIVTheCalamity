namespace XIVTheCalamity.Core.Models;

/// <summary>
/// Login result containing session ID and subscription information
/// </summary>
public record LoginResult(string SessionId, int Remain, int Sub);
