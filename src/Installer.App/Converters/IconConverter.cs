using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Installer.App.Converters;

/// <summary>
/// PNG bytes (or base64 text) → frozen bitmap. Anything missing or undecodable yields <see cref="Fallback"/>
/// (the default package icon, or null for the installed-app icon): a corrupt icon.png must never break the list.
/// </summary>
public sealed class IconConverter : IValueConverter
{
    private static readonly ConditionalWeakTable<byte[], ImageSource> Cache = new();
    private static readonly object Failed = new();
    private static readonly ConditionalWeakTable<byte[], object> FailedCache = new();

    public ImageSource? Fallback { get; set; }

    /// <summary>
    /// Pixel width to decode at (icons are drawn at 48 px; 2× keeps them crisp on high-DPI screens). Always set, so a
    /// small PNG declaring huge dimensions is scaled during decode instead of allocating a full-size bitmap.
    /// </summary>
    public int DecodeSize { get; set; } = 96;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var bytes = value switch
        {
            byte[] b => b,
            string s when s.Length > 0 => TryBase64(s),
            _ => null,
        };
        if (bytes is null || bytes.Length == 0) return Fallback;
        if (Cache.TryGetValue(bytes, out var hit)) return hit;
        if (FailedCache.TryGetValue(bytes, out _)) return Fallback;
        var img = Decode(bytes, DecodeSize);
        if (img is null) { FailedCache.AddOrUpdate(bytes, Failed); return Fallback; }
        Cache.AddOrUpdate(bytes, img);
        return img;
    }

    internal static ImageSource? Decode(byte[] bytes, int decodeSize)
    {
        try
        {
            using var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeSize > 0) bmp.DecodePixelWidth = decodeSize;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp.PixelWidth > 0 ? bmp : null;
        }
        catch (Exception) { return null; }
    }

    private static byte[]? TryBase64(string s)
    {
        try { return System.Convert.FromBase64String(s); }
        catch (FormatException) { return null; }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
