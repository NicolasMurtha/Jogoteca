using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Ludoteca.Models;
using Ludoteca.Services;
using Xunit.Abstractions;

namespace Ludoteca.Tests;

// Opens the real main window on an STA thread and walks through the main screens,
// failing on any data binding that points to a property that does not exist.
public sealed class MainWindowSmokeTests(ITestOutputHelper output)
{
    private const int LibrarySize = 200;

    [Fact]
    public void MainScreensOpenWithoutBindingErrors()
    {
        var report = new SmokeReport();
        try
        {
            RunOnStaThread(() => OpenWindowAndVisitScreens(report));
        }
        finally
        {
            lock (report)
            {
                foreach (var line in report.Log.Concat(report.BindingErrors.Distinct()))
                {
                    output.WriteLine(line);
                }
            }
        }

        Assert.Empty(report.Errors);
        Assert.Empty(report.BindingErrors);
        // Only the cards on screen (plus a small margin) are created.
        Assert.InRange(report.GridCardsRealized, 1, LibrarySize / 2);
        Assert.InRange(report.ListRowsRealized, 1, LibrarySize / 2);
    }

    private static void OpenWindowAndVisitScreens(SmokeReport report)
    {
        var dataDirectory = Path.Combine(Path.GetTempPath(), $"jogoteca-smoke-{Guid.NewGuid():N}");
        var previousDataDirectory = Environment.GetEnvironmentVariable("JOGOTECA_DATA_DIR");
        var listener = new BindingErrorListener(report.BindingErrors);
        try
        {
            Environment.SetEnvironmentVariable("JOGOTECA_DATA_DIR", dataDirectory);
            SeedLibrary(dataDirectory);

            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);

            App.ShowErrorDialogs = false;
            report.Add("Creating application");
            var app = Application.Current ?? new App();
            if (app is App ourApp)
            {
                ourApp.InitializeComponent();
            }

            var window = new MainWindow
            {
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = 0,
                Top = 0
            };
            report.Add("Showing window");
            window.Show();
            report.Add("Waiting for lists");
            PumpUntil(() => window.Library.Lists.Count > 0 || App.SuppressedErrors.Count > 0, TimeSpan.FromSeconds(20));
            report.Errors.AddRange(App.SuppressedErrors.Select(error => error.ToString()));
            report.Add($"Lists loaded: {window.Library.Lists.Count}");

            report.Add("Opening library");
            Click(window.AllGamesButton);
            PumpUntil(() => window.Library.Games.Count == LibrarySize, TimeSpan.FromSeconds(20));
            report.Add($"Library games: {window.Library.Games.Count}");
            report.GridCardsRealized = CountNamed(window.LibraryGridView, "GameCardHoverArea");
            report.Add($"Grid cards realized: {report.GridCardsRealized}");

            report.Add("Switching to list layout");
            Click(window.LibraryListViewButton);
            Pump(TimeSpan.FromSeconds(1));
            report.ListRowsRealized = CountRealizedRows(window.LibraryListView);
            report.Add($"List rows realized: {report.ListRowsRealized}");
            Click(window.LibraryGridViewButton);

            var catalogGame = new GameSearchResult
            {
                Source = "rawg",
                ExternalId = "1",
                Name = "Catalog game",
                ReleaseYear = 2024,
                PublicRating = 8.5
            };
            window.Discover.Sections[0].ApplyPage(
                new CatalogPage { Results = [catalogGame] },
                1,
                reset: true);
            report.Add("Opening discover");
            Click(window.DiscoverButton);
            Pump(TimeSpan.FromMilliseconds(500));

            window.Search.Results.Add(catalogGame);
            window.SearchOverlay.Visibility = Visibility.Visible;
            Pump(TimeSpan.FromMilliseconds(500));
            window.SearchOverlay.Visibility = Visibility.Collapsed;

            window.CatalogDetailPanel.DataContext = catalogGame;
            window.CatalogDetailOverlay.Visibility = Visibility.Visible;
            Pump(TimeSpan.FromMilliseconds(500));
            window.CatalogDetailOverlay.Visibility = Visibility.Collapsed;

            window.DetailPanel.DataContext = window.Library.Games[0];
            window.DetailOverlay.Visibility = Visibility.Visible;
            Pump(TimeSpan.FromMilliseconds(500));
            window.DetailOverlay.Visibility = Visibility.Collapsed;

            report.Add("Closing window");
            window.Close();
            Pump(TimeSpan.FromMilliseconds(200));
            report.Errors.AddRange(App.SuppressedErrors.Select(error => error.ToString()));
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
            Environment.SetEnvironmentVariable("JOGOTECA_DATA_DIR", previousDataDirectory);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(dataDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void SeedLibrary(string dataDirectory)
    {
        var database = new DatabaseService(dataDirectory);
        database.InitializeAsync().GetAwaiter().GetResult();
        var lists = database.GetListsAsync().GetAwaiter().GetResult();
        for (var index = 0; index < LibrarySize; index++)
        {
            var gameId = database.UpsertGameAsync(
                new GameSearchResult
                {
                    Source = "manual",
                    ExternalId = $"game-{index}",
                    Name = $"Jogo {index:000}",
                    Category = "Aventura",
                    ReleaseYear = 2000 + index % 25,
                    PublicRating = 7.5,
                    CompletionTimeHours = 12
                },
                string.Empty).GetAwaiter().GetResult();
            if (index % 3 == 0)
            {
                database.SaveGameDetailsAsync(
                    gameId,
                    8,
                    "Review",
                    "2026-01-01",
                    string.Empty,
                    [lists[index % lists.Count].Id]).GetAwaiter().GetResult();
            }
        }
    }

    private static int CountNamed(DependencyObject root, string name)
    {
        var count = 0;
        var pending = new Stack<DependencyObject>([root]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is FrameworkElement { Name: var elementName } && elementName == name)
            {
                count++;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }

        return count;
    }

    private static int CountRealizedRows(DependencyObject root)
    {
        var pending = new Stack<DependencyObject>([root]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (current is ItemsControl items && items.ItemsSource is not null)
            {
                return Enumerable.Range(0, items.Items.Count)
                    .Count(index => items.ItemContainerGenerator.ContainerFromIndex(index) is not null);
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                pending.Push(VisualTreeHelper.GetChild(current, index));
            }
        }

        return 0;
    }

    private static void Click(ButtonBase button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
        Pump(TimeSpan.FromMilliseconds(300));
    }

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException("The window did not reach the expected state in time.");
            }

            Pump(TimeSpan.FromMilliseconds(50));
        }

        Pump(TimeSpan.FromMilliseconds(300));
    }

    private static void Pump(TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(10);
        }
        while (stopwatch.Elapsed < duration);
    }

    private static void RunOnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
        {
            throw new TimeoutException("The main window smoke test did not finish.");
        }

        failure?.Throw();
    }

    private sealed class SmokeReport
    {
        public List<string> BindingErrors { get; } = [];
        public List<string> Errors { get; } = [];
        public List<string> Log { get; } = [];

        public void Add(string line)
        {
            lock (this)
            {
                Log.Add($"{DateTime.Now:HH:mm:ss.fff} {line}");
            }
        }
        public int GridCardsRealized { get; set; }
        public int ListRowsRealized { get; set; }
    }

    private sealed class BindingErrorListener(List<string> errors) : TraceListener
    {
        public override void Write(string? message)
        {
        }

        public override void WriteLine(string? message)
        {
            if (message is not null && message.Contains("path error", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(message);
            }
        }
    }
}
