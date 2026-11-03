using System.Buffers.Binary;
using System.Text;
using KnS.Game;

namespace KnS.Server.Endpoints;

/// <summary>
/// Serves an archived addition-data file: /addition/&lt;version&gt;/&lt;name&gt;, the layout of the
/// original CDN that protocol 48's download URL points at.
/// </summary>
/// <remarks>
/// The CDN served every file with a 68-byte trailer that DownloadFile.ValidateFile checks and
/// the client strips after a successful download, so the archived copies (pulled off a device)
/// no longer have it and it is rebuilt here. Without it the client logs "ValidateFile version
/// fail" and retries the same file forever. The client resumes partial downloads with
/// <c>Range: bytes=&lt;start&gt;-</c>, so ranges are honoured too.
/// </remarks>
public sealed class AdditionFileEndpoint(GameOptions options, ILogger<AdditionFileEndpoint> logger)
{
    public const string PathPrefix = "/addition/";

    public async Task ServeAsync(HttpContext context, string target)
    {
        string relative = Uri.UnescapeDataString(target.Split('?', 2)[0])[PathPrefix.Length..];
        string[] parts = relative.Split('/').Where(segment => segment is not ("" or "." or "..")).ToArray();
        if (parts.Length != 2 || relative.Contains('\\', StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        string root = Path.GetFullPath(options.AdditionFilesRoot);
        string path = Path.GetFullPath(Path.Combine(root, parts[0], parts[1]));
        if (!path.StartsWith(root, StringComparison.Ordinal) || !File.Exists(path))
        {
            logger.LogWarning("Addition file missing: {File}", relative);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        long fileLength = new FileInfo(path).Length;
        byte[] trailer = BuildTrailer(version: parts[0], fileName: parts[1], fileLength);
        long totalLength = fileLength + trailer.Length;
        (long start, long end) = ParseRange(context.Request.Headers.Range.ToString(), totalLength);
        bool partial = start != 0 || end != totalLength - 1;

        HttpResponse response = context.Response;
        response.StatusCode = partial ? StatusCodes.Status206PartialContent : StatusCodes.Status200OK;
        response.ContentType = "application/octet-stream";
        response.Headers.AcceptRanges = "bytes";
        if (partial)
        {
            response.Headers.ContentRange = $"bytes {start}-{end}/{totalLength}";
        }

        long length = Math.Max(0, end - start + 1);
        response.ContentLength = length;
        await WriteRangeAsync(response.Body, path, fileLength, trailer, start, length, context.RequestAborted);
    }

    /// <summary>
    /// The CDN trailer, from the end: total length including the trailer (4 bytes, big-endian),
    /// before it the file name (48 bytes) and the version ("1.4.0", 16 bytes, compared by prefix),
    /// both NUL-padded.
    /// </summary>
    public static byte[] BuildTrailer(string version, string fileName, long fileLength)
    {
        var trailer = new byte[16 + 48 + 4];
        CopyTruncated(Encoding.UTF8.GetBytes(version), trailer.AsSpan(0, 16));
        CopyTruncated(Encoding.UTF8.GetBytes(fileName), trailer.AsSpan(16, 48));
        BinaryPrimitives.WriteUInt32BigEndian(trailer.AsSpan(64, 4), (uint)(fileLength + trailer.Length));
        return trailer;
    }

    /// <summary>The byte range of "bytes=&lt;start&gt;-[&lt;end&gt;]", clamped to the content; the whole content otherwise.</summary>
    public static (long Start, long End) ParseRange(string rangeHeader, long totalLength)
    {
        long start = 0;
        long end = totalLength - 1;
        if (rangeHeader.StartsWith("bytes=", StringComparison.Ordinal))
        {
            string[] bounds = rangeHeader["bytes=".Length..].Split('-', 2);
            if (long.TryParse(bounds[0], System.Globalization.NumberStyles.None, null, out long first))
            {
                start = first;
            }

            if (bounds.Length > 1 && long.TryParse(bounds[1], System.Globalization.NumberStyles.None, null, out long last))
            {
                end = last;
            }

            start = Math.Max(0, start);
            end = Math.Min(end, totalLength - 1);
        }

        return (start, end);
    }

    private static void CopyTruncated(byte[] source, Span<byte> destination) =>
        source.AsSpan(0, Math.Min(source.Length, destination.Length)).CopyTo(destination);

    private static async Task WriteRangeAsync(
        Stream output, string path, long fileLength, byte[] trailer, long start, long length, CancellationToken cancellation)
    {
        if (length <= 0)
        {
            return;
        }

        if (start < fileLength)
        {
            long fromFile = Math.Min(length, fileLength - start);
            await using FileStream file = File.OpenRead(path);
            file.Seek(start, SeekOrigin.Begin);
            await CopyBytesAsync(file, output, fromFile, cancellation);
            length -= fromFile;
            start += fromFile;
        }

        if (length > 0)
        {
            await output.WriteAsync(trailer.AsMemory((int)(start - fileLength), (int)length), cancellation);
        }
    }

    private static async Task CopyBytesAsync(Stream source, Stream destination, long count, CancellationToken cancellation)
    {
        byte[] buffer = new byte[81920];
        while (count > 0)
        {
            int read = await source.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, count)), cancellation);
            if (read == 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellation);
            count -= read;
        }
    }
}
