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
        if (_settings.HasAnyProvider && CatalogSections.All(section => section.Games.Count == 0))
        {
            await LoadDiscoveryAsync(reset: true);
        }
    }
    private void ShowDiscoverView()
    {
        DiscoveryPanel.Visibility = Visibility.Visible;
        LibraryPanel.Visibility = Visibility.Collapsed;
        DiscoverButton.Background = ResourceBrush("NavSelectedBackground");
        AllGamesButton.Background = Brushes.Transparent;
    }
    private async Task LoadDiscoveryAsync(bool reset)
    {
        ConfigureCatalogState();
        if (!_settings.HasAnyProvider)
        {
            return;
        }

        if (reset)
        {
            _catalogCancellation?.Cancel();
            _catalogCancellation?.Dispose();
            _catalogCancellation = new CancellationTokenSource();
            foreach (var section in CatalogSections)
            {
                section.Clear();
            }
        }

        if (!reset)
        {
            return;
        }

        var loadVersion = ++_catalogLoadVersion;
        DiscoveryLoadingState.Visibility = Visibility.Visible;
        var cancellationToken = _catalogCancellation?.Token ?? CancellationToken.None;

        try
        {
            foreach (var section in CatalogSections)
            {
                section.TryBeginLoading();
            }

            var pages = await _gameApi.GetCatalogBatchAsync(
                CatalogSections.Select(section => section.Feed),
                1,
                _settings,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var section in CatalogSections)
            {
                section.ApplyPage(
                    pages.GetValueOrDefault(section.Feed) ?? new CatalogPage(),
                    1,
                    reset: true);
            }

            if (CatalogSections.All(section => section.Games.Count == 0))
            {
                DiscoveryUnavailableTitle.Text = "Catálogo temporariamente indisponível";
                DiscoveryUnavailableMessage.Text =
                    "As fontes configuradas não retornaram jogos agora. Verifique as credenciais ou tente novamente mais tarde.";
                DiscoveryContent.Visibility = Visibility.Collapsed;
                DiscoveryUnavailableState.Visibility = Visibility.Visible;
            }
        }
        catch (OperationCanceledException)
        {
            // A new catalog load superseded this one.
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível carregar o catálogo: {ex.Message}");
            if (CatalogSections.All(section => section.Games.Count == 0))
            {
                DiscoveryUnavailableTitle.Text = "Catálogo temporariamente indisponível";
                DiscoveryUnavailableMessage.Text = ex.Message;
                DiscoveryContent.Visibility = Visibility.Collapsed;
                DiscoveryUnavailableState.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            if (loadVersion == _catalogLoadVersion)
            {
                foreach (var section in CatalogSections)
                {
                    section.FinishLoading();
                }

                DiscoveryLoadingState.Visibility = Visibility.Collapsed;
            }
        }
    }
    private async Task LoadNextCatalogPageAsync(
        CatalogSection section,
        CancellationToken cancellationToken)
    {
        if (!section.TryBeginLoading())
        {
            return;
        }

        try
        {
            var nextPage = section.CurrentPage + 1;
            var page = await _gameApi.GetCatalogAsync(
                section.Feed,
                nextPage,
                _settings,
                cancellationToken);
            section.ApplyPage(page, nextPage, reset: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The catalog was refreshed or the window is closing.
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível carregar mais jogos: {ex.Message}");
        }
        finally
        {
            section.FinishLoading();
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

        var cancellationToken = _catalogCancellation?.Token ?? CancellationToken.None;
        await LoadNextCatalogPageAsync(section, cancellationToken);
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
            var cancellationToken = _catalogCancellation?.Token ?? CancellationToken.None;
            await LoadNextCatalogPageAsync(section, cancellationToken);
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
        if (DiscoveryUnavailableState is null || DiscoveryContent is null)
        {
            return;
        }

        DiscoveryUnavailableState.Visibility = _settings.HasAnyProvider
            ? Visibility.Collapsed
            : Visibility.Visible;
        DiscoveryContent.Visibility = _settings.HasAnyProvider
            ? Visibility.Visible
            : Visibility.Collapsed;
        DiscoveryUnavailableTitle.Text = "Catálogo online opcional";
        DiscoveryUnavailableMessage.Text =
            "Configure IGDB ou RAWG para ver recomendações e lançamentos. Sua biblioteca e o cadastro manual continuam funcionando normalmente.";
        CatalogAttributionText.Text = (_settings.HasIgdb, _settings.HasRawg) switch
        {
            (true, true) => "Dados por IGDB e RAWG",
            (true, false) => "Dados por IGDB",
            (false, true) => "Dados por RAWG",
            _ => string.Empty
        };
    }
}
