using System.Security.Cryptography;
using Installer.Core;
using Installer.Core.Errors;
using Installer.Core.Vault;

namespace Installer.Core.Tests.Errors;

public class ErrorTextTests
{
    public static IEnumerable<object[]> Rows() => new[]
    {
        new object[] { new UnsafePathException("u-msg"), "u-msg" },
        new object[] { new WorkspaceUnsafeException("w-msg"), "w-msg" },
        new object[] { new InvalidDataException("d-msg"), "d-msg" },
        new object[] { new NotAdminException("n-msg"), "n-msg" },
        new object[] { new BadCredentialsException("b-msg"), "b-msg" },
        new object[] { new AccountLockedException(DateTimeOffset.UtcNow), null! },
        new object[] { new PackageCorruptException("raw"), Strings.PackageCorrupt },
        new object[] { new VaultTamperedException("raw"), Strings.VaultTampered },
        new object[] { new CryptographicException("raw"), Strings.VaultTampered },
        new object[] { new UnauthorizedAccessException("D:/x"), Strings.AccessDenied },
        new object[] { new IOException("D:/x"), Strings.IoError },
        new object[] { new FileNotFoundException("D:/x"), Strings.IoError },
        new object[] { new OperationCanceledException(), "Người dùng hủy cài đặt" },
        new object[] { new InvalidOperationException("Lỗi tiếng Việt"), "Lỗi tiếng Việt" },
        new object[] { new InvalidOperationException("ascii only"), Strings.Unexpected },
        new object[] { new NullReferenceException("x"), Strings.Unexpected },
        new object[] { new CatalogCommitPendingException(new Exception("i")), ErrorText.CommitPending },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Maps_exception(Exception ex, string? expected) =>
        Assert.Equal(expected ?? ex.Message, ErrorText.ForUser(ex));

    [Fact]
    public void Constants_are_verbatim()
    {
        Assert.Equal("Không đọc/ghi được dữ liệu. Hãy kiểm tra USB hoặc ổ đĩa.", Strings.IoError);
        Assert.Equal("Không đủ quyền truy cập.", Strings.AccessDenied);
        Assert.Equal("Đã xảy ra lỗi không mong muốn.", Strings.Unexpected);
        Assert.Equal("Kho chỉ đọc — nhật ký được lưu tạm trên máy này", Strings.ReadOnlyNotice);
        Assert.Equal("Kho phiên bản {0} · cập nhật {1:dd/MM/yyyy}", Strings.VaultInfo);
        Assert.Equal("Gói này không hỗ trợ gỡ tự động", Strings.NoUninstall);
        Assert.Equal("Không có trong kho", Strings.NotInVault);
    }
}
