using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;

namespace Ludoteca.Models;

public abstract class ObservableModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class GameEntry : ObservableModel
{
    private double? _userRating;
    private string _review = string.Empty;

    public int Id { get; init; }
    public string Source { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int? ReleaseYear { get; init; }
    public string ReleaseDate { get; init; } = string.Empty;
    public string CoverUrl { get; init; } = string.Empty;
    public string LocalCoverPath { get; init; } = string.Empty;
    public string AddedAt { get; init; } = string.Empty;

    public string CoverDisplay =>
        !string.IsNullOrWhiteSpace(LocalCoverPath) && File.Exists(LocalCoverPath)
            ? LocalCoverPath
            : CoverUrl;

    public string YearDisplay => ReleaseYear?.ToString() ?? "Ano desconhecido";
    public string ReleaseDateDisplay =>
        TryParseReleaseDate(ReleaseDate, out var releaseDate)
            ? releaseDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : ReleaseYear.HasValue
                ? ReleaseYear.Value.ToString(CultureInfo.InvariantCulture)
                : "Data desconhecida";
    public DateTime? ReleaseSortDate =>
        TryParseReleaseDate(ReleaseDate, out var releaseDate)
            ? releaseDate
            : ReleaseYear.HasValue
                ? new DateTime(ReleaseYear.Value, 1, 1)
                : null;
    public string CategoryDisplay => string.IsNullOrWhiteSpace(Category) ? "Sem categoria" : Category;
    public string RatingDisplay => UserRating.HasValue ? UserRating.Value.ToString("0.0") : "-";
    public string ReviewDisplay =>
        string.IsNullOrWhiteSpace(Review)
            ? "Sem review escrita."
            : Review;
    public bool HasRating => UserRating.HasValue;

    public double? UserRating
    {
        get => _userRating;
        set
        {
            if (SetField(ref _userRating, value))
            {
                OnPropertyChanged(nameof(RatingDisplay));
                OnPropertyChanged(nameof(HasRating));
            }
        }
    }

    public string Review
    {
        get => _review;
        set
        {
            if (SetField(ref _review, value))
            {
                OnPropertyChanged(nameof(ReviewDisplay));
            }
        }
    }

    private static bool TryParseReleaseDate(string value, out DateTime releaseDate)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out releaseDate);
    }
}

public sealed record GameDragData(GameEntry Game, int? SourceListId);

public sealed record ListDragData(int ListId);

public sealed record ListEmojiOption(string Name, string Emoji);

public static class ListEmojiCatalog
{
    public const string Game = "🎮";
    public const string Target = "🎯";
    public const string Trophy = "🏆";
    public const string Star = "⭐";
    public const string Heart = "💜";

    public static IReadOnlyList<ListEmojiOption> Options { get; } =
    [
        new("Controle", Game),
        new("Quero jogar", Target),
        new("Finalizados", Trophy),
        new("Favoritos", Star),
        new("Especial", Heart),
        new("Fogo", "🔥"),
        new("Relógio", "⏳"),
        new("Diamante", "💎"),
        new("Espada", "⚔️"),
        new("Mundo", "🌍")
    ];

    public static string Normalize(string? value)
    {
        var emoji = value?.Trim() ?? string.Empty;
        return emoji switch
        {
            "" => Game,
            "\uE7FC" => Game,
            "\uE8B1" => Target,
            "\uE73E" => Trophy,
            "\uE735" => Star,
            _ when Options.Any(option => option.Emoji == emoji) => emoji,
            _ => Game
        };
    }
}

public sealed class GameList : ObservableModel
{
    private string _name = string.Empty;
    private string _color = "#CFC4F5";
    private string _emoji = ListEmojiCatalog.Game;
    private int _gameCount;

    public int Id { get; init; }
    public int Position { get; init; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetField(ref _name, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public string Emoji
    {
        get => _emoji;
        set
        {
            if (SetField(ref _emoji, ListEmojiCatalog.Normalize(value)))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public int GameCount
    {
        get => _gameCount;
        set
        {
            if (SetField(ref _gameCount, value))
            {
                OnPropertyChanged(nameof(CountDisplay));
            }
        }
    }

    public string CountDisplay => GameCount.ToString();
    public string DisplayName => $"{Emoji}  {Name}".Trim();
}

public sealed class GameSearchResult
{
    public string Source { get; init; } = string.Empty;
    public string ExternalId { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public int? ReleaseYear { get; init; }
    public string ReleaseDate { get; init; } = string.Empty;
    public string CoverUrl { get; init; } = string.Empty;
    public string CoverDisplay => CoverUrl;
    public string SourceDisplay => Source.ToLowerInvariant() switch
    {
        "igdb" => "IGDB",
        "rawg" => "RAWG",
        _ => "MANUAL"
    };
    public string YearDisplay => ReleaseYear?.ToString() ?? "Ano desconhecido";
    public string CategoryDisplay => string.IsNullOrWhiteSpace(Category) ? "Sem categoria" : Category;
    public string DescriptionDisplay =>
        string.IsNullOrWhiteSpace(Description)
            ? "Sem descrição disponível."
            : Description;
}

public sealed class AppSettings
{
    public string IgdbClientId { get; set; } = string.Empty;
    public string IgdbClientSecret { get; set; } = string.Empty;
    public string RawgApiKey { get; set; } = string.Empty;
    public string Theme { get; set; } = "light";

    public bool HasIgdb =>
        !string.IsNullOrWhiteSpace(IgdbClientId) &&
        !string.IsNullOrWhiteSpace(IgdbClientSecret);

    public bool HasRawg => !string.IsNullOrWhiteSpace(RawgApiKey);
    public bool HasAnyProvider => HasIgdb || HasRawg;
}

public sealed class LibraryStats
{
    public int TotalGames { get; init; }
    public int UnratedGames { get; init; }
    public int ReviewedGames { get; init; }
    public int UnorganizedGames { get; init; }
    public double? AverageRating { get; init; }

    public string AverageRatingDisplay => AverageRating?.ToString("0.0") ?? "-";
}

public enum GameLibraryFilter
{
    None,
    Unrated,
    Reviewed,
    Unorganized
}

public sealed class ListMembershipOption : ObservableModel
{
    private bool _isSelected;

    public int ListId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = "#CFC4F5";
    public string Emoji { get; init; } = ListEmojiCatalog.Game;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}

public sealed class GameSearchResponse
{
    public IReadOnlyList<GameSearchResult> Results { get; init; } = [];
    public string Message { get; init; } = string.Empty;
}

public enum CatalogFeed
{
    Recommended,
    Trending,
    MostAnticipated,
    Upcoming,
    Latest
}

public sealed class CatalogPage
{
    public IReadOnlyList<GameSearchResult> Results { get; init; } = [];
    public bool HasMore { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class CatalogSection : ObservableModel
{
    private bool _isVisible;
    private bool _isLoading;
    private bool _hasMore = true;
    private int _currentPage;

    public required CatalogFeed Feed { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public ObservableCollection<GameSearchResult> Games { get; } = [];

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetField(ref _isVisible, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool HasMore
    {
        get => _hasMore;
        private set => SetField(ref _hasMore, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        private set => SetField(ref _currentPage, value);
    }

    public bool TryBeginLoading()
    {
        if (IsLoading || (CurrentPage > 0 && !HasMore))
        {
            return false;
        }

        IsLoading = true;
        return true;
    }

    public void FinishLoading()
    {
        IsLoading = false;
    }

    public void ApplyPage(CatalogPage page, int pageNumber, bool reset)
    {
        if (reset)
        {
            Games.Clear();
        }

        var seen = Games
            .Select(game => $"{game.Source}:{game.ExternalId}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var game in page.Results)
        {
            if (seen.Add($"{game.Source}:{game.ExternalId}"))
            {
                Games.Add(game);
            }
        }

        CurrentPage = pageNumber;
        HasMore = page.HasMore;
        IsVisible = Games.Count > 0;
    }

    public void Clear()
    {
        Games.Clear();
        IsVisible = false;
        IsLoading = false;
        HasMore = true;
        CurrentPage = 0;
    }
}
