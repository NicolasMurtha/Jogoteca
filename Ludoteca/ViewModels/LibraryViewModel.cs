using System.Collections.ObjectModel;
using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.ViewModels;

// State of the library screen: which games are shown, from which list or filter,
// in which order, plus the counters at the top.
public sealed class LibraryViewModel(DatabaseService database) : ObservableModel
{
    public const string LibraryTitle = "Biblioteca";
    public const string LibrarySubtitle = "Todos os jogos que você adicionou";

    private int? _selectedListId;
    private GameLibraryFilter _activeFilter = GameLibraryFilter.None;
    private LibrarySortMode _sortMode = LibrarySortMode.Default;
    private string _nameFilter = string.Empty;
    private bool _isListLayout;
    private string _title = LibraryTitle;
    private string _subtitle = LibrarySubtitle;
    private string _totalGameCount = "0";
    private string _unratedGameCount = "0";
    private string _reviewedGameCount = "0";
    private string _unorganizedGameCount = "0";
    private string _averageRating = "-";
    private bool _isEmpty;
    private string _emptyTitle = "Sua biblioteca ainda está vazia";
    private string _emptyMessage =
        "Adicione jogos à biblioteca. Depois, arraste os cards entre as listas como em um kanban.";

    public ObservableCollection<GameList> Lists { get; } = [];
    public ObservableCollection<GameEntry> Games { get; } = [];

    public int? SelectedListId => _selectedListId;
    public GameList? SelectedList =>
        _selectedListId is int id ? Lists.FirstOrDefault(list => list.Id == id) : null;
    public bool IsViewingList => _selectedListId.HasValue;
    public bool IsViewingLibrary => !_selectedListId.HasValue;
    public GameLibraryFilter ActiveFilter => _activeFilter;
    public LibrarySortMode SortMode => _sortMode;
    public string NameFilter => _nameFilter;

    public string NameSortHeaderText => _sortMode switch
    {
        LibrarySortMode.NameAscending => "Nome A-Z",
        LibrarySortMode.NameDescending => "Nome Z-A",
        _ => "Nome"
    };

    public string ReleaseSortHeaderText => _sortMode switch
    {
        LibrarySortMode.ReleaseDateDescending => "Lançamento ↓",
        LibrarySortMode.ReleaseDateAscending => "Lançamento ↑",
        _ => "Lançamento"
    };

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string Subtitle
    {
        get => _subtitle;
        private set => SetField(ref _subtitle, value);
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

    public bool IsEmpty
    {
        get => _isEmpty;
        private set => SetField(ref _isEmpty, value);
    }

    public string EmptyTitle
    {
        get => _emptyTitle;
        private set => SetField(ref _emptyTitle, value);
    }

    public string EmptyMessage
    {
        get => _emptyMessage;
        private set => SetField(ref _emptyMessage, value);
    }

    public static string ListSubtitle(int gameCount) =>
        $"{gameCount} {(gameCount == 1 ? "jogo nesta lista" : "jogos nesta lista")}";

    public Task ShowAllAsync() =>
        ShowAsync(null, GameLibraryFilter.None, LibraryTitle, LibrarySubtitle);

    public Task ShowFilterAsync(GameLibraryFilter filter, string title, string subtitle) =>
        ShowAsync(null, filter, title, subtitle);

    public Task ShowListAsync(GameList list) =>
        ShowAsync(list.Id, GameLibraryFilter.None, ListTitle(list), ListSubtitle(list.GameCount));

    // Selects a list without reloading, e.g. right after creating it.
    public void SelectList(int? listId)
    {
        SetSelectedList(listId);
        if (listId is null)
        {
            Title = LibraryTitle;
            Subtitle = LibrarySubtitle;
        }
    }

    public async Task RefreshAllAsync()
    {
        await RefreshListsAsync();
        await RefreshGamesAsync();
    }

    public async Task RefreshListsAsync()
    {
        var lists = await database.GetListsAsync();
        Lists.Clear();
        foreach (var list in lists)
        {
            Lists.Add(list);
        }

        if (!_selectedListId.HasValue)
        {
            return;
        }

        if (SelectedList is not { } selected)
        {
            SetSelectedList(null);
            return;
        }

        Title = ListTitle(selected);
        Subtitle = ListSubtitle(selected.GameCount);
    }

    public async Task RefreshGamesAsync()
    {
        var filter = _selectedListId.HasValue ? GameLibraryFilter.None : _activeFilter;
        var games = await database.GetGamesAsync(_selectedListId, _nameFilter, libraryFilter: filter);
        ReplaceGames(games);
        if (_isListLayout)
        {
            ApplySort();
        }

        var stats = await database.GetStatsAsync();
        TotalGameCount = stats.TotalGames.ToString();
        UnratedGameCount = stats.UnratedGames.ToString();
        ReviewedGameCount = stats.ReviewedGames.ToString();
        UnorganizedGameCount = stats.UnorganizedGames.ToString();
        AverageRating = stats.AverageRatingDisplay;

        if (SelectedList is { } selectedList)
        {
            Subtitle = ListSubtitle(selectedList.GameCount);
        }

        IsEmpty = Games.Count == 0;
        if (IsEmpty)
        {
            (EmptyTitle, EmptyMessage) = EmptyStateText();
        }
    }

    public Task CycleNameSortAsync() => SetSortModeAsync(_sortMode switch
    {
        LibrarySortMode.NameAscending => LibrarySortMode.NameDescending,
        LibrarySortMode.NameDescending => LibrarySortMode.Default,
        _ => LibrarySortMode.NameAscending
    });

    public Task CycleReleaseSortAsync() => SetSortModeAsync(_sortMode switch
    {
        LibrarySortMode.ReleaseDateDescending => LibrarySortMode.ReleaseDateAscending,
        LibrarySortMode.ReleaseDateAscending => LibrarySortMode.Default,
        _ => LibrarySortMode.ReleaseDateDescending
    });

    public async Task ApplyNameFilterAsync(string text)
    {
        _nameFilter = text.Trim();
        await RefreshGamesAsync();
    }

    public void SetLayout(bool useList)
    {
        _isListLayout = useList;
        if (useList)
        {
            ApplySort();
        }
    }

    // Back to the plain library view, e.g. after a reset or a restored backup.
    public void Reset()
    {
        _activeFilter = GameLibraryFilter.None;
        _nameFilter = string.Empty;
        _sortMode = LibrarySortMode.Default;
        NotifySortHeaders();
        SelectList(null);
    }

    private async Task ShowAsync(int? listId, GameLibraryFilter filter, string title, string subtitle)
    {
        SetSelectedList(listId);
        _activeFilter = filter;
        Title = title;
        Subtitle = subtitle;
        await RefreshGamesAsync();
    }

    private async Task SetSortModeAsync(LibrarySortMode mode)
    {
        _sortMode = mode;
        NotifySortHeaders();
        if (mode == LibrarySortMode.Default)
        {
            await RefreshGamesAsync();
            return;
        }

        ApplySort();
    }

    private void ApplySort()
    {
        if (Games.Count > 1 && _sortMode != LibrarySortMode.Default)
        {
            ReplaceGames(LibrarySorter.Sort(Games, _sortMode));
        }
    }

    private void ReplaceGames(IEnumerable<GameEntry> games)
    {
        var snapshot = games.ToList();
        Games.Clear();
        foreach (var game in snapshot)
        {
            Games.Add(game);
        }
    }

    private void SetSelectedList(int? listId)
    {
        if (_selectedListId == listId)
        {
            return;
        }

        _selectedListId = listId;
        OnPropertyChanged(nameof(SelectedListId));
        OnPropertyChanged(nameof(IsViewingList));
        OnPropertyChanged(nameof(IsViewingLibrary));
    }

    private void NotifySortHeaders()
    {
        OnPropertyChanged(nameof(NameSortHeaderText));
        OnPropertyChanged(nameof(ReleaseSortHeaderText));
    }

    private (string Title, string Message) EmptyStateText()
    {
        if (_selectedListId.HasValue)
        {
            return (
                "Esta lista está vazia",
                "Arraste um jogo da Biblioteca ou de outra lista para começar a organizar.");
        }

        return _activeFilter switch
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

    private static string ListTitle(GameList list) => $"{list.Emoji} {list.Name}".Trim();
}
