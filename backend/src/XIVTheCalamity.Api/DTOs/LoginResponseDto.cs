namespace XIVTheCalamity.Api.DTOs;

/// <summary>
/// Response DTO for login endpoint
/// </summary>
public record LoginResponseDto
{
    /// <summary>
    /// Whether login was successful
    /// </summary>
    public bool Success { get; init; }
    
    /// <summary>
    /// Session ID if login successful
    /// </summary>
    public string? SessionId { get; init; }
    
    /// <summary>
    /// Remaining game time in seconds
    /// </summary>
    public int? Remain { get; init; }
    
    /// <summary>
    /// Subscription type: 1 = Crystal, 2 = Credit Card
    /// </summary>
    public int? SubscriptionType { get; init; }
    
    /// <summary>
    /// Message describing the result
    /// </summary>
    public string? Message { get; init; }
    
    /// <summary>
    /// Error code if login failed
    /// </summary>
    public string? ErrorCode { get; init; }
}
