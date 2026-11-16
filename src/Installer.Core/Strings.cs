namespace Installer.Core;

public static class Strings
{
    public const string UnsafePath = "Đường dẫn đích không an toàn.";
    public const string WorkspaceUnsafe = "Không tạo được thư mục làm việc an toàn.";
    public const string VaultTampered = "Kho bị sửa đổi, không thể sử dụng.";
    public const string BadCredentials = "Sai tên đăng nhập hoặc mật khẩu.";
    public const string AccountLocked = "Tài khoản bị khóa đến {0:HH:mm}.";
    public const string PackageCorrupt = "Gói bị hỏng hoặc không đầy đủ.";
    public const string VaultNotFound = "Không tìm thấy kho (thư mục vault) cạnh SecureInstaller.exe.";
    public const string IoError = "Không đọc/ghi được dữ liệu. Hãy kiểm tra USB hoặc ổ đĩa.";
    public const string AccessDenied = "Không đủ quyền truy cập.";
    public const string Unexpected = "Đã xảy ra lỗi không mong muốn.";
    public const string ReadOnlyNotice = "Kho chỉ đọc — nhật ký được lưu tạm trên máy này";
    public const string VaultInfo = "Kho phiên bản {0} · cập nhật {1:dd/MM/yyyy}";
    public const string NoUninstall = "Phần mềm này không hỗ trợ gỡ tự động";
    public const string Uninstalled = "Đã gỡ cài đặt";
    public const string UninstallTimeout = "Quá thời gian gỡ cài đặt";
    public const string UninstallIncomplete = "Gỡ chưa hoàn tất";
    public const string UninstallFolderFailed = "Không xóa được thư mục cài đặt";
    public const string NotInVault = "Không có trong kho";
    public const string PackageHidden = "Phần mềm đang ẩn";
    public const string ReasonWindowsCracked = "Windows có dấu hiệu crack";
    public const string ReasonOfficeCracked = "Office có dấu hiệu crack";
    public const string ReasonInstalledGte = "Đã cài phiên bản {0} (bằng hoặc mới hơn)";
    public const string ReasonKeywordFiles = "Phát hiện {0} file chứa từ khóa";
    public const string ReasonRequireInternet = "Yêu cầu kết nối internet";
}
