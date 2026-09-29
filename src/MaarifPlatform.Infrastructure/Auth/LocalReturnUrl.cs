namespace MaarifPlatform.Infrastructure.Auth;

public static class LocalReturnUrl
{
    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url[0] != '/' ||
            url.StartsWith("//", StringComparison.Ordinal) ||
            url.Any(c => c == '\\' || char.IsControl(c))) return "/";
        // Reject encoded separators too: proxies/browsers can decode them differently.
        var decoded = Uri.UnescapeDataString(url);
        return decoded.StartsWith("//", StringComparison.Ordinal) ||
            decoded.Any(c => c == '\\' || char.IsControl(c)) ? "/" : url;
    }
}
