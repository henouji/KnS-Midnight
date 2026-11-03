using System.Text.Json.Nodes;
using KnS.Protocol.Json;

namespace KnS.Protocol;

/// <summary>
/// The parameters of a client request, with the loose reading rules of <see cref="LenientJson"/>:
/// a value that is missing or "not set" falls back to the default. Remembers which parameters were
/// looked at, so the traffic log can show the ones the server ignored.
/// </summary>
public sealed class RequestParameters
{
    private readonly JsonObject _values;
    private readonly HashSet<string> _read = new(StringComparer.Ordinal);

    public RequestParameters(JsonObject? values)
    {
        _values = values ?? [];
    }

    public static RequestParameters Empty => new(null);

    /// <summary>The raw value, or null when absent.</summary>
    public JsonNode? this[string name] => Read(name);

    public bool Contains(string name)
    {
        _read.Add(name);
        return _values.ContainsKey(name);
    }

    /// <summary>True when the value is present and set (see <see cref="LenientJson.IsSet"/>).</summary>
    public bool IsSet(string name) => LenientJson.IsSet(Read(name));

    /// <summary>The value as an integer, or <paramref name="fallback"/> when it is not set.</summary>
    /// <exception cref="FormatException">The value is set but is not an integer.</exception>
    public long GetInt64(string name, long fallback = 0) =>
        IsSet(name) ? LenientJson.ToInt64(Read(name)) : fallback;

    /// <summary>The value as an integer, or <paramref name="fallback"/> when it is not set or not an integer.</summary>
    public long GetInt64OrFallback(string name, long fallback = 0) =>
        IsSet(name) ? LenientJson.TryToInt64(Read(name)) ?? fallback : fallback;

    /// <summary>The value as text, or <paramref name="fallback"/> when it is not set.</summary>
    public string GetText(string name, string fallback) =>
        IsSet(name) ? LenientJson.ToText(Read(name)) : fallback;

    /// <summary>The elements of an array value; empty when it is not set or not an array.</summary>
    public IReadOnlyList<JsonNode?> GetList(string name) =>
        Read(name) is JsonArray array ? [.. array] : [];

    /// <summary>A copy of the raw value (for echoing it back), or <paramref name="fallback"/> when absent.</summary>
    public JsonNode? CopyOrDefault(string name, JsonNode? fallback)
    {
        _read.Add(name);
        return _values.TryGetPropertyValue(name, out JsonNode? value) ? value?.DeepClone() : fallback;
    }

    /// <summary>The names of the parameters nothing has read yet, in request order.</summary>
    public IReadOnlyList<string> UnreadNames =>
        _values.Select(parameter => parameter.Key).Where(name => !_read.Contains(name)).ToList();

    public string ToJsonString() => _values.ToJsonString();

    private JsonNode? Read(string name)
    {
        _read.Add(name);
        return _values[name];
    }
}
