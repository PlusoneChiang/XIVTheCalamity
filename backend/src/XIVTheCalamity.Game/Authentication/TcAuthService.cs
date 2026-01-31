using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using XIVTheCalamity.Core.Exceptions;
using XIVTheCalamity.Core.Models;

namespace XIVTheCalamity.Game.Authentication;

/// <summary>
/// Taiwan region authentication service for FFXIV
/// Handles login and session management with Taiwan servers
/// </summary>
public class TcAuthService(
    HttpClient httpClient,
    ILogger<TcAuthService> logger)
{
    private const string LoginApiUrl = "https://user.ffxiv.com.tw/api/login/launcherLogin";
    private const string SessionApiUrl = "https://user.ffxiv.com.tw/api/login/launcherSession";
    
    /// <summary>
    /// Perform full login flow: get login token, then get session ID
    /// Note: email/password are already HEX encoded by frontend
    /// </summary>
    public async Task<LoginResult> LoginAsync(
        string emailHex, 
        string passwordHex, 
        string otp, 
        string recaptchaToken,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting Taiwan region login");
        
        // Step 1: Get login token
        var loginTokenResponse = await GetLoginTokenAsync(emailHex, passwordHex, otp, recaptchaToken, cancellationToken);
        logger.LogDebug("Login token obtained");
        
        // Step 2: Get session ID
        var sessionResponse = await GetSessionIdAsync(loginTokenResponse.Token, cancellationToken);
        logger.LogInformation("Login successful, session ID obtained");
        
        return new LoginResult(
            sessionResponse.SessionId, 
            loginTokenResponse.Remain,
            loginTokenResponse.Sub);
    }
    
    private async Task<LoginTokenResponse> GetLoginTokenAsync(
        string emailHex, 
        string passwordHex, 
        string otp, 
        string recaptchaToken,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            email = emailHex,      // Already HEX encoded
            password = passwordHex, // Already HEX encoded
            code = otp,
            token = recaptchaToken
        };
        
        logger.LogDebug("Calling launcherLogin API");
        var response = await httpClient.PostAsJsonAsync(LoginApiUrl, payload, cancellationToken);
        
        // Read raw JSON
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // Parse as dictionary to capture all fields
        var jsonDoc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawJson, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });
        
        if (!response.IsSuccessStatusCode || jsonDoc is null)
        {
            var errorMsg = ExtractErrorMessage(jsonDoc);
            logger.LogError("Login API failed: {StatusCode}, Error: {Error}", 
                response.StatusCode, errorMsg);
            throw new AuthenticationException(errorMsg ?? "Login failed");
        }
        
        // Check for error fields
        var errorMessage = ExtractErrorMessage(jsonDoc);
        if (!string.IsNullOrEmpty(errorMessage))
        {
            logger.LogError("Login API returned error: {Error}", errorMessage);
            throw new AuthenticationException(errorMessage);
        }
        
        // Extract token, remain, and sub
        if (!jsonDoc.TryGetValue("token", out var tokenElement) || tokenElement.ValueKind == JsonValueKind.Null)
        {
            logger.LogError("Login token is empty or null");
            throw new AuthenticationException("Login token is empty");
        }
        
        var token = tokenElement.GetString();
        var remain = jsonDoc.TryGetValue("remain", out var remainElement) ? remainElement.GetInt32() : 0;
        var sub = jsonDoc.TryGetValue("sub", out var subElement) ? subElement.GetInt32() : 0;
        
        return new LoginTokenResponse(token!, remain, sub);
    }
    
    private async Task<SessionResponse> GetSessionIdAsync(
        string loginToken,
        CancellationToken cancellationToken)
    {
        var payload = new { token = loginToken };
        
        logger.LogDebug("Calling launcherSession API");
        var response = await httpClient.PostAsJsonAsync(SessionApiUrl, payload, cancellationToken);
        
        // Read raw JSON
        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        
        // Parse as dictionary to capture all fields
        var jsonDoc = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(rawJson, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });
        
        if (!response.IsSuccessStatusCode || jsonDoc is null)
        {
            var errorMsg = ExtractErrorMessage(jsonDoc);
            logger.LogError("Session API failed: {StatusCode}, Error: {Error}", 
                response.StatusCode, errorMsg);
            throw new AuthenticationException(errorMsg ?? "Session retrieval failed");
        }
        
        // Check for error fields
        var errorMessage = ExtractErrorMessage(jsonDoc);
        if (!string.IsNullOrEmpty(errorMessage))
        {
            logger.LogError("Session API returned error: {Error}", errorMessage);
            throw new AuthenticationException(errorMessage);
        }
        
        // Extract session ID
        if (!jsonDoc.TryGetValue("sessionId", out var sessionElement) || sessionElement.ValueKind == JsonValueKind.Null)
        {
            logger.LogError("Session ID is empty");
            throw new AuthenticationException("Session ID is empty");
        }
        
        var sessionId = sessionElement.GetString();
        
        return new SessionResponse(sessionId!, null);
    }
    
    /// <summary>
    /// Extract error message from various possible error fields
    /// </summary>
    private static string? ExtractErrorMessage(Dictionary<string, JsonElement>? jsonDoc)
    {
        if (jsonDoc is null) return null;
        
        // Check common error field names
        var errorFields = new[] { "error", "errMsg", "errorMessage", "errorMsg", "err", "message" };
        
        foreach (var field in errorFields)
        {
            if (jsonDoc.TryGetValue(field, out var errorElement) && 
                errorElement.ValueKind == JsonValueKind.String)
            {
                var errorValue = errorElement.GetString();
                if (!string.IsNullOrWhiteSpace(errorValue))
                {
                    return errorValue;
                }
            }
        }
        
        return null;
    }
    
    private record LoginTokenResponse(string Token, int Remain, int Sub);
    private record SessionResponse(string SessionId, string? Error);
}
