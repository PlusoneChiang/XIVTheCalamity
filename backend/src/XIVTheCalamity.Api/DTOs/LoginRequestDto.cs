namespace XIVTheCalamity.Api.DTOs;

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
