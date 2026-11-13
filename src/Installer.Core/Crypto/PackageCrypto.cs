using System.Buffers.Binary;
using System.Security.Cryptography;
using Installer.Core.Vault;

namespace Installer.Core.Crypto;

/// <summary>
/// Chunked AES-256-GCM. Format: "SIPK" | version 1 | 8-byte base nonce | chunks.
/// Chunk: int32 LE ciphertext length | ciphertext | 16-byte tag.
/// Nonce = baseNonce(8) || uint32 BE index; AAD = 1 byte (1 = last chunk, 0 otherwise).
/// </summary>
public static class PackageCrypto
{
    public const int ChunkSize = 1 << 20;
    private const int TagSize = 16;
    private const int NonceSize = 12;
    private const int BaseNonceSize = 8;
    private const byte Version = 1;
    private static readonly byte[] Magic = "SIPK"u8.ToArray();

    public static void Encrypt(Stream plain, Stream output, byte[] key32)
    {
        CheckKey(key32);
        var baseNonce = RandomNumberGenerator.GetBytes(BaseNonceSize);
        output.Write(Magic);
        output.WriteByte(Version);
        output.Write(baseNonce);

        using var aes = new AesGcm(key32, TagSize);
        // Size buffers to the input when known (log lines are tiny); the format is unaffected.
        int bufSize = ChunkSize;
        if (plain.CanSeek)
            bufSize = (int)Math.Clamp(plain.Length - plain.Position, 1, ChunkSize);
        var cur = new byte[bufSize];
        byte[]? next = null;
        var ct = new byte[bufSize];
        var tag = new byte[TagSize];
        var nonce = new byte[NonceSize];
        baseNonce.CopyTo(nonce, 0);
        var lenBuf = new byte[4];
        var aad = new byte[1];

        int curLen = ReadFull(plain, cur);
        uint index = 0;
        while (true)
        {
            // Read ahead to know whether cur is the last chunk.
            int nextLen = curLen == ChunkSize ? ReadFull(plain, next ??= new byte[ChunkSize]) : 0;
            bool last = nextLen == 0;
            aad[0] = last ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(BaseNonceSize), index);
            aes.Encrypt(nonce, cur.AsSpan(0, curLen), ct.AsSpan(0, curLen), tag, aad);
            BinaryPrimitives.WriteInt32LittleEndian(lenBuf, curLen);
            output.Write(lenBuf);
            output.Write(ct, 0, curLen);
            output.Write(tag);
            if (last) break;
            // Only reached when curLen == ChunkSize, so both buffers are full size.
            if (cur.Length != ChunkSize) { cur = new byte[ChunkSize]; ct = new byte[ChunkSize]; }
            (cur, next) = (next!, cur);
            curLen = nextLen;
            index++;
        }
    }

    /// <summary>Decrypts a package stream into <paramref name="output"/>, streaming one chunk at a time.</summary>
    /// <remarks>
    /// Each chunk is written to <paramref name="output"/> as soon as it authenticates, but truncation or
    /// tampering of a later chunk is only detected afterwards. The output is therefore untrusted until this
    /// method returns normally. If <see cref="PackageCorruptException"/> (or any exception) is thrown, the
    /// output may already hold a valid-looking prefix; the caller must discard or delete whatever was written.
    /// </remarks>
    public static void Decrypt(Stream cipher, Stream output, byte[] key32) => Decrypt(cipher, output, key32, CancellationToken.None);

    /// <summary>Same as the 3-argument overload; <paramref name="cancel"/> is checked before each chunk.</summary>
    public static void Decrypt(Stream cipher, Stream output, byte[] key32, CancellationToken cancel)
    {
        CheckKey(key32);
        try
        {
            var header = new byte[Magic.Length + 1 + BaseNonceSize];
            if (ReadFull(cipher, header) != header.Length)
                throw new PackageCorruptException("Package header truncated.");
            if (!header.AsSpan(0, Magic.Length).SequenceEqual(Magic) || header[Magic.Length] != Version)
                throw new PackageCorruptException("Invalid package magic or version.");

            using var aes = new AesGcm(key32, TagSize);
            var ct = Array.Empty<byte>();
            var pt = Array.Empty<byte>();
            var tag = new byte[TagSize];
            var nonce = new byte[NonceSize];
            header.AsSpan(Magic.Length + 1, BaseNonceSize).CopyTo(nonce);
            var lenBuf = new byte[4];
            var aad = new byte[1];
            uint index = 0;

            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                int got = ReadFull(cipher, lenBuf);
                if (got != 4)
                    throw new PackageCorruptException("Package truncated: final chunk missing.");
                int len = BinaryPrimitives.ReadInt32LittleEndian(lenBuf);
                if (len < 0 || len > ChunkSize)
                    throw new PackageCorruptException("Invalid chunk length.");
                if (ct.Length < len) { ct = new byte[len]; pt = new byte[len]; }
                if (ReadFull(cipher, ct.AsSpan(0, len)) != len || ReadFull(cipher, tag) != TagSize)
                    throw new PackageCorruptException("Package truncated inside a chunk.");

                // Try as non-last first; a valid last chunk fails there, so try both flags.
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(BaseNonceSize), index);
                bool last;
                aad[0] = 0;
                try
                {
                    aes.Decrypt(nonce, ct.AsSpan(0, len), tag, pt.AsSpan(0, len), aad);
                    last = false;
                }
                catch (AuthenticationTagMismatchException)
                {
                    aad[0] = 1;
                    aes.Decrypt(nonce, ct.AsSpan(0, len), tag, pt.AsSpan(0, len), aad);
                    last = true;
                }

                if (!last && len != ChunkSize)
                    throw new PackageCorruptException("Non-final chunk has invalid size.");
                output.Write(pt, 0, len);
                if (last)
                {
                    if (cipher.ReadByte() != -1)
                        throw new PackageCorruptException("Unexpected data after final chunk.");
                    return;
                }
                index++;
            }
        }
        catch (PackageCorruptException)
        {
            throw;
        }
        catch (CryptographicException ex)
        {
            throw new PackageCorruptException("Package authentication failed.", ex);
        }
        catch (EndOfStreamException ex)
        {
            throw new PackageCorruptException("Package truncated.", ex);
        }
    }

    public static byte[] EncryptBytes(byte[] plain, byte[] key32)
    {
        using var input = new MemoryStream(plain, writable: false);
        using var output = new MemoryStream();
        Encrypt(input, output, key32);
        return output.ToArray();
    }

    public static byte[] DecryptBytes(byte[] cipher, byte[] key32)
    {
        using var input = new MemoryStream(cipher, writable: false);
        using var output = new MemoryStream();
        Decrypt(input, output, key32);
        return output.ToArray();
    }

    private static void CheckKey(byte[] key)
    {
        if (key is null || key.Length != 32)
            throw new ArgumentException("Key must be 32 bytes.", nameof(key));
    }

    private static int ReadFull(Stream s, Span<byte> buf)
    {
        int total = 0;
        while (total < buf.Length)
        {
            int n = s.Read(buf[total..]);
            if (n == 0) break;
            total += n;
        }
        return total;
    }
}
