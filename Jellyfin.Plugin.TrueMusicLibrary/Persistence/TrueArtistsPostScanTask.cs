using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.TrueMusicLibrary.Persistence;

/// <summary>
/// Tâche d'après scan : garde la médiathèque réparée après chaque scan (voir <see cref="PersistenceUpdater"/>).
/// </summary>
public sealed class TrueArtistsPostScanTask : ILibraryPostScanTask
{
    private readonly PersistenceUpdater _updater;
    private readonly LibraryChangeWatcher _watcher;

    public TrueArtistsPostScanTask(PersistenceUpdater updater, LibraryChangeWatcher watcher)
    {
        _updater = updater;
        _watcher = watcher;
    }

    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        // Le scan couvre les changements vus pendant qu'il tournait : pas de mise à jour « après ajouts » en plus.
        _watcher.Forget();
        await _updater.UpdateAsync(PersistenceUpdater.AfterScan, progress, cancellationToken).ConfigureAwait(false);
    }
}
