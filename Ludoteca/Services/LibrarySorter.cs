using Ludoteca.Models;

namespace Ludoteca.Services;

public enum LibrarySortMode
{
    Default,
    NameAscending,
    NameDescending,
    ReleaseDateDescending,
    ReleaseDateAscending
}

public static class LibrarySorter
{
    // Default keeps the order the database returns (most recently updated first).
    public static IReadOnlyList<GameEntry> Sort(IEnumerable<GameEntry> games, LibrarySortMode mode)
    {
        var names = StringComparer.CurrentCultureIgnoreCase;
        return mode switch
        {
            LibrarySortMode.NameAscending => games
                .OrderBy(game => game.Name, names)
                .ThenByDescending(game => game.ReleaseSortDate ?? DateTime.MinValue)
                .ToList(),
            LibrarySortMode.NameDescending => games
                .OrderByDescending(game => game.Name, names)
                .ThenByDescending(game => game.ReleaseSortDate ?? DateTime.MinValue)
                .ToList(),
            LibrarySortMode.ReleaseDateDescending => games
                .OrderByDescending(game => game.ReleaseSortDate ?? DateTime.MinValue)
                .ThenBy(game => game.Name, names)
                .ToList(),
            LibrarySortMode.ReleaseDateAscending => games
                .OrderBy(game => game.ReleaseSortDate ?? DateTime.MaxValue)
                .ThenBy(game => game.Name, names)
                .ToList(),
            _ => games.ToList()
        };
    }
}
