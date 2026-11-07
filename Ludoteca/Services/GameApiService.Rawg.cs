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
    private static double? ReadRawgPublicRating(JsonElement item)
    {
        return NormalizeRating(TryReadPositiveDouble(item, "rating"), 5);
    }
    private static double? ReadRawgCompletionTimeHours(JsonElement item)
    {
        return TryReadPositiveDouble(item, "playtime");
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
}
