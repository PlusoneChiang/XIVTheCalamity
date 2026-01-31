using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.DTOs;
using XIVTheCalamity.Api.Extensions;
using XIVTheCalamity.Api.Models;
using XIVTheCalamity.Core.Exceptions;
using XIVTheCalamity.Game.Authentication;

namespace XIVTheCalamity.Api.Controllers;

/// <summary>
/// Authentication controller for handling login and session management
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController(
    TcAuthService authService,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>
    /// Login endpoint
    /// </summary>
    /// <param name="request">Login request with HEX encoded credentials</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Login response with session ID</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(ApiResponse<LoginData>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequestDto request,
        CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Login request received");
            
            // Validate request
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.Otp) ||
                string.IsNullOrWhiteSpace(request.RecaptchaToken))
            {
                logger.LogWarning("Login request validation failed: missing required fields");
                return this.BadRequestError("VALIDATION_FAILED", "Missing required fields");
            }
            
            // Validate OTP format (6 digits)
            if (!System.Text.RegularExpressions.Regex.IsMatch(request.Otp, @"^\d{6}$"))
            {
                logger.LogWarning("Login request validation failed: invalid OTP format");
                return this.BadRequestError("AUTH_INVALID_OTP", "OTP must be 6 digits");
            }
            
            // Backend receives HEX encoded email/password from frontend
            // Call TcAuthService which expects HEX encoded credentials
            var loginResult = await authService.LoginAsync(
                request.Email,         // Already HEX encoded
                request.Password,      // Already HEX encoded
                request.Otp,
                request.RecaptchaToken,
                cancellationToken
            );
            
            logger.LogInformation("Login successful");
            
            return this.SuccessResult(new LoginData
            {
                SessionId = loginResult.SessionId,
                Remain = loginResult.Remain,
                SubscriptionType = loginResult.Sub
            });
        }
        catch (AuthenticationException ex)
        {
            logger.LogWarning("Authentication failed: {Message}", ex.Message);
            return this.UnauthorizedError("AUTH_FAILED", ex.Message);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "Network error during login");
            return this.ErrorResult(
                StatusCodes.Status503ServiceUnavailable,
                "NETWORK_ERROR",
                "Unable to connect to authentication server"
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login error");
            return this.InternalError("An error occurred during login", ex.Message);
        }
    }
}
