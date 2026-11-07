using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ludoteca.Models;
using Ludoteca.Services;
using Ludoteca.Themes;
using Microsoft.Win32;

namespace Ludoteca;

public partial class MainWindow
{
    private async void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var nextTheme = IsDarkTheme ? LightTheme : DarkTheme;
        ApplyTheme(nextTheme);
        _settings.Theme = nextTheme;

        try
        {
            await _settingsService.SaveAsync(_settings);
            ShowToast(IsDarkTheme ? "Tema escuro ativado." : "Tema claro ativado.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar o tema: {ex.Message}");
        }
    }
    private void ApplyTheme(string theme)
    {
        var normalizedTheme = NormalizeTheme(theme);
        var palette = normalizedTheme == DarkTheme
            ? ThemePalettes.Dark
            : ThemePalettes.Light;

        foreach (var (resourceKey, color) in palette)
        {
            Application.Current.Resources[resourceKey] =
                new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!);
        }

        IsDarkTheme = normalizedTheme == DarkTheme;
        _settings.Theme = normalizedTheme;
        Background = ResourceBrush("AppBackground");
        Foreground = ResourceBrush("TextPrimary");
        RefreshThemeAwareBrushes();
    }
    private static string NormalizeTheme(string theme)
    {
        return string.Equals(theme, DarkTheme, StringComparison.OrdinalIgnoreCase)
            ? DarkTheme
            : LightTheme;
    }
    private void RefreshThemeAwareBrushes()
    {
        if (DiscoveryPanel is null ||
            LibraryPanel is null ||
            LibraryGridViewButton is null ||
            LibraryListViewButton is null)
        {
            return;
        }

        if (DiscoveryPanel.Visibility == Visibility.Visible)
        {
            ShowDiscoverView();
        }
        else if (LibraryPanel.Visibility == Visibility.Visible)
        {
            ShowLibraryView();
        }

        var useList = LibraryListView?.Visibility == Visibility.Visible;
        LibraryGridViewButton.Background = useList
            ? Brushes.Transparent
            : ResourceBrush("Surface");
        LibraryListViewButton.Background = useList
            ? ResourceBrush("Surface")
            : Brushes.Transparent;
    }
}
