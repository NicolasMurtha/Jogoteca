using System.Collections.ObjectModel;
using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.ViewModels;

// The "Descobrir" screen: the catalog rows, their paging and what to show when
// there is no catalog. Errors come back as messages for the window to show.
public sealed class DiscoverViewModel(GameApiService gameApi) : ObservableModel
{
    private CancellationTokenSource _cancellation = new();
    private int _loadVersion;
    private bool _isLoading;
    private bool _isUnavailable;
    private string _unavailableTitle = string.Empty;
    private string _unavailableMessage = string.Empty;
    private string _attributionText = string.Empty;

    public ObservableCollection<CatalogSection> Sections { get; } =
    [
        new CatalogSection
        {
            Feed = CatalogFeed.Recommended,
            Title = "Para você",
            Subtitle = "Jogos bem avaliados para começar uma nova aventura."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.Trending,
            Title = "Trending games",
            Subtitle = "Os jogos que estão concentrando a atenção dos jogadores agora."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.MostAnticipated,
            Title = "Mais aguardados",
            Subtitle = "Os próximos títulos com maior expectativa da comunidade."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.Upcoming,
            Title = "Próximos lançamentos",
            Subtitle = "O que está chegando nos próximos meses, em ordem de lançamento."
        },
        new CatalogSection
        {
            Feed = CatalogFeed.Latest,
            Title = "Últimos lançamentos",
            Subtitle = "Jogos lançados recentemente para você não perder nenhuma novidade."
        }
    ];

    public bool IsEmpty => Sections.All(section => section.Games.Count == 0);

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool IsUnavailable
    {
        get => _isUnavailable;
        private set
        {
            if (SetField(ref _isUnavailable, value))
            {
                OnPropertyChanged(nameof(IsContentVisible));
            }
        }
    }

    public bool IsContentVisible => !_isUnavailable;

    public string UnavailableTitle
    {
        get => _unavailableTitle;
        private set => SetField(ref _unavailableTitle, value);
    }

    public string UnavailableMessage
    {
        get => _unavailableMessage;
        private set => SetField(ref _unavailableMessage, value);
    }

    public string AttributionText
    {
        get => _attributionText;
        private set => SetField(ref _attributionText, value);
    }

    // Shows the catalog or, without any source configured, the explanation instead.
    public void Configure(AppSettings settings)
    {
        UnavailableTitle = "Catálogo online opcional";
        UnavailableMessage =
            "Configure IGDB ou RAWG para ver recomendações e lançamentos. Sua biblioteca e o cadastro manual continuam funcionando normalmente.";
        IsUnavailable = !settings.HasAnyProvider;
        AttributionText = (settings.HasIgdb, settings.HasRawg) switch
        {
            (true, true) => "Dados por IGDB e RAWG",
            (true, false) => "Dados por IGDB",
            (false, true) => "Dados por RAWG",
            _ => string.Empty
        };
    }

    // Reloads every row from page 1. A newer load cancels and replaces an older one.
    // Returns an error message for the window to show, or null.
    public async Task<string?> LoadAsync(AppSettings settings)
    {
        Configure(settings);
        if (!settings.HasAnyProvider)
        {
            return null;
        }

        _cancellation.Cancel();
        _cancellation.Dispose();
        _cancellation = new CancellationTokenSource();
        var cancellationToken = _cancellation.Token;
        foreach (var section in Sections)
        {
            section.Clear();
        }

        var loadVersion = ++_loadVersion;
        IsLoading = true;
        try
        {
            foreach (var section in Sections)
            {
                section.TryBeginLoading();
            }

            var pages = await gameApi.GetCatalogBatchAsync(
                Sections.Select(section => section.Feed),
                1,
                settings,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var section in Sections)
            {
                section.ApplyPage(pages.GetValueOrDefault(section.Feed) ?? new CatalogPage(), 1, reset: true);
            }

            if (IsEmpty)
            {
                ShowUnavailable(
                    "As fontes configuradas não retornaram jogos agora. Verifique as credenciais ou tente novamente mais tarde.");
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            if (loadVersion == _loadVersion && IsEmpty)
            {
                ShowUnavailable(ex.Message);
            }

            return loadVersion == _loadVersion
                ? $"Não foi possível carregar o catálogo: {ex.Message}"
                : null;
        }
        finally
        {
            if (loadVersion == _loadVersion)
            {
                foreach (var section in Sections)
                {
                    section.FinishLoading();
                }

                IsLoading = false;
            }
        }
    }

    // Loads the next page of one row; returns an error message or null.
    public async Task<string?> LoadNextPageAsync(CatalogSection section, AppSettings settings)
    {
        if (!section.TryBeginLoading())
        {
            return null;
        }

        var cancellationToken = _cancellation.Token;
        try
        {
            var nextPage = section.CurrentPage + 1;
            var page = await gameApi.GetCatalogAsync(section.Feed, nextPage, settings, cancellationToken);
            section.ApplyPage(page, nextPage, reset: false);
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex)
        {
            return $"Não foi possível carregar mais jogos: {ex.Message}";
        }
        finally
        {
            section.FinishLoading();
        }
    }

    public void Clear()
    {
        _cancellation.Cancel();
        foreach (var section in Sections)
        {
            section.Clear();
        }
    }

    public void Cancel()
    {
        _cancellation.Cancel();
    }

    private void ShowUnavailable(string message)
    {
        UnavailableTitle = "Catálogo temporariamente indisponível";
        UnavailableMessage = message;
        IsUnavailable = true;
    }
}
