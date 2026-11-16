namespace Installer.Core.Platform;

/// <summary>Reads whether UEFI Secure Boot is on. Null = no Secure Boot (legacy BIOS) or unreadable.</summary>
public interface ISecureBoot
{
    bool? Enabled { get; }
}

public sealed class SecureBootInfo : ISecureBoot
{
    public bool? Enabled
    {
        get
        {
            try
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
                return k?.GetValue("UEFISecureBootEnabled") is int v ? v == 1 : (bool?)null;
            }
            catch (Exception) { return null; }
        }
    }
}

/// <summary>How to turn Secure Boot off in BIOS, by machine maker. Keys/menus are the publicly documented defaults.</summary>
public static class SecureBootGuide
{
    public static string For(string? manufacturer)
    {
        var m = (manufacturer ?? "").ToLowerInvariant();
        if (m.Contains("dell")) return "Khởi động lại, bấm F2 để vào BIOS → mục Boot Configuration (hoặc Secure Boot) → đặt Secure Boot = Disabled → F10 để lưu và thoát.";
        if (m.Contains("hp") || m.Contains("hewlett")) return "Khởi động lại, bấm F10 (hoặc Esc rồi F10) để vào BIOS → tab Security hoặc Boot Options → Secure Boot = Disabled → F10 để lưu và thoát.";
        if (m.Contains("lenovo")) return "Khởi động lại, bấm F1 (laptop ThinkPad) hoặc F2 để vào BIOS → tab Security → Secure Boot = Disabled → F10 để lưu và thoát.";
        if (m.Contains("asus")) return "Khởi động lại, bấm F2 hoặc Del để vào BIOS → Advanced Mode (F7) → tab Boot → Secure Boot → Secure Boot Control = Disabled → F10 để lưu và thoát.";
        if (m.Contains("acer")) return "Khởi động lại, bấm F2 để vào BIOS → tab Boot hoặc Security (cần đặt Supervisor Password trước) → Secure Boot = Disabled → F10 để lưu và thoát.";
        if (m.Contains("micro-star") || m.Contains("msi")) return "Khởi động lại, bấm Del để vào BIOS → Settings → Advanced → Windows OS Configuration → Secure Boot = Disabled → F10 để lưu và thoát.";
        if (m.Contains("gigabyte")) return "Khởi động lại, bấm Del để vào BIOS → tab Boot → Secure Boot = Disabled → F10 để lưu và thoát.";
        return "Khởi động lại và vào BIOS/UEFI (thường bấm một trong các phím Del, F2, F10, F1 hoặc Esc ngay khi bật máy) → tìm mục Secure Boot trong tab Boot hoặc Security → đặt Disabled → lưu và thoát (thường là F10).";
    }
}
