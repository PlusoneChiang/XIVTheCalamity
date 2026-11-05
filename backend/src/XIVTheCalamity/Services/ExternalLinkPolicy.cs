namespace XIVTheCalamity.Services;

internal static class ExternalLinkPolicy
{
    internal static bool IsAllowed(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.IdnHost.Equals("ffxiv.com.tw", StringComparison.OrdinalIgnoreCase)
            || uri.IdnHost.EndsWith(".ffxiv.com.tw", StringComparison.OrdinalIgnoreCase));
}
