using System.Text;

namespace KnS.Protocol.Crypto;

/// <summary>
/// Base64 decoding that skips characters outside the alphabet (line breaks, stray bytes), the
/// way Python's <c>base64.b64decode</c> does by default. The client's bodies have always been
/// decoded that way, so a strict decoder could start refusing requests that work today.
/// </summary>
public static class LenientBase64
{
    public static byte[] Decode(ReadOnlySpan<byte> encoded)
    {
        var alphabetOnly = new StringBuilder(encoded.Length);
        foreach (byte b in encoded)
        {
            if (IsBase64Character(b))
            {
                alphabetOnly.Append((char)b);
            }
        }

        return Convert.FromBase64String(alphabetOnly.ToString());
    }

    private static bool IsBase64Character(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z'
          or >= (byte)'a' and <= (byte)'z'
          or >= (byte)'0' and <= (byte)'9'
          or (byte)'+' or (byte)'/' or (byte)'=';
}
