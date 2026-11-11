using System.Security.Cryptography;
using Installer.Core.Vault;

namespace Installer.Core.Errors;

public static class ErrorText
{
    public const string CommitPending = "Thay đổi đã ghi một phần; hãy mở lại ứng dụng để hoàn tất.";
    public const string Cancelled = "Người dùng hủy cài đặt";

    public static string ForUser(Exception ex) => ex switch
    {
        CatalogCommitPendingException => CommitPending,
        UnsafePathException or WorkspaceUnsafeException or InvalidDataException or NotAdminException
            or BadCredentialsException or AccountLockedException => ex.Message,
        PackageCorruptException => Strings.PackageCorrupt,
        VaultTamperedException or CryptographicException => Strings.VaultTampered,
        UnauthorizedAccessException => Strings.AccessDenied,
        IOException => Strings.IoError,
        OperationCanceledException => Cancelled,
        InvalidOperationException when ex.Message.Any(c => c > 127) => ex.Message,
        _ => Strings.Unexpected
    };
}
