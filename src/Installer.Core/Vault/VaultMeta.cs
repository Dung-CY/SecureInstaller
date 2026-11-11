using Installer.Core.Model;

namespace Installer.Core.Vault;

public record UserEntry(string Username, UserRole Role, string SaltB64, string WrappedVkB64,
    string? WrappedSigningKeyB64, int FailedAttempts, DateTimeOffset? LockedUntil);

public record VaultMeta(int Format, Guid VaultId, string SigningPublicKeyB64, List<UserEntry> Users);
