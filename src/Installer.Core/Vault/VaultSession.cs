using Installer.Core.Model;

namespace Installer.Core.Vault;

public sealed class VaultSession
{
    public string Username { get; }
    public bool IsAdmin { get; }
    public byte[] VaultKey { get; }
    public byte[]? SigningPrivateKey { get; }
    public Catalog Catalog { get; internal set; }
    public VaultStore Store { get; }

    internal VaultSession(string username, bool isAdmin, byte[] vaultKey, byte[]? signingPrivateKey,
        Catalog catalog, VaultStore store)
    {
        Username = username; IsAdmin = isAdmin; VaultKey = vaultKey; SigningPrivateKey = signingPrivateKey;
        Catalog = catalog; Store = store;
    }
}
