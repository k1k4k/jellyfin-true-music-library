using System.Collections.Concurrent;
using Jellyfin.Plugin.TrueMusicLibrary.Analysis;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Persistence;

/// <summary>
/// Mise à jour après les ajouts : quand la surveillance en temps réel de Jellyfin ajoute, retire ou relit des pistes ou des
/// albums, la médiathèque est remise à jour sans attendre le prochain scan complet (<see cref="PersistenceUpdater"/>).
/// <list type="bullet">
/// <item>Elle attend un moment de calme après le dernier changement (copie en cours, dossier que Jellyfin n'a pas fini
/// de reconnaître : un dossier d'artiste est d'abord vu comme un simple dossier, et une fiche « par nom » en double est
/// créée entre-temps).</item>
/// <item>Elle attend aussi que Jellyfin ait lu les pistes et albums ajoutés (tags, identifiants MusicBrainz, images) : sa
/// file d'actualisation peut avoir du retard (beaucoup de fiches à actualiser, fournisseur lent). Sans cela, le plugin
/// déciderait d'après le seul nom du dossier, puis recommencerait une fois les tags lus. Au plus 30 minutes.</item>
/// <item>Rien pendant un scan de bibliothèque : sa tâche d'après scan s'en charge.</item>
/// <item>Les modifications que le plugin fait lui-même pendant ses opérations ne relancent rien : il ne modifie que des
/// pistes, des albums et des fiches artiste existants, jamais il n'ajoute ni ne retire de piste ou d'album.</item>
/// </list>
/// Rien tant que la médiathèque n'a pas été réparée, ou si le réglage est coupé.
/// </summary>
public sealed class LibraryChangeWatcher : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly PersistenceState _persistence;
    private readonly PersistenceUpdater _updater;
    private readonly ILogger<LibraryChangeWatcher> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _lock = new();
    private readonly Timer _timer;
    private readonly ConcurrentDictionary<Guid, byte> _added = new();
    private bool _pending;
    private int _running;
    private DateTime? _waitingSince;

    public LibraryChangeWatcher(ILibraryManager libraryManager, PersistenceState persistence, PersistenceUpdater updater, ILogger<LibraryChangeWatcher> logger)
    {
        _libraryManager = libraryManager;
        _persistence = persistence;
        _updater = updater;
        _logger = logger;
        _timer = new Timer(_ => _ = FireAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Réglage « Mettre à jour après les ajouts » (activé par défaut).</summary>
    public static bool IsEnabled => Plugin.Instance?.Configuration is not { } c || (c.EnablePersistence && c.UpdateAfterChanges);

    /// <summary>Délai de calme après le dernier changement avant la mise à jour.</summary>
    public static TimeSpan Delay => TimeSpan.FromSeconds(Math.Max(5, Plugin.Instance?.Configuration.UpdateAfterChangesDelaySeconds ?? 120));

    /// <summary>Attente maximale des éléments ajoutés que Jellyfin n'a pas encore lus.</summary>
    public static readonly TimeSpan MaxWaitForJellyfin = TimeSpan.FromMinutes(30);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemAdded;
        _libraryManager.ItemUpdated += OnItemUpdated;
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemAdded;
        _libraryManager.ItemUpdated -= OnItemUpdated;
        _libraryManager.ItemRemoved -= OnItemRemoved;
        Forget();
        _stopping.Cancel();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _stopping.Dispose();
    }

    /// <summary>Oublie les changements en attente (un scan vient de les couvrir).</summary>
    public void Forget()
    {
        lock (_lock)
        {
            _pending = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _added.Clear();
            _waitingSince = null;
        }
    }

    /// <summary>
    /// Pistes et albums. Pour un retrait, Jellyfin ne signale parfois que l'élément du haut : dossier d'artiste ou simple
    /// dossier d'une bibliothèque musique, dont les albums disparaissent avec lui (et déjà détaché : un dossier d'artiste
    /// ressemble alors à une fiche « par nom »). On se fie donc au chemin : les fiches « par nom » retirées par le
    /// nettoyage du plugin ou de Jellyfin sont rangées dans les métadonnées, hors des bibliothèques.
    /// </summary>
    private bool IsMusic(ItemChangeEventArgs e, bool removed) => e.Item switch
    {
        Audio or MusicAlbum => true,
        Folder folder => removed && InMusicLibrary(folder.Path),
        _ => false
    };

    private bool InMusicLibrary(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return _libraryManager.GetVirtualFolders()
            .Where(v => v.CollectionType == CollectionTypeOptions.music)
            .SelectMany(v => v.Locations)
            .Any(location => path.StartsWith(location.TrimEnd('/', '\\') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(location.TrimEnd('/', '\\') + '/', StringComparison.OrdinalIgnoreCase));
    }

    private void OnItemAdded(object? sender, ItemChangeEventArgs e) => Changed(e, removed: false, ownWritesPossible: false);

    private void OnItemRemoved(object? sender, ItemChangeEventArgs e) => Changed(e, removed: true, ownWritesPossible: false);

    private void OnItemUpdated(object? sender, ItemChangeEventArgs e)
    {
        if (e.UpdateReason != ItemUpdateType.ImageUpdate)
        {
            Changed(e, removed: false, ownWritesPossible: true);
        }
    }

    private void Changed(ItemChangeEventArgs e, bool removed, bool ownWritesPossible)
    {
        if (_libraryManager.IsScanRunning || !IsEnabled || _persistence.Rules is null || !IsMusic(e, removed))
        {
            return;
        }

        // Pendant une opération du plugin, les pistes et albums modifiés sont les siens.
        if (ownWritesPossible && (TaskGate.IsBusy || Volatile.Read(ref _running) == 1))
        {
            return;
        }

        if (!removed && !ownWritesPossible && e.Item is Audio or MusicAlbum)
        {
            _added[e.Item.Id] = 0;
        }

        Schedule();
    }

    /// <summary>
    /// Vrai si des pistes ou albums ajoutés n'ont pas encore été actualisés par Jellyfin (jamais lus : pas de tags, pas
    /// d'identifiants). Leur actualisation relancera l'attente de calme ; au-delà de <see cref="MaxWaitForJellyfin"/>, on
    /// n'attend plus.
    /// </summary>
    private bool WaitingForJellyfin()
    {
        foreach (var id in _added.Keys)
        {
            if (_libraryManager.GetItemById(id) is not { } item || item.DateLastRefreshed != default)
            {
                _added.TryRemove(id, out _);
            }
        }

        if (_added.IsEmpty)
        {
            _waitingSince = null;
            return false;
        }

        _waitingSince ??= DateTime.UtcNow;
        if (DateTime.UtcNow - _waitingSince < MaxWaitForJellyfin)
        {
            _logger.LogInformation("Persistance : {Count} élément(s) ajouté(s) pas encore lus par Jellyfin, mise à jour repoussée", _added.Count);
            return true;
        }

        _logger.LogWarning("Persistance : {Count} élément(s) ajouté(s) toujours pas lus par Jellyfin après {Minutes} min, mise à jour quand même", _added.Count, MaxWaitForJellyfin.TotalMinutes);
        _added.Clear();
        _waitingSince = null;
        return false;
    }

    private void Schedule()
    {
        lock (_lock)
        {
            _pending = true;
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    private async Task FireAsync()
    {
        lock (_lock)
        {
            if (!_pending)
            {
                return;
            }

            _pending = false;
        }

        if (_stopping.IsCancellationRequested || !IsEnabled || _persistence.Rules is null || _libraryManager.IsScanRunning)
        {
            return;
        }

        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            Schedule();
            return;
        }

        if (WaitingForJellyfin())
        {
            Volatile.Write(ref _running, 0);
            Schedule();
            return;
        }

        try
        {
            _logger.LogInformation("Persistance : changements de musique détectés en temps réel, mise à jour");
            if (!await _updater.UpdateAsync(PersistenceUpdater.AfterChanges, new Progress<double>(), _stopping.Token).ConfigureAwait(false))
            {
                // Une autre opération tournait (analyse, réparation…) : on réessaie plus tard.
                Schedule();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Persistance : échec de la mise à jour après ajouts");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
