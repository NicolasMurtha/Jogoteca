using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ludoteca.Models;

namespace Ludoteca.Services;

public sealed partial class GameApiService
{
    private async Task<CatalogPage?> TryReadCatalogCacheAsync(
        CatalogFeed feed,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var path = CatalogCachePath(feed, page, settings);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            var entry = await JsonSerializer.DeserializeAsync<CatalogCacheEntry>(
                stream,
                cancellationToken: cancellationToken);
            if (entry is null ||
                DateTimeOffset.UtcNow - entry.CachedAt > CatalogCacheLifetime)
            {
                return null;
            }

            return entry.Page;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
    private async Task WriteCatalogCacheAsync(
        CatalogFeed feed,
        int page,
        AppSettings settings,
        CatalogPage catalogPage,
        CancellationToken cancellationToken)
    {
        var path = CatalogCachePath(feed, page, settings);
        if (path is null)
        {
            return;
        }

        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new CatalogCacheEntry
                    {
                        CachedAt = DateTimeOffset.UtcNow,
                        Page = catalogPage
                    },
                    cancellationToken: cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cache failures must not prevent the catalog from loading.
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // A stale temporary cache file is harmless.
            }
        }
    }
    private string? CatalogCachePath(
        CatalogFeed feed,
        int page,
        AppSettings settings)
    {
        if (_catalogCacheDirectory is null)
        {
            return null;
        }

        var providers = (settings.HasIgdb, settings.HasRawg) switch
        {
            (true, true) => "igdb-rawg",
            (true, false) => "igdb",
            (false, true) => "rawg",
            _ => "offline"
        };
        return Path.Combine(
            _catalogCacheDirectory,
            $"{providers}-{feed.ToString().ToLowerInvariant()}-{Math.Max(1, page)}.json");
    }
}
