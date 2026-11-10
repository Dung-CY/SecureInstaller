namespace Installer.Core.Vault;

public class PackageCorruptException : Exception
{
    public PackageCorruptException(string message) : base(message) { }
    public PackageCorruptException(string message, Exception inner) : base(message, inner) { }
}

public class VaultNotFoundException : Exception
{
    public VaultNotFoundException(string message) : base(message) { }
}

public class VaultTamperedException : Exception
{
    public VaultTamperedException(string message) : base(message) { }
}

public class BadCredentialsException : Exception
{
    public BadCredentialsException(string message) : base(message) { }
    public BadCredentialsException(string message, Exception inner) : base(message, inner) { }
}

public class AccountLockedException : Exception
{
    public DateTimeOffset Until { get; }
    public AccountLockedException(DateTimeOffset until) : base($"Account locked until {until:O}") => Until = until;
}

public class NotAdminException : Exception
{
    public NotAdminException(string message) : base(message) { }
}
