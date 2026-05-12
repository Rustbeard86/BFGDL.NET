using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

public sealed class BigFishCatalogClient(HttpClient httpClient)
{
    private const string BaseUrl = "https://www.bigfishgames.com/graphql";
    private const string OperationName = "GetCategories";

    // Structural category names / url_key suffixes to exclude when extracting genres
    private static readonly HashSet<string> StructuralCategoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Games", "Standard Edition Games", "Tomorrow's Game Today",
        "Developers", "Secondary Genres", "Collector's Edition Games",
        "Exclusive Access", "New Games", "Top Sellers"
    };

    // Catalog list query — includes description + custom_attributes so no separate detail request is needed
    private const string ListQuery =
        "query GetCategories($id:String!$pageSize:Int!$currentPage:Int!$filters:ProductAttributeFilterInput!$sort:ProductAttributeSortInput){categories(filters:{category_uid:{in:[$id]}}){items{uid __typename}__typename}products(pageSize:$pageSize currentPage:$currentPage filter:$filters sort:$sort){items{id uid name product_list_date sku platform language url_key short_description{html}description{html}categories{uid name url_key}custom_attributes{attribute_metadata{code}entered_attribute_value{value}}__typename}page_info{total_pages __typename}total_count __typename}}";

    // Detail query — fetches full description + all custom_attributes by SKU
    private const string DetailQuery =
        "query GetProductDetail($sku:String!){products(filter:{sku:{eq:$sku}}){items{id uid name sku url_key description{html}short_description{html}categories{uid name url_key}custom_attributes{attribute_metadata{code}entered_attribute_value{value}}__typename}}}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = AppJsonSerializerContext.Default
    };

    // ── Catalog paged list (WrapIDs only — used by CLI InstallerListExporter) ─────

    public async Task<CatalogPage> GetCatalogPageAsync(
        Platform platform,
        string languageId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await GetCatalogPageCoreAsync(platform, languageId, page, pageSize, cancellationToken);
        var wrapIds = result.Items
            .Select(item => ExtractWrapIdString(item))
            .Where(w => w is not null)
            .Select(w => w!)
            .ToList();
        return new CatalogPage(wrapIds, result.TotalCount, result.TotalPages);
    }

    private static string? ExtractWrapIdString(GraphQlProductItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Sku))
        {
            var id = WrapId.TryParse(item.Sku);
            if (id is not null) return id.Value;
        }
        if (!string.IsNullOrWhiteSpace(item.UrlKey))
        {
            var v = WrapId.ExtractAll(item.UrlKey).Select(w => w.Value).FirstOrDefault();
            if (v is not null) return v;
        }
        if (!string.IsNullOrWhiteSpace(item.Uid))
            return WrapId.ExtractAll(item.Uid).Select(w => w.Value).FirstOrDefault();
        return null;
    }

    // ── Catalog paged list with full summary (used by GUI browser) ───────────────

    public async Task<CatalogPageSummary> GetCatalogPageWithSummaryAsync(
        Platform platform,
        Language language,
        string languageId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await GetCatalogPageCoreAsync(platform, languageId, page, pageSize, cancellationToken);
        var summaries = result.Items
            .Select(item => MapToSummary(item, platform, language))
            .Where(s => s is not null)
            .Select(s => s!)
            .ToList();
        return new CatalogPageSummary(summaries, result.TotalCount, result.TotalPages);
    }

    // ── Single-game detail (used by GUI detail panel) ────────────────────────────

    public async Task<CatalogGameDetail?> GetProductDetailAsync(
        string sku,
        Platform platform,
        Language language,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);

        var variables = new GraphQlSkuVariables { Sku = sku };
        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["query"] = DetailQuery;
        queryParams["operationName"] = "GetProductDetail";
        queryParams["variables"] = JsonSerializer.Serialize(variables, AppJsonSerializerContext.Default.GraphQlSkuVariables);

        var url = $"{BaseUrl}?{queryParams}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");

        using var res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var body = await JsonSerializer.DeserializeAsync(stream, AppJsonSerializerContext.Default.GraphQlResponse, cancellationToken)
            .ConfigureAwait(false);

        var item = body?.Data?.Products?.Items?.FirstOrDefault();
        if (item is null) return null;
        return MapToDetail(item, platform, language);
    }

    // ── Catalog paged list with full detail (used by CatalogFetchService) ────────

    public async Task<(List<CatalogGameDetail> Details, int TotalCount, int TotalPages)> GetCatalogPageWithDetailAsync(
        Platform platform,
        Language language,
        string languageId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var result = await GetCatalogPageCoreAsync(platform, languageId, page, pageSize, cancellationToken)
            .ConfigureAwait(false);
        var details = result.Items
            .Select(item => MapToDetail(item, platform, language))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();
        return (details, result.TotalCount, result.TotalPages);
    }

    private static CatalogGameDetail? MapToDetail(GraphQlProductItem item, Platform platform, Language language)
    {
        var summary = MapToSummary(item, platform, language);
        if (summary is null) return null;

        var attrs = BuildAttributeMap(item.CustomAttributes);

        var screenshots = new List<string>(3);
        for (var i = 1; i <= 5; i++)
        {
            var url2 = attrs.GetValueOrDefault($"screenshot_{i}_url");
            if (!string.IsNullOrWhiteSpace(url2)) screenshots.Add(url2);
        }

        var bullets = new List<string>(5);
        for (var i = 1; i <= 5; i++)
        {
            var b = attrs.GetValueOrDefault($"bullet_{i}");
            if (!string.IsNullOrWhiteSpace(b)) bullets.Add(StripHtml(b));
        }

        var sysReq = new GameSystemRequirements
        {
            Os = attrs.GetValueOrDefault("sys_req_os"),
            Cpu = attrs.GetValueOrDefault("sys_req_mhz"),
            Ram = FormatRam(attrs.GetValueOrDefault("sys_req_mem")),
            DirectX = FormatDirectX(attrs.GetValueOrDefault("sys_req_dx")),
            HardDrive = FormatHardDrive(attrs.GetValueOrDefault("sys_req_hd"))
        };

        var videoUrl = attrs.GetValueOrDefault("preview_video_url");
        if (!string.IsNullOrWhiteSpace(videoUrl) && videoUrl.StartsWith("//"))
            videoUrl = "https:" + videoUrl;

        return new CatalogGameDetail
        {
            WrapId = summary.WrapId,
            Name = summary.Name,
            UrlKey = summary.UrlKey,
            FolderName = summary.FolderName,
            ThumbnailUrl = summary.ThumbnailUrl,
            ShortDescription = summary.ShortDescription,
            Genres = summary.Genres,
            ReleaseDate = summary.ReleaseDate,
            Platform = summary.Platform,
            Language = summary.Language,
            FileSizeBytes = summary.FileSizeBytes,
            IsCollectorsEdition = summary.IsCollectorsEdition,
            FullDescriptionHtml = item.Description?.Html ?? string.Empty,
            HeroImageUrl = attrs.GetValueOrDefault("image_460x230_url") ?? string.Empty,
            FeatureImageUrl = attrs.GetValueOrDefault("image_feature_url") ?? string.Empty,
            ScreenshotUrls = screenshots,
            BulletPoints = bullets,
            PreviewVideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl,
            SystemRequirements = sysReq
        };
    }

    // ── Shared internal helpers ───────────────────────────────────────────────────

    private async Task<(List<GraphQlProductItem> Items, int TotalCount, int TotalPages)> GetCatalogPageCoreAsync(
        Platform platform,
        string languageId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var platformId = platform == Platform.Mac ? "153" : "150";
        const string categoryUid = "MTg=";

        var variables = new GraphQlVariables
        {
            CurrentPage = page,
            Id = categoryUid,
            Filters = new GraphQlFilters
            {
                Platform = new GraphQlFilter { Eq = platformId },
                Language = new GraphQlFilter { Eq = languageId },
                CategoryUid = new GraphQlFilter { Eq = categoryUid }
            },
            PageSize = pageSize,
            Sort = new GraphQlSort { ProductListDate = "DESC" }
        };

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["query"] = ListQuery;
        queryParams["operationName"] = OperationName;
        queryParams["variables"] = JsonSerializer.Serialize(variables, AppJsonSerializerContext.Default.GraphQlVariables);

        var url = $"{BaseUrl}?{queryParams}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");

        using var res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var body = await JsonSerializer.DeserializeAsync(stream, AppJsonSerializerContext.Default.GraphQlResponse, cancellationToken)
            .ConfigureAwait(false);

        var products = body?.Data?.Products;
        if (products is null) return ([], 0, 0);

        return (products.Items ?? [], products.TotalCount, products.PageInfo?.TotalPages ?? 0);
    }

    private static CatalogGameSummary? MapToSummary(GraphQlProductItem item, Platform platform, Language language)
    {
        var wrapIdStr = ExtractWrapIdString(item);
        if (wrapIdStr is null) return null;

        var attrs = BuildAttributeMap(item.CustomAttributes);

        var genres = (item.Categories ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.Name) && IsGenreCategory(c))
            .Select(c => c.Name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var releaseDate = DateTimeOffset.TryParse(item.ProductListDate, out var dt) ? dt : DateTimeOffset.MinValue;

        var thumbnail = attrs.GetValueOrDefault("image_80x80_url") ?? string.Empty;
        var folderName = attrs.GetValueOrDefault("folder_name") ?? string.Empty;
        var fileSizeStr = attrs.GetValueOrDefault("file_size");
        var fileSize = long.TryParse(fileSizeStr, out var fs) ? fs : 0L;
        var isCollectors = !string.IsNullOrWhiteSpace(attrs.GetValueOrDefault("is_collectors_edition"));

        return new CatalogGameSummary
        {
            WrapId = wrapIdStr,
            Name = item.Name ?? string.Empty,
            UrlKey = item.UrlKey ?? string.Empty,
            FolderName = folderName,
            ThumbnailUrl = thumbnail,
            ShortDescription = StripHtml(item.ShortDescription?.Html ?? string.Empty),
            Genres = genres,
            ReleaseDate = releaseDate,
            Platform = platform,
            Language = language,
            FileSizeBytes = fileSize,
            IsCollectorsEdition = isCollectors
        };
    }

    private static bool IsGenreCategory(GraphQlCategory c)
    {
        if (c.Name is null) return false;
        if (StructuralCategoryNames.Contains(c.Name)) return false;
        var key = c.UrlKey ?? string.Empty;
        // Exclude "pc-xxx-games" / "mac-xxx-games" platform-specific duplicates
        if (key.StartsWith("pc-", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("mac-", StringComparison.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static Dictionary<string, string> BuildAttributeMap(List<GraphQlCustomAttribute>? attrs)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attrs is null) return map;
        foreach (var a in attrs)
        {
            var code = a.AttributeMetadata?.Code;
            var value = a.EnteredAttributeValue?.Value;
            if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(value))
                map[code] = value;
        }
        return map;
    }

    private static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;
        return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty)
            .Replace("&mdash;", "—").Replace("&ndash;", "–").Replace("&amp;", "&")
            .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&nbsp;", " ")
            .Replace("\r\n", "\n").Trim();
    }

    private static string? FormatRam(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return long.TryParse(value, out var mb) ? $"{mb} MB" : value;
    }

    private static string? FormatDirectX(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return double.TryParse(value, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var d)
            ? ((int)d).ToString()
            : value;
    }

    private static string? FormatHardDrive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return long.TryParse(value, out var mb) ? $"{mb} MB" : value;
    }

    public sealed record CatalogPage(IReadOnlyList<string> WrapIds, int TotalCount, int TotalPages);

    public sealed record CatalogPageSummary(IReadOnlyList<CatalogGameSummary> Items, int TotalCount, int TotalPages);
}