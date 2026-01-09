using System.Text.RegularExpressions;
using System.Web;

namespace BFGDL.NET.Services;

public static partial class HtmlSanitizer
{
    [GeneratedRegex(@"[\\/:*?""<>|]+")]
    private static partial Regex InvalidFileNameCharsRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    public static string SanitizeForFileName(string input)
    {
        // Decode HTML entities
        var decoded = HttpUtility.HtmlDecode(input);

        // Prefer a readable separator for subtitles
        decoded = decoded.Replace(":", " - ");

        // Avoid awkward underscores for apostrophes in folder names
        decoded = decoded.Replace("'", string.Empty);

        // Replace invalid filename characters with underscore
        var sanitized = InvalidFileNameCharsRegex().Replace(decoded, "_");

        // Collapse whitespace introduced by replacements
        sanitized = WhitespaceRegex().Replace(sanitized, " ").Trim();

        return sanitized;
    }
}