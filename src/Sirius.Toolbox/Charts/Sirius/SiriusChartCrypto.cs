using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Sirius.Toolbox.Charts.Sirius;

public static class SiriusChartCrypto
{
    private const int IvLength = 16;
    private const int SaltLength = 8;
    private const int Pbkdf2Iterations = 1000;

    public static byte[] EncodeChart(string chartText, string password)
    {
        var plainText = new UTF8Encoding(false).GetBytes(chartText);
        var compressed = BrotliCompress(plainText);
        return Encrypt(compressed, password);
    }

    public static string DecodeChart(ReadOnlySpan<byte> encoded, string password)
    {
        var compressed = Decrypt(encoded, password);
        var plainText = BrotliDecompress(compressed);
        return new UTF8Encoding(false, true).GetString(plainText);
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> bytes, string password)
    {
        var key = ValidateKey(password);
        Span<byte> salt = stackalloc byte[SaltLength];
        salt.Clear();
        var iv = Rfc2898DeriveBytes.Pbkdf2(bytes, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, IvLength);

        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var encryptor = aes.CreateEncryptor();
        var input = bytes.ToArray();
        var cipher = encryptor.TransformFinalBlock(input, 0, input.Length);
        var output = new byte[iv.Length + cipher.Length];
        Buffer.BlockCopy(iv, 0, output, 0, iv.Length);
        Buffer.BlockCopy(cipher, 0, output, iv.Length, cipher.Length);
        return output;
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> encoded, string password)
    {
        if (encoded.Length <= IvLength || (encoded.Length - IvLength) % 16 != 0)
            throw new InvalidDataException("Invalid Sirius ENC length.");

        var key = ValidateKey(password);
        var iv = encoded[..IvLength].ToArray();
        var cipher = encoded[IvLength..].ToArray();

        using var aes = Aes.Create();
        aes.BlockSize = 128;
        aes.KeySize = 256;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    private static byte[] ValidateKey(string password)
    {
        var key = Encoding.UTF8.GetBytes(password);
        if (key.Length != 32)
            throw new ArgumentException($"The chart key must encode to exactly 32 UTF-8 bytes; got {key.Length} bytes.", nameof(password));
        return key;
    }

    private static byte[] BrotliCompress(ReadOnlySpan<byte> source)
    {
        using var output = new MemoryStream();
        using (var stream = new BrotliStream(output, CompressionLevel.Optimal, leaveOpen: true))
            stream.Write(source);
        return output.ToArray();
    }

    private static byte[] BrotliDecompress(ReadOnlySpan<byte> source)
    {
        using var input = new MemoryStream(source.ToArray(), writable: false);
        using var stream = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
