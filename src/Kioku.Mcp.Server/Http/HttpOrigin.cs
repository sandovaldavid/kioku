namespace Kioku.Mcp.Server.Http;

/// <summary>Parses and canonicalizes HTTP Origin header values for exact allowlist matching.</summary>
internal static class HttpOrigin
{
    public static bool TryNormalize(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            value.Equals("null", StringComparison.OrdinalIgnoreCase) ||
            value.Contains(',') ||
            !Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
            string.IsNullOrWhiteSpace(origin.Host) ||
            !string.IsNullOrEmpty(origin.UserInfo) ||
            !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment) ||
            (origin.AbsolutePath.Length > 0 && origin.AbsolutePath != "/"))
        {
            return false;
        }

        normalized = origin
            .GetComponents(UriComponents.SchemeAndServer, UriFormat.UriEscaped)
            .TrimEnd('/');
        return normalized.Length > 0;
    }
}
