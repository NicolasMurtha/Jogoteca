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
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettings();
    }
    private void OpenSettings()
    {
        IgdbClientIdBox.Password = _settings.IgdbClientId;
        IgdbClientSecretBox.Password = _settings.IgdbClientSecret;
        RawgApiKeyBox.Password = _settings.RawgApiKey;
        IgdbStatusText.Visibility = Visibility.Collapsed;
        RawgStatusText.Visibility = Visibility.Collapsed;
        SettingsOverlay.Visibility = Visibility.Visible;
    }
    private void ShowApiWelcomeIfNeeded()
    {
        if (_settings.HasAnyProvider || _settings.HideApiWelcome)
        {
            return;
        }

        HideApiWelcomeCheckBox.IsChecked = false;
        ApiWelcomeOverlay.Visibility = Visibility.Visible;
    }
    private async Task CloseApiWelcomeAsync()
    {
        ApiWelcomeOverlay.Visibility = Visibility.Collapsed;
        if (HideApiWelcomeCheckBox.IsChecked != true)
        {
            return;
        }

        _settings.HideApiWelcome = true;
        try
        {
            await _settingsService.SaveAsync(_settings);
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar a preferência: {ex.Message}");
        }
    }
    private async void CloseApiWelcome_Click(object sender, RoutedEventArgs e)
    {
        await CloseApiWelcomeAsync();
    }
    private async void ConfigureFromApiWelcome_Click(object sender, RoutedEventArgs e)
    {
        await CloseApiWelcomeAsync();
        OpenSettings();
        ShowApiGuide(scrollToIgdb: false);
    }
    private void OpenRawgGuide_Click(object sender, RoutedEventArgs e)
    {
        ShowApiGuide(scrollToIgdb: false);
    }
    private void OpenIgdbGuide_Click(object sender, RoutedEventArgs e)
    {
        ShowApiGuide(scrollToIgdb: true);
    }
    private void ShowApiGuide(bool scrollToIgdb)
    {
        ApiGuideOverlay.Visibility = Visibility.Visible;
        ApiGuideScrollViewer.ScrollToTop();
        if (scrollToIgdb)
        {
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                () => IgdbGuideSection.BringIntoView());
        }
    }
    private void CloseApiGuide_Click(object sender, RoutedEventArgs e)
    {
        ApiGuideOverlay.Visibility = Visibility.Collapsed;
    }
    private void ApiGuideOpenSettings_Click(object sender, RoutedEventArgs e)
    {
        ApiGuideOverlay.Visibility = Visibility.Collapsed;
        if (SettingsOverlay.Visibility != Visibility.Visible)
        {
            OpenSettings();
        }
    }
    private void OpenTwitchConsole_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://dev.twitch.tv/console");
    }
    private void ShowProviderStatus(TextBlock target, string providerName, ProviderCheckResult? result)
    {
        if (result is null)
        {
            target.Visibility = Visibility.Collapsed;
            return;
        }

        (target.Text, target.Foreground) = result switch
        {
            ProviderCheckResult.Valid => (
                $"✓ {providerName} conectado.",
                ResourceBrush("StatRatingForeground")),
            ProviderCheckResult.Invalid => (
                $"✗ O {providerName} recusou essas credenciais. Confira se copiou tudo certo, sem espaços a mais.",
                ResourceBrush("DangerForeground")),
            _ => (
                $"Não foi possível verificar o {providerName} agora (sem internet?). As credenciais foram salvas mesmo assim.",
                ResourceBrush("TextSecondary"))
        };
        target.Visibility = Visibility.Visible;
    }
    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }
    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var newSettings = new AppSettings
        {
            IgdbClientId = IgdbClientIdBox.Password.Trim(),
            IgdbClientSecret = IgdbClientSecretBox.Password.Trim(),
            RawgApiKey = RawgApiKeyBox.Password.Trim(),
            Theme = _settings.Theme,
            HideApiWelcome = _settings.HideApiWelcome
        };

        var hasPartialIgdb =
            !newSettings.HasIgdb &&
            (!string.IsNullOrWhiteSpace(newSettings.IgdbClientId) ||
             !string.IsNullOrWhiteSpace(newSettings.IgdbClientSecret));
        if (hasPartialIgdb)
        {
            IgdbStatusText.Text = "Preencha o Client ID e o Client Secret do IGDB.";
            IgdbStatusText.Foreground = ResourceBrush("DangerForeground");
            IgdbStatusText.Visibility = Visibility.Visible;
            return;
        }

        SaveSettingsButton.IsEnabled = false;
        SaveSettingsButton.Content = "Verificando...";
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var igdbCheck = newSettings.HasIgdb
                ? _gameApi.CheckIgdbCredentialsAsync(
                    newSettings.IgdbClientId,
                    newSettings.IgdbClientSecret,
                    cancellation.Token)
                : Task.FromResult<ProviderCheckResult>(default);
            var rawgCheck = newSettings.HasRawg
                ? _gameApi.CheckRawgKeyAsync(newSettings.RawgApiKey, cancellation.Token)
                : Task.FromResult<ProviderCheckResult>(default);
            await Task.WhenAll(igdbCheck, rawgCheck);
            ProviderCheckResult? igdbResult = newSettings.HasIgdb ? igdbCheck.Result : null;
            ProviderCheckResult? rawgResult = newSettings.HasRawg ? rawgCheck.Result : null;

            _settings = newSettings;
            await _settingsService.SaveAsync(_settings);
            ConfigureProviderUi();
            ShowProviderStatus(IgdbStatusText, "IGDB", igdbResult);
            ShowProviderStatus(RawgStatusText, "RAWG", rawgResult);

            if (igdbResult == ProviderCheckResult.Invalid || rawgResult == ProviderCheckResult.Invalid)
            {
                ShowToast("Configurações salvas, mas uma das chaves foi recusada.");
                return;
            }

            SettingsOverlay.Visibility = Visibility.Collapsed;
            ShowToast(igdbResult == ProviderCheckResult.Valid || rawgResult == ProviderCheckResult.Valid
                ? "Catálogo conectado! Configurações salvas com segurança."
                : "Configurações salvas com segurança.");
            await LoadDiscoveryAsync(reset: true);
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar as configurações: {ex.Message}");
        }
        finally
        {
            SaveSettingsButton.IsEnabled = true;
            SaveSettingsButton.Content = "Salvar configurações";
        }
    }
    private void OpenIgdbDocs_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://api-docs.igdb.com/#account-creation");
    }
    private void OpenRawgDocs_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal("https://rawg.io/apidocs");
    }
    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal(_database.AppDataDirectory);
    }
    private async void ExportData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Escolha onde salvar a exportação da JOGOTECA",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ExportDataButton.IsEnabled = false;
            ExportDataButton.Content = "Exportando...";
            var path = await _dataExportService.ExportGamesCsvAsync(dialog.FolderName);
            ShowToast("CSV exportado com sucesso.");
            OpenExternal(Path.GetDirectoryName(path) ?? dialog.FolderName);
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível exportar os dados: {ex.Message}");
        }
        finally
        {
            ExportDataButton.IsEnabled = true;
            ExportDataButton.Content = "Exportar dados";
        }
    }
    private async void ClearLibrary_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await ShowConfirmationAsync(
            "Limpar biblioteca",
            "Isso vai apagar todos os jogos, notas, reviews e capas salvas.\n\nSuas listas, configurações e chaves API serão mantidas.",
            "Limpar");
        if (!confirmed)
        {
            return;
        }

        try
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            DetailOverlay.Visibility = Visibility.Collapsed;
            SearchOverlay.Visibility = Visibility.Collapsed;
            CatalogDetailOverlay.Visibility = Visibility.Collapsed;
            ManualGameOverlay.Visibility = Visibility.Collapsed;
            ListEditorOverlay.Visibility = Visibility.Collapsed;

            _selectedGame = null;
            _selectedCatalogGame = null;
            _activeLibraryFilter = GameLibraryFilter.None;
            _librarySortMode = LibrarySortMode.Default;
            NotifySortHeaderChanges();
            _libraryNameFilter = string.Empty;
            LibraryNameFilterBox.Clear();
            SetSelectedListId(null);
            SelectedListTitle = "Biblioteca";
            SelectedListSubtitle = "Todos os jogos que você adicionou";
            SetListActionButtonsVisible(false);

            await _database.ClearLibraryAsync();
            ShowLibraryView();
            await RefreshAllAsync();
            ShowToast("Biblioteca limpa.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível limpar a biblioteca: {ex.Message}");
        }
    }
    private async void ResetDatabase_Click(object sender, RoutedEventArgs e)
    {
        var confirmed = await ShowConfirmationAsync(
            "Resetar database",
            "Isso vai apagar jogos, listas, notas, reviews, configurações e capas salvas.\n\nEssa ação não pode ser desfeita.",
            "Resetar");
        if (!confirmed)
        {
            return;
        }

        try
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            DetailOverlay.Visibility = Visibility.Collapsed;
            SearchOverlay.Visibility = Visibility.Collapsed;
            CatalogDetailOverlay.Visibility = Visibility.Collapsed;
            ManualGameOverlay.Visibility = Visibility.Collapsed;
            ListEditorOverlay.Visibility = Visibility.Collapsed;

            _selectedGame = null;
            _selectedCatalogGame = null;
            _editingListId = null;
            _activeLibraryFilter = GameLibraryFilter.None;
            _librarySortMode = LibrarySortMode.Default;
            NotifySortHeaderChanges();
            _libraryNameFilter = string.Empty;
            LibraryNameFilterBox.Clear();

            var currentTheme = _settings.Theme;
            await _database.ResetDatabaseAsync();
            _settings = new AppSettings { Theme = currentTheme };
            await _settingsService.SaveAsync(_settings);

            SearchResults.Clear();
            foreach (var section in CatalogSections)
            {
                section.Clear();
            }
            SetSelectedListId(null);
            SelectedListTitle = "Biblioteca";
            SelectedListSubtitle = "Todos os jogos que você adicionou";
            SetListActionButtonsVisible(false);
            ConfigureProviderUi();
            ShowLibraryView();
            await RefreshAllAsync();
            ShowToast("Database resetado.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível resetar o database: {ex.Message}");
        }
    }
    private void ConfigureProviderUi()
    {
        if (ProviderDescriptionText is null || OnlineSearchPanel is null)
        {
            return;
        }

        ProviderDescriptionText.Text = (_settings.HasIgdb, _settings.HasRawg) switch
        {
            (true, true) => "Busca no IGDB com fallback automático para o RAWG.",
            (true, false) => "Busca somente no IGDB, a fonte configurada.",
            (false, true) => "Busca somente no RAWG, a fonte configurada.",
            _ => "Sem API configurada. O cadastro manual continua disponível."
        };
        OnlineSearchPanel.Visibility = _settings.HasAnyProvider
            ? Visibility.Visible
            : Visibility.Collapsed;
        ConfigureCatalogState();
    }
}
