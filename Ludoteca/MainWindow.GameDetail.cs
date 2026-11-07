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
    private async void GameCard_Click(object sender, RoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            return;
        }

        if ((sender as FrameworkElement)?.DataContext is not GameEntry game)
        {
            return;
        }

        IReadOnlyList<ListMembershipOption> memberships;
        try
        {
            memberships = await _database.GetMembershipsAsync(game.Id);
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível abrir o jogo: {ex.Message}");
            return;
        }

        _selectedGame = game;
        DetailPanel.DataContext = game;
        MembershipOptions.Clear();
        foreach (var membership in memberships.Where(membership => membership.IsSelected))
        {
            MembershipOptions.Add(membership);
        }

        _isInitializingDetail = true;
        RatingSlider.Value = game.UserRating.HasValue
            ? Math.Round(game.UserRating.Value)
            : NoRatingSliderValue;
        UpdateRatingValueText();
        ReviewTextBox.Text = game.Review;
        StartedAtPicker.SelectedDate = ParseStoredDate(game.StartedAt);
        FinishedAtPicker.SelectedDate = ParseStoredDate(game.FinishedAt);
        _isInitializingDetail = false;
        DetailOverlay.Visibility = Visibility.Visible;
    }
    private void CloseDetailOverlay_Click(object sender, RoutedEventArgs e)
    {
        DetailOverlay.Visibility = Visibility.Collapsed;
        _selectedGame = null;
    }
    private void RatingSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RatingValueText is null || _isInitializingDetail)
        {
            return;
        }

        UpdateRatingValueText();
    }
    private void UpdateRatingValueText()
    {
        if (RatingValueText is null || RatingSlider is null)
        {
            return;
        }

        RatingValueText.Text = IsNoRatingValue(RatingSlider.Value)
            ? "Sem nota"
            : RatingSlider.Value.ToString("0");
    }
    private static bool IsNoRatingValue(double value)
    {
        return value <= NoRatingSliderValue + 0.01;
    }
    private static DateTime? ParseStoredDate(string value)
    {
        return DateTime.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var date)
            ? date
            : null;
    }
    private static string FormatStoredDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;
    }
    private async void SaveGameDetails_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
        {
            return;
        }

        double? rating = IsNoRatingValue(RatingSlider.Value)
            ? null
            : RatingSlider.Value;
        var startedAt = FormatStoredDate(StartedAtPicker.SelectedDate);
        var finishedAt = FormatStoredDate(FinishedAtPicker.SelectedDate);
        if (StartedAtPicker.SelectedDate.HasValue &&
            FinishedAtPicker.SelectedDate.HasValue &&
            StartedAtPicker.SelectedDate.Value.Date > FinishedAtPicker.SelectedDate.Value.Date)
        {
            ShowToast("A data de inicio nao pode ser depois da finalizacao.");
            return;
        }

        var selectedListIds = MembershipOptions
            .Where(option => option.IsSelected)
            .Select(option => option.ListId);

        try
        {
            await _database.SaveGameDetailsAsync(
                _selectedGame.Id,
                rating,
                ReviewTextBox.Text,
                startedAt,
                finishedAt,
                selectedListIds);
            DetailOverlay.Visibility = Visibility.Collapsed;
            _selectedGame = null;
            await RefreshAllAsync();
            ShowToast("Nota e review atualizadas.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível salvar: {ex.Message}");
        }
    }
    private async void DeleteGame_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedGame is null)
        {
            return;
        }

        await DeleteGameFromLibraryAsync(_selectedGame, closeDetailOverlay: true);
    }
    private async Task DeleteGameFromLibraryAsync(
        GameEntry game,
        bool closeDetailOverlay)
    {
        var confirmed = await ShowConfirmationAsync(
            "Remover jogo",
            $"Remover \"{game.Name}\" da sua biblioteca?\n\nO jogo também será retirado de todas as listas.",
            "Confirmar");
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _database.DeleteGameAsync(game.Id);
            if (closeDetailOverlay)
            {
                DetailOverlay.Visibility = Visibility.Collapsed;
                _selectedGame = null;
            }

            await RefreshAllAsync();
            ShowToast("Jogo removido da biblioteca.");
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível remover o jogo: {ex.Message}");
        }
    }
}
