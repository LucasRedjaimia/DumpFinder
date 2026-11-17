using System.Globalization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Styling;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var settings = AppSettings.Load();
        Loc.SetLanguage(string.IsNullOrEmpty(settings.Language) ? Loc.SystemLanguage() : settings.Language);
        ApplyTheme(settings.Theme);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = new MainWindow(settings);
        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>0 = follow macOS, 1 = dark, 2 = light (Windows AppThemeMode order).</summary>
    public static void ApplyTheme(int mode)
    {
        if (Current is null) return;
        Current.RequestedThemeVariant = mode switch { 1 => ThemeVariant.Dark, 2 => ThemeVariant.Light, _ => ThemeVariant.Default };
    }
}

/// <summary>"Home" → the Icon.Home geometry (Themes/Icons.axaml).</summary>
public sealed class IconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = "Icon." + (value as string ?? parameter as string);
        return Application.Current is { } app && app.TryGetResource(key, app.ActualThemeVariant, out var g) ? g as Geometry : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
