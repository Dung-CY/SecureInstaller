using System.Globalization;
using System.Windows.Media;
using Installer.App.Converters;

namespace Installer.App.Tests;

public class IconConverterTests
{
    static T OnSta<T>(Func<T> f)
    {
        T result = default!;
        Exception? error = null;
        var t = new Thread(() => { try { result = f(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error != null) throw new Xunit.Sdk.XunitException("STA call failed: " + error);
        return result;
    }

    static (object? Result, ImageSource Fallback) Convert(object? value) => OnSta(() =>
    {
        var fallback = new DrawingImage();
        fallback.Freeze();
        var c = new IconConverter { Fallback = fallback };
        return (c.Convert(value, typeof(ImageSource), null, CultureInfo.InvariantCulture), (ImageSource)fallback);
    });

    [Fact]
    public void Corrupt_bytes_return_fallback()
    {
        var (r, fb) = Convert(new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4, 5 });
        Assert.Same(fb, r);
    }

    [Fact]
    public void Invalid_base64_returns_fallback()
    {
        var (r, fb) = Convert("%%không phải base64%%");
        Assert.Same(fb, r);
    }

    [Fact]
    public void Null_and_empty_return_fallback()
    {
        var (r, fb) = Convert(null);
        Assert.Same(fb, r);
        var (r2, fb2) = Convert(Array.Empty<byte>());
        Assert.Same(fb2, r2);
    }
}
