using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnS.Protocol.Json;

/// <summary>JSON settings and helpers for everything the server puts on the wire.</summary>
public static class WireJson
{
    /// <summary>
    /// Compact output, properties in declaration order. The default encoder escapes every
    /// non-ASCII character, as the Python server's <c>json.dumps</c> did; the client's jansson
    /// parser reads the escapes.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
    };

    public static byte[] Serialize(object value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), Options);

    public static string SerializeToString(object value) =>
        JsonSerializer.Serialize(value, value.GetType(), Options);

    /// <summary>Serializes a response model into an object node, keeping property order.</summary>
    public static JsonObject ToJsonObject(object value) =>
        JsonSerializer.SerializeToNode(value, value.GetType(), Options) as JsonObject
        ?? throw new InvalidOperationException($"{value.GetType().Name} does not serialize to a JSON object.");

    /// <summary>
    /// Parses UTF-8 JSON (invalid sequences become U+FFFD, as Python's <c>decode("utf-8",
    /// "replace")</c> did). Returns null when the text is not a JSON object.
    /// </summary>
    public static JsonObject? ParseObject(ReadOnlySpan<byte> utf8)
    {
        string text = Encoding.UTF8.GetString(utf8);
        return JsonNode.Parse(text) as JsonObject;
    }

    /// <summary>Like <see cref="ParseObject"/>, but returns null instead of throwing on bad JSON.</summary>
    public static JsonObject? TryParseObject(ReadOnlySpan<byte> utf8)
    {
        try
        {
            return ParseObject(utf8);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
