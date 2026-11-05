using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.Tests;

public sealed class DatabaseServiceTests : IDisposable
{
    private readonly string _testDirectory =
        Path.Combine(Path.GetTempPath(), $"Ludoteca.Tests.{Guid.NewGuid():N}");

    [Fact]
    public async Task PersistsListsGamesRatingsReviewsAndMemberships()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();

        var defaultLists = await database.GetListsAsync();
        Assert.Equal(3, defaultLists.Count);

        var favorites = await database.CreateListAsync("Favoritos", "#ABCDEF", "⭐");
        var game = new GameSearchResult
        {
            Source = "igdb",
            ExternalId = "123",
            Name = "Jogo de teste",
            Description = "Uma descrição curta.",
            Category = "RPG",
            ReleaseYear = 2026,
            ReleaseDate = "2026-07-14",
            CoverUrl = "https://example.com/cover.jpg"
        };

        var firstId = await database.UpsertGameAsync(game, string.Empty);
        var secondId = await database.UpsertGameAsync(game, string.Empty);

        Assert.Equal(firstId, secondId);
        Assert.Single(await database.GetGamesAsync(null));
        Assert.DoesNotContain(await database.GetMembershipsAsync(firstId), item => item.IsSelected);

        await database.AddGameToListAsync(firstId, defaultLists[0].Id);
        await database.AddGameToListAsync(firstId, favorites.Id);

        var memberships = await database.GetMembershipsAsync(firstId);
        Assert.Equal(2, memberships.Count(item => item.IsSelected));
        Assert.Equal("⭐", memberships.Single(item => item.ListId == favorites.Id).Emoji);

        await database.UpdateListAsync(favorites.Id, "Essenciais", "#123456", "💜");
        var updatedList = (await database.GetListsAsync()).Single(item => item.Id == favorites.Id);
        Assert.Equal("Essenciais", updatedList.Name);
        Assert.Equal("#123456", updatedList.Color);
        Assert.Equal("💜", updatedList.Emoji);

        await database.SaveGameDetailsAsync(
            firstId,
            9.5,
            "Excelente.",
            "2026-01-10",
            "2026-02-20",
            [favorites.Id]);

        Assert.Empty(await database.GetGamesAsync(defaultLists[0].Id));
        var favoriteGames = await database.GetGamesAsync(favorites.Id);
        var savedGame = Assert.Single(favoriteGames);
        Assert.Equal(9.5, savedGame.UserRating);
        Assert.Equal("Excelente.", savedGame.Review);
        Assert.Equal("10/01/2026", savedGame.StartedAtDisplay);
        Assert.Equal("20/02/2026", savedGame.FinishedAtDisplay);
        Assert.Equal("2026-07-14", savedGame.ReleaseDate);
        Assert.Equal("14/07/2026", savedGame.ReleaseDateDisplay);

        var stats = await database.GetStatsAsync();
        Assert.Equal(1, stats.TotalGames);
        Assert.Equal(0, stats.UnratedGames);
        Assert.Equal(1, stats.ReviewedGames);
        Assert.Equal(0, stats.UnorganizedGames);
        Assert.Equal(9.5, stats.AverageRating);

        Assert.Empty(await database.GetGamesAsync(null, libraryFilter: GameLibraryFilter.Unrated));
        Assert.Single(await database.GetGamesAsync(null, libraryFilter: GameLibraryFilter.Reviewed));
        Assert.Empty(await database.GetGamesAsync(null, libraryFilter: GameLibraryFilter.Unorganized));

        await database.DeleteGameAsync(firstId);
        Assert.Empty(await database.GetGamesAsync(null));
    }

    [Fact]
    public async Task AcceptsManualGameWithOnlyAName()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();

        var gameId = await database.UpsertGameAsync(
            new GameSearchResult
            {
                Source = "manual",
                ExternalId = Guid.NewGuid().ToString("N"),
                Name = "Jogo sem metadados"
            },
            string.Empty);

        var saved = Assert.Single(await database.GetGamesAsync(null));
        Assert.Equal(gameId, saved.Id);
        Assert.Equal("Jogo sem metadados", saved.Name);
        Assert.Equal(string.Empty, saved.Description);
        Assert.Equal(string.Empty, saved.Category);
        Assert.Null(saved.ReleaseYear);
        Assert.Equal(string.Empty, saved.ReleaseDate);
        Assert.Equal("Data desconhecida", saved.ReleaseDateDisplay);
        Assert.Equal(string.Empty, saved.CoverUrl);
        Assert.DoesNotContain(await database.GetMembershipsAsync(gameId), item => item.IsSelected);

        var stats = await database.GetStatsAsync();
        Assert.Equal(1, stats.UnratedGames);
        Assert.Equal(0, stats.ReviewedGames);
        Assert.Equal(1, stats.UnorganizedGames);
        Assert.Single(await database.GetGamesAsync(null, libraryFilter: GameLibraryFilter.Unrated));
        Assert.Empty(await database.GetGamesAsync(null, libraryFilter: GameLibraryFilter.Reviewed));
        Assert.Single(await database.GetGamesAsync(null, libraryFilter: GameLibraryFilter.Unorganized));
    }

    [Fact]
    public async Task ReordersListsAndKeepsNewListsAtTheEnd()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();

        var initialLists = await database.GetListsAsync();
        await database.MoveListAsync(initialLists[2].Id, initialLists[0].Id);

        var reorderedLists = await database.GetListsAsync();
        Assert.Equal(initialLists[2].Id, reorderedLists[0].Id);
        Assert.Equal(initialLists[0].Id, reorderedLists[1].Id);
        Assert.Equal(initialLists[1].Id, reorderedLists[2].Id);
        Assert.Equal(new[] { 0, 1, 2 }, reorderedLists.Select(list => list.Position));

        var customList = await database.CreateListAsync("Favoritos", "#ABCDEF", "⭐");
        var listsAfterCreate = await database.GetListsAsync();

        Assert.Equal(customList.Id, listsAfterCreate.Last().Id);
        Assert.Equal(listsAfterCreate.Count - 1, listsAfterCreate.Last().Position);
    }

    [Fact]
    public async Task ResetDatabaseClearsGamesSettingsAndRestoresDefaultLists()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();

        await database.CreateListAsync("Favoritos", "#ABCDEF", "⭐");
        await database.UpsertGameAsync(
            new GameSearchResult
            {
                Source = "manual",
                ExternalId = Guid.NewGuid().ToString("N"),
                Name = "Jogo para apagar"
            },
            string.Empty);
        await database.SaveSettingsAsync(new Dictionary<string, string>
        {
            ["app_theme"] = "dark"
        });

        await database.ResetDatabaseAsync();

        Assert.Empty(await database.GetGamesAsync(null));
        Assert.Empty(await database.GetSettingsAsync());

        var lists = await database.GetListsAsync();
        Assert.Equal(3, lists.Count);
        Assert.Equal(
            new[] { "Quero jogar", "Jogando", "Finalizados" },
            lists.Select(list => list.Name));
    }

    [Fact]
    public async Task ExportsGamesCsv()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();
        var lists = await database.GetListsAsync();
        var gameId = await database.UpsertGameAsync(
            new GameSearchResult
            {
                Source = "manual",
                ExternalId = Guid.NewGuid().ToString("N"),
                Name = "Chrono Test",
                Category = "RPG",
                ReleaseYear = 1995
            },
            string.Empty);
        await database.AddGameToListAsync(gameId, lists[0].Id);
        await database.SaveGameDetailsAsync(gameId, 9, "Bom demais.", string.Empty, string.Empty, [lists[0].Id]);

        var exportService = new DataExportService(database);
        var path = await exportService.ExportGamesCsvAsync(Path.Combine(_testDirectory, "exports"));

        Assert.True(File.Exists(path), path);
        Assert.Equal(".csv", Path.GetExtension(path));

        var gamesCsv = await File.ReadAllTextAsync(path);
        Assert.Contains("Chrono Test", gamesCsv);
        Assert.Contains("1995", gamesCsv);
        Assert.Contains("Bom demais.", gamesCsv);
        Assert.Contains(lists[0].Name, gamesCsv);
    }

    [Fact]
    public async Task DisplaysYearOnlyReleasesAsYear()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();

        await database.UpsertGameAsync(
            new GameSearchResult
            {
                Source = "manual",
                ExternalId = Guid.NewGuid().ToString("N"),
                Name = "Jogo com ano",
                ReleaseYear = 1998
            },
            string.Empty);

        var saved = Assert.Single(await database.GetGamesAsync(null));
        Assert.Equal("-/-/1998", saved.ReleaseDateDisplay);
    }

    [Fact]
    public async Task PersistsThemePreferenceInSettings()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();
        var settingsService = new SettingsService(database);

        var defaultSettings = await settingsService.LoadAsync();
        Assert.Equal("light", defaultSettings.Theme);

        await settingsService.SaveAsync(new AppSettings { Theme = "dark" });

        var savedSettings = await settingsService.LoadAsync();
        Assert.Equal("dark", savedSettings.Theme);
    }

    [Fact]
    public async Task MovesGameBetweenListsAndCanRemoveOnlyTheMembership()
    {
        var database = new DatabaseService(_testDirectory);
        await database.InitializeAsync();
        var lists = await database.GetListsAsync();
        var source = lists[0];
        var target = lists[1];
        var extra = lists[2];

        var gameId = await database.UpsertGameAsync(
            new GameSearchResult
            {
                Source = "manual",
                ExternalId = Guid.NewGuid().ToString("N"),
                Name = "Jogo em movimento"
            },
            string.Empty);
        await database.AddGameToListAsync(gameId, source.Id);
        await database.AddGameToListAsync(gameId, extra.Id);

        await database.MoveGameBetweenListsAsync(gameId, source.Id, target.Id);

        Assert.Empty(await database.GetGamesAsync(source.Id));
        Assert.Single(await database.GetGamesAsync(target.Id));
        Assert.Single(await database.GetGamesAsync(extra.Id));
        Assert.Single(await database.GetGamesAsync(null));

        await database.RemoveGameFromListAsync(gameId, target.Id);

        Assert.Empty(await database.GetGamesAsync(target.Id));
        Assert.Single(await database.GetGamesAsync(extra.Id));
        Assert.Single(await database.GetGamesAsync(null));

        await database.AddGameToListAsync(gameId, target.Id);
        await database.DeleteGameAsync(gameId);

        Assert.Empty(await database.GetGamesAsync(null));
        Assert.Empty(await database.GetGamesAsync(target.Id));
        Assert.Empty(await database.GetGamesAsync(extra.Id));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
