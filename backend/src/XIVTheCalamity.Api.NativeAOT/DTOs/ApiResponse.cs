namespace XIVTheCalamity.Api.NativeAOT.DTOs;

/// <summary>
/// Standard API success response wrapper
/// </summary>
/// <typeparam name="T">Response data type</typeparam>
public class ApiResponse<T>
{
    public bool Success { get; init; } = true;
    public T? Data { get; init; }
    
    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };
}

/// <summary>
/// Standard API error response
/// </summary>
public class ApiErrorResponse
{
    public bool Success { get; init; } = false;
    public ErrorDetails Error { get; init; } = null!;
    
    public static ApiErrorResponse Create(string code, string message, object? details = null) => new()
    {
        Success = false,
        Error = new ErrorDetails { Code = code, Message = message, Details = details }
    };
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
