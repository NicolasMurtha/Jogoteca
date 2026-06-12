using Ludoteca.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Ludoteca.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;

    public DatabaseService(string? appDataDirectory = null)
    {
        var environmentDirectory =
            Environment.GetEnvironmentVariable("JOGOTECA_DATA_DIR") ??
            Environment.GetEnvironmentVariable("LUDOTECA_DATA_DIR");
        AppDataDirectory = appDataDirectory ??
                           (!string.IsNullOrWhiteSpace(environmentDirectory)
                               ? environmentDirectory
                               : ResolveDefaultDataDirectory());
        CoversDirectory = Path.Combine(AppDataDirectory, "covers");
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(CoversDirectory);

        var legacyDatabasePath = Path.Combine(AppDataDirectory, "ludoteca.db");
        var databasePath = File.Exists(legacyDatabasePath)
            ? legacyDatabasePath
            : Path.Combine(AppDataDirectory, "jogoteca.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string AppDataDirectory { get; }
    public string CoversDirectory { get; }

    public async Task InitializeAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS Lists (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Color TEXT NOT NULL,
                Emoji TEXT NOT NULL DEFAULT '🎮',
                CreatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Source TEXT NOT NULL,
                ExternalId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                ReleaseYear INTEGER NULL,
                CoverUrl TEXT NOT NULL DEFAULT '',
                LocalCoverPath TEXT NOT NULL DEFAULT '',
                UserRating REAL NULL,
                Review TEXT NOT NULL DEFAULT '',
                AddedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                UNIQUE(Source, ExternalId)
            );

            CREATE TABLE IF NOT EXISTS GameLists (
                GameId INTEGER NOT NULL,
                ListId INTEGER NOT NULL,
                AddedAt TEXT NOT NULL,
                PRIMARY KEY (GameId, ListId),
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE,
                FOREIGN KEY (ListId) REFERENCES Lists(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS Settings (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_GameLists_ListId ON GameLists(ListId);
            CREATE INDEX IF NOT EXISTS IX_Games_Name ON Games(Name);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureListEmojiColumnAsync(connection);

        var countCommand = connection.CreateCommand();
        countCommand.CommandText = "SELECT COUNT(*) FROM Lists;";
        var listCount = Convert.ToInt32(await countCommand.ExecuteScalarAsync());
        if (listCount == 0)
        {
            await InsertDefaultListsAsync(connection);
        }
    }

    public async Task<IReadOnlyList<GameList>> GetListsAsync()
    {
        var lists = new List<GameList>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT l.Id, l.Name, l.Color, l.Emoji, COUNT(gl.GameId)
            FROM Lists l
            LEFT JOIN GameLists gl ON gl.ListId = l.Id
            GROUP BY l.Id, l.Name, l.Color, l.Emoji
            ORDER BY l.Id;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            lists.Add(new GameList
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Color = reader.GetString(2),
                Emoji = reader.GetString(3),
                GameCount = reader.GetInt32(4)
            });
        }

        return lists;
    }

    public async Task<GameList> CreateListAsync(string name, string color, string emoji = "🎮")
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Lists (Name, Color, Emoji, CreatedAt)
            VALUES ($name, $color, $emoji, $createdAt);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$color", color);
        command.Parameters.AddWithValue("$emoji", NormalizeEmoji(emoji));
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

        var id = Convert.ToInt32(await command.ExecuteScalarAsync());
        return new GameList
        {
            Id = id,
            Name = name.Trim(),
            Color = color,
            Emoji = NormalizeEmoji(emoji)
        };
    }

    public async Task UpdateListAsync(int listId, string name, string color, string emoji)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE Lists
            SET Name = $name, Color = $color, Emoji = $emoji
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$name", name.Trim());
        command.Parameters.AddWithValue("$color", color);
        command.Parameters.AddWithValue("$emoji", NormalizeEmoji(emoji));
        command.Parameters.AddWithValue("$id", listId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteListAsync(int listId)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Lists WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", listId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<GameEntry>> GetGamesAsync(int? listId, string filter = "")
    {
        var games = new List<GameEntry>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        var whereParts = new List<string>();

        if (listId.HasValue)
        {
            whereParts.Add("EXISTS (SELECT 1 FROM GameLists gl WHERE gl.GameId = g.Id AND gl.ListId = $listId)");
            command.Parameters.AddWithValue("$listId", listId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter))
        {
            whereParts.Add("(g.Name LIKE $filter OR g.Category LIKE $filter)");
            command.Parameters.AddWithValue("$filter", $"%{filter.Trim()}%");
        }

        command.CommandText =
            $"""
             SELECT g.Id, g.Source, g.ExternalId, g.Name, g.Description, g.Category,
                    g.ReleaseYear, g.CoverUrl, g.LocalCoverPath, g.UserRating, g.Review, g.AddedAt
             FROM Games g
             {(whereParts.Count > 0 ? $"WHERE {string.Join(" AND ", whereParts)}" : string.Empty)}
             ORDER BY g.UpdatedAt DESC, g.Name COLLATE NOCASE;
             """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            games.Add(MapGame(reader));
        }

        return games;
    }

    public async Task<int> UpsertGameAsync(GameSearchResult game, string localCoverPath)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var upsert = connection.CreateCommand();
        upsert.Transaction = transaction;
        upsert.CommandText =
            """
            INSERT INTO Games (
                Source, ExternalId, Name, Description, Category, ReleaseYear,
                CoverUrl, LocalCoverPath, AddedAt, UpdatedAt
            )
            VALUES (
                $source, $externalId, $name, $description, $category, $releaseYear,
                $coverUrl, $localCoverPath, $now, $now
            )
            ON CONFLICT(Source, ExternalId) DO UPDATE SET
                Name = excluded.Name,
                Description = CASE
                    WHEN excluded.Description <> '' THEN excluded.Description
                    ELSE Games.Description
                END,
                Category = excluded.Category,
                ReleaseYear = excluded.ReleaseYear,
                CoverUrl = excluded.CoverUrl,
                LocalCoverPath = CASE
                    WHEN excluded.LocalCoverPath <> '' THEN excluded.LocalCoverPath
                    ELSE Games.LocalCoverPath
                END,
                UpdatedAt = excluded.UpdatedAt
            RETURNING Id;
            """;
        upsert.Parameters.AddWithValue("$source", game.Source);
        upsert.Parameters.AddWithValue("$externalId", game.ExternalId);
        upsert.Parameters.AddWithValue("$name", game.Name);
        upsert.Parameters.AddWithValue("$description", game.Description ?? string.Empty);
        upsert.Parameters.AddWithValue("$category", game.Category);
        upsert.Parameters.AddWithValue("$releaseYear", (object?)game.ReleaseYear ?? DBNull.Value);
        upsert.Parameters.AddWithValue("$coverUrl", game.CoverUrl);
        upsert.Parameters.AddWithValue("$localCoverPath", localCoverPath);
        upsert.Parameters.AddWithValue("$now", now);
        var gameId = Convert.ToInt32(await upsert.ExecuteScalarAsync());

        await transaction.CommitAsync();
        return gameId;
    }

    public async Task AddGameToListAsync(int gameId, int listId)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT OR IGNORE INTO GameLists (GameId, ListId, AddedAt)
            VALUES ($gameId, $listId, $addedAt);
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$listId", listId);
        command.Parameters.AddWithValue("$addedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<ListMembershipOption>> GetMembershipsAsync(int gameId)
    {
        var memberships = new List<ListMembershipOption>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT l.Id, l.Name, l.Color, l.Emoji,
                   CASE WHEN gl.GameId IS NULL THEN 0 ELSE 1 END AS IsSelected
            FROM Lists l
            LEFT JOIN GameLists gl ON gl.ListId = l.Id AND gl.GameId = $gameId
            ORDER BY l.Id;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            memberships.Add(new ListMembershipOption
            {
                ListId = reader.GetInt32(0),
                Name = reader.GetString(1),
                Color = reader.GetString(2),
                Emoji = reader.GetString(3),
                IsSelected = reader.GetInt32(4) == 1
            });
        }

        return memberships;
    }

    public async Task SaveGameDetailsAsync(
        int gameId,
        double? rating,
        string review,
        IEnumerable<int> selectedListIds)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText =
            """
            UPDATE Games
            SET UserRating = $rating, Review = $review, UpdatedAt = $updatedAt
            WHERE Id = $gameId;
            """;
        update.Parameters.AddWithValue("$rating", (object?)rating ?? DBNull.Value);
        update.Parameters.AddWithValue("$review", review.Trim());
        update.Parameters.AddWithValue("$updatedAt", now);
        update.Parameters.AddWithValue("$gameId", gameId);
        await update.ExecuteNonQueryAsync();

        var clearMemberships = connection.CreateCommand();
        clearMemberships.Transaction = transaction;
        clearMemberships.CommandText = "DELETE FROM GameLists WHERE GameId = $gameId;";
        clearMemberships.Parameters.AddWithValue("$gameId", gameId);
        await clearMemberships.ExecuteNonQueryAsync();

        foreach (var listId in selectedListIds.Distinct())
        {
            var insertMembership = connection.CreateCommand();
            insertMembership.Transaction = transaction;
            insertMembership.CommandText =
                """
                INSERT INTO GameLists (GameId, ListId, AddedAt)
                VALUES ($gameId, $listId, $addedAt);
                """;
            insertMembership.Parameters.AddWithValue("$gameId", gameId);
            insertMembership.Parameters.AddWithValue("$listId", listId);
            insertMembership.Parameters.AddWithValue("$addedAt", now);
            await insertMembership.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task DeleteGameAsync(int gameId)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Games WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", gameId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<LibraryStats> GetStatsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*),
                   SUM(CASE WHEN TRIM(Review) <> '' THEN 1 ELSE 0 END),
                   AVG(UserRating)
            FROM Games;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new LibraryStats
        {
            TotalGames = reader.GetInt32(0),
            ReviewedGames = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            AverageRating = reader.IsDBNull(2) ? null : reader.GetDouble(2)
        };
    }

    public async Task<Dictionary<string, string>> GetSettingsAsync()
    {
        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Key, Value FROM Settings;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            settings[reader.GetString(0)] = reader.GetString(1);
        }

        return settings;
    }

    public async Task SaveSettingsAsync(IReadOnlyDictionary<string, string> settings)
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();

        foreach (var (key, value) in settings)
        {
            var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO Settings (Key, Value)
                VALUES ($key, $value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;
                """;
            command.Parameters.AddWithValue("$key", key);
            command.Parameters.AddWithValue("$value", value);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        return connection;
    }

    private static string ResolveDefaultDataDirectory()
    {
        var localAppData =
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var currentDirectory = Path.Combine(localAppData, "JOGOTECA");
        var legacyDirectory = Path.Combine(localAppData, "Ludoteca");
        return Directory.Exists(legacyDirectory) && !Directory.Exists(currentDirectory)
            ? legacyDirectory
            : currentDirectory;
    }

    private static async Task InsertDefaultListsAsync(SqliteConnection connection)
    {
        var defaults = new[]
        {
            ("Quero jogar", "#CFC4F5", "🎯"),
            ("Jogando", "#B9DDCF", "🎮"),
            ("Finalizados", "#F5C6B5", "🏆")
        };

        foreach (var (name, color, emoji) in defaults)
        {
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Lists (Name, Color, Emoji, CreatedAt)
                VALUES ($name, $color, $emoji, $createdAt);
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$color", color);
            command.Parameters.AddWithValue("$emoji", emoji);
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureListEmojiColumnAsync(SqliteConnection connection)
    {
        var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(Lists);";
        var hasEmoji = false;
        await using (var reader = await check.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1).Equals("Emoji", StringComparison.OrdinalIgnoreCase))
                {
                    hasEmoji = true;
                    break;
                }
            }
        }

        if (hasEmoji)
        {
            return;
        }

        var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Lists ADD COLUMN Emoji TEXT NOT NULL DEFAULT '🎮';";
        await alter.ExecuteNonQueryAsync();
    }

    private static string NormalizeEmoji(string emoji)
    {
        return string.IsNullOrWhiteSpace(emoji) ? "🎮" : emoji.Trim();
    }

    private static GameEntry MapGame(SqliteDataReader reader)
    {
        return new GameEntry
        {
            Id = reader.GetInt32(0),
            Source = reader.GetString(1),
            ExternalId = reader.GetString(2),
            Name = reader.GetString(3),
            Description = reader.GetString(4),
            Category = reader.GetString(5),
            ReleaseYear = reader.IsDBNull(6) ? null : reader.GetInt32(6),
            CoverUrl = reader.GetString(7),
            LocalCoverPath = reader.GetString(8),
            UserRating = reader.IsDBNull(9) ? null : reader.GetDouble(9),
            Review = reader.GetString(10),
            AddedAt = reader.GetString(11)
        };
    }
}
