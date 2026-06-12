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
            CoverUrl = "https://example.com/cover.jpg"
        };

        var firstId = await database.UpsertGameAsync(game, string.Empty);
        var secondId = await database.UpsertGameAsync(game, string.Empty);

        Assert.Equal(firstId, secondId);
        Assert.Single(await database.GetGamesAsync(null));
        Assert.Empty((await database.GetMembershipsAsync(firstId)).Where(item => item.IsSelected));

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
            [favorites.Id]);

        Assert.Empty(await database.GetGamesAsync(defaultLists[0].Id));
        var favoriteGames = await database.GetGamesAsync(favorites.Id);
        var savedGame = Assert.Single(favoriteGames);
        Assert.Equal(9.5, savedGame.UserRating);
        Assert.Equal("Excelente.", savedGame.Review);

        var stats = await database.GetStatsAsync();
        Assert.Equal(1, stats.TotalGames);
        Assert.Equal(1, stats.ReviewedGames);
        Assert.Equal(9.5, stats.AverageRating);

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
        Assert.Equal(string.Empty, saved.CoverUrl);
        Assert.Empty((await database.GetMembershipsAsync(gameId)).Where(item => item.IsSelected));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }
}
