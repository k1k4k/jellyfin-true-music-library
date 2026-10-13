using System.Text.Json;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.TrueMusicLibrary.Store;

/// <summary>
/// Fichiers JSON du plugin :
/// <list type="bullet">
/// <item><c>&lt;plugins/configurations&gt;/TrueMusicLibrary/analysis.json</c> : dernier rapport d'analyse ;</item>
/// <item><c>…/overrides.json</c> : choix manuels, jamais écrasés par une analyse ;</item>
/// <item><c>…/snapshots/&lt;date&gt;.json</c> : valeurs d'avant chaque réparation ; <c>…/last-operation.json</c> : dernier bilan ;</item>
/// <item><c>…/applied.json</c> : décisions en vigueur depuis la dernière réparation (persistance ; absent = inactive) ;</item>
/// <item><c>&lt;cache&gt;/truemusiclibrary/musicbrainz/</c> : réponses MusicBrainz (peut être vidé sans risque).</item>
/// </list>
/// Hors du dossier versionné du plugin : rien n'est perdu à une mise à jour.
/// </summary>
public sealed class DecisionStore
{
    private readonly object _lock = new();

    public DecisionStore(IApplicationPaths applicationPaths)
    {
        DataDirectory = Path.Combine(applicationPaths.PluginConfigurationsPath, "TrueMusicLibrary");
        MusicBrainzCacheDirectory = Path.Combine(applicationPaths.CachePath, "truemusiclibrary", "musicbrainz");
    }

    public string DataDirectory { get; }

    public string MusicBrainzCacheDirectory { get; }

    private string AnalysisPath => Path.Combine(DataDirectory, "analysis.json");

    private string OverridesPath => Path.Combine(DataDirectory, "overrides.json");

    public void SaveAnalysis(AnalysisReport report)
    {
        lock (_lock)
        {
            WriteAtomic(AnalysisPath, JsonSerializer.Serialize(report, ReportJson.Options));
        }
    }

    /// <summary>Rapport brut, tel qu'écrit, pour l'API (évite de le désérialiser).</summary>
    public string? ReadAnalysisJson()
    {
        lock (_lock)
        {
            return File.Exists(AnalysisPath) ? File.ReadAllText(AnalysisPath) : null;
        }
    }

    public Dictionary<string, OverrideEntry> LoadOverrides()
    {
        lock (_lock)
        {
            if (!File.Exists(OverridesPath))
            {
                return new Dictionary<string, OverrideEntry>(StringComparer.OrdinalIgnoreCase);
            }

            var loaded = JsonSerializer.Deserialize<Dictionary<string, OverrideEntry>>(File.ReadAllText(OverridesPath), ReportJson.Options);
            return new Dictionary<string, OverrideEntry>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
        }
    }

    /// <summary>Enregistre (ou retire, avec <c>null</c>) le choix manuel d'un album.</summary>
    public void SetOverride(string albumId, OverrideEntry? entry)
    {
        lock (_lock)
        {
            var overrides = LoadOverrides();
            if (entry is null)
            {
                overrides.Remove(albumId);
            }
            else
            {
                overrides[albumId] = entry;
            }

            WriteAtomic(OverridesPath, JsonSerializer.Serialize(overrides, ReportJson.Options));
        }
    }

    private string SnapshotsDirectory => Path.Combine(DataDirectory, "snapshots");

    private string LastResultPath => Path.Combine(DataDirectory, "last-operation.json");

    /// <summary>Enregistre (ou met à jour) un instantané de réparation.</summary>
    public void SaveSnapshot(Apply.RepairSnapshot snapshot)
    {
        lock (_lock)
        {
            WriteAtomic(Path.Combine(SnapshotsDirectory, snapshot.Id + ".json"), JsonSerializer.Serialize(snapshot, ReportJson.Options));
        }
    }

    /// <summary>Instantanés, du plus récent au plus ancien.</summary>
    public List<Apply.RepairSnapshot> LoadSnapshots()
    {
        lock (_lock)
        {
            if (!Directory.Exists(SnapshotsDirectory))
            {
                return [];
            }

            return Directory.GetFiles(SnapshotsDirectory, "*.json")
                .Select(f => JsonSerializer.Deserialize<Apply.RepairSnapshot>(File.ReadAllText(f), ReportJson.Options))
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderByDescending(s => s.CreatedAt)
                .ToList();
        }
    }

    /// <summary>Instantané de la dernière réparation pas encore restaurée.</summary>
    public Apply.RepairSnapshot? LatestRestorableSnapshot() => LoadSnapshots().FirstOrDefault(s => !s.Restored);

    public void SaveLastResult(Apply.RepairResult result)
    {
        lock (_lock)
        {
            WriteAtomic(LastResultPath, JsonSerializer.Serialize(result, ReportJson.Options));
        }
    }

    public string? ReadLastResultJson()
    {
        lock (_lock)
        {
            return File.Exists(LastResultPath) ? File.ReadAllText(LastResultPath) : null;
        }
    }

    private string AppliedPath => Path.Combine(DataDirectory, "applied.json");

    /// <summary>Décisions en vigueur (persistance), ou <c>null</c> si aucune réparation n'est en vigueur.</summary>
    public AppliedState? LoadApplied()
    {
        lock (_lock)
        {
            return File.Exists(AppliedPath) ? JsonSerializer.Deserialize<AppliedState>(File.ReadAllText(AppliedPath), ReportJson.Options) : null;
        }
    }

    public void SaveApplied(AppliedState state)
    {
        lock (_lock)
        {
            WriteAtomic(AppliedPath, JsonSerializer.Serialize(state, ReportJson.Options));
        }
    }

    public void DeleteApplied()
    {
        lock (_lock)
        {
            if (File.Exists(AppliedPath))
            {
                File.Delete(AppliedPath);
            }
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
    }
}
