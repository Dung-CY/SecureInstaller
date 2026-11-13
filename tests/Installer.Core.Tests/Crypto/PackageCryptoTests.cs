using System.Security.Cryptography;
using Installer.Core.Crypto;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Crypto;

public class PackageCryptoTests
{
    private static byte[] Key() => RandomNumberGenerator.GetBytes(32);

    private static byte[] Rand(int n) => RandomNumberGenerator.GetBytes(n);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(PackageCrypto.ChunkSize - 1)]
    [InlineData(PackageCrypto.ChunkSize)]
    [InlineData(PackageCrypto.ChunkSize + 1)]
    [InlineData(3 * PackageCrypto.ChunkSize + 7)]
    public void Roundtrip_any_size(int size)
    {
        var key = Key();
        var original = Rand(size);
        var enc = PackageCrypto.EncryptBytes(original, key);
        var result = PackageCrypto.DecryptBytes(enc, key);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Wrong_key_throws_PackageCorrupt()
    {
        var enc = PackageCrypto.EncryptBytes(Rand(1000), Key());
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(enc, Key()));
    }

    [Fact]
    public void Flipped_byte_throws_PackageCorrupt()
    {
        var key = Key();
        var enc = PackageCrypto.EncryptBytes(Rand(5000), key);
        enc[100] ^= 0x01;
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(enc, key));
    }

    [Fact]
    public void Truncated_last_chunk_throws_PackageCorrupt()
    {
        var key = Key();
        var size = 2 * PackageCrypto.ChunkSize + PackageCrypto.ChunkSize / 2;
        var enc = PackageCrypto.EncryptBytes(Rand(size), key);
        // header = 13 bytes; each full chunk = 4 + ChunkSize + 16. Keep header + 2 full chunks.
        var cut = 13 + 2 * (4 + PackageCrypto.ChunkSize + 16);
        var truncated = enc.AsSpan(0, cut).ToArray();
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(truncated, key));
    }

    [Fact]
    public void Bad_magic_throws_PackageCorrupt()
    {
        var key = Key();
        var enc = PackageCrypto.EncryptBytes(Rand(100), key);
        enc[0] = (byte)'X';
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(enc, key));
    }

    private const int Full = 4 + PackageCrypto.ChunkSize + 16;

    [Fact]
    public void Trailing_byte_after_final_chunk_throws_PackageCorrupt()
    {
        var key = Key();
        var enc = PackageCrypto.EncryptBytes(Rand(1000), key);
        var bad = enc.Concat(new byte[] { 0 }).ToArray();
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(bad, key));
    }

    [Fact]
    public void Swapped_chunks_throw_PackageCorrupt()
    {
        var key = Key();
        var enc = PackageCrypto.EncryptBytes(Rand(2 * PackageCrypto.ChunkSize + PackageCrypto.ChunkSize / 2), key);
        var swapped = (byte[])enc.Clone();
        Array.Copy(enc, 13 + Full, swapped, 13, Full);
        Array.Copy(enc, 13, swapped, 13 + Full, Full);
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(swapped, key));
    }

    [Fact]
    public void Length_prefix_over_chunk_size_throws_PackageCorrupt()
    {
        var key = Key();
        var enc = PackageCrypto.EncryptBytes(Rand(100), key);
        BitConverter.GetBytes(PackageCrypto.ChunkSize + 1).CopyTo(enc, 13);
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(enc, key));
    }

    [Fact]
    public void Exact_multiple_truncated_at_boundary_throws_PackageCorrupt()
    {
        var key = Key();
        var enc = PackageCrypto.EncryptBytes(Rand(2 * PackageCrypto.ChunkSize), key);
        var truncated = enc.AsSpan(0, 13 + Full).ToArray();
        Assert.Throws<PackageCorruptException>(() => PackageCrypto.DecryptBytes(truncated, key));
    }

    private sealed class CountingSink : Stream
    {
        public long Count;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => Count;
        public override long Position { get => Count; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Count += count;
        public override void Write(ReadOnlySpan<byte> buffer) => Count += buffer.Length;
    }

    [Fact]
    public void Decrypt_streams_without_buffering_whole_file()
    {
        var key = Key();
        const int total = 64 * 1024 * 1024;
        var tmp = Path.Combine(Path.GetTempPath(), "si_crypto_" + Guid.NewGuid().ToString("N") + ".enc");
        try
        {
            using (var fs = File.Create(tmp))
            using (var plain = new MemoryStream(Rand(total)))
                PackageCrypto.Encrypt(plain, fs, key);

            var sink = new CountingSink();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            using (var input = File.OpenRead(tmp))
                PackageCrypto.Decrypt(input, sink, key);
            var delta = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(total, sink.Count);
            Assert.True(delta < 16L * 1024 * 1024, $"allocated {delta} bytes");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Decrypt_observes_cancellation_between_chunks()
    {
        var key = Key();
        var plain = new byte[3 * 1024 * 1024];
        using var enc = new MemoryStream();
        PackageCrypto.Encrypt(new MemoryStream(plain), enc, key);
        enc.Position = 0;
        using var cts = new CancellationTokenSource();
        var outp = new CancelAfterFirstWrite(cts);
        Assert.ThrowsAny<OperationCanceledException>(() => PackageCrypto.Decrypt(enc, outp, key, cts.Token));
        Assert.True(outp.Length < plain.Length);
    }

    private sealed class CancelAfterFirstWrite(CancellationTokenSource cts) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) { base.Write(buffer, offset, count); cts.Cancel(); }
    }
}
