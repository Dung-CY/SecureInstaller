namespace Installer.Core.Scanning;

public enum DeviceCategory { KeyboardMouse, UsbStorage, Printer, Camera, Bluetooth, Audio, Other }

public static class PeripheralCategorizer
{
    public static DeviceCategory Categorize(DeviceInfo d)
    {
        bool Cls(params string[] c) => d.PnpClass != null && c.Contains(d.PnpClass, StringComparer.OrdinalIgnoreCase);
        bool Pre(string p) => d.DeviceId.StartsWith(p, StringComparison.OrdinalIgnoreCase);

        if (Cls("Keyboard", "Mouse") || Pre(@"HID\")) return DeviceCategory.KeyboardMouse;
        if (Pre(@"USBSTOR\") || Cls("DiskDrive", "WPD")) return DeviceCategory.UsbStorage;
        if (Cls("Printer", "PrintQueue") || Pre(@"SWD\PRINTENUM\")) return DeviceCategory.Printer;
        if (Cls("Camera", "Image")) return DeviceCategory.Camera;
        if (Cls("Bluetooth") || Pre(@"BTHENUM\")) return DeviceCategory.Bluetooth;
        if (Cls("MEDIA", "AudioEndpoint")) return DeviceCategory.Audio;
        return DeviceCategory.Other;
    }

    public static string Label(DeviceCategory c) => c switch
    {
        DeviceCategory.KeyboardMouse => "Bàn phím & chuột",
        DeviceCategory.UsbStorage => "Lưu trữ USB",
        DeviceCategory.Printer => "Máy in",
        DeviceCategory.Camera => "Camera",
        DeviceCategory.Bluetooth => "Bluetooth",
        DeviceCategory.Audio => "Âm thanh",
        _ => "Khác"
    };
}
