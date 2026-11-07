using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Ludoteca.Services;

public sealed class BackupInfo
{
    public int FormatVersion { get; init; }
    public string AppVersion { get; init; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; }
    public int GameCount { get; init; }
    public int CoverCount { get; init; }
}

public sealed class BackupException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class BackupService(DatabaseService database)
{
    public const string FileExtension = ".jogoteca";
    public const int CurrentFormatVersion = 1;
    private const int SafetyBackupsToKeep = 5;
    private const string ManifestEntry = "manifest.json";
    private const string DatabaseEntry = "jogoteca.db";
    private const string CoversPrefix = "covers/";

    public string SafetyBackupDirectory => Path.Combine(database.AppDataDirectory, "backups");

    public static string SuggestedFileName(DateTime now) =>
        $"JOGOTECA-backup-{now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}{FileExtension}";

    public async Task<BackupInfo> CreateBackupAsync(string destinationPath)
    {
        var workDirectory = CreateWorkDirectory();
        try
        {
            var snapshotPath = Path.Combine(workDirectory, DatabaseEntry);
            await SnapshotDatabaseAsync(snapshotPath);

            var covers = Directory.Exists(database.CoversDirectory)
                ? Directory.GetFiles(database.CoversDirectory)
                : [];
            var info = new BackupInfo
            {
                FormatVersion = CurrentFormatVersion,
                AppVersion = AppVersion(),
                CreatedAt = DateTimeOffset.Now,
                GameCount = await CountGamesAsync(snapshotPath),
                CoverCount = covers.Length
            };

            var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var stream = File.Create(temporaryPath))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    var manifest = archive.CreateEntry(ManifestEntry);
                    await using (var manifestStream = manifest.Open())
                    {
                        await JsonSerializer.SerializeAsync(manifestStream, info);
                    }

                    archive.CreateEntryFromFile(snapshotPath, DatabaseEntry, CompressionLevel.Optimal);
                    foreach (var cover in covers)
                    {
                        archive.CreateEntryFromFile(
                            cover,
                            CoversPrefix + Path.GetFileName(cover),
                            CompressionLevel.NoCompression);
                    }
                }

                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            return info;
        }
        finally
        {
            Directory.Delete(workDirectory, recursive: true);
        }
    }

    public async Task<BackupInfo> ReadBackupInfoAsync(string backupPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(backupPath);
            return await ReadManifestAsync(archive);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new BackupException("Este arquivo não é um backup da JOGOTECA ou está danificado.", ex);
        }
    }

    public async Task<BackupInfo> RestoreBackupAsync(string backupPath)
    {
        var workDirectory = CreateWorkDirectory();
        try
        {
            BackupInfo info;
            var restoredDatabase = Path.Combine(workDirectory, DatabaseEntry);
            var restoredCovers = Path.Combine(workDirectory, "covers");
            Directory.CreateDirectory(restoredCovers);

            try
            {
                using var archive = ZipFile.OpenRead(backupPath);
                info = await ReadManifestAsync(archive);
                var databaseEntry = archive.GetEntry(DatabaseEntry)
                    ?? throw new BackupException("O backup não contém o banco de dados da biblioteca.");
                databaseEntry.ExtractToFile(restoredDatabase);

                foreach (var entry in archive.Entries.Where(entry =>
                             entry.FullName.StartsWith(CoversPrefix, StringComparison.Ordinal) &&
                             entry.Name.Length > 0))
                {
                    // Only plain file names: never write outside the covers folder.
                    var fileName = Path.GetFileName(entry.Name);
                    if (fileName != entry.FullName[CoversPrefix.Length..])
                    {
                        continue;
                    }

                    entry.ExtractToFile(Path.Combine(restoredCovers, fileName));
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                throw new BackupException("Este arquivo não é um backup da JOGOTECA ou está danificado.", ex);
            }

            await ValidateDatabaseAsync(restoredDatabase);

            Directory.CreateDirectory(SafetyBackupDirectory);
            var safetyPath = Path.Combine(
                SafetyBackupDirectory,
                $"antes-de-restaurar-{DateTime.Now.ToString("yyyy-MM-dd-HHmmss", CultureInfo.InvariantCulture)}{FileExtension}");
            await CreateBackupAsync(safetyPath);
            PruneSafetyBackups();

            SqliteConnection.ClearAllPools();
            File.Copy(restoredDatabase, database.DatabasePath, overwrite: true);
            DeleteIfExists($"{database.DatabasePath}-wal");
            DeleteIfExists($"{database.DatabasePath}-shm");

            ReplaceCovers(restoredCovers);
            await database.InitializeAsync();
            await database.RelocateCoverPathsAsync();
            return info;
        }
        finally
        {
            if (Directory.Exists(workDirectory))
            {
                Directory.Delete(workDirectory, recursive: true);
            }
        }
    }

    private async Task SnapshotDatabaseAsync(string snapshotPath)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = database.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path;";
        command.Parameters.AddWithValue("$path", snapshotPath);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<BackupInfo> ReadManifestAsync(ZipArchive archive)
    {
        var entry = archive.GetEntry(ManifestEntry)
            ?? throw new BackupException("Este arquivo não é um backup da JOGOTECA.");
        BackupInfo? info;
        try
        {
            await using var stream = entry.Open();
            info = await JsonSerializer.DeserializeAsync<BackupInfo>(stream);
        }
        catch (JsonException ex)
        {
            throw new BackupException("Este arquivo não é um backup da JOGOTECA ou está danificado.", ex);
        }

        if (info is null || info.FormatVersion < 1)
        {
            throw new BackupException("Este arquivo não é um backup da JOGOTECA.");
        }

        if (info.FormatVersion > CurrentFormatVersion)
        {
            throw new BackupException(
                $"Este backup foi feito na JOGOTECA {info.AppVersion}, mais nova que esta. Atualize o app antes de restaurar.");
        }

        return info;
    }

    private static async Task ValidateDatabaseAsync(string path)
    {
        try
        {
            await using var connection = new SqliteConnection(
                new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync();
            var check = connection.CreateCommand();
            check.CommandText = "PRAGMA integrity_check;";
            if (await check.ExecuteScalarAsync() as string != "ok")
            {
                throw new BackupException("O banco de dados dentro do backup está danificado.");
            }

            var tables = connection.CreateCommand();
            tables.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Games', 'Lists', 'GameLists');";
            if (Convert.ToInt32(await tables.ExecuteScalarAsync()) != 3)
            {
                throw new BackupException("O backup não contém uma biblioteca da JOGOTECA.");
            }
        }
        catch (SqliteException ex)
        {
            throw new BackupException("O banco de dados dentro do backup está danificado.", ex);
        }
    }

    private static async Task<int> CountGamesAsync(string databasePath)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = databasePath, Pooling = false }.ToString());
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM Games;";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    // Covers on screen are locked by WPF, so files are replaced one by one and
    // a locked leftover is skipped instead of failing the restore.
    private void ReplaceCovers(string restoredCovers)
    {
        Directory.CreateDirectory(database.CoversDirectory);
        var restoredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cover in Directory.GetFiles(restoredCovers))
        {
            var name = Path.GetFileName(cover);
            restoredNames.Add(name);
            TryFileOperation(() => File.Copy(cover, Path.Combine(database.CoversDirectory, name), overwrite: true));
        }

        foreach (var existing in Directory.GetFiles(database.CoversDirectory))
        {
            if (!restoredNames.Contains(Path.GetFileName(existing)))
            {
                TryFileOperation(() => File.Delete(existing));
            }
        }
    }

    private static void TryFileOperation(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void PruneSafetyBackups()
    {
        foreach (var old in new DirectoryInfo(SafetyBackupDirectory)
                     .GetFiles($"antes-de-restaurar-*{FileExtension}")
                     .OrderByDescending(file => file.Name, StringComparer.Ordinal)
                     .Skip(SafetyBackupsToKeep))
        {
            old.Delete();
        }
    }

    private string CreateWorkDirectory()
    {
        var path = Path.Combine(database.AppDataDirectory, "tmp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string AppVersion()
    {
        var version = typeof(BackupService).Assembly.GetName().Version;
        return version is null ? string.Empty : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
