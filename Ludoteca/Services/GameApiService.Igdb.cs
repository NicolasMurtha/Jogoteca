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
        var credentials = $"{settings.IgdbClientId}\n{settings.IgdbClientSecret}";
        if (!string.IsNullOrWhiteSpace(_igdbAccessToken) &&
            _igdbTokenCredentials == credentials &&
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
        _igdbTokenCredentials = credentials;
        return _igdbAccessToken;
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
    private static double? ReadIgdbPublicRating(JsonElement item)
    {
        return NormalizeRating(
            TryReadPositiveDouble(item, "rating") ??
            TryReadPositiveDouble(item, "total_rating") ??
            TryReadPositiveDouble(item, "aggregated_rating"),
            100);
    }
    private static double? SecondsToHours(double? seconds)
    {
        return seconds.HasValue
            ? Math.Round(seconds.Value / 3600, 1)
            : null;
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
            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                _igdbAccessToken = string.Empty;
            }

            return response;
        }
        finally
        {
            _igdbRequestGate.Release();
        }
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
}
