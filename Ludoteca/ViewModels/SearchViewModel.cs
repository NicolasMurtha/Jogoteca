using System.Collections.ObjectModel;
using Ludoteca.Models;
using Ludoteca.Services;

namespace Ludoteca.ViewModels;

// The online search: results, the loading indicator and the empty-state text.
public sealed class SearchViewModel(GameApiService gameApi) : ObservableModel
{
    private CancellationTokenSource? _cancellation;
    private bool _isLoading;
    private bool _isEmptyStateVisible;
    private string _emptyTitle = string.Empty;
    private string _emptyMessage = string.Empty;

    public ObservableCollection<GameSearchResult> Results { get; } = [];

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetField(ref _isLoading, value);
    }

    public bool IsEmptyStateVisible
    {
        get => _isEmptyStateVisible;
        private set => SetField(ref _isEmptyStateVisible, value);
    }

    public string EmptyTitle
    {
        get => _emptyTitle;
        private set => SetField(ref _emptyTitle, value);
    }

    public string EmptyMessage
    {
        get => _emptyMessage;
        private set => SetField(ref _emptyMessage, value);
    }

    // Runs a search, cancelling any search still running. Returns the provider message
    // to show as a toast when there are results, otherwise null.
    public async Task<string?> RunAsync(string query, AppSettings settings)
    {
        if (!settings.HasAnyProvider)
        {
            ShowEmpty("Busca online não configurada", "Cadastre o jogo manualmente ou configure IGDB/RAWG.");
            return null;
        }

        query = query.Trim();
        if (query.Length < 2)
        {
            ShowEmpty("Digite pelo menos 2 caracteres", "Assim a busca fica mais precisa.");
            return null;
        }

        _cancellation?.Cancel();
        _cancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        Results.Clear();
        IsEmptyStateVisible = false;
        IsLoading = true;
        try
        {
            var response = await gameApi.SearchAsync(query, settings, cancellation.Token);
            foreach (var game in response.Results)
            {
                Results.Add(game);
            }

            IsLoading = false;
            if (Results.Count == 0)
            {
                ShowEmpty("Nenhum jogo encontrado", response.Message);
                return null;
            }

            return response.Message;
        }
        catch (OperationCanceledException)
        {
            // Only the latest search may change what is on screen.
            if (_cancellation == cancellation)
            {
                IsLoading = false;
            }

            return null;
        }
        catch (Exception ex)
        {
            if (_cancellation == cancellation)
            {
                ShowEmpty("A busca não pôde ser concluída", ex.Message);
            }

            return null;
        }
    }

    public void ShowEmpty(string title, string message)
    {
        IsLoading = false;
        EmptyTitle = title;
        EmptyMessage = message;
        IsEmptyStateVisible = true;
    }

    public void Cancel()
    {
        _cancellation?.Cancel();
    }

    public void Clear()
    {
        Cancel();
        Results.Clear();
    }
}
