using System.Text;
using System.Text.Json.Nodes;

namespace KnS.Protocol.Json;

/// <summary>
/// Reads an <c>application/x-www-form-urlencoded</c> body the way the Python server did
/// (<c>parse_qs</c> over the Latin-1 text, first value per key, blank values dropped).
/// </summary>
public static class FormBody
{
    public static JsonObject Parse(ReadOnlySpan<byte> body)
    {
        string text = Encoding.Latin1.GetString(body);
        var fields = new JsonObject();
        foreach (string pair in text.Split('&'))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals < 0 || equals == pair.Length - 1)
            {
                continue;
            }

            string name = Unescape(pair[..equals]);
            if (!fields.ContainsKey(name))
            {
                fields[name] = Unescape(pair[(equals + 1)..]);
            }
        }

        return fields;
    }

    /// <summary>
    /// Percent-decoding as Python's <c>unquote</c> with '+' as space: runs of ASCII and %XX
    /// escapes are decoded as UTF-8 (bad sequences become U+FFFD), other characters stay as they are.
    /// </summary>
    private static string Unescape(string value)
    {
        value = value.Replace('+', ' ');
        var output = new StringBuilder(value.Length);
        var pendingBytes = new List<byte>();

        void FlushBytes()
        {
            if (pendingBytes.Count > 0)
            {
                output.Append(Encoding.UTF8.GetString(pendingBytes.ToArray()));
                pendingBytes.Clear();
            }
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '%' && i + 2 < value.Length
                && Uri.IsHexDigit(value[i + 1]) && Uri.IsHexDigit(value[i + 2]))
            {
                pendingBytes.Add(Convert.ToByte(value.Substring(i + 1, 2), 16));
                i += 2;
            }
            else if (c < 0x80)
            {
                pendingBytes.Add((byte)c);
            }
            else
            {
                FlushBytes();
                output.Append(c);
            }
        }

        FlushBytes();
        return output.ToString();
    }
}
