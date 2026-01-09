using System.Text;
using System.Xml.Linq;
using BFGDL.NET.Models;
using Microsoft.Extensions.Logging;

namespace BFGDL.NET.Services;

public sealed class BigFishGamesClient(
    HttpClient httpClient,
    DownloadOptions options,
    ILogger<BigFishGamesClient> logger) : IBigFishGamesClient
{
    private const string ApiEndpoint = "https://shop.bigfishgames.com/rest/V1/bfg/rpc/xml";

    public async Task<GameInfo> GetGameInfoAsync(string wrapId, CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Fetching game info for WrapID: {WrapId}", wrapId);

        var requestXml = BuildRequestXml(wrapId);
        var content = new StringContent(requestXml, Encoding.UTF8, "application/xml");

        var response = await httpClient.PostAsync(ApiEndpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseXml = await response.Content.ReadAsStringAsync(cancellationToken);

        return ParseGameInfo(wrapId, responseXml);
    }

    private static string BuildRequestXml(string wrapId)
    {
        return $"""
                <?xml version="1.0" encoding="utf-8"?>
                <methodCall>
                 <methodName>gms.getGameInfo</methodName>
                 <params>
                  <param>
                   <value>
                    <struct>
                     <member>
                      <name>gameWID</name>
                      <value>
                       <string>{wrapId}</string>
                      </value>
                     </member>
                     <member>
                      <name>siteID</name>
                      <value>
                       <string>1</string>
                      </value>
                     </member>
                     <member>
                      <name>languageID</name>
                      <value>
                       <string>1</string>
                      </value>
                     </member>
                     <member>
                      <name>email</name>
                      <value>
                       <string>gamemanager@bigfishgames.com</string>
                      </value>
                     </member>
                     <member>
                      <name>extData</name>
                      <value>
                       <string></string>
                      </value>
                     </member>
                     <member>
                      <name>downloadID</name>
                      <value>
                       <string>123456789</string>
                      </value>
                     </member>
                    </struct>
                   </value>
                  </param>
                 </params>
                </methodCall>
                """;
    }

    private GameInfo ParseGameInfo(string wrapId, string xmlResponse)
    {
        var doc = XDocument.Parse(xmlResponse);
        // TODO: Unused -> var ns = doc.Root?.Name.Namespace ?? XNamespace.None;

        // Navigate to methodResponse/params/param/value/struct/member
        var members = doc.Descendants("member").ToList();

        // Find gameInfo member
        var gameInfoMember = members.FirstOrDefault(m =>
                                 m.Element("name")?.Value == "gameInfo") ??
                             throw new InvalidOperationException(
                                 $"Game info not found in response for WrapID: {wrapId}");
        var gameInfoStruct = gameInfoMember.Descendants("struct").First();
        var gameInfoMembers = gameInfoStruct.Elements("member").ToList();

        var gameId = gameInfoMembers
                         .FirstOrDefault(m => m.Element("name")?.Value == "id")
                         ?.Element("value")?.Element("string")?.Value
                     ?? throw new InvalidOperationException("Game ID not found");

        var gameName = gameInfoMembers
                           .FirstOrDefault(m => m.Element("name")?.Value == "name")
                           ?.Element("value")?.Element("string")?.Value
                       ?? throw new InvalidOperationException("Game name not found");

        // Find downloadInfo member
        var downloadInfoMember = members.FirstOrDefault(m =>
                                     m.Element("name")?.Value == "downloadInfo") ??
                                 throw new InvalidOperationException(
                                     $"Download info not found in response for WrapID: {wrapId}");
        var segments = ParseDownloadSegments(downloadInfoMember);

        logger.LogInformation("Retrieved game info: {GameId} - {GameName} with {SegmentCount} segments",
            gameId, gameName, segments.Count);

        return new GameInfo
        {
            WrapId = wrapId,
            Id = gameId,
            Name = HtmlSanitizer.SanitizeForFileName(gameName),
            Segments = segments
        };
    }

    private List<DownloadSegment> ParseDownloadSegments(XElement downloadInfoMember)
    {
        var segments = new List<DownloadSegment>();

        var segmentListMember = downloadInfoMember
            .Descendants("member")
            .FirstOrDefault(m => m.Element("name")?.Value == "segmentList");

        if (segmentListMember == null) return segments;

        var segmentValues = segmentListMember
            .Descendants("value")
            .Where(v => v.Element("struct") != null);

        foreach (var segmentValue in segmentValues)
        {
            var segmentStruct = segmentValue.Element("struct");
            if (segmentStruct == null) continue;

            var segmentMembers = segmentStruct.Elements("member").ToList();

            var fileName = segmentMembers
                .FirstOrDefault(m => m.Element("name")?.Value == "fileSegmentName")
                ?.Element("value")?.Element("string")?.Value;

            var urlName = segmentMembers
                .FirstOrDefault(m => m.Element("name")?.Value == "urlName")
                ?.Element("value")?.Element("string")?.Value;

            // Filter out demo segments
            if (fileName != null && urlName != null && !fileName.Contains(".demo."))
                segments.Add(new DownloadSegment
                {
                    FileName = fileName,
                    UrlName = urlName,
                    DownloadUrl = options.DownloadUrl
                });
        }

        return segments;
    }
}