using System.Security.Cryptography;
using System.Text;

namespace AzureAIProxy.Shared.Services;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
}

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    // Version prefix to distinguish AES-GCM from legacy AES-CBC payloads.
    private const byte VersionGcm = 0x01;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public EncryptionService(string encryptionKey)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));
    }

    public string Encrypt(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSizeBytes];
        RandomNumberGenerator.Fill(nonce);

        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Format: [version(1)] [nonce(12)] [tag(16)] [ciphertext(N)]
        var result = new byte[1 + NonceSizeBytes + TagSizeBytes + cipherBytes.Length];
        result[0] = VersionGcm;

        var span = result.AsSpan();
        nonce.CopyTo(span.Slice(1, NonceSizeBytes));
        tag.CopyTo(span.Slice(1 + NonceSizeBytes, TagSizeBytes));
        cipherBytes.CopyTo(span.Slice(1 + NonceSizeBytes + TagSizeBytes));

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);

        var fullBytes = Convert.FromBase64String(cipherText);
        ReadOnlySpan<byte> fullSpan = fullBytes;

        if (fullSpan.Length < 1 + NonceSizeBytes + TagSizeBytes || fullSpan[0] != VersionGcm)
            throw new CryptographicException("Invalid AES-GCM payload.");

        var nonce = fullSpan.Slice(1, NonceSizeBytes);
        var tag = fullSpan.Slice(1 + NonceSizeBytes, TagSizeBytes);
        var cipherBytes = fullSpan.Slice(1 + NonceSizeBytes + TagSizeBytes);
        var plainBytes = new byte[cipherBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSizeBytes);
        aesGcm.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }
}
