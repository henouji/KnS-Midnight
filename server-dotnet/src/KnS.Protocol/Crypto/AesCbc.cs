using System.Security.Cryptography;

namespace KnS.Protocol.Crypto;

/// <summary>
/// AES-CBC with PKCS#7 padding, as both Com2uS ciphers use it.
/// </summary>
/// <remarks>
/// Decryption strips the padding leniently, like the original Python server: the last byte is
/// trusted as the pad length when it is 1..16 and the padding bytes themselves are not checked.
/// .NET's own PKCS#7 mode would reject a body the game accepts today.
/// </remarks>
internal static class AesCbc
{
    private const int BlockSize = 16;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> ciphertext, byte[] key, byte[] iv)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] padded = aes.DecryptCbc(ciphertext, iv, PaddingMode.None);
        return StripPadding(padded);
    }

    private static byte[] StripPadding(byte[] padded)
    {
        if (padded.Length == 0)
        {
            return padded;
        }

        int padLength = padded[^1];
        return padLength is >= 1 and <= BlockSize && padded.Length >= padLength
            ? padded[..^padLength]
            : padded;
    }
}
