using System.Text.Json.Serialization;
using BFGDL.NET.Services;

namespace BFGDL.NET.Models;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    GenerationMode = JsonSourceGenerationMode.Default)]
[JsonSerializable(typeof(GraphQlResponse))]
[JsonSerializable(typeof(GraphQlData))]
[JsonSerializable(typeof(GraphQlProducts))]
[JsonSerializable(typeof(GraphQlPageInfo))]
[JsonSerializable(typeof(GraphQlProductItem))]
[JsonSerializable(typeof(GraphQlHtmlContent))]
[JsonSerializable(typeof(GraphQlCategory))]
[JsonSerializable(typeof(GraphQlCustomAttribute))]
[JsonSerializable(typeof(GraphQlAttributeMetadata))]
[JsonSerializable(typeof(GraphQlAttributeValue))]
[JsonSerializable(typeof(GraphQlVariables))]
[JsonSerializable(typeof(GraphQlFilters))]
[JsonSerializable(typeof(GraphQlFilter))]
[JsonSerializable(typeof(GraphQlSort))]
[JsonSerializable(typeof(GraphQlSkuVariables))]
[JsonSerializable(typeof(InstallerListExportMetadata))]
[JsonSerializable(typeof(InstallerListExportFailure))]
// Cache serialisation
[JsonSerializable(typeof(CatalogGameSummary))]
[JsonSerializable(typeof(CatalogGameDetail))]
[JsonSerializable(typeof(GameSystemRequirements))]
[JsonSerializable(typeof(CachedPageData))]
[JsonSerializable(typeof(List<CatalogGameSummary>))]
[JsonSerializable(typeof(List<string>))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
