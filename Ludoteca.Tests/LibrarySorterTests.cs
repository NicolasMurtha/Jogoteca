using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.Tests;

public sealed class LibrarySorterTests
{
    private static readonly GameEntry[] Games =
    [
        new() { Id = 1, Name = "Celeste", ReleaseDate = "2018-01-25" },
        new() { Id = 2, Name = "alan wake", ReleaseDate = "2010-05-14" },
        new() { Id = 3, Name = "Hades", ReleaseYear = 2020 },
        new() { Id = 4, Name = "Bloodborne" }
    ];

    [Fact]
    public void DefaultKeepsDatabaseOrder()
    {
        var sorted = LibrarySorter.Sort(Games, LibrarySortMode.Default);

        Assert.Equal([1, 2, 3, 4], sorted.Select(game => game.Id));
    }

    [Fact]
    public void SortsByNameIgnoringCase()
    {
        Assert.Equal(
            [2, 4, 1, 3],
            LibrarySorter.Sort(Games, LibrarySortMode.NameAscending).Select(game => game.Id));
        Assert.Equal(
            [3, 1, 4, 2],
            LibrarySorter.Sort(Games, LibrarySortMode.NameDescending).Select(game => game.Id));
    }

    [Fact]
    public void SortsByReleaseWithUnknownDatesLast()
    {
        Assert.Equal(
            [3, 1, 2, 4],
            LibrarySorter.Sort(Games, LibrarySortMode.ReleaseDateDescending).Select(game => game.Id));
        Assert.Equal(
            [2, 1, 3, 4],
            LibrarySorter.Sort(Games, LibrarySortMode.ReleaseDateAscending).Select(game => game.Id));
    }
}
