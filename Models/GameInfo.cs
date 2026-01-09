using System.Text.RegularExpressions;

namespace BFGDL.NET.Models;

public sealed record GameInfo
{
    public required string WrapId
    {
        get;
        init => field = value.ToUpperInvariant();
    }

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<DownloadSegment> Segments { get; init; }

    public string SanitizedDisplayName => $"{Id} - {Name}";
}

public sealed record DownloadSegment
{
    public required string FileName { get; init; }
    public required string UrlName { get; init; }

    public required string DownloadUrl
    {
        get;
        init => field = value.TrimEnd('/') + "/";
    }

    public string FullUrl => $"{DownloadUrl}{UrlName}";
}

public sealed partial record WrapId
{
    private const string Pattern = @"F\d+T\dL\d";

    public required string Value
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            var match = MatchRegex().Match(value);
            if (!match.Success)
                throw new ArgumentException(
                    $"Invalid WrapID format: {value}. Expected format: F<number>T<number>L<number>", nameof(value));
            field = match.Value.ToUpperInvariant();
        }
    }

    public static WrapId? TryParse(string input)
    {
        try
        {
            return new WrapId { Value = input };
        }
        catch
        {
            return null;
        }
    }

    public static IEnumerable<WrapId> ExtractAll(string input)
    {
        var matches = MatchesRegex().Matches(input);
        foreach (Match match in matches)
        {
            var wrapId = TryParse(match.Value);
            if (wrapId is not null) yield return wrapId;
        }
    }

    public bool MatchesFilter(string filter)
    {
        return Value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(Pattern, RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MatchRegex();

    [GeneratedRegex(Pattern, RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex MatchesRegex();
}