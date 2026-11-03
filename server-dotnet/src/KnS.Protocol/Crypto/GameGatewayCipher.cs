using System.Text;

namespace KnS.Protocol.Crypto;

/// <summary>
/// Envelope cipher of the native game gateway (<c>/1mhero/gateway.php</c>):
/// body = base64(AES-128-CBC(json)) with constants embedded in libgame.so.
/// </summary>
public static class GameGatewayCipher
{
    /// <summary>The embedded key string at 0x1cbd54 in libgame.so.</summary>
    private static readonly byte[] Key = Encoding.ASCII.GetBytes("1m!1HUB2@hero#Re");

    /// <summary>The constant IV at 0x1D2594 in libgame.so.</summary>
    private static readonly byte[] InitializationVector =
        Convert.FromHexString("de295106d3872f949783942fdc9f01e2");

    /// <summary>Decodes a request body into its JSON plaintext bytes.</summary>
    public static byte[] Decrypt(ReadOnlySpan<byte> base64Body) =>
        AesCbc.Decrypt(LenientBase64.Decode(base64Body), Key, InitializationVector);

    /// <summary>Encrypts JSON plaintext into a base64 response body (ASCII bytes).</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        byte[] ciphertext = AesCbc.Encrypt(plaintext, Key, InitializationVector);
        return Encoding.ASCII.GetBytes(Convert.ToBase64String(ciphertext));
    }
}
