using System.IO.Compression;
using System.Text;
using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"jogoteca-backup-tests-{Guid.NewGuid():N}");

    [Fact]
    public async Task RestoresLibraryOnAnotherComputer()
    {
        var source = new DatabaseService(Path.Combine(_root, "pc-a"));
        await source.InitializeAsync();
        var cover = Path.Combine(source.CoversDirectory, "hades.jpg");
        await File.WriteAllBytesAsync(cover, [1, 2, 3]);
        var gameId = await source.UpsertGameAsync(
            new GameSearchResult { Source = "rawg", ExternalId = "1", Name = "Hades", ReleaseYear = 2020 },
            cover);
        var list = await source.CreateListAsync("Favoritos", "#123456");
        await source.SaveGameDetailsAsync(gameId, 9, "Ótimo", "2026-01-01", string.Empty, [list.Id]);

        var backupPath = Path.Combine(_root, "biblioteca.jogoteca");
        var created = await new BackupService(source).CreateBackupAsync(backupPath);
        Assert.Equal(1, created.GameCount);
        Assert.Equal(1, created.CoverCount);

        var target = new DatabaseService(Path.Combine(_root, "pc-b"));
        await target.InitializeAsync();
        await target.UpsertGameAsync(
            new GameSearchResult { Source = "manual", ExternalId = "x", Name = "Outro jogo" },
            string.Empty);
        var targetBackups = new BackupService(target);

        await targetBackups.RestoreBackupAsync(backupPath);

        var game = Assert.Single(await target.GetGamesAsync(null));
        Assert.Equal("Hades", game.Name);
        Assert.Equal(9, game.UserRating);
        Assert.Equal("Ótimo", game.Review);
        Assert.StartsWith(target.CoversDirectory, game.LocalCoverPath);
        Assert.True(File.Exists(game.LocalCoverPath));
        Assert.Contains(await target.GetListsAsync(), item => item.Name == "Favoritos" && item.GameCount == 1);
        Assert.Single(Directory.GetFiles(targetBackups.SafetyBackupDirectory));
    }

    [Fact]
    public async Task RejectsFileThatIsNotABackupWithoutTouchingLibrary()
    {
        var database = new DatabaseService(Path.Combine(_root, "pc"));
        await database.InitializeAsync();
        await database.UpsertGameAsync(
            new GameSearchResult { Source = "manual", ExternalId = "x", Name = "Meu jogo" },
            string.Empty);
        var junk = Path.Combine(_root, "junk.jogoteca");
        await File.WriteAllTextAsync(junk, "isto não é um backup");

        await Assert.ThrowsAsync<BackupException>(() => new BackupService(database).RestoreBackupAsync(junk));

        Assert.Single(await database.GetGamesAsync(null));
    }

    [Fact]
    public async Task RejectsBackupFromNewerFormat()
    {
        var database = new DatabaseService(Path.Combine(_root, "pc"));
        await database.InitializeAsync();
        var path = Path.Combine(_root, "futuro.jogoteca");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("manifest.json");
            await using var stream = entry.Open();
            await stream.WriteAsync(Encoding.UTF8.GetBytes("""{"FormatVersion":99,"AppVersion":"9.0.0"}"""));
        }

        var error = await Assert.ThrowsAsync<BackupException>(
            () => new BackupService(database).ReadBackupInfoAsync(path));

        Assert.Contains("9.0.0", error.Message);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
