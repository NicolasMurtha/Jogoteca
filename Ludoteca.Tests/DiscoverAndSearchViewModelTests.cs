using System.Net;
using System.Net.Http;
using System.Text;
using Ludoteca.Models;
using Ludoteca.Services;
using Ludoteca.ViewModels;

namespace Ludoteca.Tests;

public sealed class DiscoverAndSearchViewModelTests
{
    private static readonly AppSettings RawgOnly = new() { RawgApiKey = "key" };

    [Fact]
    public void DiscoverExplainsWhenNoSourceIsConfigured()
    {
        using var api = new GameApiService(new FakeApi(_ => Json("{}")));
        var discover = new DiscoverViewModel(api);

        discover.Configure(new AppSettings());

        Assert.True(discover.IsUnavailable);
        Assert.False(discover.IsContentVisible);
        Assert.Equal("Catálogo online opcional", discover.UnavailableTitle);
        Assert.Equal(string.Empty, discover.AttributionText);
    }

    [Fact]
    public async Task DiscoverFillsEveryRow()
    {
        using var api = new GameApiService(new FakeApi(_ => Json(
            """{"results":[{"id":1,"name":"Hades","released":"2020-09-17"}]}""")));
        var discover = new DiscoverViewModel(api);

        var error = await discover.LoadAsync(RawgOnly);

        Assert.Null(error);
        Assert.All(discover.Sections, section => Assert.Equal("Hades", Assert.Single(section.Games).Name));
        Assert.False(discover.IsLoading);
        Assert.False(discover.IsUnavailable);
        Assert.Equal("Dados por RAWG", discover.AttributionText);
    }

    [Fact]
    public async Task DiscoverShowsUnavailableWhenSourcesReturnNothing()
    {
        using var api = new GameApiService(new FakeApi(_ => Json("""{"results":[]}""")));
        var discover = new DiscoverViewModel(api);

        await discover.LoadAsync(RawgOnly);

        Assert.True(discover.IsUnavailable);
        Assert.Equal("Catálogo temporariamente indisponível", discover.UnavailableTitle);
    }

    [Fact]
    public async Task SearchAsksForLongerQuery()
    {
        using var api = new GameApiService(new FakeApi(_ => Json("{}")));
        var search = new SearchViewModel(api);

        await search.RunAsync("a", RawgOnly);

        Assert.True(search.IsEmptyStateVisible);
        Assert.Equal("Digite pelo menos 2 caracteres", search.EmptyTitle);
    }

    [Fact]
    public async Task SearchShowsResults()
    {
        using var api = new GameApiService(new FakeApi(_ => Json(
            """{"results":[{"id":7,"name":"Celeste","released":"2018-01-25"}]}""")));
        var search = new SearchViewModel(api);

        var message = await search.RunAsync("Celeste", RawgOnly);

        Assert.NotNull(message);
        Assert.Equal("Celeste", Assert.Single(search.Results).Name);
        Assert.False(search.IsLoading);
        Assert.False(search.IsEmptyStateVisible);
    }

    [Fact]
    public async Task SearchWithoutSourceExplainsHowToSetUp()
    {
        using var api = new GameApiService(new FakeApi(_ => Json("{}")));
        var search = new SearchViewModel(api);

        await search.RunAsync("Celeste", new AppSettings());

        Assert.Equal("Busca online não configurada", search.EmptyTitle);
        Assert.Empty(search.Results);
    }

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    private sealed class FakeApi(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(respond(request));
    }
}
