using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Planner.Core.Models;

namespace Planner.App.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (Invert || string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class NullOrEmptyToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var empty = value is null || value is string s && string.IsNullOrWhiteSpace(s);
        if (Invert)
        {
            empty = !empty;
        }

        return empty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InvertBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

public sealed class EqualityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Equals(value?.ToString(), parameter?.ToString());

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class HexToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            var hex = value as string ?? "#0F766E";
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        }
        catch
        {
            return new SolidColorBrush(Color.FromRgb(15, 118, 110));
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PlannerTaskStatus status)
        {
            return ThemeBrush("StatusIdleBrush", Color.FromRgb(100, 116, 139));
        }

        return status switch
        {
            PlannerTaskStatus.Baslamadi => ThemeBrush("StatusIdleBrush", Color.FromRgb(100, 116, 139)),
            PlannerTaskStatus.DevamEdiyor => ThemeBrush("StatusActiveBrush", Color.FromRgb(37, 99, 235)),
            PlannerTaskStatus.Duraklatildi => ThemeBrush("StatusPausedBrush", Color.FromRgb(217, 119, 6)),
            PlannerTaskStatus.Tamamlandi => ThemeBrush("StatusDoneBrush", Color.FromRgb(5, 150, 105)),
            _ => ThemeBrush("StatusIdleBrush", Color.FromRgb(100, 116, 139))
        };
    }

    private static SolidColorBrush ThemeBrush(string key, Color fallback)
    {
        return Application.Current?.TryFindResource(key) as SolidColorBrush
               ?? new SolidColorBrush(fallback);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
