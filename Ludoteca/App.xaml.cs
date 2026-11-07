using System.Windows;
using System.Windows.Threading;

namespace Ludoteca;

public partial class App : Application
{
    private static bool _hasShownUnhandledError;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
    }

    // Automated tests turn the error dialogs off: a dialog would wait forever for a click.
    internal static bool ShowErrorDialogs { get; set; } = true;
    internal static List<Exception> SuppressedErrors { get; } = [];

    internal static void ReportError(string title, string message, Exception exception)
    {
        if (!ShowErrorDialogs)
        {
            SuppressedErrors.Add(exception);
            return;
        }

        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_hasShownUnhandledError && ShowErrorDialogs)
        {
            return;
        }

        _hasShownUnhandledError = true;
        ReportError(
            "Erro inesperado",
            $"A JOGOTECA encontrou um erro inesperado.\n\n{e.Exception.Message}",
            e.Exception);
    }
}
