using System.Text.Json.Serialization;
using KnS.Game.Gateway;
using Microsoft.Extensions.Logging;

namespace KnS.Game.Protocols.Session;

/// <summary>
/// Protocol 48: version check and the addition-file list (JanssonParser case @0x15cfb0).
/// </summary>
/// <remarks>
/// The client's DownloadFile fetches each listed file from
/// <c>addition_file_download_url + PatchFileVersion + "/" + PatchFileName</c> and learns its size
/// from Content-Length; files the device already has are skipped. The list is empty unless
/// <see cref="GameOptions.ServeAdditionFiles"/>: the current APK bundles the files.
/// </remarks>
public sealed class VersionCheckHandler(GameOptions options, ILogger<VersionCheckHandler> logger) : IProtocolHandler
{
    /// <summary>What the client echoes back in protocol 50.</summary>
    public const string ServerVersion = "1.01.10";

    public ProtocolResult Handle(GatewayRequest request)
    {
        string clientVersion = request.Parameters.GetText("strClientVersion", "1.4.0");
        var fileNames = new List<string>();
        string versionFolder = Path.Combine(options.AdditionFilesRoot, clientVersion);
        if (!options.ServeAdditionFiles)
        {
            logger.LogInformation("Assets are bundled in the APK: empty addition list");
        }
        else if (Directory.Exists(versionFolder))
        {
            fileNames.AddRange(Directory.EnumerateFiles(versionFolder).Select(Path.GetFileName).OfType<string>());
            fileNames.Sort(StringComparer.Ordinal);
        }
        else
        {
            logger.LogWarning("No addition data at {Folder}: the client gets an empty list", versionFolder);
        }

        string host = string.IsNullOrEmpty(request.Host) ? "127.0.0.1:8080" : request.Host;
        logger.LogInformation("Offering {Count} addition files for version {Version}", fileNames.Count, clientVersion);
        return ProtocolResult.Success(new VersionCheckResponse
        {
            // DownloadFile appends straight onto this, so it must end with "/".
            AdditionFileDownloadUrl = $"http://{host}/addition/",
            AdditionFiles = fileNames.Select(name => new AdditionFile(name, clientVersion)).ToList(),
        });
    }
}

public sealed record VersionCheckResponse
{
    [JsonPropertyName("server_version")] public string ServerVersion { get; init; } = VersionCheckHandler.ServerVersion;
    [JsonPropertyName("addition_file_download_url")] public string AdditionFileDownloadUrl { get; init; } = "";
    [JsonPropertyName("additionfile_filelist")] public IReadOnlyList<AdditionFile> AdditionFiles { get; init; } = [];
}

public sealed record AdditionFile(
    [property: JsonPropertyName("PatchFileName")] string FileName,
    [property: JsonPropertyName("PatchFileVersion")] string Version);
