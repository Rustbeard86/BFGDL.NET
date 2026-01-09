namespace BFGDL.NET.Services;

public sealed record InstallerListExportGame
{
    public required string WrapId { get; init; }
    public required string GameId { get; init; }
    public required string Name { get; init; }
    public required int SegmentCount { get; init; }
    public required IReadOnlyList<InstallerListExportSegment> Segments { get; init; }
}

public sealed record InstallerListExportSegment
{
    public required string Url { get; init; }
    public required string FileName { get; init; }
    public required string UrlName { get; init; }
}

public sealed record InstallerListExportMetadata
{
    public required string Platform { get; init; }
    public required string ExportFormat { get; init; }

    public required DateTimeOffset GeneratedAtUtc { get; init; }
    public DateTimeOffset? StartedAtUtc { get; init; }
    public DateTimeOffset? FinishedAtUtc { get; init; }
    public double? DurationSeconds { get; init; }

    public required int PageSize { get; init; }

    public required IReadOnlyDictionary<string, int> PagesParsedByLanguageL { get; init; }
    public required IReadOnlyDictionary<string, int> WrapIdsFoundByLanguageL { get; init; }
    public required IReadOnlyDictionary<string, int> GamesExportedByLanguageL { get; init; }
    public IReadOnlyDictionary<string, int>? CatalogTotalPagesByLanguageL { get; init; }
    public IReadOnlyDictionary<string, int>? CatalogTotalCountByLanguageL { get; init; }

    public required int TotalWrapIdsFound { get; init; }
    public required int TotalGamesExported { get; init; }
    public required int TotalSegmentsExported { get; init; }
    public required int FailedGames { get; init; }

    public IReadOnlyList<InstallerListExportFailure>? Failures { get; init; }

    public required int Jobs { get; init; }
    public required int? ExportLimit { get; init; }
}

public sealed record InstallerListExportFailure
{
    public required string WrapId { get; init; }
    public required string ExceptionType { get; init; }
    public required string Message { get; init; }
}