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

    [JsonPropertyName("short_description")] public GraphQlHtmlContent? ShortDescription { get; init; }

    [JsonPropertyName("description")] public GraphQlHtmlContent? Description { get; init; }

    public List<GraphQlCategory>? Categories { get; init; }

    [JsonPropertyName("custom_attributes")] public List<GraphQlCustomAttribute>? CustomAttributes { get; init; }
}

internal sealed class GraphQlHtmlContent
{
    public string? Html { get; init; }
}

internal sealed class GraphQlCategory
{
    public string? Uid { get; init; }
    public string? Name { get; init; }

    [JsonPropertyName("url_key")] public string? UrlKey { get; init; }
}

internal sealed class GraphQlCustomAttribute
{
    [JsonPropertyName("attribute_metadata")] public GraphQlAttributeMetadata? AttributeMetadata { get; init; }

    [JsonPropertyName("entered_attribute_value")] public GraphQlAttributeValue? EnteredAttributeValue { get; init; }
}

internal sealed class GraphQlAttributeMetadata
{
    public string? Code { get; init; }
}

internal sealed class GraphQlAttributeValue
{
    public string? Value { get; init; }
}

// GraphQL Query Variables Types
internal sealed class GraphQlVariables
{
    [JsonPropertyName("currentPage")] public int CurrentPage { get; init; }

    [JsonPropertyName("id")] public required string Id { get; init; }

    [JsonPropertyName("filters")] public required GraphQlFilters Filters { get; init; }

    [JsonPropertyName("pageSize")] public int PageSize { get; init; }

    [JsonPropertyName("sort")] public required GraphQlSort Sort { get; init; }
}

internal sealed class GraphQlFilters
{
    [JsonPropertyName("platform")] public required GraphQlFilter Platform { get; init; }

    [JsonPropertyName("language")] public required GraphQlFilter Language { get; init; }

    [JsonPropertyName("category_uid")] public required GraphQlFilter CategoryUid { get; init; }
}

internal sealed class GraphQlFilter
{
    [JsonPropertyName("eq")] public required string Eq { get; init; }
}

internal sealed class GraphQlSort
{
    [JsonPropertyName("product_list_date")] public required string ProductListDate { get; init; }
}

// Variables for the per-SKU product detail query
internal sealed class GraphQlSkuVariables
{
    [JsonPropertyName("sku")] public required string Sku { get; init; }
}
