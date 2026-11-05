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
using Microsoft.Win32;

namespace Ludoteca;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private enum LibrarySortMode
    {
        Default,
        NameAscending,
        NameDescending,
        ReleaseDateDescending,
        ReleaseDateAscending
    }

    private static readonly string[] ListColors =
    [
        "#CFC4F5",
        "#B9DDCF",
        "#F5C6B5",
        "#EEDDA8",
        "#BFD6ED",
        "#E6C4D8"
    ];

    private const string LightTheme = "light";
    private const string DarkTheme = "dark";
    private const double NoRatingSliderValue = -1;

    private static readonly IReadOnlyDictionary<string, string> LightThemePalette =
        new Dictionary<string, string>
        {
            ["AppBackground"] = "#F4F3F1",
            ["Surface"] = "#FBFAF8",
            ["SurfaceMuted"] = "#ECEAE7",
            ["SidebarBackground"] = "#E8E6E3",
            ["TitleBarBackground"] = "#F7F6F4",
            ["TextPrimary"] = "#29282D",
            ["TextSecondary"] = "#77747D",
            ["TextInverted"] = "#FFFFFF",
            ["BorderBrush"] = "#DCD9D5",
            ["ControlHover"] = "#DEDCD8",
            ["FocusBorder"] = "#9C8DCE",
            ["PrimaryButtonBackground"] = "#29282D",
            ["PrimaryButtonForeground"] = "#FFFFFF",
            ["DangerBackground"] = "#F3DEDA",
            ["DangerForeground"] = "#A24949",
            ["ConfirmButtonBackground"] = "#BFE8C8",
            ["ConfirmButtonForeground"] = "#28543A",
            ["CancelButtonBackground"] = "#F2C0BC",
            ["CancelButtonForeground"] = "#7B2E2E",
            ["NavSelectedBackground"] = "#D7D2E8",
            ["NavCounterBackground"] = "#C4B8E9",
            ["VersionBadgeBackground"] = "#E7E1F7",
            ["VersionBadgeForeground"] = "#66568E",
            ["CoverPlaceholderBackground"] = "#DDD9D3",
            ["CoverPlaceholderIcon"] = "#AAA6A0",
            ["OverlayScrim"] = "#7629282D",
            ["LoadingPillBackground"] = "#EEFBFAF8",
            ["FloatingButtonBackground"] = "#DDFBFAF8",
            ["WarningBackground"] = "#FFF3D7",
            ["WarningBorder"] = "#E9D18F",
            ["AccentForeground"] = "#66568E",
            ["LinkAccent"] = "#66568E",
            ["SearchPlaceholderText"] = "#9A969E",
            ["StatLibraryBackground"] = "#E7E1F7",
            ["StatLibraryForeground"] = "#746991",
            ["StatRatingBackground"] = "#DDEEE7",
            ["StatRatingForeground"] = "#56796D",
            ["StatReviewBackground"] = "#F6E2DA",
            ["StatReviewForeground"] = "#956E5E",
            ["ActionNeutralBackground"] = "#F3EEE6",
            ["ActionNeutralForeground"] = "#7C6B55",
            ["ActionDangerBackground"] = "#F3DEDA",
            ["ActionDangerForeground"] = "#B23A3A",
            ["RatingOverlayBackground"] = "#EE29282D",
            ["RatingOverlayForeground"] = "#FFFFFF",
            ["StarBrush"] = "#F0D791",
            ["StarMutedBrush"] = "#A78024",
            ["EmptyIconBackground"] = "#E7E1F7",
            ["EmptyIconForeground"] = "#8D7CBE",
            ["EmptyGlyphForeground"] = "#A09BA5",
            ["RatingValueBackground"] = "#E7E1F7",
            ["SelectedSwatchBorder"] = "#29282D",
            ["ToastBackground"] = "#F329282D"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkThemePalette =
        new Dictionary<string, string>
        {
            ["AppBackground"] = "#121018",
            ["Surface"] = "#211B2A",
            ["SurfaceMuted"] = "#2D2538",
            ["SidebarBackground"] = "#17131F",
            ["TitleBarBackground"] = "#111018",
            ["TextPrimary"] = "#F6F1FF",
            ["TextSecondary"] = "#B7ABC8",
            ["TextInverted"] = "#121017",
            ["BorderBrush"] = "#3A3147",
            ["ControlHover"] = "#352B43",
            ["FocusBorder"] = "#A996FF",
            ["PrimaryButtonBackground"] = "#8B75F5",
            ["PrimaryButtonForeground"] = "#FFFFFF",
            ["DangerBackground"] = "#4B2630",
            ["DangerForeground"] = "#FFB1BE",
            ["ConfirmButtonBackground"] = "#72C58C",
            ["ConfirmButtonForeground"] = "#102418",
            ["CancelButtonBackground"] = "#E88983",
            ["CancelButtonForeground"] = "#2F1212",
            ["NavSelectedBackground"] = "#44346F",
            ["NavCounterBackground"] = "#7560B8",
            ["VersionBadgeBackground"] = "#3F3168",
            ["VersionBadgeForeground"] = "#DED4FF",
            ["CoverPlaceholderBackground"] = "#2B2435",
            ["CoverPlaceholderIcon"] = "#70647D",
            ["OverlayScrim"] = "#C00A0810",
            ["LoadingPillBackground"] = "#EE2A2336",
            ["FloatingButtonBackground"] = "#E62A2336",
            ["WarningBackground"] = "#3B2F1A",
            ["WarningBorder"] = "#7B6125",
            ["AccentForeground"] = "#D7CCFF",
            ["LinkAccent"] = "#D7CCFF",
            ["SearchPlaceholderText"] = "#8F849C",
            ["StatLibraryBackground"] = "#332954",
            ["StatLibraryForeground"] = "#DACFFF",
            ["StatRatingBackground"] = "#1F3A31",
            ["StatRatingForeground"] = "#9FE3CA",
            ["StatReviewBackground"] = "#432A2A",
            ["StatReviewForeground"] = "#F3B8B2",
            ["ActionNeutralBackground"] = "#3A3325",
            ["ActionNeutralForeground"] = "#E5D39D",
            ["ActionDangerBackground"] = "#4B2630",
            ["ActionDangerForeground"] = "#FFA4B2",
            ["RatingOverlayBackground"] = "#F00D0C12",
            ["RatingOverlayForeground"] = "#FFFFFF",
            ["StarBrush"] = "#FFD56A",
            ["StarMutedBrush"] = "#E6B94E",
            ["EmptyIconBackground"] = "#332954",
            ["EmptyIconForeground"] = "#DACFFF",
            ["EmptyGlyphForeground"] = "#8A7D98",
            ["RatingValueBackground"] = "#332954",
            ["SelectedSwatchBorder"] = "#F2EEF7",
            ["ToastBackground"] = "#F00F0D15"
        };

    private readonly DatabaseService _database = new();
    private readonly GameApiService _gameApi;
    private readonly DataExportService _dataExportService;
    private readonly DispatcherTimer _toastTimer;
    private readonly SettingsService _settingsService;

    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _catalogCancellation;
    private TaskCompletionSource<bool>? _confirmationCompletion;
    private AppSettings _settings = new();
    private int? _selectedListId;
    private int? _editingListId;
    private GameEntry? _selectedGame;
    private GameSearchResult? _selectedCatalogGame;
    private Point _dragStartPoint;
    private Point _listDragStartPoint;
    private bool _isDragging;
    private bool _isListDragging;
    private bool _isInitializingDetail;
    private bool _isCatalogLoading;
    private LibrarySortMode _librarySortMode = LibrarySortMode.Default;
    private GameLibraryFilter _activeLibraryFilter = GameLibraryFilter.None;
    private string _selectedListColor = ListColors[0];
    private string _selectedListEmoji = ListEmojiCatalog.Game;
    private string _selectedListTitle = "Biblioteca";
    private string _selectedListSubtitle = "Todos os jogos que você adicionou";
    private string _libraryNameFilter = string.Empty;
    private string _totalGameCount = "0";
    private string _unratedGameCount = "0";
    private string _reviewedGameCount = "0";
    private string _unorganizedGameCount = "0";
    private string _averageRating = "-";
    private bool _isDarkTheme;
    private bool _hasUnreadNotifications;

    public MainWindow()
    {
        InitializeComponent();
        _gameApi = new GameApiService(
            catalogCacheDirectory: Path.Combine(_database.AppDataDirectory, "catalog-cache"));
        _dataExportService = new DataExportService(_database);
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
    public ObservableCollection<ListEmojiOption> ListEmojiOptions { get; } =
        new(ListEmojiCatalog.Options);
    public ObservableCollection<CatalogSection> CatalogSections { get; } =
    [
        new CatalogSection
        {
            Feed = CatalogFeed.Recommended,
            Title = "Para você",
            Subtitle = "Jogos bem avaliados para começar uma nova aventura."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.Trending,
            Title = "Trending games",
            Subtitle = "Os jogos que estão concentrando a atenção dos jogadores agora."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.MostAnticipated,
            Title = "Mais aguardados",
            Subtitle = "Os próximos títulos com maior expectativa da comunidade."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.Upcoming,
            Title = "Próximos lançamentos",
            Subtitle = "O que está chegando nos próximos meses, em ordem de lançamento."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.Latest,
            Title = "Últimos lançamentos",
            Subtitle = "Jogos lançados recentemente para você não perder nenhuma novidade."
        }
    ];
    public ObservableCollection<ListMembershipOption> MembershipOptions { get; } = [];
    public ObservableCollection<ReleaseNotification> ReleaseNotifications { get; } = [];
    public ObservableCollection<UpcomingReleaseWatch> UpcomingReleaseWatches { get; } = [];

    public bool IsViewingList => _selectedListId.HasValue;
    public bool IsViewingLibrary => !_selectedListId.HasValue;
    public string ThemeToggleText => IsDarkTheme ? "Usar tema claro" : "Usar tema escuro";
    public string ThemeToggleIcon => IsDarkTheme ? "\uE706" : "\uE708";
    public string NameSortHeaderText => _librarySortMode switch
    {
        LibrarySortMode.NameAscending => "Nome A-Z",
        LibrarySortMode.NameDescending => "Nome Z-A",
        _ => "Nome"
    };
    public string ReleaseSortHeaderText => _librarySortMode switch
    {
        LibrarySortMode.ReleaseDateDescending => "Lançamento ↓",
        LibrarySortMode.ReleaseDateAscending => "Lançamento ↑",
        _ => "Lançamento"
    };

    public string SelectedListEmoji
    {
        get => _selectedListEmoji;
        set => SetField(ref _selectedListEmoji, ListEmojiCatalog.Normalize(value));
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        private set
        {
            if (SetField(ref _isDarkTheme, value))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeToggleText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeToggleIcon)));
            }
        }
    }

    public bool HasUnreadNotifications
    {
        get => _hasUnreadNotifications;
        private set => SetField(ref _hasUnreadNotifications, value);
    }

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

    public string UnratedGameCount
    {
        get => _unratedGameCount;
        private set => SetField(ref _unratedGameCount, value);
    }

    public string ReviewedGameCount
    {
        get => _reviewedGameCount;
        private set => SetField(ref _reviewedGameCount, value);
    }

    public string UnorganizedGameCount
    {
        get => _unorganizedGameCount;
        private set => SetField(ref _unorganizedGameCount, value);
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
            ApplyTheme(_settings.Theme);
            await RefreshAllAsync();
            ConfigureProviderUi();
            ShowDiscoverView();
            await LoadDiscoveryAsync(reset: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Não foi possível iniciar a JOGOTECA.\n\n{ex.Message}",
                "Erro ao iniciar",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _catalogCancellation?.Cancel();
        _catalogCancellation?.Dispose();
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

    private async void ToggleNotifications_Click(object sender, RoutedEventArgs e)
    {
        var shouldOpen = NotificationsFlyout.Visibility != Visibility.Visible;
        NotificationsFlyout.Visibility = shouldOpen
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!shouldOpen)
        {
            return;
        }

        try
        {
            await RefreshReleaseNotificationsAsync();
            if (HasUnreadNotifications)
            {
                await _database.MarkReleaseNotificationsReadAsync();
                await RefreshReleaseNotificationsAsync();
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Nao foi possivel carregar notificacoes: {ex.Message}");
        }
    }

    private void CloseNotifications_Click(object sender, RoutedEventArgs e)
    {
        NotificationsFlyout.Visibility = Visibility.Collapsed;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        MaximizeIcon.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
    }

    private async void DiscoverButton_Click(object sender, RoutedEventArgs e)
    {
        ShowDiscoverView();
        if (_settings.HasAnyProvider && CatalogSections.All(section => section.Games.Count == 0))
        {
            await LoadDiscoveryAsync(reset: true);
        }
    }

    private async void AllGamesButton_Click(object sender, RoutedEventArgs e)
    {
        _activeLibraryFilter = GameLibraryFilter.None;
        SetSelectedListId(null);
        SelectedListTitle = "Biblioteca";
        SelectedListSubtitle = "Todos os jogos que você adicionou";
        SetListActionButtonsVisible(false);
        ShowLibraryView();
        await RefreshLibraryAsync();
    }

    private async void ListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isListDragging)
        {
            _isListDragging = false;
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        SetSelectedListId(list.Id);
        _activeLibraryFilter = GameLibraryFilter.None;
        SelectedListTitle = $"{list.Emoji} {list.Name}".Trim();
        SelectedListSubtitle = ListSubtitle(list.GameCount);
        SetListActionButtonsVisible(true);
        ShowLibraryView();
        await RefreshLibraryAsync();
    }

    private void ShowDiscoverView()
    {
        DiscoveryPanel.Visibility = Visibility.Visible;
        LibraryPanel.Visibility = Visibility.Collapsed;
        DiscoverButton.Background = ResourceBrush("NavSelectedBackground");
        AllGamesButton.Background = Brushes.Transparent;
    }

    private void ShowLibraryView()
    {
        DiscoveryPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        DiscoverButton.Background = Brushes.Transparent;
        AllGamesButton.Background = _selectedListId.HasValue
            ? Brushes.Transparent
            : ResourceBrush("NavSelectedBackground");
    }

    private void ShowLibraryGridView_Click(object sender, RoutedEventArgs e)
    {
        SetLibraryLayout(useList: false);
    }

    private void ShowLibraryListView_Click(object sender, RoutedEventArgs e)
    {
        SetLibraryLayout(useList: true);
    }

    private void SetLibraryLayout(bool useList)
    {
        LibraryGridView.Visibility = useList ? Visibility.Collapsed : Visibility.Visible;
        LibraryListView.Visibility = useList ? Visibility.Visible : Visibility.Collapsed;
        LibraryGridViewButton.Background = useList
            ? Brushes.Transparent
            : FindResource("Surface") as Brush;
        LibraryListViewButton.Background = useList
            ? FindResource("Surface") as Brush
            : Brushes.Transparent;

        if (useList)
        {
            ApplyLibrarySort();
        }
    }

    private void SortByNameHeader_Click(object sender, RoutedEventArgs e)
    {
        _librarySortMode = _librarySortMode switch
        {
            LibrarySortMode.NameAscending => LibrarySortMode.NameDescending,
            LibrarySortMode.NameDescending => LibrarySortMode.Default,
            _ => LibrarySortMode.NameAscending
        };
        NotifySortHeaderChanges();
        ApplyLibrarySort();
    }

    private async void ShowUnratedGames_Click(object sender, RoutedEventArgs e)
    {
        await ShowLibraryFilterAsync(
            GameLibraryFilter.Unrated,
            "Sem nota",
            "Jogos que ainda precisam de uma nota.");
    }

    private async void ShowReviewedGames_Click(object sender, RoutedEventArgs e)
    {
        await ShowLibraryFilterAsync(
            GameLibraryFilter.Reviewed,
            "Reviews escritas",
            "Jogos que já têm observação ou review registrada.");
    }

    private async void ShowUnorganizedGames_Click(object sender, RoutedEventArgs e)
    {
        await ShowLibraryFilterAsync(
            GameLibraryFilter.Unorganized,
            "Não organizados",
            "Jogos que ainda não estão em nenhuma lista.");
    }

    private void SortByReleaseHeader_Click(object sender, RoutedEventArgs e)
    {
        _librarySortMode = _librarySortMode switch
        {
            LibrarySortMode.ReleaseDateDescending => LibrarySortMode.ReleaseDateAscending,
            LibrarySortMode.ReleaseDateAscending => LibrarySortMode.Default,
            _ => LibrarySortMode.ReleaseDateDescending
        };
        NotifySortHeaderChanges();
        ApplyLibrarySort();
    }

    private async void ApplyLibraryNameFilter_Click(object sender, RoutedEventArgs e)
    {
        _libraryNameFilter = LibraryNameFilterBox.Text.Trim();
        await RefreshLibraryAsync();
    }

    private async void LibraryNameFilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _libraryNameFilter = LibraryNameFilterBox.Text.Trim();
            await RefreshLibraryAsync();
        }
    }

    private void LibraryNameFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LibraryNameFilterPlaceholder is not null)
        {
            LibraryNameFilterPlaceholder.Visibility =
                string.IsNullOrWhiteSpace(LibraryNameFilterBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    private void NotifySortHeaderChanges()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameSortHeaderText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReleaseSortHeaderText)));
    }

    private async Task ShowLibraryFilterAsync(
        GameLibraryFilter filter,
        string title,
        string subtitle)
    {
        _activeLibraryFilter = filter;
        SetSelectedListId(null);
        SelectedListTitle = title;
        SelectedListSubtitle = subtitle;
        SetListActionButtonsVisible(false);
        ShowLibraryView();
        await RefreshLibraryAsync();
    }

    private void SetListActionButtonsVisible(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        EditCurrentListButton.Visibility = visibility;
        DeleteCurrentListButton.Visibility = visibility;
    }

    private void OpenCreateList_Click(object sender, RoutedEventArgs e)
    {
        _editingListId = null;
        _selectedListColor = ListColors[Lists.Count % ListColors.Length];
        SelectedListEmoji = ListEmojiCatalog.Game;
        ListEditorTitle.Text = "Criar uma lista";
        ListEditorSubtitle.Text = "Personalize o nome, o emoji e a cor da lista.";
        SaveListButton.Content = "Criar lista";
        NewListNameBox.Clear();
        UpdateSelectedColorPreview();
        ListEditorOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => NewListNameBox.Focus());
    }

    private void EditCurrentList_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedListId.HasValue)
        {
            return;
        }

        var list = Lists.FirstOrDefault(item => item.Id == _selectedListId.Value);
        if (list is null)
        {
            return;
        }

        _editingListId = list.Id;
        _selectedListColor = list.Color;
        SelectedListEmoji = list.Emoji;
        ListEditorTitle.Text = "Editar lista";
        ListEditorSubtitle.Text = "Altere o nome, o emoji ou a cor desta lista.";
        SaveListButton.Content = "Salvar alterações";
        NewListNameBox.Text = list.Name;
        UpdateSelectedColorPreview();
        ListEditorOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            NewListNameBox.Focus();
            NewListNameBox.SelectAll();
        });
    }

    private void CloseListEditor_Click(object sender, RoutedEventArgs e)
    {
        ListEditorOverlay.Visibility = Visibility.Collapsed;
    }

    private void NewListNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveList_Click(sender, e);
        }
    }

    private void ListColor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string color)
        {
            return;
        }

        _selectedListColor = color;
        UpdateSelectedColorPreview();
    }

    private void UpdateSelectedColorPreview()
    {
        var buttons = new[]
        {
            ListColorLavenderButton,
            ListColorMintButton,
            ListColorPeachButton,
            ListColorYellowButton,
            ListColorBlueButton,
            ListColorPinkButton
        };

        foreach (var button in buttons)
        {
            if (button.Tag is string color &&
                color.Equals(_selectedListColor, StringComparison.OrdinalIgnoreCase))
            {
                button.BorderBrush = BrushFromHex("#E85D5D");
                button.BorderThickness = new Thickness(3);
            }
            else
            {
                button.BorderBrush = Brushes.Transparent;
                button.BorderThickness = new Thickness(2);
            }
        }
    }

    private async void SaveList_Click(object sender, RoutedEventArgs e)
    {
        var name = NewListNameBox.Text.Trim();
        var emoji = SelectedListEmoji;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("Digite um nome para a lista.");
            return;
        }

        if (Lists.Any(list =>
                list.Id != _editingListId &&
                list.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast("Já existe uma lista com esse nome.");
            return;
        }

        try
        {
            if (_editingListId.HasValue)
            {
                await _database.UpdateListAsync(
                    _editingListId.Value,
                    name,
                    _selectedListColor,
                    emoji);
                ShowToast($"Lista \"{name}\" atualizada.");
            }
            else
            {
                var list = await _database.CreateListAsync(name, _selectedListColor, emoji);
                SetSelectedListId(list.Id);
                ShowToast($"Lista \"{name}\" criada.");
            }

            ListEditorOverlay.Visibility = Visibility.Collapsed;
            await RefreshListsAsync();
            var selected = _selectedListId.HasValue
                ? Lists.FirstOrDefault(list => list.Id == _selectedListId.Value)
                : null;
            if (selected is not null)
            {
                SelectedListTitle = $"{selected.Emoji} {selected.Name}".Trim();
                SelectedListSubtitle = ListSubtitle(selected.GameCount);
                SetListActionButtonsVisible(true);
                ShowLibraryView();
                await RefreshLibraryAsync();
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar a lista: {ex.Message}");
        }
    }

    private async void DeleteCurrentList_Click(object sender, RoutedEventArgs e)
    {
        if (!_selectedListId.HasValue)
        {
            return;
        }

        var selected = Lists.FirstOrDefault(list => list.Id == _selectedListId.Value);
        if (selected is null)
        {
            return;
        }

        var confirmed = await ShowConfirmationAsync(
            "Excluir lista",
            $"Excluir a lista \"{selected.Name}\"?\n\nOs jogos continuarão na biblioteca.",
            "Confirmar");
        if (!confirmed)
        {
            return;
        }

        await _database.DeleteListAsync(selected.Id);
        SetSelectedListId(null);
        SelectedListTitle = "Biblioteca";
        SelectedListSubtitle = "Todos os jogos que você adicionou";
        SetListActionButtonsVisible(false);
        await RefreshAllAsync();
        ShowLibraryView();
        ShowToast("Lista excluída.");
    }

    private void GameCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void ListButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _listDragStartPoint = e.GetPosition(null);
        _isListDragging = false;
    }

    private void ListButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            (sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _listDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _listDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isListDragging = true;
        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new DataObject(typeof(ListDragData), new ListDragData(list.Id)),
            DragDropEffects.Move);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => _isListDragging = false);
    }

    private void GameCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            (sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        var dragData = new GameDragData(game, _selectedListId);
        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new DataObject(typeof(GameDragData), dragData),
            _selectedListId.HasValue
                ? DragDropEffects.Move
                : DragDropEffects.Copy);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => _isDragging = false);
    }

    private void ListButton_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ListDragData)) is ListDragData)
        {
            e.Effects = DragDropEffects.Move;
            if (sender is Button listButton)
            {
                listButton.Background = ResourceBrush("NavSelectedBackground");
            }

            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(GameDragData)) is not GameDragData dragData)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = dragData.SourceListId.HasValue
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
        if (sender is Button button)
        {
            button.Background = ResourceBrush("NavSelectedBackground");
        }
        e.Handled = true;
    }

    private void ListButton_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = Brushes.Transparent;
        }
    }

    private async void ListButton_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is Button button)
        {
            button.Background = Brushes.Transparent;
        }

        if (e.Data.GetData(typeof(ListDragData)) is ListDragData listDragData &&
            (sender as FrameworkElement)?.DataContext is GameList targetList)
        {
            if (listDragData.ListId == targetList.Id)
            {
                return;
            }

            try
            {
                await _database.MoveListAsync(listDragData.ListId, targetList.Id);
                await RefreshListsAsync();
                ShowToast("Ordem das listas atualizada.");
            }
            catch (Exception ex)
            {
                ShowToast($"Não foi possível reordenar a lista: {ex.Message}");
            }

            return;
        }

        if (e.Data.GetData(typeof(GameDragData)) is not GameDragData dragData ||
            (sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        var game = dragData.Game;
        if (dragData.SourceListId == list.Id)
        {
            ShowToast($"\"{game.Name}\" já está nesta lista.");
            return;
        }

        try
        {
            if (dragData.SourceListId.HasValue)
            {
                await _database.MoveGameBetweenListsAsync(
                    game.Id,
                    dragData.SourceListId.Value,
                    list.Id);
                ShowToast($"\"{game.Name}\" foi movido para {list.Name}.");
            }
            else
            {
                await _database.AddGameToListAsync(game.Id, list.Id);
                ShowToast($"\"{game.Name}\" foi adicionado a {list.Name}.");
            }

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível mover o jogo: {ex.Message}");
        }
    }

    private async void RemoveGameFromCurrentList_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!_selectedListId.HasValue ||
            (sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        try
        {
            await _database.RemoveGameFromListAsync(game.Id, _selectedListId.Value);
            await RefreshAllAsync();
            ShowToast($"\"{game.Name}\" foi retirado desta lista.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível retirar o jogo da lista: {ex.Message}");
        }
    }

    private async void DeleteGameFromCard_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is GameEntry game)
        {
            await DeleteGameFromLibraryAsync(game, closeDetailOverlay: false);
        }
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

        if (_isCatalogLoading || !reset)
        {
            return;
        }

        _isCatalogLoading = true;
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
            foreach (var section in CatalogSections)
            {
                section.FinishLoading();
            }

            _isCatalogLoading = false;
            DiscoveryLoadingState.Visibility = Visibility.Collapsed;
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
            SearchResults.Clear();
            ShowSearchEmpty(
                "Busca online não configurada",
                "Cadastre manualmente ou configure uma API. Nenhuma informação além do nome é obrigatória.");
        }
        else
        {
            ShowSearchEmpty(
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
                "Cadastre o jogo manualmente ou configure IGDB/RAWG.");
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
            if (_selectedListId.HasValue)
            {
                await _database.AddGameToListAsync(gameId, _selectedListId.Value);
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
            if (_selectedListId.HasValue)
            {
                await _database.AddGameToListAsync(gameId, _selectedListId.Value);
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

    private async void GameCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        _selectedGame = game;
        DetailPanel.DataContext = game;
        MembershipOptions.Clear();
        foreach (var membership in await _database.GetMembershipsAsync(game.Id))
        {
            if (membership.IsSelected)
            {
                MembershipOptions.Add(membership);
            }
        }

        _isInitializingDetail = true;
        RatingSlider.Value = game.UserRating.HasValue
            ? Math.Round(game.UserRating.Value)
            : NoRatingSliderValue;
        UpdateRatingValueText();
        ReviewTextBox.Text = game.Review;
        StartedAtPicker.SelectedDate = ParseStoredDate(game.StartedAt);
        FinishedAtPicker.SelectedDate = ParseStoredDate(game.FinishedAt);
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

        UpdateRatingValueText();
    }

    private void UpdateRatingValueText()
    {
        if (RatingValueText is null || RatingSlider is null)
        {
            return;
        }

        RatingValueText.Text = IsNoRatingValue(RatingSlider.Value)
            ? "Sem nota"
            : RatingSlider.Value.ToString("0");
    }

    private static bool IsNoRatingValue(double value)
    {
        return value <= NoRatingSliderValue + 0.01;
    }

    private static DateTime? ParseStoredDate(string value)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }

    private static string FormatStoredDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private async void SaveGameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
        {
            return;
        }

        double? rating = IsNoRatingValue(RatingSlider.Value)
            ? null
            : RatingSlider.Value;
        var startedAt = FormatStoredDate(StartedAtPicker.SelectedDate);
        var finishedAt = FormatStoredDate(FinishedAtPicker.SelectedDate);
        if (StartedAtPicker.SelectedDate.HasValue &&
            FinishedAtPicker.SelectedDate.HasValue &&
            StartedAtPicker.SelectedDate.Value.Date > FinishedAtPicker.SelectedDate.Value.Date)
        {
            ShowToast("A data de inicio nao pode ser depois da finalizacao.");
            return;
        }

        var selectedListIds = MembershipOptions
            .Where(option => option.IsSelected)
            .Select(option => option.ListId);

        try
        {
            await _database.SaveGameDetailsAsync(
                _selectedGame.Id,
                rating,
                ReviewTextBox.Text,
                startedAt,
                finishedAt,
                selectedListIds);
            DetailOverlay.Visibility = Visibility.Collapsed;
            _selectedGame = null;
            await RefreshAllAsync();
            ShowToast("Nota e review atualizadas.");
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

        await DeleteGameFromLibraryAsync(_selectedGame, closeDetailOverlay: true);
    }

    private async Task DeleteGameFromLibraryAsync(
        GameEntry game,
        bool closeDetailOverlay)
    {
        var confirmed = await ShowConfirmationAsync(
            "Remover jogo",
            $"Remover \"{game.Name}\" da sua biblioteca?\n\nO jogo também será retirado de todas as listas.",
            "Confirmar");
        if (!confirmed)
        {
            return;
        }

        await _database.DeleteGameAsync(game.Id);
        if (closeDetailOverlay)
        {
            DetailOverlay.Visibility = Visibility.Collapsed;
            _selectedGame = null;
        }
        await RefreshAllAsync();
        ShowToast("Jogo removido da biblioteca.");
    }

    private Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string confirmText)
    {
        if (_confirmationCompletion is not null)
        {
            return Task.FromResult(false);
        }

        ConfirmationTitle.Text = title;
        ConfirmationMessage.Text = message;
        ConfirmationConfirmButton.Content = confirmText;
        _confirmationCompletion = new TaskCompletionSource<bool>();
        ConfirmationOverlay.Visibility = Visibility.Visible;
        return _confirmationCompletion.Task;
    }

    private void ConfirmConfirmation_Click(object sender, RoutedEventArgs e)
    {
        CompleteConfirmation(true);
    }

    private void CancelConfirmation_Click(object sender, RoutedEventArgs e)
    {
        CompleteConfirmation(false);
    }

    private void CompleteConfirmation(bool confirmed)
    {
        ConfirmationOverlay.Visibility = Visibility.Collapsed;
        var completion = _confirmationCompletion;
        _confirmationCompletion = null;
        completion?.TrySetResult(confirmed);
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
            RawgApiKey = RawgApiKeyBox.Password.Trim(),
            Theme = _settings.Theme
        };

        try
        {
            await _settingsService.SaveAsync(_settings);
            SettingsOverlay.Visibility = Visibility.Collapsed;
            ConfigureProviderUi();
            await LoadDiscoveryAsync(reset: true);
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

    private void CatalogAttributionText_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        OpenExternal(_settings.HasRawg
            ? "https://rawg.io/"
            : "https://www.igdb.com/");
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
            CatalogSections.Clear();
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

    private async Task RefreshAllAsync()
    {
        await RefreshListsAsync();
        await RefreshLibraryAsync();
        await RefreshReleaseNotificationsAsync();
    }

    private async Task RefreshListsAsync()
    {
        var lists = await _database.GetListsAsync();
        Lists.Clear();
        foreach (var list in lists)
        {
            Lists.Add(list);
        }

        if (!_selectedListId.HasValue)
        {
            return;
        }

        var selected = Lists.FirstOrDefault(list => list.Id == _selectedListId.Value);
        if (selected is null)
        {
            SetSelectedListId(null);
            SetListActionButtonsVisible(false);
            return;
        }

        SelectedListTitle = $"{selected.Emoji} {selected.Name}".Trim();
        SelectedListSubtitle = ListSubtitle(selected.GameCount);
    }

    private async Task RefreshLibraryAsync()
    {
        var libraryFilter = _selectedListId.HasValue
            ? GameLibraryFilter.None
            : _activeLibraryFilter;
        var games = await _database.GetGamesAsync(
            _selectedListId,
            _libraryNameFilter,
            libraryFilter: libraryFilter);
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        if (LibraryListView?.Visibility == Visibility.Visible)
        {
            ApplyLibrarySort();
        }

        var stats = await _database.GetStatsAsync();
        TotalGameCount = stats.TotalGames.ToString();
        UnratedGameCount = stats.UnratedGames.ToString();
        ReviewedGameCount = stats.ReviewedGames.ToString();
        UnorganizedGameCount = stats.UnorganizedGames.ToString();
        AverageRating = stats.AverageRatingDisplay;

        if (_selectedListId.HasValue)
        {
            var selectedList = Lists.FirstOrDefault(list => list.Id == _selectedListId.Value);
            if (selectedList is not null)
            {
                SelectedListSubtitle = ListSubtitle(selectedList.GameCount);
            }
        }

        EmptyLibraryState.Visibility = Games.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (Games.Count == 0)
        {
            SetEmptyLibraryMessage();
        }
    }

    private async Task RefreshReleaseNotificationsAsync()
    {
        await _database.SyncReleaseNotificationsAsync();
        var notifications = await _database.GetReleaseNotificationsAsync();
        var watches = await _database.GetUpcomingReleaseWatchesAsync();

        ReleaseNotifications.Clear();
        foreach (var notification in notifications)
        {
            ReleaseNotifications.Add(notification);
        }

        UpcomingReleaseWatches.Clear();
        foreach (var watch in watches)
        {
            UpcomingReleaseWatches.Add(watch);
        }

        HasUnreadNotifications = notifications.Any(notification => !notification.IsRead);
        if (NotificationEmptyState is not null)
        {
            NotificationEmptyState.Visibility = ReleaseNotifications.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (UpcomingReleaseSection is not null)
        {
            UpcomingReleaseSection.Visibility = UpcomingReleaseWatches.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    private void SetEmptyLibraryMessage()
    {
        if (_selectedListId.HasValue)
        {
            EmptyLibraryTitle.Text = "Esta lista está vazia";
            EmptyLibraryMessage.Text =
                "Arraste um jogo da Biblioteca ou de outra lista para começar a organizar.";
            return;
        }

        (EmptyLibraryTitle.Text, EmptyLibraryMessage.Text) = _activeLibraryFilter switch
        {
            GameLibraryFilter.Unrated => (
                "Nenhum jogo sem nota",
                "Todos os jogos da biblioteca já têm uma nota registrada."),
            GameLibraryFilter.Reviewed => (
                "Nenhuma review escrita",
                "Quando você escrever observações ou reviews, elas aparecem aqui."),
            GameLibraryFilter.Unorganized => (
                "Tudo organizado",
                "Todos os jogos da biblioteca já estão em pelo menos uma lista."),
            _ => (
                "Sua biblioteca ainda está vazia",
                "Adicione jogos à biblioteca. Depois, arraste os cards entre as listas como em um kanban.")
        };
    }

    private void ApplyLibrarySort()
    {
        if (Games.Count <= 1)
        {
            return;
        }

        var sortedGames = _librarySortMode switch
        {
            LibrarySortMode.NameDescending => Games
                .OrderByDescending(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(game => game.ReleaseSortDate ?? DateTime.MinValue)
                .ToList(),
            LibrarySortMode.ReleaseDateDescending => Games
                .OrderByDescending(game => game.ReleaseSortDate ?? DateTime.MinValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            LibrarySortMode.ReleaseDateAscending => Games
                .OrderBy(game => game.ReleaseSortDate ?? DateTime.MaxValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList(),
            LibrarySortMode.NameAscending => Games
                .OrderBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenByDescending(game => game.ReleaseSortDate ?? DateTime.MinValue)
                .ToList(),
            _ => Games
                .OrderByDescending(game => DateTime.TryParse(game.AddedAt, out var addedAt)
                    ? addedAt
                    : DateTime.MinValue)
                .ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
        };

        Games.Clear();
        foreach (var game in sortedGames)
        {
            Games.Add(game);
        }
    }

    private static string ListSubtitle(int gameCount)
    {
        return $"{gameCount} {(gameCount == 1 ? "jogo nesta lista" : "jogos nesta lista")}";
    }

    private void SetSelectedListId(int? listId)
    {
        if (_selectedListId == listId)
        {
            return;
        }

        _selectedListId = listId;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsViewingList)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsViewingLibrary)));
    }

    private void ShowSearchEmpty(string title, string message)
    {
        SearchLoadingState.Visibility = Visibility.Collapsed;
        SearchEmptyTitle.Text = title;
        SearchEmptyMessage.Text = message;
        SearchEmptyState.Visibility = Visibility.Visible;
    }

    private void ApplyTheme(string theme)
    {
        var normalizedTheme = NormalizeTheme(theme);
        var palette = normalizedTheme == DarkTheme
            ? DarkThemePalette
            : LightThemePalette;

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

    private void ShowToast(string message)
    {
        ToastText.Text = message;
        Toast.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private Brush ResourceBrush(string key)
    {
        return (Brush)FindResource(key);
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

    private static Brush BrushFromHex(string color)
    {
        return (Brush)new BrushConverter().ConvertFromString(color)!;
    }

    private static void OpenExternal(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
            // Shell launch failures do not block local app usage.
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
