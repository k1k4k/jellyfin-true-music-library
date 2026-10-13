using Jellyfin.Plugin.TrueMusicLibrary.Analysis;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using Jellyfin.Plugin.TrueMusicLibrary.Store;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Apply;

/// <summary>
/// Tâche « Réparer la médiathèque musicale » : analyse à jour, instantané, application, nettoyage des fiches,
/// puis nouvelle analyse de contrôle (0 changement restant attendu), dont les décisions deviennent celles que la
/// persistance applique ensuite (§5.4). Aucun déclencheur par défaut.
/// </summary>
public sealed class RepairTask : IScheduledTask
{
    public const string TaskKey = "TrueMusicLibraryRepair";

    private readonly AnalysisService _analysis;
    private readonly LibraryApplier _applier;
    private readonly DecisionStore _store;
    private readonly PersistenceState _persistence;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<RepairTask> _logger;

    public RepairTask(AnalysisService analysis, LibraryApplier applier, DecisionStore store, PersistenceState persistence, ILibraryManager libraryManager, ILogger<RepairTask> logger)
    {
        _analysis = analysis;
        _applier = applier;
        _store = store;
        _persistence = persistence;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => Messages.RepairName;

    public string Key => TaskKey;

    public string Description => Messages.RepairDescription;

    public string Category => Messages.Category;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var gate = await TaskGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        if (_libraryManager.IsScanRunning)
        {
            throw new InvalidOperationException(Messages.ScanRunningRepair);
        }

        // Valeurs remplacées par les fournisseurs depuis le dernier scan : enregistrées avant, pour que la restauration
        // les retrouve dans l'ordre.
        _persistence.FlushJournal();
        var activatedAt = _persistence.State?.ActivatedAt ?? DateTime.UtcNow;

        var report = await _analysis.AnalyzeAsync(Step(progress, 0, 40), cancellationToken).ConfigureAwait(false);
        var snapshot = new RepairSnapshot { PluginVersion = AnalysisService.Version, Target = AnalysisService.Target };
        var result = await _applier.RepairAsync(report, snapshot, _store.SaveSnapshot, Step(progress, 40, 90), cancellationToken).ConfigureAwait(false);

        var check = await _analysis.AnalyzeAsync(Step(progress, 90, 100), cancellationToken).ConfigureAwait(false);
        _persistence.Activate(AppliedState.FromReport(check, activatedAt));
        result.RemainingAlbumChanges = check.Libraries.Sum(l => l.Summary.ChangedAlbums);
        result.RemainingTrackChanges = check.Libraries.Sum(l => l.Summary.ChangedTracks);
        result.FinishedAt = DateTime.UtcNow;
        _store.SaveLastResult(result);

        _logger.LogInformation(
            "Réparation terminée : {Albums} albums, {Tracks} pistes, {Renamed} fiches renommées, {Created} créées, {Deleted} supprimées, {Errors} erreur(s), instantané {Snapshot}, reste {RemainingAlbums} albums / {RemainingTracks} pistes",
            result.AlbumsUpdated,
            result.TracksUpdated,
            result.ArtistsRenamed,
            result.ArtistsCreated,
            result.ArtistsDeleted,
            result.Errors.Count,
            result.SnapshotId ?? "aucun",
            result.RemainingAlbumChanges,
            result.RemainingTrackChanges);
    }

    internal static IProgress<double> Step(IProgress<double> progress, double from, double to)
        => new Progress<double>(p => progress.Report(from + ((to - from) * p / 100)));
}

/// <summary>
/// Tâche « Restaurer la médiathèque musicale » : coupe la persistance, puis remet les valeurs d'avant les réparations pas
/// encore restaurées (et d'avant les changements faits ensuite par la persistance).
/// </summary>
public sealed class RestoreTask : IScheduledTask
{
    public const string TaskKey = "TrueMusicLibraryRestore";

    private readonly AnalysisService _analysis;
    private readonly LibraryApplier _applier;
    private readonly DecisionStore _store;
    private readonly PersistenceState _persistence;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<RestoreTask> _logger;

    public RestoreTask(AnalysisService analysis, LibraryApplier applier, DecisionStore store, PersistenceState persistence, ILibraryManager libraryManager, ILogger<RestoreTask> logger)
    {
        _analysis = analysis;
        _applier = applier;
        _store = store;
        _persistence = persistence;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => Messages.RestoreName;

    public string Key => TaskKey;

    public string Description => Messages.RestoreDescription;

    public string Category => Messages.Category;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        using var gate = await TaskGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        if (_libraryManager.IsScanRunning)
        {
            throw new InvalidOperationException(Messages.ScanRunningRestore);
        }

        // Plus aucune décision appliquée pendant les scans ; les dernières valeurs remplacées par les fournisseurs sont
        // enregistrées pour être restaurées elles aussi.
        _persistence.FlushJournal();
        _persistence.Deactivate();

        // Tous les instantanés pas encore restaurés (réparations, persistance) : on revient à l'état d'avant la première
        // réparation, ou à ce que les fichiers disent aujourd'hui quand la sonde les a relus depuis.
        var snapshots = _store.LoadSnapshots().Where(s => !s.Restored).ToList();
        if (snapshots.Count == 0)
        {
            throw new InvalidOperationException(Messages.NothingToRestore);
        }

        var result = await _applier.RestoreAsync(snapshots, RepairTask.Step(progress, 0, 90), cancellationToken).ConfigureAwait(false);
        foreach (var snapshot in snapshots)
        {
            snapshot.Restored = true;
            snapshot.RestoredAt = DateTime.UtcNow;
            _store.SaveSnapshot(snapshot);
        }

        var check = await _analysis.AnalyzeAsync(RepairTask.Step(progress, 90, 100), cancellationToken).ConfigureAwait(false);
        result.RemainingAlbumChanges = check.Libraries.Sum(l => l.Summary.ChangedAlbums);
        result.RemainingTrackChanges = check.Libraries.Sum(l => l.Summary.ChangedTracks);
        result.FinishedAt = DateTime.UtcNow;
        _store.SaveLastResult(result);

        _logger.LogInformation(
            "Restauration de {Snapshot} terminée : {Albums} albums, {Tracks} pistes, {Renamed} fiches renommées, {Created} recréées, {Deleted} supprimées, {Errors} erreur(s)",
            result.SnapshotId,
            result.AlbumsUpdated,
            result.TracksUpdated,
            result.ArtistsRenamed,
            result.ArtistsCreated,
            result.ArtistsDeleted,
            result.Errors.Count);
    }
}
