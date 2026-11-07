using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ludoteca.Models;

namespace Ludoteca.Services;

public sealed partial class GameApiService : IDisposable
{
    private static readonly TimeSpan CatalogCacheLifetime = TimeSpan.FromHours(12);
    private const int IgdbExactDateFormat = 0;
    private readonly HttpClient _httpClient;
    private readonly string? _catalogCacheDirectory;
    private readonly SemaphoreSlim _igdbRequestGate = new(1, 1);
    private DateTimeOffset _nextIgdbRequestAt = DateTimeOffset.MinValue;

    public GameApiService(
        HttpMessageHandler? messageHandler = null,
        string? catalogCacheDirectory = null)
    {
        _httpClient = messageHandler is null
            ? new HttpClient()
            : new HttpClient(messageHandler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
        var version = typeof(GameApiService).Assembly.GetName().Version;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            version is null
                ? "JOGOTECA"
                : $"JOGOTECA/{version.Major}.{version.Minor}.{version.Build}");
        _catalogCacheDirectory = string.IsNullOrWhiteSpace(catalogCacheDirectory)
            ? null
            : catalogCacheDirectory;
        if (_catalogCacheDirectory is not null)
        {
            Directory.CreateDirectory(_catalogCacheDirectory);
        }
    }

    private string _igdbAccessToken = string.Empty;
    private string _igdbTokenCredentials = string.Empty;
    private DateTimeOffset _igdbTokenExpiresAt = DateTimeOffset.MinValue;

    public async Task<GameSearchResponse> SearchAsync(
        string query,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.HasAnyProvider)
        {
            throw new InvalidOperationException(
                "Configure o IGDB ou o RAWG antes de buscar jogos.");
        }

        Exception? igdbError = null;
        if (settings.HasIgdb)
        {
            try
            {
                var igdbResults = await SearchIgdbAsync(query, settings, cancellationToken);
                if (igdbResults.Count > 0)
                {
                    return new GameSearchResponse
                    {
                        Results = igdbResults,
                        Message = $"Resultados encontrados no IGDB para \"{query}\"."
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                igdbError = ex;
            }
        }

        if (settings.HasRawg)
        {
            try
            {
                var rawgResults = await SearchRawgAsync(query, settings, cancellationToken);
                var fallbackMessage = settings.HasIgdb
                    ? "O IGDB não retornou resultados; fallback concluído pelo RAWG."
                    : "Resultados encontrados no RAWG.";
                return new GameSearchResponse
                {
                    Results = rawgResults,
                    Message = rawgResults.Count > 0
                        ? fallbackMessage
                        : settings.HasIgdb
                            ? "Nenhum jogo foi encontrado no IGDB ou no RAWG."
                            : "Nenhum jogo foi encontrado no RAWG."
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new HttpRequestException(
                    "Não foi possível consultar o RAWG. Verifique a chave e sua conexão.",
                    ex);
            }
        }

        if (igdbError is not null)
        {
            throw new HttpRequestException(
                "Não foi possível consultar o IGDB. Verifique o Client ID e o Client Secret.",
                igdbError);
        }

        return new GameSearchResponse
        {
            Message = "Nenhum jogo foi encontrado no IGDB."
        };
    }

    public async Task<CatalogPage> GetCatalogAsync(
        CatalogFeed feed,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var cached = await TryReadCatalogCacheAsync(feed, page, settings, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var result = await GetCatalogUncachedAsync(feed, page, settings, cancellationToken);
        await WriteCatalogCacheAsync(feed, page, settings, result, cancellationToken);
        return result;
    }

    public async Task<IReadOnlyDictionary<CatalogFeed, CatalogPage>> GetCatalogBatchAsync(
        IEnumerable<CatalogFeed> feeds,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var requestedFeeds = feeds.Distinct().ToArray();
        var pages = new Dictionary<CatalogFeed, CatalogPage>();
        var missingFeeds = new List<CatalogFeed>();

        foreach (var feed in requestedFeeds)
        {
            var cached = await TryReadCatalogCacheAsync(feed, page, settings, cancellationToken);
            if (cached is null)
            {
                missingFeeds.Add(feed);
            }
            else
            {
                pages[feed] = cached;
            }
        }

        if (missingFeeds.Count == 0)
        {
            return pages;
        }

        var feedsToCache = missingFeeds.ToHashSet();
        Exception? igdbError = null;
        if (settings.HasIgdb)
        {
            try
            {
                var igdbPages = await GetIgdbCatalogBatchAsync(
                    missingFeeds,
                    page,
                    settings,
                    cancellationToken);
                foreach (var feed in missingFeeds.ToArray())
                {
                    var igdbPage = igdbPages.GetValueOrDefault(feed) ?? new CatalogPage();
                    if (igdbPage.Results.Count == 0 && settings.HasRawg)
                    {
                        continue;
                    }

                    pages[feed] = igdbPage;
                    missingFeeds.Remove(feed);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                igdbError = ex;
            }
        }

        if (settings.HasRawg)
        {
            foreach (var feed in missingFeeds.ToArray())
            {
                var rawgResults = await GetRawgCatalogAsync(feed, page, settings, cancellationToken);
                pages[feed] = new CatalogPage
                {
                    Results = rawgResults,
                    HasMore = rawgResults.Count == PageSize(feed),
                    Message = settings.HasIgdb
                        ? "Catálogo carregado pelo RAWG após fallback."
                        : "Catálogo carregado pelo RAWG."
                };
                missingFeeds.Remove(feed);
            }
        }

        if (missingFeeds.Count > 0 && igdbError is not null)
        {
            throw new HttpRequestException(
                "Não foi possível carregar o catálogo pelo IGDB.",
                igdbError);
        }

        foreach (var feed in missingFeeds)
        {
            pages[feed] = new CatalogPage();
        }

        foreach (var (feed, catalogPage) in pages)
        {
            if (!feedsToCache.Contains(feed))
            {
                continue;
            }

            await WriteCatalogCacheAsync(
                feed,
                page,
                settings,
                catalogPage,
                cancellationToken);
        }

        return pages;
    }

    private async Task<CatalogPage> GetCatalogUncachedAsync(
        CatalogFeed feed,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!settings.HasAnyProvider)
        {
            return new CatalogPage
            {
                Message = "Configure IGDB ou RAWG para carregar o catálogo."
            };
        }

        Exception? igdbError = null;
        if (settings.HasIgdb)
        {
            try
            {
                var igdbResults = await GetIgdbCatalogAsync(feed, page, settings, cancellationToken);
                if (igdbResults.Count > 0 || !settings.HasRawg)
                {
                    return new CatalogPage
                    {
                        Results = igdbResults,
                        HasMore = igdbResults.Count == PageSize(feed),
                        Message = "Catálogo carregado pelo IGDB."
                    };
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                igdbError = ex;
            }
        }

        if (settings.HasRawg)
        {
            try
            {
                var rawgResults = await GetRawgCatalogAsync(feed, page, settings, cancellationToken);
                return new CatalogPage
                {
                    Results = rawgResults,
                    HasMore = rawgResults.Count == PageSize(feed),
                    Message = settings.HasIgdb
                        ? "Catálogo carregado pelo RAWG após fallback."
                        : "Catálogo carregado pelo RAWG."
                };
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                throw new HttpRequestException(
                    "Não foi possível carregar o catálogo. Verifique a chave e sua conexão.",
                    ex);
            }
        }

        throw new HttpRequestException(
            "Não foi possível carregar o catálogo pelo IGDB.",
            igdbError);
    }

    public async Task EnrichAsync(
        GameSearchResult game,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!game.Source.Equals("rawg", StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrWhiteSpace(game.Description) ||
            !settings.HasRawg)
        {
            return;
        }

        var url =
            $"https://api.rawg.io/api/games/{Uri.EscapeDataString(game.ExternalId)}" +
            $"?key={Uri.EscapeDataString(settings.RawgApiKey)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.TryGetProperty("description_raw", out var description))
        {
            game.Description = description.GetString() ?? string.Empty;
        }
    }

    public async Task<GameSearchResult?> GetGameDetailsAsync(
        GameSearchResult game,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return await GetGameDetailsAsync(
            game.Source,
            game.ExternalId,
            settings,
            cancellationToken);
    }

    public async Task<GameSearchResult?> GetGameDetailsAsync(
        GameEntry game,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        return await GetGameDetailsAsync(
            game.Source,
            game.ExternalId,
            settings,
            cancellationToken);
    }

    private async Task<GameSearchResult?> GetGameDetailsAsync(
        string source,
        string externalId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (source.Equals("igdb", StringComparison.OrdinalIgnoreCase))
        {
            return settings.HasIgdb
                ? await GetIgdbGameByIdAsync(externalId, settings, cancellationToken)
                : null;
        }

        if (source.Equals("rawg", StringComparison.OrdinalIgnoreCase))
        {
            return settings.HasRawg
                ? await GetRawgGameByIdAsync(externalId, settings, cancellationToken)
                : null;
        }

        return null;
    }

    public async Task<string> DownloadCoverAsync(
        GameSearchResult game,
        string coversDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(game.CoverUrl))
        {
            return string.Empty;
        }

        var hashBytes = SHA256.HashData(
            Encoding.UTF8.GetBytes($"{game.Source}:{game.ExternalId}:{game.CoverUrl}"));
        var fileName = $"{Convert.ToHexString(hashBytes)[..24].ToLowerInvariant()}.jpg";
        var destination = Path.Combine(coversDirectory, fileName);
        if (File.Exists(destination))
        {
            return destination;
        }

        using var response = await _httpClient.GetAsync(
            game.CoverUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var temporaryPath = $"{destination}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(temporaryPath))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            File.Move(temporaryPath, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return destination;
    }

    public void Dispose()
    {
        _igdbRequestGate.Dispose();
        _httpClient.Dispose();
    }

    private static double? NormalizeRating(double? value, double sourceMax)
    {
        if (!value.HasValue || value.Value <= 0 || sourceMax <= 0)
        {
            return null;
        }

        return Math.Round(Math.Clamp(value.Value / sourceMax * 10, 0, 10), 1);
    }

    private static double? TryReadPositiveDouble(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number ||
            !value.TryGetDouble(out var number) ||
            number <= 0)
        {
            return null;
        }

        return number;
    }

    private static int? ReleaseYearFromDate(string releaseDate)
    {
        return DateTime.TryParseExact(
            releaseDate,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date.Year
            : null;
    }

    private static string ReadGenres(JsonElement item)
    {
        if (!item.TryGetProperty("genres", out var genres) ||
            genres.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        return string.Join(
            ", ",
            genres.EnumerateArray()
                .Select(genre =>
                    genre.TryGetProperty("name", out var name)
                        ? name.GetString()
                        : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Take(3));
    }

    private static int PageSize(CatalogFeed feed)
    {
        return 40;
    }

    private sealed class CatalogCacheEntry
    {
        public DateTimeOffset CachedAt { get; init; }
        public CatalogPage Page { get; init; } = new();
    }
}
