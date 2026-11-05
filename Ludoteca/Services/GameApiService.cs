using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ludoteca.Models;

namespace Ludoteca.Services;

public sealed class GameApiService : IDisposable
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
        _catalogCacheDirectory = string.IsNullOrWhiteSpace(catalogCacheDirectory)
            ? null
            : catalogCacheDirectory;
        if (_catalogCacheDirectory is not null)
        {
            Directory.CreateDirectory(_catalogCacheDirectory);
        }
    }

    private string _igdbAccessToken = string.Empty;
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

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination);
        await source.CopyToAsync(target, cancellationToken);
        return destination;
    }

    public void Dispose()
    {
        _igdbRequestGate.Dispose();
        _httpClient.Dispose();
    }

    private async Task<IReadOnlyList<GameSearchResult>> SearchIgdbAsync(
        string query,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var token = await GetIgdbTokenAsync(settings, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", settings.IgdbClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var escapedQuery = query
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
        request.Content = new StringContent(
            $"""
             search "{escapedQuery}";
             fields id,name,summary,first_release_date,release_dates.date,
                    release_dates.date_format,release_dates.y,release_dates.release_region,
                    genres.name,cover.image_id,rating,total_rating,aggregated_rating;
             where version_parent = null;
             limit 30;
             """,
            Encoding.UTF8,
            "text/plain");

        using var response = await SendIgdbAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var results = new List<GameSearchResult>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            var id = item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture);
            var name = item.GetProperty("name").GetString() ?? "Jogo sem nome";
            var description = item.TryGetProperty("summary", out var summary)
                ? summary.GetString() ?? string.Empty
                : string.Empty;
            var (releaseDate, year) = ReadIgdbReleaseInfo(item);
            var category = ReadGenres(item);
            var coverUrl = string.Empty;
            if (item.TryGetProperty("cover", out var cover) &&
                cover.TryGetProperty("image_id", out var imageId))
            {
                coverUrl =
                    $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId.GetString()}.jpg";
            }

            results.Add(new GameSearchResult
            {
                Source = "igdb",
                ExternalId = id,
                Name = name,
                Description = description,
                Category = category,
                ReleaseYear = year,
                ReleaseDate = releaseDate,
                CoverUrl = coverUrl,
                PublicRating = ReadIgdbPublicRating(item)
            });
        }

        return results;
    }

    private async Task<GameSearchResult?> GetIgdbGameByIdAsync(
        string externalId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(externalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
        {
            return null;
        }

        var token = await GetIgdbTokenAsync(settings, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", settings.IgdbClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            $"""
             fields id,name,summary,first_release_date,release_dates.date,
                    release_dates.date_format,release_dates.y,release_dates.release_region,
                    genres.name,cover.image_id,rating,total_rating,aggregated_rating;
             where id = {id};
             limit 1;
             """,
            Encoding.UTF8,
            "text/plain");

        using var response = await SendIgdbAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var result = ParseIgdbGames(document.RootElement).FirstOrDefault();
        if (result is not null)
        {
            try
            {
                result.CompletionTimeHours = await GetIgdbCompletionTimeHoursAsync(
                    id,
                    settings,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
            {
                result.CompletionTimeHours = null;
            }
        }

        return result;
    }

    private async Task<double?> GetIgdbCompletionTimeHoursAsync(
        long gameId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var token = await GetIgdbTokenAsync(settings, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.igdb.com/v4/game_time_to_beats");
        request.Headers.Add("Client-ID", settings.IgdbClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(
            $"""
             fields game_id,hastily,normally,completely;
             where game_id = {gameId};
             limit 1;
             """,
            Encoding.UTF8,
            "text/plain");

        using var response = await SendIgdbAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var item = document.RootElement.ValueKind == JsonValueKind.Array
            ? document.RootElement.EnumerateArray().FirstOrDefault()
            : default;

        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return SecondsToHours(
            TryReadPositiveDouble(item, "normally") ??
            TryReadPositiveDouble(item, "completely") ??
            TryReadPositiveDouble(item, "hastily"));
    }

    private async Task<IReadOnlyList<GameSearchResult>> GetIgdbCatalogAsync(
        CatalogFeed feed,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var token = await GetIgdbTokenAsync(settings, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", settings.IgdbClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = BuildIgdbCatalogQuery(feed, page);
        request.Content = new StringContent(body, Encoding.UTF8, "text/plain");

        using var response = await SendIgdbAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseIgdbGames(document.RootElement);
    }

    private async Task<IReadOnlyDictionary<CatalogFeed, CatalogPage>> GetIgdbCatalogBatchAsync(
        IReadOnlyCollection<CatalogFeed> feeds,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var token = await GetIgdbTokenAsync(settings, cancellationToken);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.igdb.com/v4/multiquery");
        request.Headers.Add("Client-ID", settings.IgdbClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var queries = feeds.Select(feed =>
            $"query games \"{feed}\" {{{Environment.NewLine}" +
            $"{IndentQuery(BuildIgdbCatalogQuery(feed, page), 2)}{Environment.NewLine}" +
            "};");
        request.Content = new StringContent(
            string.Join(Environment.NewLine, queries),
            Encoding.UTF8,
            "text/plain");

        using var response = await SendIgdbAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var pages = new Dictionary<CatalogFeed, CatalogPage>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("name", out var nameElement) ||
                !Enum.TryParse<CatalogFeed>(nameElement.GetString(), out var feed))
            {
                continue;
            }

            var results = item.TryGetProperty("result", out var resultElement) &&
                          resultElement.ValueKind == JsonValueKind.Array
                ? ParseIgdbGames(resultElement)
                : [];
            pages[feed] = new CatalogPage
            {
                Results = results,
                HasMore = results.Count == PageSize(feed),
                Message = "Catálogo carregado pelo IGDB."
            };
        }

        return pages;
    }

    private async Task<string> GetIgdbTokenAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_igdbAccessToken) &&
            DateTimeOffset.UtcNow < _igdbTokenExpiresAt)
        {
            return _igdbAccessToken;
        }

        var url =
            "https://id.twitch.tv/oauth2/token" +
            $"?client_id={Uri.EscapeDataString(settings.IgdbClientId)}" +
            $"&client_secret={Uri.EscapeDataString(settings.IgdbClientSecret)}" +
            "&grant_type=client_credentials";
        using var response = await _httpClient.PostAsync(url, null, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        _igdbAccessToken = document.RootElement.GetProperty("access_token").GetString() ?? string.Empty;
        var expiresIn = document.RootElement.GetProperty("expires_in").GetInt32();
        _igdbTokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn - 120));
        return _igdbAccessToken;
    }

    private async Task<IReadOnlyList<GameSearchResult>> SearchRawgAsync(
        string query,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var url =
            "https://api.rawg.io/api/games" +
            $"?key={Uri.EscapeDataString(settings.RawgApiKey)}" +
            $"&search={Uri.EscapeDataString(query)}" +
            "&search_precise=true&page_size=30";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var results = new List<GameSearchResult>();

        if (!document.RootElement.TryGetProperty("results", out var items))
        {
            return results;
        }

        foreach (var item in items.EnumerateArray())
        {
            var releaseDate = TryReadDate(item, "released");
            var releaseYear = ReleaseYearFromDate(releaseDate);
            results.Add(new GameSearchResult
            {
                Source = "rawg",
                ExternalId = item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
                Name = item.GetProperty("name").GetString() ?? "Jogo sem nome",
                Category = ReadGenres(item),
                ReleaseYear = releaseYear,
                ReleaseDate = releaseDate,
                CoverUrl = item.TryGetProperty("background_image", out var cover) &&
                           cover.ValueKind == JsonValueKind.String
                    ? cover.GetString() ?? string.Empty
                    : string.Empty,
                PublicRating = ReadRawgPublicRating(item),
                CompletionTimeHours = ReadRawgCompletionTimeHours(item)
            });
        }

        return results;
    }

    private async Task<GameSearchResult?> GetRawgGameByIdAsync(
        string externalId,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        if (!long.TryParse(externalId, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            return null;
        }

        var url =
            $"https://api.rawg.io/api/games/{Uri.EscapeDataString(externalId)}" +
            $"?key={Uri.EscapeDataString(settings.RawgApiKey)}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var releaseDate = TryReadDate(root, "released");

        return new GameSearchResult
        {
            Source = "rawg",
            ExternalId = root.TryGetProperty("id", out var id)
                ? id.GetInt64().ToString(CultureInfo.InvariantCulture)
                : externalId,
            Name = root.TryGetProperty("name", out var name)
                ? name.GetString() ?? "Jogo sem nome"
                : "Jogo sem nome",
            Description = root.TryGetProperty("description_raw", out var description)
                ? description.GetString() ?? string.Empty
                : string.Empty,
            Category = ReadGenres(root),
            ReleaseYear = ReleaseYearFromDate(releaseDate),
            ReleaseDate = releaseDate,
            CoverUrl = root.TryGetProperty("background_image", out var cover) &&
                       cover.ValueKind == JsonValueKind.String
                ? cover.GetString() ?? string.Empty
                : string.Empty,
            PublicRating = ReadRawgPublicRating(root),
            CompletionTimeHours = ReadRawgCompletionTimeHours(root)
        };
    }

    private async Task<IReadOnlyList<GameSearchResult>> GetRawgCatalogAsync(
        CatalogFeed feed,
        int page,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var todayDate = DateTime.UtcNow.Date;
        var today = todayDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var tomorrow = todayDate.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var recent = todayDate.AddMonths(-18).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var sixMonthsAhead = todayDate.AddMonths(6).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var twoYearsAhead = todayDate.AddYears(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var (ordering, dates) = feed switch
        {
            CatalogFeed.Recommended => ("-rating", $"2020-01-01,{today}"),
            CatalogFeed.Trending => ("-added", $"{recent},{today}"),
            CatalogFeed.MostAnticipated => ("-added", $"{tomorrow},{twoYearsAhead}"),
            CatalogFeed.Upcoming => ("released", $"{tomorrow},{sixMonthsAhead}"),
            _ => ("-released", $"{recent},{today}")
        };
        var pageSize = PageSize(feed);
        var url =
            "https://api.rawg.io/api/games" +
            $"?key={Uri.EscapeDataString(settings.RawgApiKey)}" +
            $"&ordering={Uri.EscapeDataString(ordering)}" +
            $"&dates={Uri.EscapeDataString(dates)}" +
            "&exclude_additions=true" +
            $"&page={Math.Max(1, page)}" +
            $"&page_size={pageSize}";
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ParseRawgGames(document.RootElement);
    }

    private static int? TryReadUnixYear(JsonElement item, string propertyName)
    {
        return ReleaseYearFromDate(TryReadUnixDate(item, propertyName));
    }

    private static (string ReleaseDate, int? ReleaseYear) ReadIgdbReleaseInfo(JsonElement item)
    {
        if (item.TryGetProperty("release_dates", out var releaseDates) &&
            releaseDates.ValueKind == JsonValueKind.Array)
        {
            var exactDate = releaseDates.EnumerateArray()
                .Select(ReadIgdbReleaseDate)
                .Where(date => !string.IsNullOrWhiteSpace(date))
                .OrderBy(date => date, StringComparer.Ordinal)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(exactDate))
            {
                return (exactDate, ReleaseYearFromDate(exactDate));
            }

            var year = releaseDates.EnumerateArray()
                .Select(ReadIgdbReleaseYear)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Order()
                .FirstOrDefault();
            if (year > 0)
            {
                return (string.Empty, year);
            }
        }

        var fallbackDate = TryReadUnixDate(item, "first_release_date");
        return (fallbackDate, ReleaseYearFromDate(fallbackDate) ?? TryReadUnixYear(item, "first_release_date"));
    }

    private static string ReadIgdbReleaseDate(JsonElement releaseDate)
    {
        if (!releaseDate.TryGetProperty("date_format", out var format) ||
            format.ValueKind != JsonValueKind.Number ||
            format.GetInt32() != IgdbExactDateFormat)
        {
            return string.Empty;
        }

        return TryReadUnixDate(releaseDate, "date");
    }

    private static int? ReadIgdbReleaseYear(JsonElement releaseDate)
    {
        if (releaseDate.TryGetProperty("y", out var year) &&
            year.ValueKind == JsonValueKind.Number)
        {
            return year.GetInt32();
        }

        return TryReadUnixYear(releaseDate, "date");
    }

    private static string TryReadUnixDate(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return string.Empty;
        }

        return DateTimeOffset
            .FromUnixTimeSeconds(value.GetInt64())
            .UtcDateTime
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string TryReadDate(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var date))
        {
            return string.Empty;
        }

        return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static double? ReadIgdbPublicRating(JsonElement item)
    {
        return NormalizeRating(
            TryReadPositiveDouble(item, "rating") ??
            TryReadPositiveDouble(item, "total_rating") ??
            TryReadPositiveDouble(item, "aggregated_rating"),
            100);
    }

    private static double? ReadRawgPublicRating(JsonElement item)
    {
        return NormalizeRating(TryReadPositiveDouble(item, "rating"), 5);
    }

    private static double? ReadRawgCompletionTimeHours(JsonElement item)
    {
        return TryReadPositiveDouble(item, "playtime");
    }

    private static double? NormalizeRating(double? value, double sourceMax)
    {
        if (!value.HasValue || value.Value <= 0 || sourceMax <= 0)
        {
            return null;
        }

        return Math.Round(Math.Clamp(value.Value / sourceMax * 10, 0, 10), 1);
    }

    private static double? SecondsToHours(double? seconds)
    {
        return seconds.HasValue
            ? Math.Round(seconds.Value / 3600, 1)
            : null;
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

    private async Task<HttpResponseMessage> SendIgdbAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await _igdbRequestGate.WaitAsync(cancellationToken);
        try
        {
            var delay = _nextIgdbRequestAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }

            _nextIgdbRequestAt = DateTimeOffset.UtcNow.AddMilliseconds(275);
            return await _httpClient.SendAsync(request, cancellationToken);
        }
        finally
        {
            _igdbRequestGate.Release();
        }
    }

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

    private static int PageSize(CatalogFeed feed)
    {
        return 40;
    }

    private static string BuildIgdbCatalogQuery(CatalogFeed feed, int page)
    {
        var nowDate = DateTimeOffset.UtcNow;
        var now = nowDate.ToUnixTimeSeconds();
        var recent = nowDate.AddMonths(-18).ToUnixTimeSeconds();
        var sixMonthsAhead = nowDate.AddMonths(6).ToUnixTimeSeconds();
        var limit = PageSize(feed);
        var offset = Math.Max(0, page - 1) * limit;
        return feed switch
        {
            CatalogFeed.Recommended => BuildIgdbCatalogQuery(
                $"first_release_date <= {now} & total_rating_count > 100",
                "rating desc",
                limit,
                offset),
            CatalogFeed.Trending => BuildIgdbCatalogQuery(
                $"first_release_date >= {recent} & first_release_date <= {now} & total_rating_count > 5",
                "total_rating_count desc",
                limit,
                offset),
            CatalogFeed.MostAnticipated => BuildIgdbCatalogQuery(
                $"first_release_date > {now} & hypes != null",
                "hypes desc",
                limit,
                offset),
            CatalogFeed.Upcoming => BuildIgdbCatalogQuery(
                $"first_release_date > {now} & first_release_date <= {sixMonthsAhead}",
                "first_release_date asc",
                limit,
                offset),
            _ => BuildIgdbCatalogQuery(
                $"first_release_date >= {recent} & first_release_date <= {now}",
                "first_release_date desc",
                limit,
                offset)
        };
    }

    private static string BuildIgdbCatalogQuery(
        string feedFilter,
        string sort,
        int limit,
        int offset)
    {
        return $"""
                fields id,name,summary,first_release_date,release_dates.date,
                       release_dates.date_format,release_dates.y,release_dates.release_region,
                       genres.name,cover.image_id,
                       rating,total_rating,aggregated_rating,rating_count,total_rating_count,hypes;
                where version_parent = null & cover != null & first_release_date != null &
                      {feedFilter};
                sort {sort};
                limit {limit};
                offset {offset};
                """;
    }

    private static string IndentQuery(string query, int spaces)
    {
        var indentation = new string(' ', spaces);
        return string.Join(
            Environment.NewLine,
            query.Split(Environment.NewLine)
                .Select(line => $"{indentation}{line}"));
    }

    private static IReadOnlyList<GameSearchResult> ParseIgdbGames(JsonElement items)
    {
        var results = new List<GameSearchResult>();
        foreach (var item in items.EnumerateArray())
        {
            var (releaseDate, year) = ReadIgdbReleaseInfo(item);
            var coverUrl = string.Empty;
            if (item.TryGetProperty("cover", out var cover) &&
                cover.TryGetProperty("image_id", out var imageId))
            {
                coverUrl =
                    $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId.GetString()}.jpg";
            }

            results.Add(new GameSearchResult
            {
                Source = "igdb",
                ExternalId = item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
                Name = item.GetProperty("name").GetString() ?? "Jogo sem nome",
                Description = item.TryGetProperty("summary", out var summary)
                    ? summary.GetString() ?? string.Empty
                    : string.Empty,
                Category = ReadGenres(item),
                ReleaseYear = year,
                ReleaseDate = releaseDate,
                CoverUrl = coverUrl,
                PublicRating = ReadIgdbPublicRating(item)
            });
        }

        return results;
    }

    private static IReadOnlyList<GameSearchResult> ParseRawgGames(JsonElement root)
    {
        var results = new List<GameSearchResult>();
        if (!root.TryGetProperty("results", out var items))
        {
            return results;
        }

        foreach (var item in items.EnumerateArray())
        {
            var releaseDate = TryReadDate(item, "released");
            results.Add(new GameSearchResult
            {
                Source = "rawg",
                ExternalId = item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
                Name = item.GetProperty("name").GetString() ?? "Jogo sem nome",
                Category = ReadGenres(item),
                ReleaseYear = ReleaseYearFromDate(releaseDate),
                ReleaseDate = releaseDate,
                CoverUrl = item.TryGetProperty("background_image", out var cover) &&
                           cover.ValueKind == JsonValueKind.String
                    ? cover.GetString() ?? string.Empty
                    : string.Empty,
                PublicRating = ReadRawgPublicRating(item),
                CompletionTimeHours = ReadRawgCompletionTimeHours(item)
            });
        }

        return results;
    }

    private sealed class CatalogCacheEntry
    {
        public DateTimeOffset CachedAt { get; init; }
        public CatalogPage Page { get; init; } = new();
    }
}
