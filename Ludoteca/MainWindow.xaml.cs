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
using Ludoteca.ViewModels;
using Microsoft.Win32;

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

    private const string LightTheme = "light";
    private const string DarkTheme = "dark";
    private const double NoRatingSliderValue = -1;

    private readonly DatabaseService _database = new();
    private readonly GameApiService _gameApi;
    private readonly DataExportService _dataExportService;
    private readonly DispatcherTimer _toastTimer;
    private readonly SettingsService _settingsService;
    private readonly BackupService _backupService;
    private readonly UpdateService _updateService = new();

    private TaskCompletionSource<bool>? _confirmationCompletion;
    private AppSettings _settings = new();
    private int? _editingListId;
    private GameEntry? _selectedGame;
    private GameSearchResult? _selectedCatalogGame;
    private Point _dragStartPoint;
    private Point _listDragStartPoint;
    private bool _isDragging;
    private bool _isListDragging;
    private bool _isInitializingDetail;
    private string _selectedListColor = ListColors[0];
    private string _selectedListEmoji = ListEmojiCatalog.Game;
    private bool _isDarkTheme;
    private bool _hasUnreadNotifications;

    public MainWindow()
    {
        InitializeComponent();
        _gameApi = new GameApiService(
            catalogCacheDirectory: Path.Combine(_database.AppDataDirectory, "catalog-cache"));
        _dataExportService = new DataExportService(_database);
        _settingsService = new SettingsService(_database);
        _backupService = new BackupService(_database);
        Library = new LibraryViewModel(_database);
        Discover = new DiscoverViewModel(_gameApi);
        Search = new SearchViewModel(_gameApi);
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast.Visibility = Visibility.Collapsed;
        };
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryViewModel Library { get; }
    public DiscoverViewModel Discover { get; }
    public SearchViewModel Search { get; }

    public ObservableCollection<ListEmojiOption> ListEmojiOptions { get; } =
        new(ListEmojiCatalog.Options);
    public ObservableCollection<ListMembershipOption> MembershipOptions { get; } = [];
    public ObservableCollection<ReleaseNotification> ReleaseNotifications { get; } = [];
    public ObservableCollection<UpcomingReleaseWatch> UpcomingReleaseWatches { get; } = [];

    public string AppVersionDisplay { get; } =
        System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version is { } version
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : string.Empty;
    public string ThemeToggleText => IsDarkTheme ? "Usar tema claro" : "Usar tema escuro";
    public string ThemeToggleIcon => IsDarkTheme ? "\uE706" : "\uE708";

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
            await LoadDiscoveryAsync();
            await CheckForUpdatesAsync();
            ShowApiWelcomeIfNeeded();
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
        Search.Cancel();
        Discover.Cancel();
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

    private async Task RefreshAllAsync()
    {
        await Library.RefreshAllAsync();
        await RefreshReleaseNotificationsAsync();
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
