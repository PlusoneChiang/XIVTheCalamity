namespace XIVTheCalamity.Api.NativeAOT.DTOs;

/// <summary>
/// Request DTO for login endpoint
/// Email and password are already HEX encoded by frontend
/// </summary>
public record LoginRequestDto
{
    /// <summary>
    /// HEX encoded email address
    /// </summary>
    public required string Email { get; init; }
    
    /// <summary>
    /// HEX encoded password
    /// </summary>
    public required string Password { get; init; }
    
    /// <summary>
    /// 6-digit OTP code
    /// </summary>
    public required string Otp { get; init; }
    
    /// <summary>
    /// reCAPTCHA Enterprise token
    /// </summary>
    public required string RecaptchaToken { get; init; }
}

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

/// <summary>
/// Request for game launch
/// </summary>
public record LaunchRequest
{
    public string SessionId { get; init; } = string.Empty;
}

/// <summary>
/// Request for update check
/// </summary>
public record CheckUpdateRequest
{
    public string GamePath { get; init; } = string.Empty;
}
