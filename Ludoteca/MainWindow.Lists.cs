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
    private async void ListButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isListDragging)
        {
            _isListDragging = false;
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not GameList list)
        {
            return;
        }

        var showing = Library.ShowListAsync(list);
        ShowLibraryView();
        await showing;
    }
    private void OpenCreateList_Click(object sender, RoutedEventArgs e)
    {
        _editingListId = null;
        _selectedListColor = ListColors[Library.Lists.Count % ListColors.Length];
        SelectedListEmoji = ListEmojiCatalog.Game;
        ListEditorTitle.Text = "Criar uma lista";
        ListEditorSubtitle.Text = "Personalize o nome, o emoji e a cor da lista.";
        SaveListButton.Content = "Criar lista";
        NewListNameBox.Clear();
        UpdateSelectedColorPreview();
        ListEditorOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => NewListNameBox.Focus());
    }
    private void EditCurrentList_Click(object sender, RoutedEventArgs e)
    {
        if (Library.SelectedList is not { } list)
        {
            return;
        }

        _editingListId = list.Id;
        _selectedListColor = list.Color;
        SelectedListEmoji = list.Emoji;
        ListEditorTitle.Text = "Editar lista";
        ListEditorSubtitle.Text = "Altere o nome, o emoji ou a cor desta lista.";
        SaveListButton.Content = "Salvar alterações";
        NewListNameBox.Text = list.Name;
        UpdateSelectedColorPreview();
        ListEditorOverlay.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() =>
        {
            NewListNameBox.Focus();
            NewListNameBox.SelectAll();
        });
    }
    private void CloseListEditor_Click(object sender, RoutedEventArgs e)
    {
        ListEditorOverlay.Visibility = Visibility.Collapsed;
    }
    private void NewListNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            SaveList_Click(sender, e);
        }
    }
    private void ListColor_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string color)
        {
            return;
        }

        _selectedListColor = color;
        UpdateSelectedColorPreview();
    }
    private void UpdateSelectedColorPreview()
    {
        var buttons = new[]
        {
            ListColorLavenderButton,
            ListColorMintButton,
            ListColorPeachButton,
            ListColorYellowButton,
            ListColorBlueButton,
            ListColorPinkButton
        };

        foreach (var button in buttons)
        {
            if (button.Tag is string color &&
                color.Equals(_selectedListColor, StringComparison.OrdinalIgnoreCase))
            {
                button.BorderBrush = BrushFromHex("#E85D5D");
                button.BorderThickness = new Thickness(3);
            }
            else
            {
                button.BorderBrush = Brushes.Transparent;
                button.BorderThickness = new Thickness(2);
            }
        }
    }
    private async void SaveList_Click(object sender, RoutedEventArgs e)
    {
        var name = NewListNameBox.Text.Trim();
        var emoji = SelectedListEmoji;
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowToast("Digite um nome para a lista.");
            return;
        }

        if (Library.Lists.Any(list =>
                list.Id != _editingListId &&
                list.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowToast("Já existe uma lista com esse nome.");
            return;
        }

        try
        {
            if (_editingListId.HasValue)
            {
                await _database.UpdateListAsync(
                    _editingListId.Value,
                    name,
                    _selectedListColor,
                    emoji);
                ShowToast($"Lista \"{name}\" atualizada.");
            }
            else
            {
                var list = await _database.CreateListAsync(name, _selectedListColor, emoji);
                Library.SelectList(list.Id);
                ShowToast($"Lista \"{name}\" criada.");
            }

            ListEditorOverlay.Visibility = Visibility.Collapsed;
            await Library.RefreshListsAsync();
            if (Library.SelectedList is { } selected)
            {
                var showing = Library.ShowListAsync(selected);
                ShowLibraryView();
                await showing;
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar a lista: {ex.Message}");
        }
    }
    private async void DeleteCurrentList_Click(object sender, RoutedEventArgs e)
    {
        if (Library.SelectedList is not { } selected)
        {
            return;
        }

        var confirmed = await ShowConfirmationAsync(
            "Excluir lista",
            $"Excluir a lista \"{selected.Name}\"?\n\nOs jogos continuarão na biblioteca.",
            "Confirmar");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _database.DeleteListAsync(selected.Id);
            Library.SelectList(null);
            await RefreshAllAsync();
            ShowLibraryView();
            ShowToast("Lista excluída.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível excluir a lista: {ex.Message}");
        }
    }
}
