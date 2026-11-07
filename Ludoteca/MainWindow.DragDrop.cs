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
    private void GameCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }
    private void ListButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _listDragStartPoint = e.GetPosition(null);
        _isListDragging = false;
    }
    private void ListButton_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            (sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _listDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _listDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isListDragging = true;
        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new DataObject(typeof(ListDragData), new ListDragData(list.Id)),
            DragDropEffects.Move);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => _isListDragging = false);
    }
    private void GameCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            (sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        var currentPosition = e.GetPosition(null);
        if (Math.Abs(currentPosition.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _isDragging = true;
        var dragData = new GameDragData(game, _selectedListId);
        DragDrop.DoDragDrop(
            (DependencyObject)sender,
            new DataObject(typeof(GameDragData), dragData),
            _selectedListId.HasValue
                ? DragDropEffects.Move
                : DragDropEffects.Copy);
        _ = Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            () => _isDragging = false);
    }
    private void ListButton_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(ListDragData)) is ListDragData)
        {
            e.Effects = DragDropEffects.Move;
            if (sender is Button listButton)
            {
                listButton.Background = ResourceBrush("NavSelectedBackground");
            }

            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(GameDragData)) is not GameDragData dragData)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        e.Effects = dragData.SourceListId.HasValue
            ? DragDropEffects.Move
            : DragDropEffects.Copy;
        if (sender is Button button)
        {
            button.Background = ResourceBrush("NavSelectedBackground");
        }
        e.Handled = true;
    }
    private void ListButton_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is Button button)
        {
            button.Background = Brushes.Transparent;
        }
    }
    private async void ListButton_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;
        if (sender is Button button)
        {
            button.Background = Brushes.Transparent;
        }

        if (e.Data.GetData(typeof(ListDragData)) is ListDragData listDragData &&
            (sender as FrameworkElement)?.DataContext is GameList targetList)
        {
            if (listDragData.ListId == targetList.Id)
            {
                return;
            }

            try
            {
                await _database.MoveListAsync(listDragData.ListId, targetList.Id);
                await RefreshListsAsync();
                ShowToast("Ordem das listas atualizada.");
            }
            catch (Exception ex)
            {
                ShowToast($"Não foi possível reordenar a lista: {ex.Message}");
            }

            return;
        }

        if (e.Data.GetData(typeof(GameDragData)) is not GameDragData dragData ||
            (sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        var game = dragData.Game;
        if (dragData.SourceListId == list.Id)
        {
            ShowToast($"\"{game.Name}\" já está nesta lista.");
            return;
        }

        try
        {
            if (dragData.SourceListId.HasValue)
            {
                await _database.MoveGameBetweenListsAsync(
                    game.Id,
                    dragData.SourceListId.Value,
                    list.Id);
                ShowToast($"\"{game.Name}\" foi movido para {list.Name}.");
            }
            else
            {
                await _database.AddGameToListAsync(game.Id, list.Id);
                ShowToast($"\"{game.Name}\" foi adicionado a {list.Name}.");
            }

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível mover o jogo: {ex.Message}");
        }
    }
}
