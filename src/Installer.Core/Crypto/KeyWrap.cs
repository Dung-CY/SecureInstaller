using System.Security.Cryptography;
using Installer.Core.Vault;

namespace Installer.Core.Crypto;

public static class KeyWrap
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    /// <summary>Returns nonce(12) || ciphertext || tag(16), AES-256-GCM, no AAD.</summary>
    public static byte[] Wrap(byte[] secret, byte[] kek)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(kek);
        var result = new byte[NonceSize + secret.Length + TagSize];
        var nonce = result.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(kek, TagSize);
        aes.Encrypt(nonce, secret, result.AsSpan(NonceSize, secret.Length), result.AsSpan(NonceSize + secret.Length, TagSize));
        return result;
    }

    /// <exception cref="BadCredentialsException">Wrong key, tampered or malformed input.</exception>
    public static byte[] Unwrap(byte[] wrapped, byte[] kek)
    {
        ArgumentNullException.ThrowIfNull(wrapped);
        ArgumentNullException.ThrowIfNull(kek);
        if (wrapped.Length < NonceSize + TagSize)
            throw new BadCredentialsException("Wrapped key is too short.");
        var len = wrapped.Length - NonceSize - TagSize;
        var plain = new byte[len];
        try
        {
            using var aes = new AesGcm(kek, TagSize);
            aes.Decrypt(wrapped.AsSpan(0, NonceSize), wrapped.AsSpan(NonceSize, len),
                wrapped.AsSpan(NonceSize + len, TagSize), plain);
            return plain;
        }
        catch (CryptographicException ex)
        {
            throw new BadCredentialsException("Cannot unwrap key: wrong key or corrupted data.", ex);
        }
    }
}
