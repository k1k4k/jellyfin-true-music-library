using Jellyfin.Plugin.TrueMusicLibrary.Analysis;
using Jellyfin.Plugin.TrueMusicLibrary.Apply;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using Jellyfin.Plugin.TrueMusicLibrary.Store;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Persistence;

/// <summary>
/// Tâche d'après scan : garde la médiathèque réparée après chaque scan.
/// <list type="number">
/// <item>enregistre dans un instantané les valeurs remplacées par les fournisseurs pendant le scan ;</item>
/// <item>cherche les albums dont la décision n'est plus à jour (empreinte des entrées) : nouveaux albums, pistes ajoutées
/// ou retaggées, autre release identifiée, réglages ou override changés. S'il n'y en a pas, elle s'arrête là ;</item>
/// <item>sinon : analyse (cache MusicBrainz gardé quel que soit son âge, réseau seulement pour les nouveautés), application
/// avec instantané, nettoyage des fiches, actualisation directe des fiches créées, puis décisions mises à jour.</item>
/// </list>
/// Rien tant que la médiathèque n'a pas été réparée. Si une autre opération True Music Library tourne, elle passe son tour.
/// </summary>
public sealed class TrueArtistsPostScanTask : ILibraryPostScanTask
{
    private readonly PersistenceState _persistence;
    private readonly AnalysisService _analysis;
    private readonly LibraryApplier _applier;
    private readonly DecisionStore _store;
    private readonly ILogger<TrueArtistsPostScanTask> _logger;

    public TrueArtistsPostScanTask(
        PersistenceState persistence,
        AnalysisService analysis,
        LibraryApplier applier,
        DecisionStore store,
        ILogger<TrueArtistsPostScanTask> logger)
    {
        _persistence = persistence;
        _analysis = analysis;
        _applier = applier;
        _store = store;
        _logger = logger;
    }

    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (_persistence.Rules?.State is not { } state)
        {
            return;
        }

        IDisposable gate;
        try
        {
            gate = await TaskGate.EnterAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            _logger.LogInformation("Persistance : une autre opération True Music Library est en cours, mise à jour après scan sautée");
            return;
        }

        using (gate)
        {
            _persistence.FlushJournal();

            var stale = _analysis.CountStaleAlbums(state, cancellationToken);
            progress.Report(10);
            if (stale == 0)
            {
                _logger.LogInformation("Persistance : décisions à jour après le scan");
                return;
            }

            _logger.LogInformation("Persistance : {Count} album(s) à résoudre après le scan", stale);
            var report = await _analysis.AnalyzeAsync(RepairTask.Step(progress, 10, 45), cancellationToken, allowStaleCache: true).ConfigureAwait(false);
            var snapshot = new RepairSnapshot { Origin = "Scan", PluginVersion = AnalysisService.Version, Target = AnalysisService.Target };
            var result = await _applier.RepairAsync(report, snapshot, _store.SaveSnapshot, RepairTask.Step(progress, 45, 80), cancellationToken, refreshCreatedArtists: true)
                .ConfigureAwait(false);

            // Rien d'écrit : l'analyse décrit déjà la médiathèque telle qu'elle est, pas besoin d'analyse de contrôle.
            var changed = result.AlbumsUpdated + result.TracksUpdated + result.ArtistsRenamed + result.ArtistsCreated + result.ArtistsDeleted > 0;
            var check = changed
                ? await _analysis.AnalyzeAsync(RepairTask.Step(progress, 80, 100), cancellationToken, allowStaleCache: true).ConfigureAwait(false)
                : report;
            _persistence.Activate(AppliedState.FromReport(check, state.ActivatedAt));

            result.Operation = "Scan";
            result.RemainingAlbumChanges = check.Libraries.Sum(l => l.Summary.ChangedAlbums);
            result.RemainingTrackChanges = check.Libraries.Sum(l => l.Summary.ChangedTracks);
            result.FinishedAt = DateTime.UtcNow;
            if (changed || result.Errors.Count > 0)
            {
                _store.SaveLastResult(result);
            }

            _logger.LogInformation(
                "Persistance : mise à jour après scan terminée, {Albums} albums, {Tracks} pistes, {Renamed} fiches renommées, {Created} créées, {Deleted} supprimées, {Errors} erreur(s), reste {RemainingAlbums} albums / {RemainingTracks} pistes",
                result.AlbumsUpdated,
                result.TracksUpdated,
                result.ArtistsRenamed,
                result.ArtistsCreated,
                result.ArtistsDeleted,
                result.Errors.Count,
                result.RemainingAlbumChanges,
                result.RemainingTrackChanges);
        }
    }
}
