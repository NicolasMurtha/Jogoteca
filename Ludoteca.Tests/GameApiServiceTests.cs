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
        Assert.Equal(2024, result.ReleaseYear);
        Assert.Equal("2024-01-01", result.ReleaseDate);
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
        Assert.Equal("2025-01-15", Assert.Single(response.Results).ReleaseDate);
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

    [Fact]
    public async Task LoadsPagedLatestCatalogUsingOnlyRawgWhenConfigured()
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
                          "id": 501,
                          "name": "Novo lançamento",
                          "released": "2026-06-01",
                          "background_image": "https://example.com/new.jpg",
                          "genres": [{"name": "Action"}]
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.InternalServerError);
        });

        using var service = new GameApiService(handler);
        var page = await service.GetCatalogAsync(
            CatalogFeed.Latest,
            2,
            new AppSettings { RawgApiKey = "rawg" },
            CancellationToken.None);

        var game = Assert.Single(page.Results);
        Assert.Equal("rawg", game.Source);
        Assert.Equal("Novo lançamento", game.Name);
        Assert.False(page.HasMore);
        Assert.Contains("page=2", handler.LastRawgUri?.Query);
        Assert.Contains("ordering=-released", handler.LastRawgUri?.Query);
        Assert.Equal(0, handler.IgdbCalls);
    }

    [Fact]
    public async Task CatalogFallsBackToRawgWhenIgdbReturnsNoGames()
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
                          "id": 777,
                          "name": "Catálogo fallback",
                          "released": "2025-05-10",
                          "background_image": null,
                          "genres": []
                        }
                      ]
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var service = new GameApiService(handler);
        var page = await service.GetCatalogAsync(
            CatalogFeed.Recommended,
            1,
            CompleteSettings(),
            CancellationToken.None);

        Assert.Equal("rawg", Assert.Single(page.Results).Source);
        Assert.Contains("fallback", page.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, handler.IgdbCalls);
        Assert.Equal(1, handler.RawgCalls);
    }

    [Theory]
    [InlineData(CatalogFeed.Trending, "ordering=-added")]
    [InlineData(CatalogFeed.MostAnticipated, "ordering=-added")]
    [InlineData(CatalogFeed.Upcoming, "ordering=released")]
    [InlineData(CatalogFeed.Latest, "ordering=-released")]
    public async Task RawgCatalogUsesExpectedOrdering(
        CatalogFeed feed,
        string expectedOrdering)
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri?.Host == "api.rawg.io"
                ? Json("""{"results":[]}""")
                : new HttpResponseMessage(HttpStatusCode.NotFound));

        using var service = new GameApiService(handler);
        await service.GetCatalogAsync(
            feed,
            1,
            new AppSettings { RawgApiKey = "rawg" },
            CancellationToken.None);

        Assert.Contains(expectedOrdering, handler.LastRawgUri?.Query);
    }

    [Theory]
    [InlineData(CatalogFeed.Trending, "sort total_rating_count desc")]
    [InlineData(CatalogFeed.MostAnticipated, "hypes != null")]
    [InlineData(CatalogFeed.Upcoming, "sort first_release_date asc")]
    [InlineData(CatalogFeed.Latest, "sort first_release_date desc")]
    public async Task IgdbCatalogUsesExpectedFeedFilter(
        CatalogFeed feed,
        string expectedQueryFragment)
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "id.twitch.tv")
            {
                return Json("""{"access_token":"token","expires_in":3600}""");
            }

            return request.RequestUri?.Host == "api.igdb.com"
                ? Json("[]")
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var service = new GameApiService(handler);
        await service.GetCatalogAsync(
            feed,
            1,
            new AppSettings
            {
                IgdbClientId = "client",
                IgdbClientSecret = "secret"
            },
            CancellationToken.None);

        Assert.Contains(expectedQueryFragment, handler.LastIgdbBody);
    }

    [Fact]
    public async Task LoadsInitialIgdbFeedsWithOneMultiqueryRequest()
    {
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "id.twitch.tv")
            {
                return Json("""{"access_token":"token","expires_in":3600}""");
            }

            if (request.RequestUri?.AbsolutePath.EndsWith("/multiquery") == true)
            {
                return Json(
                    """
                    [
                      {
                        "name": "Recommended",
                        "result": [
                          {
                            "id": 42,
                            "name": "Batch Game",
                            "summary": "Loaded once.",
                            "first_release_date": 1704067200,
                            "genres": [{"name": "RPG"}],
                            "cover": {"image_id": "cover42"}
                          }
                        ]
                      },
                      {
                        "name": "Trending",
                        "result": []
                      }
                    ]
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var service = new GameApiService(handler);
        var pages = await service.GetCatalogBatchAsync(
            [CatalogFeed.Recommended, CatalogFeed.Trending],
            1,
            new AppSettings
            {
                IgdbClientId = "client",
                IgdbClientSecret = "secret"
            },
            CancellationToken.None);

        Assert.Equal("Batch Game", Assert.Single(pages[CatalogFeed.Recommended].Results).Name);
        Assert.Equal("2024-01-01", Assert.Single(pages[CatalogFeed.Recommended].Results).ReleaseDate);
        Assert.Empty(pages[CatalogFeed.Trending].Results);
        Assert.Equal(1, handler.IgdbCalls);
        Assert.Contains("query games \"Recommended\"", handler.LastIgdbBody);
        Assert.Contains("query games \"Trending\"", handler.LastIgdbBody);
    }

    [Fact]
    public async Task ReusesCatalogCacheAcrossServiceInstances()
    {
        var cacheDirectory =
            Path.Combine(Path.GetTempPath(), $"Jogoteca.CatalogCache.{Guid.NewGuid():N}");
        try
        {
            var firstHandler = new StubHttpMessageHandler(request =>
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
                            "id": 99,
                            "name": "Cached Game",
                            "first_release_date": 1704067200,
                            "genres": [],
                            "cover": {"image_id": "cached99"}
                          }
                        ]
                        """);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using (var firstService = new GameApiService(firstHandler, cacheDirectory))
            {
                var firstPage = await firstService.GetCatalogAsync(
                    CatalogFeed.Latest,
                    1,
                    new AppSettings
                    {
                        IgdbClientId = "client",
                        IgdbClientSecret = "secret"
                    },
                    CancellationToken.None);
                Assert.Equal("Cached Game", Assert.Single(firstPage.Results).Name);
            }

            var secondHandler = new StubHttpMessageHandler(
                _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
            using var secondService = new GameApiService(secondHandler, cacheDirectory);
            var cachedPage = await secondService.GetCatalogAsync(
                CatalogFeed.Latest,
                1,
                new AppSettings
                {
                    IgdbClientId = "client",
                    IgdbClientSecret = "secret"
                },
                CancellationToken.None);

            Assert.Equal("Cached Game", Assert.Single(cachedPage.Results).Name);
            Assert.Equal(0, secondHandler.IgdbCalls);
            Assert.Equal(0, secondHandler.TwitchCalls);
        }
        finally
        {
            if (Directory.Exists(cacheDirectory))
            {
                Directory.Delete(cacheDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task RequestsNewIgdbTokenWhenCredentialsChange()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri?.Host switch
            {
                "id.twitch.tv" => Json("""{"access_token":"token","expires_in":3600}"""),
                "api.igdb.com" => Json("""[{"id":1,"name":"Game"}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            });
        using var service = new GameApiService(handler);
        var igdbOnly = new AppSettings { IgdbClientId = "client", IgdbClientSecret = "secret" };

        await service.SearchAsync("Game", igdbOnly, CancellationToken.None);
        await service.SearchAsync("Game", igdbOnly, CancellationToken.None);
        Assert.Equal(1, handler.TwitchCalls);

        igdbOnly.IgdbClientId = "other-client";
        await service.SearchAsync("Game", igdbOnly, CancellationToken.None);
        Assert.Equal(2, handler.TwitchCalls);
    }

    [Fact]
    public async Task RequestsNewIgdbTokenAfterUnauthorizedResponse()
    {
        var igdbResponses = 0;
        var handler = new StubHttpMessageHandler(request =>
        {
            if (request.RequestUri?.Host == "id.twitch.tv")
            {
                return Json("""{"access_token":"token","expires_in":3600}""");
            }

            return ++igdbResponses == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : Json("""[{"id":1,"name":"Game"}]""");
        });
        using var service = new GameApiService(handler);
        var igdbOnly = new AppSettings { IgdbClientId = "client", IgdbClientSecret = "secret" };

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.SearchAsync("Game", igdbOnly, CancellationToken.None));
        var response = await service.SearchAsync("Game", igdbOnly, CancellationToken.None);

        Assert.Single(response.Results);
        Assert.Equal(2, handler.TwitchCalls);
    }

    [Fact]
    public async Task FailedCoverDownloadLeavesNoFileBehind()
    {
        var coversDirectory = Path.Combine(Path.GetTempPath(), $"jogoteca-covers-{Guid.NewGuid():N}");
        Directory.CreateDirectory(coversDirectory);
        try
        {
            var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new FailingStream())
            });
            using var service = new GameApiService(handler);
            var game = new GameSearchResult
            {
                Source = "rawg",
                ExternalId = "1",
                Name = "Game",
                CoverUrl = "https://example.com/cover.jpg"
            };

            await Assert.ThrowsAsync<IOException>(() =>
                service.DownloadCoverAsync(game, coversDirectory, CancellationToken.None));

            Assert.Empty(Directory.GetFiles(coversDirectory));
        }
        finally
        {
            Directory.Delete(coversDirectory, recursive: true);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, ProviderCheckResult.Valid)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderCheckResult.Invalid)]
    [InlineData(HttpStatusCode.InternalServerError, ProviderCheckResult.Unreachable)]
    public async Task ChecksRawgKey(HttpStatusCode status, ProviderCheckResult expected)
    {
        var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        using var service = new GameApiService(handler);

        var result = await service.CheckRawgKeyAsync("key", CancellationToken.None);

        Assert.Equal(expected, result);
        Assert.Contains("key=key", handler.LastRawgUri?.Query);
    }

    [Fact]
    public async Task RawgKeyCheckReportsUnreachableWhenOffline()
    {
        var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("offline"));
        using var service = new GameApiService(handler);

        var result = await service.CheckRawgKeyAsync("key", CancellationToken.None);

        Assert.Equal(ProviderCheckResult.Unreachable, result);
    }

    [Fact]
    public async Task IgdbCheckReportsInvalidWhenTwitchRejectsSecret()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri?.Host == "id.twitch.tv"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : Json("[]"));
        using var service = new GameApiService(handler);

        var result = await service.CheckIgdbCredentialsAsync("client", "wrong", CancellationToken.None);

        Assert.Equal(ProviderCheckResult.Invalid, result);
        Assert.Equal(0, handler.IgdbCalls);
    }

    [Fact]
    public async Task IgdbCheckReportsValidWhenTokenAndQueryWork()
    {
        var handler = new StubHttpMessageHandler(request =>
            request.RequestUri?.Host == "id.twitch.tv"
                ? Json("""{"access_token":"token","expires_in":3600}""")
                : Json("""[{"id":1}]"""));
        using var service = new GameApiService(handler);

        var result = await service.CheckIgdbCredentialsAsync("client", "secret", CancellationToken.None);

        Assert.Equal(ProviderCheckResult.Valid, result);
        Assert.Equal(1, handler.IgdbCalls);
    }

    [Fact]
    public async Task IdentifiesItselfWithUserAgent()
    {
        string? userAgent = null;
        var handler = new StubHttpMessageHandler(request =>
        {
            userAgent = request.Headers.UserAgent.ToString();
            return Json("""{"results":[]}""");
        });
        using var service = new GameApiService(handler);

        await service.CheckRawgKeyAsync("key", CancellationToken.None);

        Assert.StartsWith("JOGOTECA/", userAgent);
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

    private sealed class FailingStream : Stream
    {
        private bool _sentFirstChunk;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_sentFirstChunk)
            {
                throw new IOException("Conexão interrompida.");
            }

            _sentFirstChunk = true;
            buffer[offset] = 0xFF;
            return 1;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public int RawgCalls { get; private set; }
        public int IgdbCalls { get; private set; }
        public int TwitchCalls { get; private set; }
        public Uri? LastRawgUri { get; private set; }
        public string? LastIgdbBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.Host == "api.rawg.io")
            {
                RawgCalls++;
                LastRawgUri = request.RequestUri;
            }
            else if (request.RequestUri?.Host == "api.igdb.com")
            {
                IgdbCalls++;
                LastIgdbBody = request.Content?
                    .ReadAsStringAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
            }
            else if (request.RequestUri?.Host == "id.twitch.tv")
            {
                TwitchCalls++;
            }

            return Task.FromResult(responder(request));
        }
    }
}
