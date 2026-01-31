namespace XIVTheCalamity.Api.DTOs;

/// <summary>
/// Login success data
/// </summary>
public record LoginData
{
    /// <summary>
    /// Session ID for game login
    /// </summary>
    public string SessionId { get; init; } = null!;
    
    /// <summary>
    /// Remaining game time in seconds
    /// </summary>
    public int Remain { get; init; }
    
    /// <summary>
    /// Subscription type: 1 = Crystal, 2 = Credit Card
    /// </summary>
    public int SubscriptionType { get; init; }
}
