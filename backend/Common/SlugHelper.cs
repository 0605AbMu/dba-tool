namespace DbaTool.Api.Common;

public static class SlugHelper
{
    public static string Sanitize(string? raw, string fallback)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        var chars = raw.Trim().ToLowerInvariant()
            .Select(c => (char.IsLetterOrDigit(c) || c == '-' || c == '_') ? c : '-')
            .ToArray();

        var slug = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(slug) ? fallback : slug;
    }
}
