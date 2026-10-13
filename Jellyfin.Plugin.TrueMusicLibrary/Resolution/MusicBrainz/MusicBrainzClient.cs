using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;

/// <summary>Réglages du client MusicBrainz.</summary>
public sealed class MusicBrainzOptions
{
    public const string OfficialServer = "https://musicbrainz.org";

    /// <summary>Serveur (celui du plugin MusicBrainz de Jellyfin, ou un miroir).</summary>
    public string Server { get; init; } = OfficialServer;

    /// <summary>Secondes entre deux requêtes. Le serveur officiel impose au moins 1.</summary>
    public double RateLimitSeconds { get; init; } = 1.0;

    /// <summary>User-Agent identifiable, exigé par MusicBrainz.</summary>
    public string UserAgent { get; init; } = "Jellyfin-TrueMusicLibrary/0.1 ( https://github.com/k1k4k/jellyfin-true-music-library )";

    /// <summary>Dossier du cache des réponses (une réponse par fichier).</summary>
    public required string CacheDirectory { get; init; }

    public int CacheDays { get; init; } = 30;

    /// <summary>Réponses en cache gardées quel que soit leur âge (tâche d'après scan : réseau seulement pour les nouveautés).</summary>
    public bool AllowStaleCache { get; init; }

    /// <summary>Cache seulement, aucune requête réseau.</summary>
    public bool Offline { get; init; }
}

/// <summary>Compteurs d'une analyse.</summary>
public sealed class MusicBrainzStats
{
    public int Requests { get; set; }

    public int CacheHits { get; set; }

    public int NotFound { get; set; }

    public int Failures { get; set; }
}

/// <summary>
/// Client minimal de l'API MusicBrainz WS/2 : une requête par release (crédits de l'album et des pistes,
/// type de sortie, alias des artistes), limite de débit globale, cache disque, réessais sur 503.
/// </summary>
public sealed class MusicBrainzClient
{
    private const string ReleaseIncludes = "artist-credits+recordings+release-groups+aliases";
    private const string ReleaseGroupIncludes = "artist-credits+aliases";
    private const string ArtistIncludes = "aliases";

    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static DateTime _lastRequestUtc = DateTime.MinValue;

    private readonly HttpClient _http;
    private readonly MusicBrainzOptions _options;
    private readonly ILogger _logger;

    public MusicBrainzClient(HttpClient http, MusicBrainzOptions options, ILogger logger)
    {
        _http = http;
        _options = options;
        _logger = logger;
    }

    public MusicBrainzStats Stats { get; } = new();

    public Task<MbRelease?> GetReleaseAsync(string mbid, CancellationToken cancellationToken)
        => GetAsync<MbRelease>("release", mbid, ReleaseIncludes, cancellationToken);

    public Task<MbReleaseGroup?> GetReleaseGroupAsync(string mbid, CancellationToken cancellationToken)
        => GetAsync<MbReleaseGroup>("release-group", mbid, ReleaseGroupIncludes, cancellationToken);

    public Task<MbArtist?> GetArtistAsync(string mbid, CancellationToken cancellationToken)
        => GetAsync<MbArtist>("artist", mbid, ArtistIncludes, cancellationToken);

    private async Task<T?> GetAsync<T>(string entity, string mbid, string includes, CancellationToken cancellationToken)
        where T : class
    {
        // Un identifiant qui n'est pas un GUID ne part ni dans une URL ni dans un nom de fichier.
        if (!Guid.TryParse(mbid, out var id))
        {
            return null;
        }

        var key = id.ToString("D");
        var cacheFile = Path.Combine(_options.CacheDirectory, entity, key + ".json");
        var cached = ReadCache<T>(cacheFile, out var hit);
        if (hit)
        {
            Stats.CacheHits++;
            return cached;
        }

        if (_options.Offline)
        {
            return null;
        }

        var url = $"{_options.Server.TrimEnd('/')}/ws/2/{entity}/{key}?inc={includes}&fmt=json";
        for (var attempt = 0; attempt < 4; attempt++)
        {
            await WaitTurnAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd(_options.UserAgent);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
                Stats.Requests++;

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    Stats.NotFound++;
                    WriteCache(cacheFile, "{\"notFound\":true}");
                    return null;
                }

                if (response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2 << attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                WriteCache(cacheFile, text);
                return JsonSerializer.Deserialize<T>(text);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "MusicBrainz {Entity} {Mbid} : échec de la requête (essai {Attempt})", entity, key, attempt + 1);
                if (attempt == 3)
                {
                    break;
                }
            }
        }

        Stats.Failures++;
        _logger.LogWarning("MusicBrainz {Entity} {Mbid} : abandon après 4 essais", entity, key);
        return null;
    }

    private async Task WaitTurnAsync(CancellationToken cancellationToken)
    {
        var minimum = TimeSpan.FromSeconds(
            string.Equals(new Uri(_options.Server).Host, new Uri(MusicBrainzOptions.OfficialServer).Host, StringComparison.OrdinalIgnoreCase)
                ? Math.Max(1.0, _options.RateLimitSeconds)
                : Math.Max(0.0, _options.RateLimitSeconds));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var wait = _lastRequestUtc + minimum - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
            }

            _lastRequestUtc = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

    private T? ReadCache<T>(string file, out bool hit)
        where T : class
    {
        hit = false;
        try
        {
            var info = new FileInfo(file);
            if (!info.Exists || (!_options.AllowStaleCache && info.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-_options.CacheDays)))
            {
                return null;
            }

            var text = File.ReadAllText(file);
            hit = true;
            return text.Contains("\"notFound\":true", StringComparison.Ordinal) && text.Length < 32 ? null : JsonSerializer.Deserialize<T>(text);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Cache MusicBrainz illisible : {File}", file);
            hit = false;
            return null;
        }
    }

    private void WriteCache(string file, string text)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Écriture du cache MusicBrainz impossible : {File}", file);
        }
    }
}
