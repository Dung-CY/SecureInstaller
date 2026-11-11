using System.Security.Cryptography;

namespace Installer.Core.Crypto;

public static class CatalogSigner
{
    public static (byte[] PrivatePkcs8, byte[] PublicSpki) NewKeyPair()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ec.ExportPkcs8PrivateKey(), ec.ExportSubjectPublicKeyInfo());
    }

    public static byte[] Sign(byte[] data, byte[] privatePkcs8)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ec.ImportPkcs8PrivateKey(privatePkcs8, out _);
        return ec.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Returns false for a wrong or malformed signature. A malformed public key may throw
    /// <see cref="CryptographicException"/>.
    /// </summary>
    public static bool Verify(byte[] data, byte[] signature, byte[] publicSpki)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        ec.ImportSubjectPublicKeyInfo(publicSpki, out _);
        try
        {
            return ec.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException) { return false; }
    }
}
