using Microsoft.AspNetCore.Mvc;
using XIVTheCalamity.Api.Models;

namespace XIVTheCalamity.Api.Extensions;

/// <summary>
/// Extension methods for standardized API responses
/// </summary>
public static class ControllerExtensions
{
    /// <summary>
    /// Return standardized success response
    /// </summary>
    public static IActionResult SuccessResult<T>(
        this ControllerBase controller, T data)
    {
        return controller.Ok(new ApiResponse<T> { Data = data });
    }
    
    /// <summary>
    /// Return standardized error response
    /// </summary>
    public static IActionResult ErrorResult(
        this ControllerBase controller,
        int statusCode,
        string errorCode,
        string message,
        object? details = null)
    {
        return controller.StatusCode(statusCode, new ApiErrorResponse
        {
            Error = new ErrorDetails
            {
                Code = errorCode,
                Message = message,
                Details = details
            }
        });
    }
    
    /// <summary>
    /// Return 400 Bad Request error
    /// </summary>
    public static IActionResult BadRequestError(
        this ControllerBase controller,
        string errorCode,
        string message,
        object? details = null)
    {
        return controller.ErrorResult(400, errorCode, message, details);
    }
    
    /// <summary>
    /// Return 401 Unauthorized error
    /// </summary>
    public static IActionResult UnauthorizedError(
        this ControllerBase controller,
        string errorCode,
        string message)
    {
        return controller.ErrorResult(401, errorCode, message);
    }
    
    /// <summary>
    /// Return 500 Internal Server Error
    /// </summary>
    public static IActionResult InternalError(
        this ControllerBase controller,
        string message,
        object? details = null)
    {
        return controller.ErrorResult(500, "INTERNAL_ERROR", message, details);
    }
}
