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
        var showing = Library.ShowAllAsync();
        ShowLibraryView();
        await showing;
    }
    private void ShowLibraryView()
    {
        DiscoveryPanel.Visibility = Visibility.Collapsed;
        LibraryPanel.Visibility = Visibility.Visible;
        DiscoverButton.Background = Brushes.Transparent;
        AllGamesButton.Background = Library.IsViewingList
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
        Library.SetLayout(useList);
    }
    private async void SortByNameHeader_Click(object sender, RoutedEventArgs e)
    {
        await Library.CycleNameSortAsync();
    }
    private async void SortByReleaseHeader_Click(object sender, RoutedEventArgs e)
    {
        await Library.CycleReleaseSortAsync();
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
    private async Task ShowLibraryFilterAsync(GameLibraryFilter filter, string title, string subtitle)
    {
        var showing = Library.ShowFilterAsync(filter, title, subtitle);
        ShowLibraryView();
        await showing;
    }
    private async void ApplyLibraryNameFilter_Click(object sender, RoutedEventArgs e)
    {
        await Library.ApplyNameFilterAsync(LibraryNameFilterBox.Text);
    }
    private async void LibraryNameFilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await Library.ApplyNameFilterAsync(LibraryNameFilterBox.Text);
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
    private async void RemoveGameFromCurrentList_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (Library.SelectedListId is not int listId ||
            (sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        try
        {
            await _database.RemoveGameFromListAsync(game.Id, listId);
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
}
