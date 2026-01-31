namespace XIVTheCalamity.Api.Models;

/// <summary>
/// Standard API success response wrapper
/// </summary>
/// <typeparam name="T">Response data type</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
}

/// <summary>
/// Standard API error response
/// </summary>
public class ApiErrorResponse
{
    public bool Success { get; init; } = false;
    public ErrorDetails Error { get; init; } = null!;
}

/// <summary>
/// Error details structure
/// </summary>
public class ErrorDetails
{
    public string Code { get; init; } = null!;
    public string Message { get; init; } = null!;
    public object? Details { get; init; }
}
