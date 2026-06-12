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

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (_hasShownUnhandledError)
        {
            return;
        }

        _hasShownUnhandledError = true;
        MessageBox.Show(
            $"A Ludoteca encontrou um erro inesperado.\n\n{e.Exception.Message}",
            "Erro inesperado",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
