using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace Installer.App.Converters;

/// <summary>Shows a path as its file name, or its folder when ConverterParameter is "dir".</summary>
public sealed class FileNamePart : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not string p ? null : parameter as string == "dir" ? Path.GetDirectoryName(p) : Path.GetFileName(p);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
