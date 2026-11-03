using System.Security.Cryptography;
using System.Text;

namespace KnS.Protocol.Crypto;

/// <summary>
/// Cipher of the Com2uS ActiveUser module (<c>gateway.php</c>), reversed from
/// <c>com.com2us.module.activeuser.ActiveUserNetwork</c>.
/// </summary>
/// <remarks>
/// The client sends <c>REQ-TIMESTAMP</c> (unix seconds) and <c>REQ-AUTHKEY</c> = md5(plaintext).
/// key = the first 16 hex characters of md5(timestamp), used as ASCII (AES-128); IV = 16 zero
/// bytes; body = base64(AES-CBC-PKCS7(json)). Responses use the same timestamp key.
/// </remarks>
public static class ActiveUserCipher
{
    private static readonly byte[] ZeroInitializationVector = new byte[16];

    public static byte[] Decrypt(string timestamp, ReadOnlySpan<byte> base64Body) =>
        AesCbc.Decrypt(LenientBase64.Decode(base64Body), DeriveKey(timestamp), ZeroInitializationVector);

    /// <summary>Encrypts JSON plaintext into a base64 response body (ASCII bytes).</summary>
    public static byte[] Encrypt(string timestamp, ReadOnlySpan<byte> plaintext)
    {
        byte[] ciphertext = AesCbc.Encrypt(plaintext, DeriveKey(timestamp), ZeroInitializationVector);
        return Encoding.ASCII.GetBytes(Convert.ToBase64String(ciphertext));
    }

    /// <summary>The <c>REQ-AUTHKEY</c> header value for a plaintext: its lowercase md5 hex.</summary>
    public static string ComputeAuthKey(ReadOnlySpan<byte> plaintext) => Md5Hex(plaintext);

    private static byte[] DeriveKey(string timestamp) =>
        Encoding.ASCII.GetBytes(Md5Hex(Encoding.UTF8.GetBytes(timestamp))[..16]);

    private static string Md5Hex(ReadOnlySpan<byte> data)
    {
#pragma warning disable CA5351 // MD5 is what the client protocol uses; not a security choice.
        return Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
#pragma warning restore CA5351
    }
}
