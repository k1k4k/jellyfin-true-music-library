using System.Collections.Concurrent;
using Jellyfin.Plugin.TrueMusicLibrary.Apply;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using Jellyfin.Plugin.TrueMusicLibrary.Store;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Persistence;

/// <summary>
/// Persistance : décisions en vigueur gardées en mémoire pour les fournisseurs, et journal des valeurs qu'ils
/// remplacent.
/// <list type="bullet">
/// <item>Active après « Réparer », tant que la médiathèque n'est pas restaurée et que le réglage le permet.</item>
/// <item>Le journal note la valeur d'origine de chaque élément modifié par un fournisseur (pistes d'un nouvel album, ou relues
/// par « Remplacer toutes les métadonnées »). Il est vidé dans un instantané par la tâche d'après scan, « Réparer » et
/// « Restaurer » : la restauration revient ainsi aussi sur ces changements.</item>
/// </list>
/// </summary>
public sealed class PersistenceState
{
    private readonly DecisionStore _store;
    private readonly ILogger<PersistenceState> _logger;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, ItemSnapshot> _journal = new(StringComparer.Ordinal);
    private volatile LiveRules? _rules;
    private volatile bool _loaded;

    public PersistenceState(DecisionStore store, ILogger<PersistenceState> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Réglage « Garder la médiathèque réparée » (activé par défaut).</summary>
    public static bool IsEnabled => Plugin.Instance?.Configuration.EnablePersistence != false;

    /// <summary>Règles à appliquer, ou <c>null</c> si la persistance est inactive.</summary>
    public LiveRules? Rules => IsEnabled ? Load() : null;

    /// <summary>Décisions en vigueur, même si le réglage coupe la persistance.</summary>
    public AppliedState? State => Load()?.State;

    /// <summary>Règles en vigueur dès que la médiathèque est réparée, même si le réglage coupe la persistance (listes d'artistes).</summary>
    public LiveRules? RepairedRules => Load();

    /// <summary>Après « Réparer » ou la tâche d'après scan : nouvelles décisions en vigueur.</summary>
    public void Activate(AppliedState state)
    {
        lock (_lock)
        {
            _store.SaveApplied(state);
            _rules = new LiveRules(state);
            _loaded = true;
        }
    }

    /// <summary>Avant « Restaurer » : plus aucune décision appliquée.</summary>
    public void Deactivate()
    {
        lock (_lock)
        {
            _store.DeleteApplied();
            _rules = null;
            _loaded = true;
        }
    }

    /// <summary>
    /// Note la valeur d'un élément avant sa modification par un fournisseur. Une valeur native (sonde qui vient de relire le
    /// fichier) remplace celle déjà notée ; les autres ne remplacent rien.
    /// </summary>
    public void Record(ItemSnapshot original)
    {
        if (original.Native)
        {
            _journal[original.Id] = original;
        }
        else
        {
            _journal.TryAdd(original.Id, original);
        }
    }

    public int JournalCount => _journal.Count;

    /// <summary>Enregistre le journal dans un instantané (« Providers ») et le vide. Rien si le journal est vide.</summary>
    public RepairSnapshot? FlushJournal()
    {
        if (_journal.IsEmpty)
        {
            return null;
        }

        var snapshot = new RepairSnapshot { Origin = "Providers", PluginVersion = Analysis.AnalysisService.Version, Target = Analysis.AnalysisService.Target };
        foreach (var id in _journal.Keys.ToList())
        {
            if (_journal.TryRemove(id, out var item))
            {
                snapshot.Items.Add(item);
            }
        }

        _store.SaveSnapshot(snapshot);
        _logger.LogInformation("Persistance : {Count} valeur(s) remplacée(s) par les fournisseurs enregistrée(s) dans l'instantané {Snapshot}", snapshot.Items.Count, snapshot.Id);
        return snapshot;
    }

    private LiveRules? Load()
    {
        if (_loaded)
        {
            return _rules;
        }

        lock (_lock)
        {
            if (!_loaded)
            {
                try
                {
                    var state = _store.LoadApplied();
                    _rules = state is null ? null : new LiveRules(state);
                }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
                {
                    _logger.LogError(ex, "Persistance : applied.json illisible, persistance inactive jusqu'à la prochaine réparation");
                    _rules = null;
                }

                _loaded = true;
            }

            return _rules;
        }
    }
}
