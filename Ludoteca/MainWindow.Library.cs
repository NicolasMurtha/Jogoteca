using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Ludoteca.Models;
using Ludoteca.Services;
using Ludoteca.Themes;
using Microsoft.Win32;

namespace Ludoteca;

public partial class MainWindow
{
    private async void AllGamesButton_Click(object sender, RoutedEventArgs e)
    {
        _activeLibraryFilter = GameLibraryFilter.None;
        SetSelectedListId(null);
        SelectedListTitle = "Biblioteca";
        SelectedListSubtitle = "Todos os jogos que você adicionou";
        SetListActionButtonsVisible(false);
        ShowLibraryView();
        await RefreshLibraryAsync();
    }
    private void ShowLibraryView()
    {
        DiscoveryPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        DiscoverButton.Background = Brushes.Transparent;
        AllGamesButton.Background = _selectedListId.HasValue
            ? Brushes.Transparent
            : ResourceBrush("NavSelectedBackground");
    }
    private void ShowLibraryGridView_Click(object sender, RoutedEventArgs e)
    {
        SetLibraryLayout(useList: false);
    }
    private void ShowLibraryListView_Click(object sender, RoutedEventArgs e)
    {
        SetLibraryLayout(useList: true);
    }
    private void SetLibraryLayout(bool useList)
    {
        LibraryGridView.Visibility = useList ? Visibility.Collapsed : Visibility.Visible;
        LibraryListView.Visibility = useList ? Visibility.Visible : Visibility.Collapsed;
        LibraryGridViewButton.Background = useList
            ? Brushes.Transparent
            : FindResource("Surface") as Brush;
        LibraryListViewButton.Background = useList
            ? FindResource("Surface") as Brush
            : Brushes.Transparent;

        if (useList)
        {
            ApplyLibrarySort();
        }
    }
    private async void SortByNameHeader_Click(object sender, RoutedEventArgs e)
    {
        _librarySortMode = _librarySortMode switch
        {
            LibrarySortMode.NameAscending => LibrarySortMode.NameDescending,
            LibrarySortMode.NameDescending => LibrarySortMode.Default,
            _ => LibrarySortMode.NameAscending
        };
        await ApplySortModeAsync();
    }
    private async void ShowUnratedGames_Click(object sender, RoutedEventArgs e)
    {
        await ShowLibraryFilterAsync(
            GameLibraryFilter.Unrated,
            "Sem nota",
            "Jogos que ainda precisam de uma nota.");
    }
    private async void ShowReviewedGames_Click(object sender, RoutedEventArgs e)
    {
        await ShowLibraryFilterAsync(
            GameLibraryFilter.Reviewed,
            "Reviews escritas",
            "Jogos que já têm observação ou review registrada.");
    }
    private async void ShowUnorganizedGames_Click(object sender, RoutedEventArgs e)
    {
        await ShowLibraryFilterAsync(
            GameLibraryFilter.Unorganized,
            "Não organizados",
            "Jogos que ainda não estão em nenhuma lista.");
    }
    private async void SortByReleaseHeader_Click(object sender, RoutedEventArgs e)
    {
        _librarySortMode = _librarySortMode switch
        {
            LibrarySortMode.ReleaseDateDescending => LibrarySortMode.ReleaseDateAscending,
            LibrarySortMode.ReleaseDateAscending => LibrarySortMode.Default,
            _ => LibrarySortMode.ReleaseDateDescending
        };
        await ApplySortModeAsync();
    }
    private async void ApplyLibraryNameFilter_Click(object sender, RoutedEventArgs e)
    {
        _libraryNameFilter = LibraryNameFilterBox.Text.Trim();
        await RefreshLibraryAsync();
    }
    private async void LibraryNameFilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _libraryNameFilter = LibraryNameFilterBox.Text.Trim();
            await RefreshLibraryAsync();
        }
    }
    private void LibraryNameFilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (LibraryNameFilterPlaceholder is not null)
        {
            LibraryNameFilterPlaceholder.Visibility =
                string.IsNullOrWhiteSpace(LibraryNameFilterBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }
    private async Task ApplySortModeAsync()
    {
        NotifySortHeaderChanges();
        if (_librarySortMode == LibrarySortMode.Default)
        {
            await RefreshLibraryAsync();
            return;
        }

        ApplyLibrarySort();
    }
    private void NotifySortHeaderChanges()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(NameSortHeaderText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReleaseSortHeaderText)));
    }
    private async Task ShowLibraryFilterAsync(
        GameLibraryFilter filter,
        string title,
        string subtitle)
    {
        _activeLibraryFilter = filter;
        SetSelectedListId(null);
        SelectedListTitle = title;
        SelectedListSubtitle = subtitle;
        SetListActionButtonsVisible(false);
        ShowLibraryView();
        await RefreshLibraryAsync();
    }
    private async void RemoveGameFromCurrentList_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!_selectedListId.HasValue ||
            (sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        try
        {
            await _database.RemoveGameFromListAsync(game.Id, _selectedListId.Value);
            await RefreshAllAsync();
            ShowToast($"\"{game.Name}\" foi retirado desta lista.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível retirar o jogo da lista: {ex.Message}");
        }
    }
    private async void DeleteGameFromCard_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if ((sender as FrameworkElement)?.DataContext is GameEntry game)
        {
            await DeleteGameFromLibraryAsync(game, closeDetailOverlay: false);
        }
    }
    private async Task RefreshLibraryAsync()
    {
        var libraryFilter = _selectedListId.HasValue
            ? GameLibraryFilter.None
            : _activeLibraryFilter;
        var games = await _database.GetGamesAsync(
            _selectedListId,
            _libraryNameFilter,
            libraryFilter: libraryFilter);
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        if (LibraryListView?.Visibility == Visibility.Visible)
        {
            ApplyLibrarySort();
        }

        var stats = await _database.GetStatsAsync();
        TotalGameCount = stats.TotalGames.ToString();
        UnratedGameCount = stats.UnratedGames.ToString();
        ReviewedGameCount = stats.ReviewedGames.ToString();
        UnorganizedGameCount = stats.UnorganizedGames.ToString();
        AverageRating = stats.AverageRatingDisplay;

        if (_selectedListId.HasValue)
        {
            var selectedList = Lists.FirstOrDefault(list => list.Id == _selectedListId.Value);
            if (selectedList is not null)
            {
                SelectedListSubtitle = ListSubtitle(selectedList.GameCount);
            }
        }

        EmptyLibraryState.Visibility = Games.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (Games.Count == 0)
        {
            SetEmptyLibraryMessage();
        }
    }
    private void SetEmptyLibraryMessage()
    {
        if (_selectedListId.HasValue)
        {
            EmptyLibraryTitle.Text = "Esta lista está vazia";
            EmptyLibraryMessage.Text =
                "Arraste um jogo da Biblioteca ou de outra lista para começar a organizar.";
            return;
        }

        (EmptyLibraryTitle.Text, EmptyLibraryMessage.Text) = _activeLibraryFilter switch
        {
            GameLibraryFilter.Unrated => (
                "Nenhum jogo sem nota",
                "Todos os jogos da biblioteca já têm uma nota registrada."),
            GameLibraryFilter.Reviewed => (
                "Nenhuma review escrita",
                "Quando você escrever observações ou reviews, elas aparecem aqui."),
            GameLibraryFilter.Unorganized => (
                "Tudo organizado",
                "Todos os jogos da biblioteca já estão em pelo menos uma lista."),
            _ => (
                "Sua biblioteca ainda está vazia",
                "Adicione jogos à biblioteca. Depois, arraste os cards entre as listas como em um kanban.")
        };
    }
    private void ApplyLibrarySort()
    {
        if (Games.Count <= 1 || _librarySortMode == LibrarySortMode.Default)
        {
            return;
        }

        var sortedGames = LibrarySorter.Sort(Games, _librarySortMode);
        Games.Clear();
        foreach (var game in sortedGames)
        {
            Games.Add(game);
        }
    }
}
