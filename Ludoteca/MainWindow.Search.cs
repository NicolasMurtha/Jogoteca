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
    private async void SearchOnline_Click(object sender, RoutedEventArgs e)
    {
        SearchOverlay.Visibility = Visibility.Visible;
        OverlaySearchBox.Clear();
        ConfigureProviderUi();

        if (_settings.HasAnyProvider && !string.IsNullOrWhiteSpace(OverlaySearchBox.Text))
        {
            await RunSearchAsync(OverlaySearchBox.Text);
        }
        else if (!_settings.HasAnyProvider)
        {
            Search.Clear();
            Search.ShowEmpty(
                "Busca online não configurada",
                "Cadastre manualmente ou configure uma API. Nenhuma informação além do nome é obrigatória.");
        }
        else
        {
            Search.ShowEmpty(
                "Busque pelo nome de um jogo",
                "Os resultados da fonte configurada aparecerão aqui.");
            _ = Dispatcher.BeginInvoke(() => OverlaySearchBox.Focus());
        }
    }
    private void FocusSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchOnline_Click(sender, e);
    }
    private void CloseSearchOverlay_Click(object sender, RoutedEventArgs e)
    {
        Search.Cancel();
        SearchOverlay.Visibility = Visibility.Collapsed;
    }
    private async void OverlaySearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.HasAnyProvider)
        {
            OpenManualGame_Click(sender, e);
            return;
        }

        await RunSearchAsync(OverlaySearchBox.Text);
    }
    private async void OverlaySearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _settings.HasAnyProvider)
        {
            await RunSearchAsync(OverlaySearchBox.Text);
        }
    }
    private void OverlaySearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OverlaySearchPlaceholder is not null)
        {
            OverlaySearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(OverlaySearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }
    private async Task RunSearchAsync(string query)
    {
        if (await Search.RunAsync(query, _settings) is { } message)
        {
            ShowToast(message);
        }
    }
    private async void AddSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameSearchResult game)
        {
            return;
        }

        if (sender is Button button)
        {
            button.IsEnabled = false;
            button.Content = "Adicionando...";
        }

        if (await AddGameToLibraryAsync(game))
        {
            if (sender is Button completedButton)
            {
                completedButton.Content = "Adicionado";
            }
        }
        else if (sender is Button failedButton)
        {
            failedButton.IsEnabled = true;
            failedButton.Content = "Adicionar";
        }
    }
    private async Task<bool> AddGameToLibraryAsync(GameSearchResult game)
    {
        try
        {
            if (await _database.FindEquivalentGameAsync(game) is int existingId)
            {
                if (Library.SelectedListId.HasValue)
                {
                    await _database.AddGameToListAsync(existingId, Library.SelectedListId.Value);
                }

                await RefreshAllAsync();
                ShowToast(Library.SelectedListId.HasValue
                    ? $"\"{game.Name}\" já estava na biblioteca e foi colocado na lista atual."
                    : $"\"{game.Name}\" já está na sua biblioteca.");
                return true;
            }

            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var gameToSave = game;
            try
            {
                gameToSave = await _gameApi.GetGameDetailsAsync(
                    game,
                    _settings,
                    cancellation.Token) ?? game;
            }
            catch
            {
                try
                {
                    await _gameApi.EnrichAsync(game, _settings, cancellation.Token);
                }
                catch
                {
                    // Search data is enough when a provider detail call is unavailable.
                }
            }

            var coverPath = string.Empty;
            try
            {
                coverPath = await _gameApi.DownloadCoverAsync(
                    gameToSave,
                    _database.CoversDirectory,
                    cancellation.Token);
            }
            catch
            {
                // The remote URL remains available when local cover caching fails.
            }

            var gameId = await _database.UpsertGameAsync(gameToSave, coverPath);
            var addedToCurrentList = false;
            if (Library.SelectedListId.HasValue)
            {
                await _database.AddGameToListAsync(gameId, Library.SelectedListId.Value);
                addedToCurrentList = true;
            }

            await RefreshAllAsync();
            ShowToast(addedToCurrentList
                ? $"\"{game.Name}\" foi adicionado à biblioteca e à lista atual."
                : $"\"{game.Name}\" foi adicionado à biblioteca.");
            return true;
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível adicionar o jogo: {ex.Message}");
            return false;
        }
    }
    private void OpenManualGame_Click(object sender, RoutedEventArgs e)
    {
        ManualNameBox.Text = OverlaySearchBox.Text.Trim();
        ManualCategoryBox.Clear();
        ManualYearBox.Clear();
        ManualDescriptionBox.Clear();
        ManualCoverUrlBox.Clear();
        ManualGameOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => ManualNameBox.Focus());
    }
    private void CloseManualGame_Click(object sender, RoutedEventArgs e)
    {
        ManualGameOverlay.Visibility = Visibility.Collapsed;
    }
    private async void SaveManualGame_Click(object sender, RoutedEventArgs e)
    {
        var name = ManualNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("Digite o nome do jogo.");
            ManualNameBox.Focus();
            return;
        }

        int? releaseYear = null;
        var yearText = ManualYearBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(yearText))
        {
            if (!int.TryParse(yearText, out var parsedYear) ||
                parsedYear < 1950 ||
                parsedYear > DateTime.Now.Year + 10)
            {
                ShowToast("Informe um ano válido ou deixe o campo vazio.");
                ManualYearBox.Focus();
                return;
            }

            releaseYear = parsedYear;
        }

        var coverUrl = ManualCoverUrlBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(coverUrl) &&
            (!Uri.TryCreate(coverUrl, UriKind.Absolute, out var coverUri) ||
             (coverUri.Scheme != Uri.UriSchemeHttp && coverUri.Scheme != Uri.UriSchemeHttps)))
        {
            ShowToast("A URL da capa precisa começar com http:// ou https://.");
            ManualCoverUrlBox.Focus();
            return;
        }

        var game = new GameSearchResult
        {
            Source = "manual",
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = ManualDescriptionBox.Text.Trim(),
            Category = ManualCategoryBox.Text.Trim(),
            ReleaseYear = releaseYear,
            CoverUrl = coverUrl
        };

        var coverPath = string.Empty;
        if (!string.IsNullOrWhiteSpace(coverUrl))
        {
            try
            {
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                coverPath = await _gameApi.DownloadCoverAsync(
                    game,
                    _database.CoversDirectory,
                    cancellation.Token);
            }
            catch
            {
                // The URL is kept when the local cover download is unavailable.
            }
        }

        try
        {
            var gameId = await _database.UpsertGameAsync(game, coverPath);
            var addedToCurrentList = false;
            if (Library.SelectedListId.HasValue)
            {
                await _database.AddGameToListAsync(gameId, Library.SelectedListId.Value);
                addedToCurrentList = true;
            }

            ManualGameOverlay.Visibility = Visibility.Collapsed;
            SearchOverlay.Visibility = Visibility.Collapsed;
            await RefreshAllAsync();
            ShowToast(addedToCurrentList
                ? $"\"{name}\" foi adicionado à biblioteca e à lista atual."
                : $"\"{name}\" foi adicionado à biblioteca.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível adicionar o jogo: {ex.Message}");
        }
    }
}
