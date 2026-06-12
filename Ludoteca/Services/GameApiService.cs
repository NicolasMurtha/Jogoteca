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
    private readonly HttpClient _httpClient;

    public GameApiService(HttpMessageHandler? messageHandler = null)
    {
        _httpClient = messageHandler is null
            ? new HttpClient()
            : new HttpClient(messageHandler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(20);
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
             fields id,name,summary,first_release_date,genres.name,cover.image_id;
             where version_parent = null;
             limit 30;
             """,
            Encoding.UTF8,
            "text/plain");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
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
            var year = TryReadUnixYear(item, "first_release_date");
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
                CoverUrl = coverUrl
            });
        }

        return results;
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
            var releaseYear = TryReadDateYear(item, "released");
            results.Add(new GameSearchResult
            {
                Source = "rawg",
                ExternalId = item.GetProperty("id").GetInt64().ToString(CultureInfo.InvariantCulture),
                Name = item.GetProperty("name").GetString() ?? "Jogo sem nome",
                Category = ReadGenres(item),
                ReleaseYear = releaseYear,
                CoverUrl = item.TryGetProperty("background_image", out var cover) &&
                           cover.ValueKind == JsonValueKind.String
                    ? cover.GetString() ?? string.Empty
                    : string.Empty
            });
        }

        return results;
    }

    private static int? TryReadUnixYear(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(value.GetInt64()).Year;
    }

    private static int? TryReadDateYear(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String ||
            !DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var date))
        {
            return null;
        }

        return date.Year;
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
}
