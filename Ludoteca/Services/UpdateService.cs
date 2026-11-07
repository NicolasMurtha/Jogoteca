using Velopack;
using Velopack.Sources;

namespace Ludoteca.Services;

public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/NicolasMurtha/Jogoteca";

    private UpdateManager? _manager;
    private bool _managerCreated;

    public bool IsInstalled => Manager?.IsInstalled == true;

    // Created on first use: outside an installed app (tests, designer) Velopack is not
    // initialized and the app simply behaves as not installed.
    private UpdateManager? Manager
    {
        get
        {
            if (!_managerCreated)
            {
                _managerCreated = true;
                try
                {
                    _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, false));
                }
                catch (InvalidOperationException)
                {
                    _manager = null;
                }
            }

            return _manager;
        }
    }

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        return Manager is { IsInstalled: true } manager
            ? await manager.CheckForUpdatesAsync()
            : null;
    }

    public async Task DownloadAndRestartAsync(UpdateInfo update)
    {
        var manager = Manager ?? throw new InvalidOperationException("O atualizador não está disponível.");
        await manager.DownloadUpdatesAsync(update);
        manager.ApplyUpdatesAndRestart(update);
    }
}
