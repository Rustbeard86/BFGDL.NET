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
[JsonSerializable(typeof(InstallerListExportMetadata))]
[JsonSerializable(typeof(InstallerListExportFailure))]
internal partial class AppJsonSerializerContext : JsonSerializerContext
{
}
