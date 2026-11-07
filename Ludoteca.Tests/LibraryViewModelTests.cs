using Ludoteca.Models;
using Ludoteca.Services;
using Ludoteca.ViewModels;

namespace Ludoteca.Tests;

public sealed class LibraryViewModelTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"jogoteca-library-vm-{Guid.NewGuid():N}");

    [Fact]
    public async Task ShowsWholeLibraryWithCounters()
    {
        var (database, library) = await CreateAsync();
        await AddGameAsync(database, "Hades", rating: 9);
        await AddGameAsync(database, "Celeste");

        await library.RefreshAllAsync();
        await library.ShowAllAsync();

        Assert.Equal(2, library.Games.Count);
        Assert.Equal("2", library.TotalGameCount);
        Assert.Equal("1", library.UnratedGameCount);
        Assert.Equal(LibraryViewModel.LibraryTitle, library.Title);
        Assert.True(library.IsViewingLibrary);
        Assert.False(library.IsEmpty);
    }

    [Fact]
    public async Task ShowsOnlyGamesOfSelectedList()
    {
        var (database, library) = await CreateAsync();
        var hades = await AddGameAsync(database, "Hades");
        await AddGameAsync(database, "Celeste");
        var favorites = await database.CreateListAsync("Favoritos", "#123456", "⭐");
        await database.AddGameToListAsync(hades, favorites.Id);
        await library.RefreshAllAsync();

        await library.ShowListAsync(library.Lists.Single(list => list.Id == favorites.Id));

        Assert.True(library.IsViewingList);
        Assert.Equal("⭐ Favoritos", library.Title);
        Assert.Equal("1 jogo nesta lista", library.Subtitle);
        Assert.Equal("Hades", Assert.Single(library.Games).Name);
    }

    [Fact]
    public async Task EmptyFilterExplainsWhy()
    {
        var (database, library) = await CreateAsync();
        await AddGameAsync(database, "Hades", rating: 9);
        await library.RefreshAllAsync();

        await library.ShowFilterAsync(GameLibraryFilter.Unrated, "Sem nota", "Jogos sem nota");

        Assert.True(library.IsEmpty);
        Assert.Equal("Nenhum jogo sem nota", library.EmptyTitle);
    }

    [Fact]
    public async Task NameSortCyclesAndReturnsToDatabaseOrder()
    {
        var (database, library) = await CreateAsync();
        await AddGameAsync(database, "Celeste");
        await AddGameAsync(database, "alan wake");
        await AddGameAsync(database, "Hades");
        await library.RefreshAllAsync();
        await library.ShowAllAsync();
        var databaseOrder = library.Games.Select(game => game.Name).ToList();
        library.SetLayout(useList: true);

        await library.CycleNameSortAsync();
        Assert.Equal("Nome A-Z", library.NameSortHeaderText);
        Assert.Equal(["alan wake", "Celeste", "Hades"], library.Games.Select(game => game.Name));

        await library.CycleNameSortAsync();
        Assert.Equal("Nome Z-A", library.NameSortHeaderText);
        Assert.Equal(["Hades", "Celeste", "alan wake"], library.Games.Select(game => game.Name));

        await library.CycleNameSortAsync();
        Assert.Equal("Nome", library.NameSortHeaderText);
        Assert.Equal(databaseOrder, library.Games.Select(game => game.Name));
    }

    [Fact]
    public async Task DeletedListFallsBackToLibrary()
    {
        var (database, library) = await CreateAsync();
        var list = await database.CreateListAsync("Temporária", "#123456");
        await library.RefreshAllAsync();
        await library.ShowListAsync(library.Lists.Single(item => item.Id == list.Id));

        await database.DeleteListAsync(list.Id);
        await library.RefreshListsAsync();

        Assert.False(library.IsViewingList);
        Assert.Null(library.SelectedList);
    }

    private async Task<(DatabaseService Database, LibraryViewModel Library)> CreateAsync()
    {
        var database = new DatabaseService(_directory);
        await database.InitializeAsync();
        return (database, new LibraryViewModel(database));
    }

    private static async Task<int> AddGameAsync(DatabaseService database, string name, double? rating = null)
    {
        var id = await database.UpsertGameAsync(
            new GameSearchResult { Source = "manual", ExternalId = Guid.NewGuid().ToString("N"), Name = name },
            string.Empty);
        if (rating.HasValue)
        {
            await database.SaveGameDetailsAsync(id, rating, string.Empty, string.Empty, string.Empty, []);
        }

        return id;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
