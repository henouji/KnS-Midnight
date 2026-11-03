using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KnS.Protocol.Json;

/// <summary>
/// Loose value rules for client JSON, carried over from the Python server so requests are read
/// exactly as before: what counts as "not set" (<c>null</c>, <c>0</c>, <c>""</c>, <c>false</c>,
/// empty arrays and objects) and how a value converts to an integer or text.
/// </summary>
public static class LenientJson
{
    /// <summary>True for a value the Python server treated as set (Python truthiness).</summary>
    public static bool IsSet(JsonNode? node) => node switch
    {
        null => false,
        JsonObject obj => obj.Count > 0,
        JsonArray array => array.Count > 0,
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>().Length > 0,
            JsonValueKind.Number => NumberOf(value) != 0,
            JsonValueKind.True => true,
            _ => false,
        },
        _ => false,
    };

    /// <summary>
    /// Converts like Python's <c>int()</c>: integers as they are, fractions truncated, numeric
    /// strings parsed, booleans as 1 / 0.
    /// </summary>
    /// <exception cref="FormatException">The value has no integer reading.</exception>
    public static long ToInt64(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            switch (value.GetValueKind())
            {
                case JsonValueKind.Number:
                    return value.TryGetValue(out long whole) ? whole : (long)Math.Truncate(NumberOf(value));
                case JsonValueKind.String:
                    string text = value.GetValue<string>().Trim().Replace("_", "", StringComparison.Ordinal);
                    if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed))
                    {
                        return parsed;
                    }

                    break;
                case JsonValueKind.True:
                    return 1;
                case JsonValueKind.False:
                    return 0;
            }
        }

        throw new FormatException($"Not an integer: {node?.ToJsonString() ?? "null"}");
    }

    /// <summary>Like <see cref="ToInt64"/>, or null when the value has no integer reading.</summary>
    public static long? TryToInt64(JsonNode? node)
    {
        try
        {
            return ToInt64(node);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// True for a JSON integer (or boolean): the values Python's <c>isinstance(v, int)</c> accepted.
    /// A number written with a fraction or exponent is a float there and does not count.
    /// </summary>
    public static bool IsInteger(JsonNode? node, out long number)
    {
        number = 0;
        if (node is not JsonValue value)
        {
            return false;
        }

        switch (value.GetValueKind())
        {
            case JsonValueKind.True:
                number = 1;
                return true;
            case JsonValueKind.False:
                return true;
            case JsonValueKind.Number:
                string raw = value.ToJsonString();
                return raw.IndexOfAny(['.', 'e', 'E']) < 0 && long.TryParse(raw, CultureInfo.InvariantCulture, out number);
            default:
                return false;
        }
    }

    /// <summary>True when the value equals <paramref name="number"/> numerically (booleans count as 1 / 0).</summary>
    public static bool NumericallyEquals(JsonNode? node, long number)
    {
        if (node is not JsonValue value)
        {
            return false;
        }

        return value.GetValueKind() switch
        {
            JsonValueKind.Number => NumberOf(value) == number,
            JsonValueKind.True => number == 1,
            JsonValueKind.False => number == 0,
            _ => false,
        };
    }

    /// <summary>Converts like Python's <c>str()</c> for the scalar values clients send.</summary>
    public static string ToText(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            switch (value.GetValueKind())
            {
                case JsonValueKind.String:
                    return value.GetValue<string>();
                case JsonValueKind.True:
                    return "True";
                case JsonValueKind.False:
                    return "False";
                case JsonValueKind.Number:
                    return value.ToJsonString();
            }
        }

        return node is null ? "None" : node.ToJsonString();
    }

    /// <summary>
    /// A database parameter for a raw client value: numbers and booleans as integers or doubles,
    /// strings as text, anything else as its JSON text.
    /// </summary>
    public static object? ToDatabaseValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (IsInteger(node, out long number))
        {
            return number;
        }

        if (node is JsonValue value)
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.Number => NumberOf(value),
                JsonValueKind.String => value.GetValue<string>(),
                _ => value.ToJsonString(),
            };
        }

        return node.ToJsonString();
    }

    /// <summary>A number value as a double, whether it was parsed or created in code.</summary>
    private static double NumberOf(JsonValue value) =>
        double.Parse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture);
}
