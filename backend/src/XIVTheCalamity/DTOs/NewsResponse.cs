namespace XIVTheCalamity.DTOs;

public record NewsBanner(string ImageUrl, string Url);
public record NewsItem(string Category, string Title, string Date, string Url, bool IsPinned);
public record NewsResponse(NewsBanner[] Banners, NewsItem[] Items, string MoreUrl);
