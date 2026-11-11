namespace Installer.Core.Install;

public static class ExitCodeMapper
{
    public static InstallOutcome Map(int exitCode) => exitCode switch
    {
        0 => new(InstallStatus.Success, 0, "Cài đặt thành công"),
        3010 or 1641 => new(InstallStatus.RebootRequired, exitCode, "Cần khởi động lại máy"),
        1602 => new(InstallStatus.Failed, exitCode, Errors.ErrorText.Cancelled),
        1618 => new(InstallStatus.Failed, exitCode, "Đang có một bộ cài khác chạy"),
        _ => new(InstallStatus.Failed, exitCode, $"Lỗi cài đặt (mã {exitCode})"),
    };
}
