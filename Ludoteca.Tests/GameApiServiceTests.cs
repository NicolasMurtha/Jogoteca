using System.Net;
using System.Net.Http;
using System.Text;
using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.Tests;

public sealed class GameApiServiceTests
{
    [Fact]
    public async Task FallsBackToRawgWhenIgdbReturnsNoResults()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "id.twitch.tv")
            {
                return Json("""{"access_token":"token","expires_in":3600}""");
            }

            if (request.RequestUri?.Host == "api.igdb.com")
            {
                return Json("[]");
            }

            if (request.RequestUri?.Host == "api.rawg.io")
            {
                return Json(
                    """
                    {
                      "results": [
                        {
                          "id": 77,
                          "name": "Fallback Game",
                          "released": "2024-10-01",
                          "background_image": "https://example.com/game.jpg",
                          "genres": [{"name": "Adventure"}]
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var service = new GameApiService(handler);
        var response = await service.SearchAsync(
            "Fallback",
            CompleteSettings(),
            CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.Equal("rawg", result.Source);
        Assert.Contains("fallback", response.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.RawgCalls);
    }

    [Fact]
    public async Task UsesIgdbWithoutCallingRawgWhenResultsExist()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "id.twitch.tv")
            {
                return Json("""{"access_token":"token","expires_in":3600}""");
            }

            if (request.RequestUri?.Host == "api.igdb.com")
            {
                return Json(
                    """
                    [
                      {
                        "id": 42,
                        "name": "Primary Game",
                        "summary": "From IGDB.",
                        "first_release_date": 1704067200,
                        "genres": [{"name": "RPG"}],
                        "cover": {"image_id": "cover42"}
                      }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var service = new GameApiService(handler);
        var response = await service.SearchAsync(
            "Primary",
            CompleteSettings(),
            CancellationToken.None);

        var result = Assert.Single(response.Results);
        Assert.Equal("igdb", result.Source);
        Assert.Equal("Primary Game", result.Name);
        Assert.Equal(0, handler.RawgCalls);
    }

    [Fact]
    public async Task UsesOnlyRawgWhenOnlyRawgIsConfigured()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "api.rawg.io")
            {
                return Json(
                    """
                    {
                      "results": [
                        {
                          "id": 91,
                          "name": "RAWG Only",
                          "released": "2025-01-15",
                          "background_image": null,
                          "genres": []
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var service = new GameApiService(handler);
        var response = await service.SearchAsync(
            "RAWG",
            new AppSettings { RawgApiKey = "rawg" },
            CancellationToken.None);

        Assert.Equal("rawg", Assert.Single(response.Results).Source);
        Assert.Equal(1, handler.RawgCalls);
        Assert.Equal(0, handler.IgdbCalls);
        Assert.Equal(0, handler.TwitchCalls);
    }

    [Fact]
    public async Task UsesOnlyIgdbWhenOnlyIgdbIsConfigured()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "id.twitch.tv")
            {
                return Json("""{"access_token":"token","expires_in":3600}""");
            }

            if (request.RequestUri?.Host == "api.igdb.com")
            {
                return Json("[]");
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var service = new GameApiService(handler);
        var response = await service.SearchAsync(
            "IGDB",
            new AppSettings
            {
                IgdbClientId = "client",
                IgdbClientSecret = "secret"
            },
            CancellationToken.None);

        Assert.Empty(response.Results);
        Assert.Equal(1, handler.IgdbCalls);
        Assert.Equal(1, handler.TwitchCalls);
        Assert.Equal(0, handler.RawgCalls);
    }

    private static AppSettings CompleteSettings()
    {
        return new AppSettings
        {
            IgdbClientId = "client",
            IgdbClientSecret = "secret",
            RawgApiKey = "rawg"
        };
    }

    private static HttpResponseMessage Json(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RawgCalls { get; private set; }
        public int IgdbCalls { get; private set; }
        public int TwitchCalls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.rawg.io")
            {
                RawgCalls++;
            }
            else if (request.RequestUri?.Host == "api.igdb.com")
            {
                IgdbCalls++;
            }
            else if (request.RequestUri?.Host == "id.twitch.tv")
            {
                TwitchCalls++;
            }

            return Task.FromResult(responder(request));
        }
    }
}
