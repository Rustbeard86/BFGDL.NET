using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using BFGDL.NET.Models;

namespace BFGDL.NET.Services;

public sealed class BigFishCatalogClient(HttpClient httpClient)
{
    private const string BaseUrl = "https://www.bigfishgames.com/graphql";
    private const string OperationName = "GetCategories";

    // Matches the web catalog query shape currently used by bigfishgames.com
    private const string Query =
        "query GetCategories($id:String!$pageSize:Int!$currentPage:Int!$filters:ProductAttributeFilterInput!$sort:ProductAttributeSortInput){categories(filters:{category_uid:{in:[$id]}}){items{uid ...CategoryFragmentExtended __typename}__typename}products(pageSize:$pageSize currentPage:$currentPage filter:$filters sort:$sort){...ProductsFragmentExtended __typename}}fragment CategoryFragmentExtended on CategoryTree{...CategoryFragment url_key __typename}fragment CategoryFragment on CategoryTree{uid meta_title meta_keywords meta_description __typename}fragment ProductsFragmentExtended on Products{items{id uid name product_list_date sku platform language url_key __typename}page_info{total_pages __typename}total_count __typename}";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<CatalogPage> GetCatalogPageAsync(
        Platform platform,
        string languageId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(page, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var platformId = platform == Platform.Mac ? "153" : "150";

        // CategoryUid for the main games listing. This matches the provided sample.
        const string categoryUid = "MTg=";

        var variables = new
        {
            currentPage = page,
            id = categoryUid,
            filters = new
            {
                platform = new { eq = platformId },
                language = new { eq = languageId },
                category_uid = new { eq = categoryUid }
            },
            pageSize,
            sort = new { product_list_date = "DESC" }
        };

        var queryParams = HttpUtility.ParseQueryString(string.Empty);
        queryParams["query"] = Query;
        queryParams["operationName"] = OperationName;
        queryParams["variables"] = JsonSerializer.Serialize(variables, JsonOptions);

        var url = $"{BaseUrl}?{queryParams}";

        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/json");

        using var res = await httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        res.EnsureSuccessStatusCode();

        await using var stream = await res.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var body = await JsonSerializer.DeserializeAsync<GraphQlResponse>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        var products = body?.Data?.Products;
        if (products is null)
            return new CatalogPage([], 0, 0);

        var items = products.Items ?? [];
        var wrapIds = new List<string>(items.Count);

        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.Sku))
            {
                var id = WrapId.TryParse(item.Sku);
                if (id is not null)
                {
                    wrapIds.Add(id.Value);
                    continue;
                }
            }

            // Fallbacks: url_key sometimes includes the wrapId suffix
            if (!string.IsNullOrWhiteSpace(item.UrlKey))
            {
                var extracted = WrapId.ExtractAll(item.UrlKey).Select(w => w.Value).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(extracted))
                {
                    wrapIds.Add(extracted);
                    continue;
                }
            }

            // Last resort: attempt extraction from uid (rarely useful)
            if (!string.IsNullOrWhiteSpace(item.Uid))
            {
                var extracted = WrapId.ExtractAll(item.Uid).Select(w => w.Value).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(extracted))
                    wrapIds.Add(extracted);
            }
        }

        return new CatalogPage(wrapIds, products.TotalCount, products.PageInfo?.TotalPages ?? 0);
    }

    public sealed record CatalogPage(IReadOnlyList<string> WrapIds, int TotalCount, int TotalPages);

    private sealed class GraphQlResponse
    {
        public GraphQlData? Data { get; init; }
    }

    private sealed class GraphQlData
    {
        public Products? Products { get; init; }
    }

    private sealed class Products
    {
        public List<ProductItem>? Items { get; init; }

        [JsonPropertyName("total_count")] public int TotalCount { get; init; }

        [JsonPropertyName("page_info")] public PageInfo? PageInfo { get; init; }
    }

    private sealed class PageInfo
    {
        [JsonPropertyName("total_pages")] public int TotalPages { get; init; }
    }

    private sealed class ProductItem
    {
        public string Uid { get; } = string.Empty;

        public string? Name { get; init; }

        public int? Platform { get; init; }

        public int? Language { get; init; }

        [JsonPropertyName("product_list_date")]
        public string? ProductListDate { get; init; }

        public string? Sku { get; init; }

        [JsonPropertyName("url_key")] public string? UrlKey { get; init; }
    }
}