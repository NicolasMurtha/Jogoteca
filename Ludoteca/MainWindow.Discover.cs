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
    private async void DiscoverButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDiscoverView();
        if (_settings.HasAnyProvider && Discover.IsEmpty)
        {
            await LoadDiscoveryAsync();
        }
    }
    private void ShowDiscoverView()
    {
        DiscoveryPanel.Visibility = Visibility.Visible;
        LibraryPanel.Visibility = Visibility.Collapsed;
        DiscoverButton.Background = ResourceBrush("NavSelectedBackground");
        AllGamesButton.Background = Brushes.Transparent;
    }
    private async Task LoadDiscoveryAsync()
    {
        if (await Discover.LoadAsync(_settings) is { } error)
        {
            ShowToast(error);
        }
    }
    private async Task LoadNextCatalogPageAsync(CatalogSection section)
    {
        if (await Discover.LoadNextPageAsync(section, _settings) is { } error)
        {
            ShowToast(error);
        }
    }
    private async void CatalogSectionScrollViewer_ScrollChanged(
        object sender,
        ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange <= 0 ||
            sender is not ScrollViewer scrollViewer ||
            scrollViewer.DataContext is not CatalogSection section ||
            scrollViewer.ScrollableWidth <= 0)
        {
            return;
        }

        var remaining = scrollViewer.ScrollableWidth - scrollViewer.HorizontalOffset;
        if (remaining > scrollViewer.ViewportWidth * 1.25)
        {
            return;
        }

        await LoadNextCatalogPageAsync(section);
    }
    private void CatalogSectionScrollViewer_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (DiscoveryScrollViewer is null)
        {
            return;
        }

        DiscoveryScrollViewer.ScrollToVerticalOffset(
            Math.Max(0, DiscoveryScrollViewer.VerticalOffset - e.Delta));
        e.Handled = true;
    }
    private void ScrollCatalogSectionLeft_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScrollViewer scrollViewer)
        {
            return;
        }

        scrollViewer.ScrollToHorizontalOffset(
            Math.Max(0, scrollViewer.HorizontalOffset - scrollViewer.ViewportWidth * 0.85));
    }
    private async void ScrollCatalogSectionRight_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScrollViewer scrollViewer)
        {
            return;
        }

        var targetOffset =
            scrollViewer.HorizontalOffset + scrollViewer.ViewportWidth * 0.85;
        scrollViewer.ScrollToHorizontalOffset(targetOffset);

        if (scrollViewer.DataContext is CatalogSection section &&
            targetOffset >= scrollViewer.ScrollableWidth - scrollViewer.ViewportWidth * 0.3)
        {
            await LoadNextCatalogPageAsync(section);
        }
    }
    private async void CatalogCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameSearchResult game)
        {
            return;
        }

        _selectedCatalogGame = game;
        CatalogDetailPanel.DataContext = game;
        AddCatalogGameButton.IsEnabled = true;
        AddCatalogGameButton.Content = "Adicionar à biblioteca";
        CatalogDetailOverlay.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(game.Description))
        {
            return;
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await _gameApi.EnrichAsync(game, _settings, cancellation.Token);
            CatalogDetailPanel.DataContext = null;
            CatalogDetailPanel.DataContext = game;
        }
        catch
        {
            // Catalog metadata is still useful when the provider detail call fails.
        }
    }
    private void CloseCatalogDetail_Click(object sender, RoutedEventArgs e)
    {
        CatalogDetailOverlay.Visibility = Visibility.Collapsed;
        _selectedCatalogGame = null;
    }
    private async void AddCatalogGame_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCatalogGame is null)
        {
            return;
        }

        AddCatalogGameButton.IsEnabled = false;
        AddCatalogGameButton.Content = "Adicionando...";
        if (await AddGameToLibraryAsync(_selectedCatalogGame))
        {
            AddCatalogGameButton.Content = "Adicionado";
        }
        else
        {
            AddCatalogGameButton.IsEnabled = true;
            AddCatalogGameButton.Content = "Adicionar à biblioteca";
        }
    }
    private void CatalogAttributionText_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        OpenExternal(_settings.HasRawg
            ? "https://rawg.io/"
            : "https://www.igdb.com/");
    }
    private void ConfigureCatalogState()
    {
        Discover.Configure(_settings);
    }
}
