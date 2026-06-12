using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private static readonly string[] ListColors =
    [
        "#CFC4F5",
        "#B9DDCF",
        "#F5C6B5",
        "#EEDDA8",
        "#BFD6ED",
        "#E6C4D8"
    ];

    private readonly DatabaseService _database = new();
    private readonly GameApiService _gameApi = new();
    private readonly DispatcherTimer _toastTimer;
    private readonly SettingsService _settingsService;

    private CancellationTokenSource? _searchCancellation;
    private AppSettings _settings = new();
    private int? _selectedListId;
    private GameEntry? _selectedGame;
    private bool _isInitializingDetail;
    private string _selectedListTitle = "Todos os jogos";
    private string _selectedListSubtitle = "Sua biblioteca completa";
    private string _totalGameCount = "0";
    private string _reviewedGameCount = "0";
    private string _averageRating = "-";

    public MainWindow()
    {
        InitializeComponent();
        _settingsService = new SettingsService(_database);
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GameList> Lists { get; } = [];
    public ObservableCollection<GameEntry> Games { get; } = [];
    public ObservableCollection<GameSearchResult> SearchResults { get; } = [];
    public ObservableCollection<ListMembershipOption> MembershipOptions { get; } = [];

    public string SelectedListTitle
    {
        get => _selectedListTitle;
        private set => SetField(ref _selectedListTitle, value);
    }

    public string SelectedListSubtitle
    {
        get => _selectedListSubtitle;
        private set => SetField(ref _selectedListSubtitle, value);
    }

    public string TotalGameCount
    {
        get => _totalGameCount;
        private set => SetField(ref _totalGameCount, value);
    }

    public string ReviewedGameCount
    {
        get => _reviewedGameCount;
        private set => SetField(ref _reviewedGameCount, value);
    }

    public string AverageRating
    {
        get => _averageRating;
        private set => SetField(ref _averageRating, value);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await _database.InitializeAsync();
            _settings = await _settingsService.LoadAsync();
            await RefreshAllAsync();
            ConfigureSearchProviderUi();

            if (!_settings.HasAnyProvider)
            {
                ApiBanner.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível iniciar a Ludoteca.\n\n{ex.Message}",
                "Erro ao iniciar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _gameApi.Dispose();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximize();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private async void AllGamesButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedListId = null;
        SelectedListTitle = "Todos os jogos";
        SelectedListSubtitle = "Sua biblioteca completa";
        DeleteCurrentListButton.Visibility = Visibility.Collapsed;
        AllGamesButton.Background = new SolidColorBrush(Color.FromRgb(215, 210, 232));
        await RefreshLibraryAsync();
    }

    private async void ListButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        _selectedListId = list.Id;
        SelectedListTitle = list.Name;
        SelectedListSubtitle = $"{list.GameCount} {(list.GameCount == 1 ? "jogo nesta lista" : "jogos nesta lista")}";
        DeleteCurrentListButton.Visibility = Visibility.Visible;
        AllGamesButton.Background = Brushes.Transparent;
        await RefreshLibraryAsync();
    }

    private void OpenCreateList_Click(object sender, RoutedEventArgs e)
    {
        NewListNameBox.Clear();
        CreateListOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => NewListNameBox.Focus());
    }

    private void CloseCreateList_Click(object sender, RoutedEventArgs e)
    {
        CreateListOverlay.Visibility = Visibility.Collapsed;
    }

    private void NewListNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CreateList_Click(sender, e);
        }
    }

    private async void CreateList_Click(object sender, RoutedEventArgs e)
    {
        var name = NewListNameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("Digite um nome para a lista.");
            return;
        }

        if (Lists.Any(list => list.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast("Já existe uma lista com esse nome.");
            return;
        }

        try
        {
            var color = ListColors[Lists.Count % ListColors.Length];
            var list = await _database.CreateListAsync(name, color);
            CreateListOverlay.Visibility = Visibility.Collapsed;
            await RefreshListsAsync();
            _selectedListId = list.Id;
            SelectedListTitle = list.Name;
            SelectedListSubtitle = "0 jogos nesta lista";
            DeleteCurrentListButton.Visibility = Visibility.Visible;
            AllGamesButton.Background = Brushes.Transparent;
            await RefreshLibraryAsync();
            ShowToast($"Lista \"{list.Name}\" criada.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível criar a lista: {ex.Message}");
        }
    }

    private async void DeleteCurrentList_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedListId.HasValue)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Excluir a lista \"{SelectedListTitle}\"?\n\nOs jogos continuarão na biblioteca.",
            "Excluir lista",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _database.DeleteListAsync(_selectedListId.Value);
        _selectedListId = null;
        SelectedListTitle = "Todos os jogos";
        SelectedListSubtitle = "Sua biblioteca completa";
        DeleteCurrentListButton.Visibility = Visibility.Collapsed;
        AllGamesButton.Background = new SolidColorBrush(Color.FromRgb(215, 210, 232));
        await RefreshAllAsync();
        ShowToast("Lista excluída.");
    }

    private async void SearchOnline_Click(object sender, RoutedEventArgs e)
    {
        SearchOverlay.Visibility = Visibility.Visible;
        OverlaySearchBox.Text = OnlineSearchBox.Text.Trim();
        SelectTargetList();
        ConfigureSearchProviderUi();

        if (_settings.HasAnyProvider && !string.IsNullOrWhiteSpace(OverlaySearchBox.Text))
        {
            await RunSearchAsync(OverlaySearchBox.Text);
        }
        else if (!_settings.HasAnyProvider)
        {
            SearchResults.Clear();
            ShowSearchEmpty(
                "Busca online não configurada",
                "Você pode cadastrar o jogo manualmente ou configurar uma API nas configurações.");
        }
        else
        {
            ShowSearchEmpty(
                "Busque pelo nome de um jogo",
                "Os resultados da API configurada aparecerão aqui.");
            _ = Dispatcher.BeginInvoke(() => OverlaySearchBox.Focus());
        }
    }

    private void OnlineSearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SearchOnline_Click(sender, e);
        }
    }

    private void OnlineSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (OnlineSearchPlaceholder is not null)
        {
            OnlineSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(OnlineSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private void FocusSearch_Click(object sender, RoutedEventArgs e)
    {
        SearchOnline_Click(sender, e);
    }

    private void CloseSearchOverlay_Click(object sender, RoutedEventArgs e)
    {
        _searchCancellation?.Cancel();
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
        if (!_settings.HasAnyProvider)
        {
            ShowSearchEmpty(
                "Busca online não configurada",
                "Cadastre o jogo manualmente ou configure IGDB/RAWG nas configurações.");
            return;
        }

        query = query.Trim();
        if (query.Length < 2)
        {
            ShowSearchEmpty("Digite pelo menos 2 caracteres", "Assim a busca fica mais precisa.");
            return;
        }

        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();

        SearchResults.Clear();
        SearchEmptyState.Visibility = Visibility.Collapsed;
        SearchLoadingState.Visibility = Visibility.Visible;

        try
        {
            var response = await _gameApi.SearchAsync(
                query,
                _settings,
                _searchCancellation.Token);
            foreach (var game in response.Results)
            {
                SearchResults.Add(game);
            }

            SearchLoadingState.Visibility = Visibility.Collapsed;
            if (SearchResults.Count == 0)
            {
                ShowSearchEmpty("Nenhum jogo encontrado", response.Message);
            }
            else
            {
                SearchEmptyState.Visibility = Visibility.Collapsed;
                ShowToast(response.Message);
            }
        }
        catch (OperationCanceledException)
        {
            SearchLoadingState.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            SearchLoadingState.Visibility = Visibility.Collapsed;
            ShowSearchEmpty("A busca não pôde ser concluída", ex.Message);
        }
    }

    private async void AddSearchResult_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameSearchResult game)
        {
            return;
        }

        if (TargetListCombo.SelectedItem is not GameList targetList)
        {
            ShowToast("Escolha uma lista de destino.");
            return;
        }

        if (sender is Button button)
        {
            button.IsEnabled = false;
            button.Content = "Adicionando...";
        }

        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _gameApi.EnrichAsync(game, _settings, cancellation.Token);
            }
            catch (Exception)
            {
                // The search data is enough to add a game if the detail endpoint is unavailable.
            }

            var coverPath = string.Empty;
            try
            {
                coverPath = await _gameApi.DownloadCoverAsync(
                    game,
                    _database.CoversDirectory,
                    cancellation.Token);
            }
            catch (Exception)
            {
                // The remote URL remains stored, so a failed cover download is non-fatal.
            }

            await _database.UpsertGameAsync(game, targetList.Id, coverPath);
            await RefreshAllAsync();
            ShowToast($"\"{game.Name}\" foi adicionado a {targetList.Name}.");
            if (sender is Button completedButton)
            {
                completedButton.Content = "Adicionado";
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível adicionar o jogo: {ex.Message}");
            if (sender is Button failedButton)
            {
                failedButton.IsEnabled = true;
                failedButton.Content = "Adicionar";
            }
        }
    }

    private void OpenManualGame_Click(object sender, RoutedEventArgs e)
    {
        ManualNameBox.Text = string.IsNullOrWhiteSpace(OverlaySearchBox.Text)
            ? OnlineSearchBox.Text.Trim()
            : OverlaySearchBox.Text.Trim();
        ManualCategoryBox.Clear();
        ManualYearBox.Clear();
        ManualDescriptionBox.Clear();
        ManualCoverUrlBox.Clear();
        SelectManualTargetList();
        ManualGameOverlay.Visibility = Visibility.Visible;
        _ = Dispatcher.BeginInvoke(() => ManualNameBox.Focus());
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

        if (ManualTargetListCombo.SelectedItem is not GameList targetList)
        {
            ShowToast("Escolha uma lista de destino.");
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
                // The URL is kept in the database if the local download is unavailable.
            }
        }

        try
        {
            await _database.UpsertGameAsync(game, targetList.Id, coverPath);
            ManualGameOverlay.Visibility = Visibility.Collapsed;
            SearchOverlay.Visibility = Visibility.Collapsed;
            OnlineSearchBox.Clear();
            await RefreshAllAsync();
            ShowToast($"\"{name}\" foi adicionado a {targetList.Name}.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível adicionar o jogo: {ex.Message}");
        }
    }

    private async void GameCard_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        _selectedGame = game;
        DetailPanel.DataContext = game;
        MembershipOptions.Clear();
        foreach (var membership in await _database.GetMembershipsAsync(game.Id))
        {
            MembershipOptions.Add(membership);
        }

        _isInitializingDetail = true;
        RatingSlider.Value = game.UserRating ?? 0;
        NoRatingCheckBox.IsChecked = !game.UserRating.HasValue;
        RatingSlider.IsEnabled = game.UserRating.HasValue;
        RatingValueText.Text = game.UserRating?.ToString("0.0") ?? "-";
        ReviewTextBox.Text = game.Review;
        _isInitializingDetail = false;
        DetailOverlay.Visibility = Visibility.Visible;
    }

    private void CloseDetailOverlay_Click(object sender, RoutedEventArgs e)
    {
        DetailOverlay.Visibility = Visibility.Collapsed;
        _selectedGame = null;
    }

    private void RatingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RatingValueText is null || _isInitializingDetail)
        {
            return;
        }

        RatingValueText.Text = RatingSlider.Value.ToString("0.0");
        if (NoRatingCheckBox.IsChecked == true)
        {
            NoRatingCheckBox.IsChecked = false;
        }
    }

    private void NoRatingCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (RatingSlider is null || RatingValueText is null || _isInitializingDetail)
        {
            return;
        }

        var noRating = NoRatingCheckBox.IsChecked == true;
        RatingSlider.IsEnabled = !noRating;
        RatingValueText.Text = noRating ? "-" : RatingSlider.Value.ToString("0.0");
    }

    private async void SaveGameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
        {
            return;
        }

        double? rating = NoRatingCheckBox.IsChecked == true ? null : RatingSlider.Value;
        var selectedListIds = MembershipOptions
            .Where(option => option.IsSelected)
            .Select(option => option.ListId);

        try
        {
            await _database.SaveGameDetailsAsync(
                _selectedGame.Id,
                rating,
                ReviewTextBox.Text,
                selectedListIds);
            DetailOverlay.Visibility = Visibility.Collapsed;
            _selectedGame = null;
            await RefreshAllAsync();
            ShowToast("Nota, review e listas atualizadas.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar: {ex.Message}");
        }
    }

    private async void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
        {
            return;
        }

        var result = MessageBox.Show(
            $"Remover \"{_selectedGame.Name}\" da sua biblioteca?",
            "Remover jogo",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        await _database.DeleteGameAsync(_selectedGame.Id);
        DetailOverlay.Visibility = Visibility.Collapsed;
        _selectedGame = null;
        await RefreshAllAsync();
        ShowToast("Jogo removido da biblioteca.");
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        IgdbClientIdBox.Password = _settings.IgdbClientId;
        IgdbClientSecretBox.Password = _settings.IgdbClientSecret;
        RawgApiKeyBox.Password = _settings.RawgApiKey;
        SettingsOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        _settings = new AppSettings
        {
            IgdbClientId = IgdbClientIdBox.Password.Trim(),
            IgdbClientSecret = IgdbClientSecretBox.Password.Trim(),
            RawgApiKey = RawgApiKeyBox.Password.Trim()
        };

        try
        {
            await _settingsService.SaveAsync(_settings);
            SettingsOverlay.Visibility = Visibility.Collapsed;
            ApiBanner.Visibility = _settings.HasAnyProvider
                ? Visibility.Collapsed
                : Visibility.Visible;
            ConfigureSearchProviderUi();
            ShowToast("Configurações salvas com segurança.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar as configurações: {ex.Message}");
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

    private void ConfigureSearchProviderUi()
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
    }

    private async Task RefreshAllAsync()
    {
        await RefreshListsAsync();
        await RefreshLibraryAsync();
    }

    private async Task RefreshListsAsync()
    {
        var lists = await _database.GetListsAsync();
        Lists.Clear();
        foreach (var list in lists)
        {
            Lists.Add(list);
        }

        if (TargetListCombo is not null)
        {
            SelectTargetList();
        }
        if (ManualTargetListCombo is not null)
        {
            SelectManualTargetList();
        }
    }

    private async Task RefreshLibraryAsync()
    {
        var games = await _database.GetGamesAsync(_selectedListId);
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        var stats = await _database.GetStatsAsync();
        TotalGameCount = stats.TotalGames.ToString();
        ReviewedGameCount = stats.ReviewedGames.ToString();
        AverageRating = stats.AverageRatingDisplay;

        if (_selectedListId.HasValue)
        {
            var selectedList = Lists.FirstOrDefault(list => list.Id == _selectedListId.Value);
            if (selectedList is not null)
            {
                SelectedListSubtitle =
                    $"{selectedList.GameCount} {(selectedList.GameCount == 1 ? "jogo nesta lista" : "jogos nesta lista")}";
            }
        }

        EmptyLibraryState.Visibility = Games.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void SelectTargetList()
    {
        if (Lists.Count == 0 || TargetListCombo is null)
        {
            return;
        }

        TargetListCombo.SelectedItem = _selectedListId.HasValue
            ? Lists.FirstOrDefault(list => list.Id == _selectedListId.Value) ?? Lists[0]
            : Lists[0];
    }

    private void SelectManualTargetList()
    {
        if (Lists.Count == 0 || ManualTargetListCombo is null)
        {
            return;
        }

        ManualTargetListCombo.SelectedItem = _selectedListId.HasValue
            ? Lists.FirstOrDefault(list => list.Id == _selectedListId.Value) ?? Lists[0]
            : Lists[0];
    }

    private void ShowSearchEmpty(string title, string message)
    {
        SearchLoadingState.Visibility = Visibility.Collapsed;
        SearchEmptyTitle.Text = title;
        SearchEmptyMessage.Text = message;
        SearchEmptyState.Visibility = Visibility.Visible;
    }

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Shell launch failures are non-critical and are surfaced by the caller's context.
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
