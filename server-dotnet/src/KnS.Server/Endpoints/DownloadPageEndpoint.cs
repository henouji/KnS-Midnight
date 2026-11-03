using System.Text.Json;

namespace KnS.Server.Endpoints;

/// <summary>
/// A small public page that lists the APKs in a folder and serves them: <c>/</c> and <c>/games</c>
/// (the page), <c>/admin</c> (the tester admin page, which calls /admin/testers and /admin/flags with the admin key), <c>/games.json</c> (the list) and <c>/download/&lt;file&gt;</c> (range-capable).
/// </summary>
/// <remarks>
/// The APKs stay out of git and out of the deploy zip: they sit in <c>KNS_DOWNLOAD_DIR</c>
/// (App Service: /home/site/apks, which survives deploys). An optional <c>catalog.json</c> there
/// gives each file a title, description and note; without it every *.apk is listed by file name.
/// Usage is kept low on purpose: the page is one static file, the list is read from disk per
/// request (a handful of files), downloads are streamed and cached by the browser.
/// </remarks>
public sealed class DownloadPageEndpoint
{
    private static readonly string[] PagePaths = ["/", "/games", "/games/", "/admin", "/admin/", "/banner.jpg", "/icon.png", "/favicon.ico"];
    private const string DownloadPrefix = "/download/";

    private readonly string _directory;
    private static readonly string WebDirectory = Path.Combine(AppContext.BaseDirectory, "web");

    public DownloadPageEndpoint()
    {
        _directory = Environment.GetEnvironmentVariable("KNS_DOWNLOAD_DIR") is { Length: > 0 } configured
            ? configured
            : Directory.Exists("/home/site") ? "/home/site/apks" : "downloads";
    }

    public static bool Matches(string method, string target)
    {
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        string path = target.Split('?', 2)[0];
        return PagePaths.Contains(path) || path == "/games.json" || path.StartsWith(DownloadPrefix, StringComparison.Ordinal);
    }

    public async Task ServeAsync(HttpContext context, string target)
    {
        string path = target.Split('?', 2)[0];
        if (path == "/games.json")
        {
            context.Response.Headers.CacheControl = "no-cache";
            await context.Response.WriteAsJsonAsync(ReadCatalog(), context.RequestAborted);
        }
        else if (path.StartsWith(DownloadPrefix, StringComparison.Ordinal))
        {
            await ServeFileAsync(context, Uri.UnescapeDataString(path[DownloadPrefix.Length..]));
        }
        else if (path is "/banner.jpg" or "/icon.png" or "/favicon.ico")
        {
            string asset = Path.Combine(WebDirectory, path == "/banner.jpg" ? "banner.jpg" : "icon.png");
            if (!File.Exists(asset))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = path == "/banner.jpg" ? "image/jpeg" : "image/png";
            context.Response.Headers.CacheControl = "public, max-age=86400";
            await context.Response.SendFileAsync(asset, context.RequestAborted);
        }
        else if (Path.Combine(WebDirectory, path.StartsWith("/admin", StringComparison.Ordinal) ? "admin.html" : "games.html") is var page && File.Exists(page))
        {
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "public, max-age=300";
            await context.Response.SendFileAsync(page, context.RequestAborted);
        }
        else
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
        }
    }

    private async Task ServeFileAsync(HttpContext context, string name)
    {
        // A bare *.apk name inside the folder only: no separators, no traversal.
        string file = Path.Combine(_directory, name);
        if (name.Contains('/', StringComparison.Ordinal) || name.Contains('\\', StringComparison.Ordinal) || name.Contains("..", StringComparison.Ordinal)
            || !name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase) || !File.Exists(file))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var info = new FileInfo(file);
        context.Response.ContentType = "application/vnd.android.package-archive";
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{name}\"";
        context.Response.Headers.CacheControl = "public, max-age=86400";
        context.Response.Headers.ETag = $"\"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}\"";
        context.Response.Headers.AcceptRanges = "bytes";

        // Range support lets a phone resume a 250 MB download instead of starting over (and paying twice).
        long total = info.Length;
        (long start, long end) = ParseRange(context.Request.Headers.Range.ToString(), total);
        bool partial = start != 0 || end != total - 1;
        context.Response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        if (partial)
        {
            context.Response.Headers.ContentRange = $"bytes {start}-{end}/{total}";
        }

        long length = Math.Max(0, end - start + 1);
        context.Response.ContentLength = length;
        if (HttpMethods.IsHead(context.Request.Method) || length == 0)
        {
            return;
        }

        await context.Response.SendFileAsync(file, start, length, context.RequestAborted);
    }

    private static (long Start, long End) ParseRange(string header, long total)
    {
        if (header.StartsWith("bytes=", StringComparison.Ordinal) && header[6..].Split('-', 2) is [var from, var to]
            && long.TryParse(from, out long start) && start >= 0 && start < total)
        {
            long end = long.TryParse(to, out long parsed) ? Math.Min(parsed, total - 1) : total - 1;
            return (start, Math.Max(start, end));
        }

        return (0, total - 1);
    }

    private List<GameEntry> ReadCatalog()
    {
        if (!Directory.Exists(_directory))
        {
            return [];
        }

        var catalog = new Dictionary<string, GameEntry>(StringComparer.OrdinalIgnoreCase);
        string catalogPath = Path.Combine(_directory, "catalog.json");
        if (File.Exists(catalogPath))
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (GameEntry entry in JsonSerializer.Deserialize<List<GameEntry>>(File.ReadAllText(catalogPath), options) ?? [])
            {
                catalog[entry.File] = entry;
            }
        }

        var games = new List<GameEntry>();
        foreach (string file in Directory.EnumerateFiles(_directory, "*.apk").Order())
        {
            string name = Path.GetFileName(file);
            GameEntry entry = catalog.GetValueOrDefault(name) ?? new GameEntry { File = name, Title = Path.GetFileNameWithoutExtension(name) };
            games.Add(entry with { Size = new FileInfo(file).Length, Url = DownloadPrefix + Uri.EscapeDataString(name) });
        }

        return games;
    }

    private sealed record GameEntry
    {
        public string File { get; init; } = "";
        public string Title { get; init; } = "";
        public string? Description { get; init; }
        public string? Version { get; init; }
        public string? Note { get; init; }
        public long Size { get; init; }
        public string? Url { get; init; }
    }
}
