using Ludoteca.Models;
using Microsoft.Data.Sqlite;
using System.IO;

namespace Ludoteca.Services;

public sealed class DatabaseService
{
    private readonly string _connectionString;
    private readonly string _databasePath;

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
        _databasePath = File.Exists(legacyDatabasePath)
            ? legacyDatabasePath
            : Path.Combine(AppDataDirectory, "jogoteca.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            ForeignKeys = true,
            Pooling = false
        }.ToString();
    }

    public string AppDataDirectory { get; }
    public string CoversDirectory { get; }
    public string DatabasePath => _databasePath;

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
                CreatedAt TEXT NOT NULL,
                Position INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Games (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Source TEXT NOT NULL,
                ExternalId TEXT NOT NULL,
                Name TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '',
                ReleaseYear INTEGER NULL,
                ReleaseDate TEXT NOT NULL DEFAULT '',
                CoverUrl TEXT NOT NULL DEFAULT '',
                LocalCoverPath TEXT NOT NULL DEFAULT '',
                PublicRating REAL NULL,
                CompletionTimeHours REAL NULL,
                StartedAt TEXT NOT NULL DEFAULT '',
                FinishedAt TEXT NOT NULL DEFAULT '',
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

            CREATE TABLE IF NOT EXISTS UpcomingReleaseWatches (
                GameId INTEGER PRIMARY KEY,
                ReleaseDate TEXT NOT NULL,
                AddedAt TEXT NOT NULL,
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS ReleaseNotifications (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                GameId INTEGER NOT NULL,
                GameName TEXT NOT NULL,
                ReleaseDate TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                IsRead INTEGER NOT NULL DEFAULT 0,
                UNIQUE(GameId, ReleaseDate),
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_GameLists_ListId ON GameLists(ListId);
            CREATE INDEX IF NOT EXISTS IX_Games_Name ON Games(Name);
            CREATE INDEX IF NOT EXISTS IX_ReleaseNotifications_IsRead ON ReleaseNotifications(IsRead);
            """;
        await command.ExecuteNonQueryAsync();
        await EnsureListEmojiColumnAsync(connection);
        await EnsureListPositionColumnAsync(connection);
        await EnsureGameReleaseDateColumnAsync(connection);
        await EnsureGameMetadataColumnsAsync(connection);

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
            SELECT l.Id, l.Name, l.Color, l.Emoji, l.Position, COUNT(gl.GameId)
            FROM Lists l
            LEFT JOIN GameLists gl ON gl.ListId = l.Id
            GROUP BY l.Id, l.Name, l.Color, l.Emoji, l.Position
            ORDER BY l.Position, l.Id;
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
                Position = reader.GetInt32(4),
                GameCount = reader.GetInt32(5)
            });
        }

        return lists;
    }

    public async Task<GameList> CreateListAsync(string name, string color, string emoji = ListEmojiCatalog.Game)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO Lists (Name, Color, Emoji, CreatedAt, Position)
            VALUES ($name, $color, $emoji, $createdAt, COALESCE((SELECT MAX(Position) + 1 FROM Lists), 0));
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
            Emoji = NormalizeEmoji(emoji),
            Position = await GetListPositionAsync(connection, id)
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

    public async Task MoveListAsync(int movedListId, int targetListId)
    {
        if (movedListId == targetListId)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();

        var ids = new List<int>();
        var select = connection.CreateCommand();
        select.Transaction = transaction;
        select.CommandText = "SELECT Id FROM Lists ORDER BY Position, Id;";
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt32(0));
            }
        }

        var movedIndex = ids.IndexOf(movedListId);
        if (movedIndex < 0 || !ids.Contains(targetListId))
        {
            await transaction.CommitAsync();
            return;
        }

        ids.RemoveAt(movedIndex);
        var targetIndex = ids.IndexOf(targetListId);
        if (targetIndex < 0)
        {
            await transaction.CommitAsync();
            return;
        }

        ids.Insert(targetIndex, movedListId);
        await UpdateListPositionsAsync(connection, transaction, ids);
        await transaction.CommitAsync();
    }

    public async Task DeleteListAsync(int listId)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Lists WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", listId);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<GameEntry>> GetGamesAsync(
        int? listId,
        string filter = "",
        GameLibraryFilter libraryFilter = GameLibraryFilter.None)
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

        switch (libraryFilter)
        {
            case GameLibraryFilter.Unrated:
                whereParts.Add("g.UserRating IS NULL");
                break;
            case GameLibraryFilter.Reviewed:
                whereParts.Add("TRIM(g.Review) <> ''");
                break;
            case GameLibraryFilter.Unorganized:
                whereParts.Add("NOT EXISTS (SELECT 1 FROM GameLists gl WHERE gl.GameId = g.Id)");
                break;
        }

        command.CommandText =
            $"""
             SELECT g.Id, g.Source, g.ExternalId, g.Name, g.Description, g.Category,
                    g.ReleaseYear, g.ReleaseDate, g.CoverUrl, g.LocalCoverPath,
                    g.PublicRating, g.CompletionTimeHours, g.StartedAt, g.FinishedAt,
                    g.UserRating, g.Review, g.AddedAt
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
                Source, ExternalId, Name, Description, Category, ReleaseYear, ReleaseDate,
                CoverUrl, LocalCoverPath, PublicRating, CompletionTimeHours, AddedAt, UpdatedAt
            )
            VALUES (
                $source, $externalId, $name, $description, $category, $releaseYear, $releaseDate,
                $coverUrl, $localCoverPath, $publicRating, $completionTimeHours, $now, $now
            )
            ON CONFLICT(Source, ExternalId) DO UPDATE SET
                Name = excluded.Name,
                Description = CASE
                    WHEN excluded.Description <> '' THEN excluded.Description
                    ELSE Games.Description
                END,
                Category = CASE
                    WHEN excluded.Category <> '' THEN excluded.Category
                    ELSE Games.Category
                END,
                ReleaseYear = COALESCE(excluded.ReleaseYear, Games.ReleaseYear),
                ReleaseDate = CASE
                    WHEN excluded.ReleaseDate <> '' THEN excluded.ReleaseDate
                    ELSE Games.ReleaseDate
                END,
                CoverUrl = CASE
                    WHEN excluded.CoverUrl <> '' THEN excluded.CoverUrl
                    ELSE Games.CoverUrl
                END,
                LocalCoverPath = CASE
                    WHEN excluded.LocalCoverPath <> '' THEN excluded.LocalCoverPath
                    ELSE Games.LocalCoverPath
                END,
                PublicRating = COALESCE(excluded.PublicRating, Games.PublicRating),
                CompletionTimeHours = COALESCE(excluded.CompletionTimeHours, Games.CompletionTimeHours),
                UpdatedAt = excluded.UpdatedAt
            RETURNING Id;
            """;
        upsert.Parameters.AddWithValue("$source", game.Source);
        upsert.Parameters.AddWithValue("$externalId", game.ExternalId);
        upsert.Parameters.AddWithValue("$name", game.Name);
        upsert.Parameters.AddWithValue("$description", game.Description ?? string.Empty);
        upsert.Parameters.AddWithValue("$category", game.Category);
        upsert.Parameters.AddWithValue("$releaseYear", (object?)game.ReleaseYear ?? DBNull.Value);
        upsert.Parameters.AddWithValue("$releaseDate", game.ReleaseDate ?? string.Empty);
        upsert.Parameters.AddWithValue("$coverUrl", game.CoverUrl);
        upsert.Parameters.AddWithValue("$localCoverPath", localCoverPath);
        upsert.Parameters.AddWithValue("$publicRating", (object?)game.PublicRating ?? DBNull.Value);
        upsert.Parameters.AddWithValue("$completionTimeHours", (object?)game.CompletionTimeHours ?? DBNull.Value);
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

    public async Task MoveGameBetweenListsAsync(
        int gameId,
        int sourceListId,
        int targetListId)
    {
        if (sourceListId == targetListId)
        {
            return;
        }

        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow.ToString("O");

        var addTarget = connection.CreateCommand();
        addTarget.Transaction = transaction;
        addTarget.CommandText =
            """
            INSERT OR IGNORE INTO GameLists (GameId, ListId, AddedAt)
            VALUES ($gameId, $targetListId, $addedAt);
            """;
        addTarget.Parameters.AddWithValue("$gameId", gameId);
        addTarget.Parameters.AddWithValue("$targetListId", targetListId);
        addTarget.Parameters.AddWithValue("$addedAt", now);
        await addTarget.ExecuteNonQueryAsync();

        var removeSource = connection.CreateCommand();
        removeSource.Transaction = transaction;
        removeSource.CommandText =
            """
            DELETE FROM GameLists
            WHERE GameId = $gameId AND ListId = $sourceListId;
            """;
        removeSource.Parameters.AddWithValue("$gameId", gameId);
        removeSource.Parameters.AddWithValue("$sourceListId", sourceListId);
        await removeSource.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task RemoveGameFromListAsync(int gameId, int listId)
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            DELETE FROM GameLists
            WHERE GameId = $gameId AND ListId = $listId;
            """;
        command.Parameters.AddWithValue("$gameId", gameId);
        command.Parameters.AddWithValue("$listId", listId);
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
            ORDER BY l.Position, l.Id;
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
        string startedAt,
        string finishedAt,
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
            SET UserRating = $rating,
                Review = $review,
                StartedAt = $startedAt,
                FinishedAt = $finishedAt,
                UpdatedAt = $updatedAt
            WHERE Id = $gameId;
            """;
        update.Parameters.AddWithValue("$rating", (object?)rating ?? DBNull.Value);
        update.Parameters.AddWithValue("$review", review.Trim());
        update.Parameters.AddWithValue("$startedAt", startedAt);
        update.Parameters.AddWithValue("$finishedAt", finishedAt);
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
        string? coverPath;
        await using (var connection = await OpenConnectionAsync())
        {
            var select = connection.CreateCommand();
            select.CommandText = "SELECT LocalCoverPath FROM Games WHERE Id = $id;";
            select.Parameters.AddWithValue("$id", gameId);
            coverPath = await select.ExecuteScalarAsync() as string;

            var command = connection.CreateCommand();
            command.CommandText = "DELETE FROM Games WHERE Id = $id;";
            command.Parameters.AddWithValue("$id", gameId);
            await command.ExecuteNonQueryAsync();

            if (!string.IsNullOrWhiteSpace(coverPath))
            {
                var stillUsed = connection.CreateCommand();
                stillUsed.CommandText = "SELECT COUNT(*) FROM Games WHERE LocalCoverPath = $path;";
                stillUsed.Parameters.AddWithValue("$path", coverPath);
                if (Convert.ToInt32(await stillUsed.ExecuteScalarAsync()) > 0)
                {
                    coverPath = null;
                }
            }
        }

        DeleteCoverFile(coverPath);
    }

    // Restored backups carry cover paths from the computer that made them.
    public async Task RelocateCoverPathsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var paths = new List<(int Id, string Path)>();
        var select = connection.CreateCommand();
        select.CommandText = "SELECT Id, LocalCoverPath FROM Games WHERE LocalCoverPath <> '';";
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                paths.Add((reader.GetInt32(0), reader.GetString(1)));
            }
        }

        await using var transaction = connection.BeginTransaction();
        foreach (var (id, path) in paths)
        {
            var fileName = Path.GetFileName(path.Replace('\\', '/'));
            var relocated = Path.Combine(CoversDirectory, fileName);
            if (relocated == path)
            {
                continue;
            }

            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Games SET LocalCoverPath = $path WHERE Id = $id;";
            update.Parameters.AddWithValue("$path", relocated);
            update.Parameters.AddWithValue("$id", id);
            await update.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<int?> FindEquivalentGameAsync(GameSearchResult game)
    {
        var name = game.Name.Trim();
        if (name.Length == 0)
        {
            return null;
        }

        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, Name, Source, ExternalId
            FROM Games
            WHERE ReleaseYear IS $releaseYear;
            """;
        command.Parameters.AddWithValue("$releaseYear", (object?)game.ReleaseYear ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var sameEntry =
                reader.GetString(2).Equals(game.Source, StringComparison.OrdinalIgnoreCase) &&
                reader.GetString(3) == game.ExternalId;
            if (!sameEntry &&
                string.Equals(reader.GetString(1).Trim(), name, StringComparison.CurrentCultureIgnoreCase))
            {
                return reader.GetInt32(0);
            }
        }

        return null;
    }

    private void DeleteCoverFile(string? coverPath)
    {
        if (string.IsNullOrWhiteSpace(coverPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(coverPath);
        var coversRoot = Path.GetFullPath(CoversDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(coversRoot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            File.Delete(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cover still in use by the window is cleaned up with the library later.
        }
    }

    public async Task<LibraryStats> GetStatsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*),
                   SUM(CASE WHEN UserRating IS NULL THEN 1 ELSE 0 END),
                   SUM(CASE WHEN TRIM(Review) <> '' THEN 1 ELSE 0 END),
                   SUM(CASE WHEN NOT EXISTS (
                       SELECT 1 FROM GameLists gl WHERE gl.GameId = Games.Id
                   ) THEN 1 ELSE 0 END),
                   AVG(UserRating)
            FROM Games;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new LibraryStats
        {
            TotalGames = reader.GetInt32(0),
            UnratedGames = reader.IsDBNull(1) ? 0 : reader.GetInt32(1),
            ReviewedGames = reader.IsDBNull(2) ? 0 : reader.GetInt32(2),
            UnorganizedGames = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
            AverageRating = reader.IsDBNull(4) ? null : reader.GetDouble(4)
        };
    }

    public async Task SyncReleaseNotificationsAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var now = DateTimeOffset.UtcNow.ToString("O");

        var createNotifications = connection.CreateCommand();
        createNotifications.Transaction = transaction;
        createNotifications.CommandText =
            """
            INSERT OR IGNORE INTO ReleaseNotifications (
                GameId, GameName, ReleaseDate, CreatedAt, IsRead
            )
            SELECT g.Id, g.Name, g.ReleaseDate, $now, 0
            FROM Games g
            INNER JOIN UpcomingReleaseWatches w ON w.GameId = g.Id
            WHERE g.ReleaseDate <> ''
              AND date(g.ReleaseDate) <= date($today);
            """;
        createNotifications.Parameters.AddWithValue("$today", today);
        createNotifications.Parameters.AddWithValue("$now", now);
        await createNotifications.ExecuteNonQueryAsync();

        var removeCompletedWatches = connection.CreateCommand();
        removeCompletedWatches.Transaction = transaction;
        removeCompletedWatches.CommandText =
            """
            DELETE FROM UpcomingReleaseWatches
            WHERE NOT EXISTS (
                SELECT 1 FROM Games g WHERE g.Id = UpcomingReleaseWatches.GameId
            )
               OR EXISTS (
                SELECT 1
                FROM Games g
                WHERE g.Id = UpcomingReleaseWatches.GameId
                  AND (g.ReleaseDate = '' OR date(g.ReleaseDate) <= date($today))
            );
            """;
        removeCompletedWatches.Parameters.AddWithValue("$today", today);
        await removeCompletedWatches.ExecuteNonQueryAsync();

        var upsertWatches = connection.CreateCommand();
        upsertWatches.Transaction = transaction;
        upsertWatches.CommandText =
            """
            INSERT INTO UpcomingReleaseWatches (GameId, ReleaseDate, AddedAt)
            SELECT Id, ReleaseDate, $now
            FROM Games
            WHERE ReleaseDate <> ''
              AND date(ReleaseDate) > date($today)
            ON CONFLICT(GameId) DO UPDATE SET
                ReleaseDate = excluded.ReleaseDate;
            """;
        upsertWatches.Parameters.AddWithValue("$today", today);
        upsertWatches.Parameters.AddWithValue("$now", now);
        await upsertWatches.ExecuteNonQueryAsync();

        await transaction.CommitAsync();
    }

    public async Task<IReadOnlyList<ReleaseNotification>> GetReleaseNotificationsAsync()
    {
        var notifications = new List<ReleaseNotification>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, GameId, GameName, ReleaseDate, CreatedAt, IsRead
            FROM ReleaseNotifications
            ORDER BY IsRead ASC, date(ReleaseDate) DESC, CreatedAt DESC;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            notifications.Add(new ReleaseNotification
            {
                Id = reader.GetInt32(0),
                GameId = reader.GetInt32(1),
                GameName = reader.GetString(2),
                ReleaseDate = reader.GetString(3),
                CreatedAt = reader.GetString(4),
                IsRead = reader.GetInt32(5) == 1
            });
        }

        return notifications;
    }

    public async Task<IReadOnlyList<UpcomingReleaseWatch>> GetUpcomingReleaseWatchesAsync()
    {
        var watches = new List<UpcomingReleaseWatch>();
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT g.Id, g.Name, w.ReleaseDate
            FROM UpcomingReleaseWatches w
            INNER JOIN Games g ON g.Id = w.GameId
            ORDER BY date(w.ReleaseDate), g.Name COLLATE NOCASE;
            """;

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            watches.Add(new UpcomingReleaseWatch
            {
                GameId = reader.GetInt32(0),
                GameName = reader.GetString(1),
                ReleaseDate = reader.GetString(2)
            });
        }

        return watches;
    }

    public async Task MarkReleaseNotificationsReadAsync()
    {
        await using var connection = await OpenConnectionAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE ReleaseNotifications SET IsRead = 1 WHERE IsRead = 0;";
        await command.ExecuteNonQueryAsync();
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

    public async Task ClearLibraryAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var transaction = connection.BeginTransaction();

        var clearReleaseNotifications = connection.CreateCommand();
        clearReleaseNotifications.Transaction = transaction;
        clearReleaseNotifications.CommandText = "DELETE FROM ReleaseNotifications;";
        await clearReleaseNotifications.ExecuteNonQueryAsync();

        var clearReleaseWatches = connection.CreateCommand();
        clearReleaseWatches.Transaction = transaction;
        clearReleaseWatches.CommandText = "DELETE FROM UpcomingReleaseWatches;";
        await clearReleaseWatches.ExecuteNonQueryAsync();

        var clearMemberships = connection.CreateCommand();
        clearMemberships.Transaction = transaction;
        clearMemberships.CommandText = "DELETE FROM GameLists;";
        await clearMemberships.ExecuteNonQueryAsync();

        var clearGames = connection.CreateCommand();
        clearGames.Transaction = transaction;
        clearGames.CommandText = "DELETE FROM Games;";
        await clearGames.ExecuteNonQueryAsync();

        await transaction.CommitAsync();

        if (Directory.Exists(CoversDirectory))
        {
            Directory.Delete(CoversDirectory, recursive: true);
        }

        Directory.CreateDirectory(CoversDirectory);
    }

    public async Task ResetDatabaseAsync()
    {
        SqliteConnection.ClearAllPools();

        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }

        var writeAheadLogPath = $"{_databasePath}-wal";
        if (File.Exists(writeAheadLogPath))
        {
            File.Delete(writeAheadLogPath);
        }

        var sharedMemoryPath = $"{_databasePath}-shm";
        if (File.Exists(sharedMemoryPath))
        {
            File.Delete(sharedMemoryPath);
        }

        if (Directory.Exists(CoversDirectory))
        {
            Directory.Delete(CoversDirectory, recursive: true);
        }

        Directory.CreateDirectory(CoversDirectory);
        await InitializeAsync();
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
            ("Quero jogar", "#CFC4F5", ListEmojiCatalog.Target),
            ("Jogando", "#B9DDCF", ListEmojiCatalog.Game),
            ("Finalizados", "#F5C6B5", ListEmojiCatalog.Trophy)
        };

        for (var index = 0; index < defaults.Length; index++)
        {
            var (name, color, emoji) = defaults[index];
            var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO Lists (Name, Color, Emoji, CreatedAt, Position)
                VALUES ($name, $color, $emoji, $createdAt, $position);
                """;
            command.Parameters.AddWithValue("$name", name);
            command.Parameters.AddWithValue("$color", color);
            command.Parameters.AddWithValue("$emoji", emoji);
            command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
            command.Parameters.AddWithValue("$position", index);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<int> GetListPositionAsync(SqliteConnection connection, int listId)
    {
        var command = connection.CreateCommand();
        command.CommandText = "SELECT Position FROM Lists WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", listId);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
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

    private static async Task EnsureListPositionColumnAsync(SqliteConnection connection)
    {
        var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(Lists);";
        var hasPosition = false;
        await using (var reader = await check.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1).Equals("Position", StringComparison.OrdinalIgnoreCase))
                {
                    hasPosition = true;
                    break;
                }
            }
        }

        if (!hasPosition)
        {
            var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE Lists ADD COLUMN Position INTEGER NOT NULL DEFAULT 0;";
            await alter.ExecuteNonQueryAsync();
        }

        await NormalizeListPositionsAsync(connection);
    }

    private static async Task NormalizeListPositionsAsync(SqliteConnection connection)
    {
        var ids = new List<int>();
        var select = connection.CreateCommand();
        select.CommandText = "SELECT Id FROM Lists ORDER BY Position, Id;";
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ids.Add(reader.GetInt32(0));
            }
        }

        await UpdateListPositionsAsync(connection, null, ids);
    }

    private static async Task UpdateListPositionsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        IReadOnlyList<int> ids)
    {
        for (var index = 0; index < ids.Count; index++)
        {
            var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = "UPDATE Lists SET Position = $position WHERE Id = $id;";
            update.Parameters.AddWithValue("$position", index);
            update.Parameters.AddWithValue("$id", ids[index]);
            await update.ExecuteNonQueryAsync();
        }
    }

    private static async Task EnsureGameReleaseDateColumnAsync(SqliteConnection connection)
    {
        var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(Games);";
        var hasReleaseDate = false;
        await using (var reader = await check.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1).Equals("ReleaseDate", StringComparison.OrdinalIgnoreCase))
                {
                    hasReleaseDate = true;
                    break;
                }
            }
        }

        if (hasReleaseDate)
        {
            return;
        }

        var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Games ADD COLUMN ReleaseDate TEXT NOT NULL DEFAULT '';";
        await alter.ExecuteNonQueryAsync();
    }

    private static async Task EnsureGameMetadataColumnsAsync(SqliteConnection connection)
    {
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(Games);";
        await using (var reader = await check.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
            }
        }

        var definitions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["PublicRating"] = "REAL NULL",
            ["CompletionTimeHours"] = "REAL NULL",
            ["StartedAt"] = "TEXT NOT NULL DEFAULT ''",
            ["FinishedAt"] = "TEXT NOT NULL DEFAULT ''"
        };

        foreach (var (column, definition) in definitions)
        {
            if (columns.Contains(column))
            {
                continue;
            }

            var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE Games ADD COLUMN {column} {definition};";
            await alter.ExecuteNonQueryAsync();
        }
    }

    private static string NormalizeEmoji(string emoji)
    {
        return ListEmojiCatalog.Normalize(emoji);
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
            ReleaseDate = reader.GetString(7),
            CoverUrl = reader.GetString(8),
            LocalCoverPath = reader.GetString(9),
            PublicRating = reader.IsDBNull(10) ? null : reader.GetDouble(10),
            CompletionTimeHours = reader.IsDBNull(11) ? null : reader.GetDouble(11),
            StartedAt = reader.GetString(12),
            FinishedAt = reader.GetString(13),
            UserRating = reader.IsDBNull(14) ? null : reader.GetDouble(14),
            Review = reader.GetString(15),
            AddedAt = reader.GetString(16)
        };
    }
}
