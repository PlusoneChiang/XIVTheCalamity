using System.Text.RegularExpressions;
using XIVTheCalamity.Api.NativeAOT.DTOs;
using XIVTheCalamity.Core.Exceptions;
using XIVTheCalamity.Game.Authentication;

namespace XIVTheCalamity.Api.NativeAOT.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth");

        // POST /api/auth/login
        group.MapPost("/login", async (
            LoginRequestDto request,
            TcAuthService authService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
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
                    return Results.BadRequest(ApiErrorResponse.Create("VALIDATION_FAILED", "Missing required fields"));
                }
                
                // Validate OTP format (6 digits)
                if (!Regex.IsMatch(request.Otp, @"^\d{6}$"))
                {
                    logger.LogWarning("Login request validation failed: invalid OTP format");
                    return Results.BadRequest(ApiErrorResponse.Create("AUTH_INVALID_OTP", "OTP must be 6 digits"));
                }
                
                var loginResult = await authService.LoginAsync(
                    request.Email,
                    request.Password,
                    request.Otp,
                    request.RecaptchaToken,
                    cancellationToken
                );
                
                logger.LogInformation("Login successful");
                
                return Results.Ok(ApiResponse<LoginData>.Ok(new LoginData
                {
                    SessionId = loginResult.SessionId,
                    Remain = loginResult.Remain,
                    SubscriptionType = loginResult.Sub
                }));
            }
            catch (AuthenticationException ex)
            {
                logger.LogWarning("Authentication failed: {Message}", ex.Message);
                return Results.Json(ApiErrorResponse.Create("AUTH_FAILED", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 401);
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex, "Network error during login");
                return Results.Json(ApiErrorResponse.Create("NETWORK_ERROR", "Unable to connect to authentication server"), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 503);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Login error");
                return Results.Json(ApiErrorResponse.Create("INTERNAL_ERROR", "An error occurred during login", ex.Message), 
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 500);
            }
        });
    }
}
