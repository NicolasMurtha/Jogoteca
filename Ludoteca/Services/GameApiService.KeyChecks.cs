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
    public async Task<ProviderCheckResult> CheckRawgKeyAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        var url =
            "https://api.rawg.io/api/games" +
            $"?key={Uri.EscapeDataString(apiKey.Trim())}&page_size=1";
        try
        {
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            return ClassifyCheckResponse(response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ProviderCheckResult.Unreachable;
        }
    }
    public async Task<ProviderCheckResult> CheckIgdbCredentialsAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        try
        {
            var tokenUrl =
                "https://id.twitch.tv/oauth2/token" +
                $"?client_id={Uri.EscapeDataString(clientId.Trim())}" +
                $"&client_secret={Uri.EscapeDataString(clientSecret.Trim())}" +
                "&grant_type=client_credentials";
            using var tokenResponse = await _httpClient.PostAsync(tokenUrl, null, cancellationToken);
            if (!tokenResponse.IsSuccessStatusCode)
            {
                return ClassifyCheckResponse(tokenResponse.StatusCode);
            }

            await using var stream = await tokenResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var token = document.RootElement.GetProperty("access_token").GetString() ?? string.Empty;

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            request.Headers.Add("Client-ID", clientId.Trim());
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent("fields id; limit 1;", Encoding.UTF8, "text/plain");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            return ClassifyCheckResponse(response.StatusCode);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
        {
            return ProviderCheckResult.Unreachable;
        }
    }
    private static ProviderCheckResult ClassifyCheckResponse(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code switch
        {
            >= 200 and < 300 => ProviderCheckResult.Valid,
            400 or 401 or 403 => ProviderCheckResult.Invalid,
            _ => ProviderCheckResult.Unreachable
        };
    }
}
