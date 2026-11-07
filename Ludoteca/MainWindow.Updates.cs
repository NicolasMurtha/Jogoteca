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
    private async Task CheckForUpdatesAsync()
    {
        Velopack.UpdateInfo? update;
        try
        {
            update = await _updateService.CheckForUpdateAsync();
        }
        catch
        {
            // Offline or GitHub unavailable: try again on the next start.
            return;
        }

        if (update is null)
        {
            return;
        }

        var version = update.TargetFullRelease.Version.ToString();
        var confirmed = await ShowConfirmationAsync(
            "Nova versão disponível",
            $"A JOGOTECA {version} está disponível.\n\nAtualizar agora? O app será reiniciado e seus jogos e listas continuam salvos.",
            "Atualizar");
        if (!confirmed)
        {
            return;
        }

        try
        {
            ShowToast($"Baixando a JOGOTECA {version}...");
            await _updateService.DownloadAndRestartAsync(update);
        }
        catch (Exception ex)
        {
            ShowToast($"Não foi possível atualizar agora: {ex.Message}");
        }
    }
}
