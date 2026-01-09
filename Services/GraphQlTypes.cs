using System.Text.Json.Serialization;

namespace BFGDL.NET.Services;

internal sealed class GraphQlResponse
{
    public GraphQlData? Data { get; init; }
}

internal sealed class GraphQlData
{
    public GraphQlProducts? Products { get; init; }
}

internal sealed class GraphQlProducts
{
    public List<GraphQlProductItem>? Items { get; init; }

    [JsonPropertyName("total_count")] public int TotalCount { get; init; }

    [JsonPropertyName("page_info")] public GraphQlPageInfo? PageInfo { get; init; }
}

internal sealed class GraphQlPageInfo
{
    [JsonPropertyName("total_pages")] public int TotalPages { get; init; }
}

internal sealed class GraphQlProductItem
{
    public string Uid { get; init; } = string.Empty;

    public string? Name { get; init; }

    public int? Platform { get; init; }

    public int? Language { get; init; }

    [JsonPropertyName("product_list_date")]
    public string? ProductListDate { get; init; }

    public string? Sku { get; init; }

    [JsonPropertyName("url_key")] public string? UrlKey { get; init; }
}
