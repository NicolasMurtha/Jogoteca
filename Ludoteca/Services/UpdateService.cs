using Velopack;
using Velopack.Sources;

namespace Ludoteca.Services;

public sealed class UpdateService
{
    private const string RepositoryUrl = "https://github.com/NicolasMurtha/Jogoteca";

    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, null, false));

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<UpdateInfo?> CheckForUpdateAsync()
    {
        return _manager.IsInstalled
            ? await _manager.CheckForUpdatesAsync()
            : null;
    }

    public async Task DownloadAndRestartAsync(UpdateInfo update)
    {
        await _manager.DownloadUpdatesAsync(update);
        _manager.ApplyUpdatesAndRestart(update);
    }
}
