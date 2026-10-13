using Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>Réponse d'une source pour un album.</summary>
public sealed class SourceResult
{
    public required string SourceName { get; init; }

    /// <summary>Crédits avec rôles déjà connus (MusicBrainz).</summary>
    public IReadOnlyList<Credit> Credits { get; init; } = [];

    /// <summary>Noms bruts d'artistes d'album (fournisseurs génériques) : le résolveur les découpe.</summary>
    public IReadOnlyList<string> RawAlbumArtists { get; init; } = [];

    /// <summary>MBID de l'artiste d'album, quand la source le donne pour un artiste unique.</summary>
    public string? AlbumArtistMbid { get; init; }

    public Confidence Confidence { get; init; } = Confidence.Medium;

    public string? ReleaseType { get; init; }

    /// <summary>Crédits par piste (Id de piste → crédits), quand la source les a.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<Credit>> TrackCredits { get; init; } = new Dictionary<string, IReadOnlyList<Credit>>();

    /// <summary>Titre de la release (pour les remarques du rapport).</summary>
    public string? ReleaseTitle { get; init; }

    /// <summary>Pistes locales reconnues dans la release (null si la source ne sait pas apparier).</summary>
    public int? MatchedTracks { get; init; }
}

/// <summary>Une source de métadonnées d'album, dans l'ordre configuré pour la bibliothèque.</summary>
public interface IAlbumSource
{
    string Name { get; }

    Task<SourceResult?> ResolveAsync(AlbumSnapshot album, CancellationToken cancellationToken);
}

/// <summary>
/// MusicBrainz, interrogé directement (le fournisseur intégré à Jellyfin aplatit les crédits et perd les jointures).
/// Les releases sont lues une première fois par <see cref="PrefetchAsync"/>, pour que tous les artistes soient connus
/// avant la moindre décision.
/// </summary>
public sealed class MusicBrainzSource : IAlbumSource
{
    public const string SourceName = "MusicBrainz";

    private readonly MusicBrainzClient _client;
    private readonly CreditParser _parser;
    private readonly ArtistIdentity _identity;
    private readonly Dictionary<string, MbRelease?> _releases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MbReleaseGroup?> _groups = new(StringComparer.Ordinal);

    public MusicBrainzSource(MusicBrainzClient client, CreditParser parser, ArtistIdentity identity)
    {
        _client = client;
        _parser = parser;
        _identity = identity;
    }

    public string Name => SourceName;

    public async Task PrefetchAsync(AlbumSnapshot album, CancellationToken cancellationToken)
    {
        if (_releases.ContainsKey(album.Id) || _groups.ContainsKey(album.Id))
        {
            return;
        }

        var releaseId = album.GetProviderId(ProviderKeys.MusicBrainzAlbum);
        if (releaseId is not null)
        {
            var release = await _client.GetReleaseAsync(releaseId, cancellationToken).ConfigureAwait(false);
            _releases[album.Id] = release;
            _identity.AddRelease(release);
            if (release is not null)
            {
                return;
            }
        }

        var groupId = album.GetProviderId(ProviderKeys.MusicBrainzReleaseGroup);
        if (groupId is not null)
        {
            var group = await _client.GetReleaseGroupAsync(groupId, cancellationToken).ConfigureAwait(false);
            _groups[album.Id] = group;
            foreach (var credit in group?.ArtistCredit ?? [])
            {
                _identity.AddCredit(credit);
            }
        }
    }

    public async Task<SourceResult?> ResolveAsync(AlbumSnapshot album, CancellationToken cancellationToken)
    {
        await PrefetchAsync(album, cancellationToken).ConfigureAwait(false);

        if (_releases.GetValueOrDefault(album.Id) is { } release)
        {
            var trackCredits = new Dictionary<string, IReadOnlyList<Credit>>(StringComparer.Ordinal);
            var matches = TrackMatcher.Match(album, release);
            foreach (var (trackId, mbTrack) in matches)
            {
                var credits = _parser.FromMusicBrainz(mbTrack.ArtistCredit ?? mbTrack.Recording?.ArtistCredit);
                if (credits.Count > 0)
                {
                    trackCredits[trackId] = credits;
                }
            }

            return new SourceResult
            {
                SourceName = SourceName,
                Credits = _parser.FromMusicBrainz(release.ArtistCredit ?? release.ReleaseGroup?.ArtistCredit),
                Confidence = Confidence.High,
                ReleaseType = release.ReleaseGroup?.DescribeType(),
                TrackCredits = trackCredits,
                ReleaseTitle = release.Title,
                MatchedTracks = matches.Count
            };
        }

        if (_groups.GetValueOrDefault(album.Id) is { } group)
        {
            return new SourceResult
            {
                SourceName = SourceName,
                Credits = _parser.FromMusicBrainz(group.ArtistCredit),
                Confidence = Confidence.High,
                ReleaseType = group.DescribeType()
            };
        }

        return null;
    }
}

/// <summary>Ce qu'un fournisseur Jellyfin générique renvoie pour un album.</summary>
public sealed record ProviderAlbumResult(IReadOnlyList<string> AlbumArtists, string? AlbumArtistMbid);

/// <summary>
/// Fournisseur de métadonnées Jellyfin quelconque (TheAudioDB, Discogs, VGMdb…), appelé tel quel : on ne garde
/// que ses artistes d'album. La fonction d'appel est fournie par le plugin (elle a besoin de l'élément Jellyfin).
/// </summary>
public sealed class ProviderSource : IAlbumSource
{
    private readonly Func<AlbumSnapshot, CancellationToken, Task<ProviderAlbumResult?>> _fetch;

    public ProviderSource(string name, Func<AlbumSnapshot, CancellationToken, Task<ProviderAlbumResult?>> fetch)
    {
        Name = name;
        _fetch = fetch;
    }

    public string Name { get; }

    public async Task<SourceResult?> ResolveAsync(AlbumSnapshot album, CancellationToken cancellationToken)
    {
        var result = await _fetch(album, cancellationToken).ConfigureAwait(false);
        var names = result?.AlbumArtists.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        return names is { Count: > 0 }
            ? new SourceResult { SourceName = Name, RawAlbumArtists = names, AlbumArtistMbid = result!.AlbumArtistMbid, Confidence = Confidence.Medium }
            : null;
    }
}

/// <summary>Rapproche les pistes locales des pistes d'une release MusicBrainz.</summary>
public static class TrackMatcher
{
    public static Dictionary<string, MbTrack> Match(AlbumSnapshot album, MbRelease release)
    {
        var candidates = (release.Media ?? [])
            .SelectMany(m => (m.Tracks ?? []).Select(t => (Disc: m.Position <= 0 ? 1 : m.Position, Track: t)))
            .ToList();
        var result = new Dictionary<string, MbTrack>(StringComparer.Ordinal);
        if (candidates.Count == 0)
        {
            return result;
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sameCount = candidates.Count == album.Tracks.Count;

        bool Take(TrackSnapshot local, MbTrack? track)
        {
            if (track is null || !used.Add(track.Id))
            {
                return false;
            }

            result[local.Id] = track;
            return true;
        }

        // 1. Identifiants MusicBrainz des tags (piste, puis enregistrement).
        foreach (var local in album.Tracks)
        {
            var trackId = local.GetProviderId(ProviderKeys.MusicBrainzTrack);
            var recordingId = local.GetProviderId(ProviderKeys.MusicBrainzRecording);
            var match = candidates.FirstOrDefault(c => trackId is not null && string.Equals(c.Track.Id, trackId, StringComparison.OrdinalIgnoreCase)).Track
                ?? candidates.FirstOrDefault(c => recordingId is not null && string.Equals(c.Track.Recording?.Id, recordingId, StringComparison.OrdinalIgnoreCase)).Track;
            Take(local, match);
        }

        // 2. Disque + numéro, confirmé par le titre (ou par un nombre de pistes identique).
        foreach (var local in album.Tracks.Where(t => !result.ContainsKey(t.Id) && t.Number is not null))
        {
            var disc = local.Disc ?? 1;
            var match = candidates.FirstOrDefault(c => c.Disc == disc && c.Track.Position == local.Number && !used.Contains(c.Track.Id)).Track;
            if (match is not null && (sameCount || TitlesMatch(local.Name, match.Title)))
            {
                Take(local, match);
            }
        }

        // 3. Titre seul, s'il est unique.
        foreach (var local in album.Tracks.Where(t => !result.ContainsKey(t.Id)))
        {
            var matches = candidates.Where(c => !used.Contains(c.Track.Id) && TitlesMatch(local.Name, c.Track.Title)).ToList();
            if (matches.Count == 1)
            {
                Take(local, matches[0].Track);
            }
        }

        return result;
    }

    /// <summary>Titres équivalents, en ignorant casse, accents, ponctuation et parenthèses (« (feat. X) », « (extended) »).</summary>
    public static bool TitlesMatch(string? a, string? b)
    {
        var x = NormalizeTitle(a);
        var y = NormalizeTitle(b);
        return x.Length > 0 && y.Length > 0 && (x == y || x.StartsWith(y + " ", StringComparison.Ordinal) || y.StartsWith(x + " ", StringComparison.Ordinal));
    }

    private static string NormalizeTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var withoutBrackets = System.Text.RegularExpressions.Regex.Replace(title, @"[\(\[][^\)\]]*[\)\]]", " ");
        return ArtistNames.Key(withoutBrackets);
    }
}
