using System.Reflection;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using Jellyfin.Plugin.TrueMusicLibrary.Store;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Analysis;

/// <summary>
/// Analyse de toutes les bibliothèques musique (lecture seule), partagée par les tâches Analyser, Réparer et Restaurer.
/// Le rapport est enregistré dans <c>analysis.json</c>.
/// </summary>
public sealed class AnalysisService
{
    private static readonly Guid _musicBrainzPluginId = new("8c95c4d2-e50c-4fb0-a4f3-6c06ff0f9a1a");

    private readonly LibraryReader _reader;
    private readonly DecisionStore _store;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IPluginManager _pluginManager;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(LibraryReader reader, DecisionStore store, IHttpClientFactory httpClientFactory, IPluginManager pluginManager, ILogger<AnalysisService> logger)
    {
        _reader = reader;
        _store = store;
        _httpClientFactory = httpClientFactory;
        _pluginManager = pluginManager;
        _logger = logger;
    }

    public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    public static string Target =>
#if JF10
        "jf10";
#else
        "jf12";
#endif

    private static ResolverSettings CurrentSettings => (Plugin.Instance?.Configuration ?? new Configuration.PluginConfiguration()).ToResolverSettings();

    /// <param name="progress">Progression (0 à 100).</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <param name="allowStaleCache">Garder les réponses MusicBrainz en cache quel que soit leur âge (tâche d'après scan).</param>
    public async Task<AnalysisReport> AnalyzeAsync(IProgress<double> progress, CancellationToken cancellationToken, bool allowStaleCache = false)
    {
        var settings = CurrentSettings;
        var libraries = _reader.ReadMusicLibraries(cancellationToken);
        progress.Report(5);

        var client = new MusicBrainzClient(_httpClientFactory.CreateClient(), MusicBrainzOptionsFromJellyfin(allowStaleCache), _logger);
        var runner = new AnalysisRunner(settings, client, _logger);
        var overrides = _store.LoadOverrides();
        var report = new AnalysisReport
        {
            GeneratedAt = DateTime.UtcNow,
            PluginVersion = Version,
            Target = Target,
            Settings = settings,
            MusicBrainz = client.Stats
        };

        for (var i = 0; i < libraries.Count; i++)
        {
            var start = 5 + (95.0 * i / libraries.Count);
            var span = 95.0 / libraries.Count;
            var libraryProgress = new Progress<double>(p => progress.Report(start + (span * p / 100)));
            _logger.LogInformation("Analyse de la bibliothèque {Library} : {Albums} albums", libraries[i].Name, libraries[i].Albums.Count);
            report.Libraries.Add(await runner.AnalyzeAsync(libraries[i], overrides, libraryProgress, cancellationToken).ConfigureAwait(false));
        }

        _store.SaveAnalysis(report);
        _logger.LogInformation(
            "Analyse terminée : {Libraries} bibliothèque(s), MusicBrainz {Requests} requêtes / {Hits} en cache / {Failures} échecs",
            libraries.Count,
            client.Stats.Requests,
            client.Stats.CacheHits,
            client.Stats.Failures);
        progress.Report(100);
        return report;
    }

    /// <summary>
    /// Albums dont la décision en vigueur n'est plus à jour : nouveaux, disparus, ou dont l'empreinte des entrées a changé
    /// (pistes ajoutées ou retaggées, autre release identifiée, réglages ou override modifiés). Lecture seule, sans réseau.
    /// </summary>
    public int CountStaleAlbums(AppliedState state, CancellationToken cancellationToken)
    {
        var settings = CurrentSettings;
        var overrides = _store.LoadOverrides();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var stale = 0;
        foreach (var (_, language, albums) in _reader.ReadAlbums(cancellationToken))
        {
            var librarySettings = AnalysisRunner.SettingsFor(settings, language);
            foreach (var album in albums)
            {
                seen.Add(album.Id);
                if (!state.Albums.TryGetValue(album.Id, out var applied)
                    || applied.InputHash != ReleaseArtistResolver.ComputeInputHash(librarySettings, overrides, album))
                {
                    stale++;
                }
            }
        }

        return stale + state.Albums.Keys.Count(id => !seen.Contains(id));
    }

    /// <summary>Serveur et limite de débit du plugin MusicBrainz de Jellyfin (lus par réflexion : il fait partie du cœur).</summary>
    private MusicBrainzOptions MusicBrainzOptionsFromJellyfin(bool allowStaleCache)
    {
        var server = MusicBrainzOptions.OfficialServer;
        var rateLimit = 1.0;
        try
        {
            if (_pluginManager.GetPlugin(_musicBrainzPluginId)?.Instance is IHasPluginConfiguration { Configuration: { } configuration })
            {
                server = configuration.GetType().GetProperty("Server")?.GetValue(configuration) as string ?? server;
                rateLimit = configuration.GetType().GetProperty("RateLimit")?.GetValue(configuration) as double? ?? rateLimit;
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidCastException)
        {
            _logger.LogDebug(ex, "Configuration du plugin MusicBrainz illisible : valeurs par défaut");
        }

        return new MusicBrainzOptions
        {
            Server = string.IsNullOrWhiteSpace(server) ? MusicBrainzOptions.OfficialServer : server,
            RateLimitSeconds = rateLimit,
            CacheDirectory = _store.MusicBrainzCacheDirectory,
            AllowStaleCache = allowStaleCache,
            UserAgent = $"Jellyfin-TrueMusicLibrary/{Version} ( https://github.com/k1k4k/jellyfin-true-music-library )"
        };
    }
}

/// <summary>
/// Une seule opération True Music Library à la fois (analyser, réparer, restaurer) : deux tâches qui écrivent ou
/// lisent la médiathèque en même temps se marcheraient dessus.
/// </summary>
public static class TaskGate
{
    private static readonly SemaphoreSlim _gate = new(1, 1);

    public static async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException(Messages.OtherOperationRunning);
        }

        return new Releaser();
    }

    private sealed class Releaser : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _gate.Release();
            }
        }
    }
}
