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
    private async void ToggleNotifications_Click(object sender, RoutedEventArgs e)
    {
        var shouldOpen = NotificationsFlyout.Visibility != Visibility.Visible;
        NotificationsFlyout.Visibility = shouldOpen
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (!shouldOpen)
        {
            return;
        }

        try
        {
            await RefreshReleaseNotificationsAsync();
            if (HasUnreadNotifications)
            {
                await _database.MarkReleaseNotificationsReadAsync();
                await RefreshReleaseNotificationsAsync();
            }
        }
        catch (Exception ex)
        {
            ShowToast($"Nao foi possivel carregar notificacoes: {ex.Message}");
        }
    }
    private void CloseNotifications_Click(object sender, RoutedEventArgs e)
    {
        NotificationsFlyout.Visibility = Visibility.Collapsed;
    }
    private async Task RefreshReleaseNotificationsAsync()
    {
        await _database.SyncReleaseNotificationsAsync();
        var notifications = await _database.GetReleaseNotificationsAsync();
        var watches = await _database.GetUpcomingReleaseWatchesAsync();

        ReleaseNotifications.Clear();
        foreach (var notification in notifications)
        {
            ReleaseNotifications.Add(notification);
        }

        UpcomingReleaseWatches.Clear();
        foreach (var watch in watches)
        {
            UpcomingReleaseWatches.Add(watch);
        }

        HasUnreadNotifications = notifications.Any(notification => !notification.IsRead);
        if (NotificationEmptyState is not null)
        {
            NotificationEmptyState.Visibility = ReleaseNotifications.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (UpcomingReleaseSection is not null)
        {
            UpcomingReleaseSection.Visibility = UpcomingReleaseWatches.Count == 0
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }
}
