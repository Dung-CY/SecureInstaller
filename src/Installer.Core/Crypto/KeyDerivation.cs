using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Installer.Core.Crypto;

public static class KeyDerivation
{
    public const int SaltSize = 16;
    public const int KeySize = 32;

    /// <summary>Derives a 32-byte key-encryption-key from a password using Argon2id (64 MiB, 3 iterations, parallelism 2).</summary>
    public static byte[] DeriveKek(string password, byte[] salt16)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt16);
        if (salt16.Length != SaltSize)
            throw new ArgumentException("Salt must be 16 bytes.", nameof(salt16));

        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt16,
            MemorySize = 65536,
            Iterations = 3,
            DegreeOfParallelism = 2,
        };
        return argon.GetBytes(KeySize);
    }

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    public static byte[] NewKey() => RandomNumberGenerator.GetBytes(KeySize);
}
