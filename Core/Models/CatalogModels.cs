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
