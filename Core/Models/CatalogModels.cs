namespace BFGDL.NET.Models;

/// <summary>
/// Lightweight game data used in the catalog list view.
/// Populated from the catalog GraphQL query.
/// </summary>
public record CatalogGameSummary
{
    public required string WrapId { get; init; }
    public required string Name { get; init; }
    public required string UrlKey { get; init; }
    public required string FolderName { get; init; }
    public required string ThumbnailUrl { get; init; }
    public required string ShortDescription { get; init; }
    public required IReadOnlyList<string> Genres { get; init; }
    public required DateTimeOffset ReleaseDate { get; init; }
    public required Platform Platform { get; init; }
    public required Language Language { get; init; }
    public long FileSizeBytes { get; init; }
    public bool IsCollectorsEdition { get; init; }
}

/// <summary>
/// Full game data used in the detail panel.
/// Populated from a per-SKU GraphQL query.
/// </summary>
public sealed record CatalogGameDetail : CatalogGameSummary
{
    public required string FullDescriptionHtml { get; init; }
    public required string HeroImageUrl { get; init; }
    public required string FeatureImageUrl { get; init; }
    public required IReadOnlyList<string> ScreenshotUrls { get; init; }
    public required IReadOnlyList<string> BulletPoints { get; init; }
    public string? PreviewVideoUrl { get; init; }
    public GameSystemRequirements? SystemRequirements { get; init; }
}

/// <summary>PC/Mac system requirements for a game.</summary>
public sealed record GameSystemRequirements
{
    public string? Os { get; init; }
    public string? Cpu { get; init; }
    public string? Ram { get; init; }
    public string? DirectX { get; init; }
    public string? HardDrive { get; init; }
}

/// <summary>
/// Serialisable envelope for a cached catalog page.
/// Mirrors <c>BigFishCatalogClient.CatalogPageSummary</c> but lives in Models
/// so the source-gen serialiser context can register it.
/// </summary>
public sealed record CachedPageData(
    IReadOnlyList<CatalogGameSummary> Items,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Progress snapshot reported by <see cref="BFGDL.NET.Services.CatalogFetchService"/>
/// after each completed page.  Consumed by the GUI Cache Manager panel via
/// <see cref="IProgress{T}"/>; the CLI passes <c>null</c> and uses Console output instead.
/// </summary>
public sealed record CatalogFetchProgress
{
    public required Language Language { get; init; }
    public required int Page { get; init; }
    public required int TotalPages { get; init; }
    public required int GamesNew { get; init; }
    public required int GamesSkipped { get; init; }
    public required int GamesFailed { get; init; }
    public required int ImagesNew { get; init; }
    public required int ImagesOnDisk { get; init; }
    public required int ImagesFailed { get; init; }
    /// <summary>True on the final report for this language.</summary>
    public required bool IsComplete { get; init; }
}

/// <summary>Progress update for a single downloading segment.</summary>
public sealed record DownloadSegmentProgress
{
    public required string GameName { get; init; }
    public required string SegmentFileName { get; init; }
    public required DownloadSegmentStatus Status { get; init; }
    public required int SegmentsCompleted { get; init; }
    public required int SegmentsTotal { get; init; }
}

public enum DownloadSegmentStatus
{
    Starting,
    Completed,
    Failed
}
