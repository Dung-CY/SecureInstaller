using System.Security.Cryptography;
using Installer.Core.Crypto;
using Installer.Core.Vault;
using Xunit;

namespace Installer.Core.Tests.Crypto;

public class KeyTests
{
    [Fact]
    public void DeriveKek_is_deterministic_and_32_bytes()
    {
        var salt = KeyDerivation.NewSalt();
        var a = KeyDerivation.DeriveKek("pw", salt);
        var b = KeyDerivation.DeriveKek("pw", salt);
        Assert.Equal(32, a.Length);
        Assert.Equal(a, b);
    }

    // WARNING: changing the Argon2 parameters or library orphans every existing vault.
    [Fact]
    public void DeriveKek_known_answer_vector()
    {
        var salt = Enumerable.Range(0, 16).Select(i => (byte)i).ToArray();
        var kek = KeyDerivation.DeriveKek("SecureInstaller-KAT", salt);
        Assert.Equal("D0812FC8B2719DA61E62BB13DB428B0F9A0B8C84A6289E73959C34358421D085", Convert.ToHexString(kek));
    }

    [Fact]
    public void DeriveKek_differs_by_salt_and_password()
    {
        var s1 = KeyDerivation.NewSalt();
        var s2 = KeyDerivation.NewSalt();
        var a = KeyDerivation.DeriveKek("pw", s1);
        Assert.NotEqual(a, KeyDerivation.DeriveKek("pw", s2));
        Assert.NotEqual(a, KeyDerivation.DeriveKek("pw2", s1));
    }

    [Fact]
    public void DeriveKek_handles_vietnamese_password()
    {
        var k = KeyDerivation.DeriveKek("Mật-khẩu ưu tiên 1", KeyDerivation.NewSalt());
        Assert.Equal(32, k.Length);
    }

    [Fact]
    public void Wrap_unwrap_roundtrip()
    {
        var kek = KeyDerivation.NewKey();
        var secret = KeyDerivation.NewKey();
        var wrapped = KeyWrap.Wrap(secret, kek);
        Assert.Equal(12 + secret.Length + 16, wrapped.Length);
        Assert.Equal(secret, KeyWrap.Unwrap(wrapped, kek));
    }

    [Fact]
    public void Unwrap_wrong_kek_throws_BadCredentials()
    {
        var wrapped = KeyWrap.Wrap(KeyDerivation.NewKey(), KeyDerivation.NewKey());
        Assert.Throws<BadCredentialsException>(() => KeyWrap.Unwrap(wrapped, KeyDerivation.NewKey()));
        Assert.Throws<BadCredentialsException>(() => KeyWrap.Unwrap(new byte[10], KeyDerivation.NewKey()));
    }

    [Fact]
    public void Sign_verify_true_and_false_after_tamper()
    {
        var (priv, pub) = CatalogSigner.NewKeyPair();
        var data = new byte[] { 1, 2, 3, 4, 5 };
        var sig = CatalogSigner.Sign(data, priv);
        Assert.True(CatalogSigner.Verify(data, sig, pub));
        data[2] ^= 0xFF;
        Assert.False(CatalogSigner.Verify(data, sig, pub));
        Assert.False(CatalogSigner.Verify(data, new byte[3], pub));
    }

    [Fact]
    public void Verify_with_other_public_key_is_false()
    {
        var (priv, _) = CatalogSigner.NewKeyPair();
        var (_, otherPub) = CatalogSigner.NewKeyPair();
        var data = new byte[] { 9, 8, 7 };
        Assert.False(CatalogSigner.Verify(data, CatalogSigner.Sign(data, priv), otherPub));
    }
}
