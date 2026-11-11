using Installer.Core.Scanning;

namespace Installer.Core.Tests.Scanning;

public class PeripheralCategorizerTests
{
    [Theory]
    [InlineData("Keyboard", @"X\1", DeviceCategory.KeyboardMouse)]
    [InlineData("Mouse", @"X\1", DeviceCategory.KeyboardMouse)]
    [InlineData(null, @"hid\vid_1", DeviceCategory.KeyboardMouse)]
    [InlineData(null, @"USBSTOR\DISK&VEN", DeviceCategory.UsbStorage)]
    [InlineData("DiskDrive", @"X\1", DeviceCategory.UsbStorage)]
    [InlineData("WPD", @"X\1", DeviceCategory.UsbStorage)]
    [InlineData("Printer", @"X\1", DeviceCategory.Printer)]
    [InlineData("PrintQueue", @"X\1", DeviceCategory.Printer)]
    [InlineData(null, @"SWD\PRINTENUM\{1}", DeviceCategory.Printer)]
    [InlineData("Camera", @"X\1", DeviceCategory.Camera)]
    [InlineData("Image", @"X\1", DeviceCategory.Camera)]
    [InlineData("Bluetooth", @"X\1", DeviceCategory.Bluetooth)]
    [InlineData(null, @"BTHENUM\DEV", DeviceCategory.Bluetooth)]
    [InlineData("MEDIA", @"X\1", DeviceCategory.Audio)]
    [InlineData("AudioEndpoint", @"X\1", DeviceCategory.Audio)]
    [InlineData("USB", @"USB\VID_1", DeviceCategory.Other)]
    public void Categorize_rules(string? cls, string id, DeviceCategory expected) =>
        Assert.Equal(expected, PeripheralCategorizer.Categorize(new DeviceInfo("n", cls, id)));

    [Fact]
    public void Labels_verbatim()
    {
        Assert.Equal("Bàn phím & chuột", PeripheralCategorizer.Label(DeviceCategory.KeyboardMouse));
        Assert.Equal("Lưu trữ USB", PeripheralCategorizer.Label(DeviceCategory.UsbStorage));
        Assert.Equal("Máy in", PeripheralCategorizer.Label(DeviceCategory.Printer));
        Assert.Equal("Camera", PeripheralCategorizer.Label(DeviceCategory.Camera));
        Assert.Equal("Bluetooth", PeripheralCategorizer.Label(DeviceCategory.Bluetooth));
        Assert.Equal("Âm thanh", PeripheralCategorizer.Label(DeviceCategory.Audio));
        Assert.Equal("Khác", PeripheralCategorizer.Label(DeviceCategory.Other));
    }
}
