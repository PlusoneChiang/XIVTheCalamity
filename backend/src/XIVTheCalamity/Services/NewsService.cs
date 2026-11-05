using System.Net;
using System.Text.RegularExpressions;
using XIVTheCalamity.DTOs;

namespace XIVTheCalamity.Services;

public sealed class NewsService(IHttpClientFactory httpClientFactory)
{
    private const string SourceUrl = "https://user-cdn.ffxiv.com.tw/news/251115/launcher_left.html";
    private const string MoreUrl = "https://www.ffxiv.com.tw/web/news/news_list.aspx";
    private static readonly string[] LinkHosts = ["www.ffxiv.com.tw", "event.ffxiv.com.tw"];
    private const string ImageHost = "file.ffxiv.com.tw";
    private static readonly Regex BannerRegex = new(
        """<div\s+[^>]*class\s*=\s*["'][^"']*swiper-slide[^"']*["'][^>]*>\s*<a\s+[^>]*href\s*=\s*["'](?<url>[^"']+)["'][^>]*>\s*<img\s+[^>]*src\s*=\s*["'](?<image>[^"']+)["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex ItemRegex = new(
        """<div\s+[^>]*class\s*=\s*["']item["'][^>]*>\s*<div\s+[^>]*class\s*=\s*["']publish_date["'][^>]*>(?<date>[^<]+)</div>.*?<div\s+[^>]*class\s*=\s*["'][^"']*type\s+(?<category>event|update|maintain|other)\b[^"']*["'][^>]*>.*?<div\s+[^>]*class\s*=\s*["'][^"']*title[^"']*["'][^>]*>(?<badges>.*?)<a\s+[^>]*href\s*=\s*["'](?<url>[^"']+)["'][^>]*>(?<title>.*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline, TimeSpan.FromSeconds(2));
    private static readonly Regex PinnedRegex = new(
        """<span\b[^>]*class\s*=\s*["'][^"']*\btop\b[^"']*["']""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
    private static readonly Regex TagRegex = new("<.*?>", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    public async Task<NewsResponse> GetNewsAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient("Announcements");
        client.Timeout = TimeSpan.FromSeconds(15);
        client.MaxResponseContentBufferSize = 1_000_000;
        client.DefaultRequestHeaders.UserAgent.ParseAdd("XIVTheCalamity/1.0");
        using var response = await client.GetAsync(SourceUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var banners = BannerRegex.Matches(html)
            .Select(m => new NewsBanner(WebUtility.HtmlDecode(m.Groups["image"].Value), WebUtility.HtmlDecode(m.Groups["url"].Value)))
            .Where(b => IsAllowedImage(b.ImageUrl) && IsAllowedLink(b.Url))
            .Take(10)
            .ToArray();

        var items = ItemRegex.Matches(html)
            .Select(m => new NewsItem(
                CategoryName(m.Groups["category"].Value),
                WebUtility.HtmlDecode(TagRegex.Replace(m.Groups["title"].Value, string.Empty)).Trim(),
                m.Groups["date"].Value.Trim(),
                WebUtility.HtmlDecode(m.Groups["url"].Value),
                PinnedRegex.IsMatch(m.Groups["badges"].Value)))
            .Where(i => i.Title.Length > 0 && IsAllowedLink(i.Url))
            .DistinctBy(i => i.Url, StringComparer.Ordinal)
            .Take(100)
            .ToArray();

        if (banners.Length == 0 || items.Length == 0)
            throw new InvalidDataException("官方公告資料不完整。");

        return new NewsResponse(banners, items, MoreUrl);
    }

    private static bool IsAllowedImage(string value) => IsHttpsHost(value, ImageHost);
    private static bool IsAllowedLink(string value) => LinkHosts.Any(host => IsHttpsHost(value, host));

    private static bool IsHttpsHost(string value, string host) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo);

    private static string CategoryName(string category) => category.ToLowerInvariant() switch
    {
        "event" => "活動",
        "update" => "更新",
        "maintain" => "維護",
        _ => "其他"
    };
}
