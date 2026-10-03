using System.Windows;
using MemoryGuard.Core.Models;
using Microsoft.Win32;

namespace MemoryGuard.UI.Themes;

public static class ThemeManager
{
    private static ResourceDictionary? _currentThemeDict;

    public static void ApplyTheme(ThemeMode theme)
    {
        bool useDark = theme switch
        {
            ThemeMode.Dark => true,
            ThemeMode.Light => false,
            ThemeMode.System => IsWindowsInDarkMode(),
            _ => true
        };

        string themeUri = useDark 
            ? "pack://application:,,,/MemoryGuard.UI;component/Themes/DarkTheme.xaml"
            : "pack://application:,,,/MemoryGuard.UI;component/Themes/LightTheme.xaml";

        var newThemeDict = new ResourceDictionary { Source = new Uri(themeUri, UriKind.Absolute) };

        var appMerged = Application.Current?.Resources?.MergedDictionaries;
        if (appMerged != null)
        {
            if (_currentThemeDict != null)
            {
                appMerged.Remove(_currentThemeDict);
            }

            appMerged.Insert(0, newThemeDict);
            _currentThemeDict = newThemeDict;
        }
    }

    public static bool IsWindowsInDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int intVal)
            {
                return intVal == 0;
            }
        }
        catch
        {
            // Ignore
        }
        return true;
    }
}
