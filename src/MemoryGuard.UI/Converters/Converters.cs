using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MemoryGuard.Core.Models;

namespace MemoryGuard.UI.Converters;

public sealed class BytesToReadableSizeConverter : IValueConverter
{
    public static readonly BytesToReadableSizeConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long bytes)
        {
            if (value is int intVal) bytes = intVal;
            else if (value is double dblVal) bytes = (long)dblVal;
            else return "0 B";
        }

        if (bytes < 0) bytes = 0;

        string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
        int counter = 0;
        decimal number = bytes;

        while (Math.Round(number / 1024m) >= 1m && counter < suffixes.Length - 1)
        {
            number /= 1024m;
            counter++;
        }

        return counter == 0 
            ? $"{number:n0} {suffixes[counter]}" 
            : $"{number:n1} {suffixes[counter]}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class PercentToBrushConverter : IValueConverter
{
    public static readonly PercentToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent = 0.0;
        if (value is double d) percent = d;
        else if (value is int i) percent = i;
        else if (value is float f) percent = f;

        if (percent >= 90.0)
            return new SolidColorBrush(Color.FromRgb(224, 79, 95)); // Soft Red

        if (percent >= 80.0)
            return new SolidColorBrush(Color.FromRgb(245, 166, 35)); // Warning Amber

        return new SolidColorBrush(Color.FromRgb(46, 174, 96)); // Healthy Green
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class RiskToBrushConverter : IValueConverter
{
    public static readonly RiskToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is RiskLevel risk)
        {
            return risk switch
            {
                RiskLevel.Critical => new SolidColorBrush(Color.FromRgb(224, 79, 95)),
                RiskLevel.High => new SolidColorBrush(Color.FromRgb(240, 110, 80)),
                RiskLevel.Medium => new SolidColorBrush(Color.FromRgb(245, 166, 35)),
                RiskLevel.Low => new SolidColorBrush(Color.FromRgb(46, 174, 96)),
                _ => new SolidColorBrush(Color.FromRgb(140, 140, 140))
            };
        }

        return new SolidColorBrush(Color.FromRgb(140, 140, 140));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();
    public static readonly BoolToVisibilityConverter Inverted = new(true);

    private readonly bool _isInverted;

    public BoolToVisibilityConverter(bool isInverted = false)
    {
        _isInverted = isInverted;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool boolVal => boolVal,
            string strVal => !string.IsNullOrWhiteSpace(strVal),
            int intVal => intVal != 0,
            long longVal => longVal != 0,
            _ => value != null
        };
        if (_isInverted) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Visibility v)
        {
            bool b = v == Visibility.Visible;
            return _isInverted ? !b : b;
        }
        return false;
    }
}
