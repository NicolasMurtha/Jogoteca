using System.Windows;

namespace Ludoteca;

public partial class MainWindow
{
    private const string SupportPixUrl =
        "https://nubank.com.br/cobrar/sj3lc/6aee81d8-0eec-4b0c-bbee-c28cf971d22f";

    private const string SupportWhatsAppUrl =
        "https://wa.me/5516993827602?text=Ol%C3%A1!%20Vim%20pela%20JOGOTECA.";

    private void OpenSupport_Click(object sender, RoutedEventArgs e)
    {
        SupportOverlay.Visibility = Visibility.Visible;
    }

    private void CloseSupport_Click(object sender, RoutedEventArgs e)
    {
        SupportOverlay.Visibility = Visibility.Collapsed;
    }

    private void OpenSupportPix_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal(SupportPixUrl);
    }

    private void OpenSupportWhatsApp_Click(object sender, RoutedEventArgs e)
    {
        OpenExternal(SupportWhatsAppUrl);
    }
}
