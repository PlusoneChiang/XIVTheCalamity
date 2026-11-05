using XIVTheCalamity.DTOs;
using XIVTheCalamity.Services;

namespace XIVTheCalamity.Endpoints;

public static class NewsEndpoints
{
    public static void MapNewsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/news", async (NewsService newsService, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Json(ApiResponse<NewsResponse>.Ok(await newsService.GetNewsAsync(cancellationToken)),
                    AppJsonContext.Default.ApiResponseNewsResponse);
            }
            catch (Exception ex)
            {
                return Results.Json(ApiErrorResponse.Create("NEWS_UNAVAILABLE", "無法載入本機公告資料。", ex.Message),
                    AppJsonContext.Default.ApiErrorResponse, statusCode: 502);
            }
        });
    }
}
