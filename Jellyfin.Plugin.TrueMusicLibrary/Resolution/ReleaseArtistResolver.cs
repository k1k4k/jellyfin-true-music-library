using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>Choix manuel d'un admin pour un album (page du plugin).</summary>
public sealed class OverrideEntry
{
    public List<string> MainArtists { get; set; } = [];

    public string? Note { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Décide les artistes principaux d'un album et les artistes de ses pistes : override ou verrou, puis les sources dans l'ordre
/// réglé (par défaut fournisseurs dans l'ordre de la bibliothèque → tags → dossier), puis l'artiste majoritaire des pistes.
/// </summary>
public sealed class ReleaseArtistResolver
{
    private readonly ResolverSettings _settings;
    private readonly ArtistIdentity _identity;
    private readonly CreditParser _parser;
    private readonly IReadOnlyList<IAlbumSource> _sources;
    private readonly IReadOnlyDictionary<string, OverrideEntry> _overrides;

    public ReleaseArtistResolver(
        ResolverSettings settings,
        ArtistIdentity identity,
        CreditParser parser,
        IReadOnlyList<IAlbumSource> sources,
        IReadOnlyDictionary<string, OverrideEntry>? overrides = null)
    {
        _settings = settings;
        _identity = identity;
        _parser = parser;
        _sources = sources;
        _overrides = overrides ?? new Dictionary<string, OverrideEntry>();
    }

    public async Task<AlbumDecision> ResolveAsync(AlbumSnapshot album, CancellationToken cancellationToken)
    {
        var decision = new AlbumDecision { AlbumId = album.Id, InputHash = InputHash(album) };

        if (album.IsLocked)
        {
            decision.Source = DecisionSource.Locked;
            decision.Flags |= DecisionFlags.Locked;
            decision.Confidence = Confidence.High;
            decision.MainArtists = album.AlbumArtists.Select(n => new ArtistRef(n)).ToList();
            decision.Notes.Add(Messages.AlbumLocked);
            foreach (var track in album.Tracks)
            {
                decision.Tracks.Add(Unchanged(track));
            }

            return decision;
        }

        var tags = AnalyzeTags(album);
        IReadOnlyDictionary<string, IReadOnlyList<Credit>>? trackCredits = null;
        List<ArtistRef>? mains = null;
        ProviderPick? provider = null;

        if (_overrides.TryGetValue(album.Id, out var manual) && manual.MainArtists.Count > 0)
        {
            mains = manual.MainArtists.Select(n => _identity.Resolve(n)).ToList();
            decision.Source = DecisionSource.Override;
            decision.Flags |= DecisionFlags.Override;
            decision.Confidence = Confidence.High;
            if (!string.IsNullOrWhiteSpace(manual.Note))
            {
                decision.Notes.Add(Messages.Override(manual.Note));
            }
        }

        foreach (var source in _sources)
        {
            var result = await source.ResolveAsync(album, cancellationToken).ConfigureAwait(false);
            if (result is null)
            {
                continue;
            }

            var credits = result.Credits.Count > 0 ? result.Credits : _parser.ParseTagValues(result.RawAlbumArtists);
            if (result.AlbumArtistMbid is not null && credits.Count == 1)
            {
                credits = [credits[0] with { Mbid = result.AlbumArtistMbid }];
            }

            var albumMains = CreditParser.MainCredits(credits, _settings.Collaboration);
            if (IsUnrelatedRelease(album, result, albumMains, tags))
            {
                // Release identifiée par recherche de nom (fournisseur MusicBrainz de Jellyfin) qui ne correspond pas :
                // ex. dossier compilation pris pour le single d'un de ses artistes. On l'écarte et on le dit.
                decision.Notes.Add(Messages.ReleaseRejected(result.SourceName, result.ReleaseTitle, result.MatchedTracks, album.Tracks.Count));
                decision.Flags |= DecisionFlags.Disagreement;
                continue;
            }

            if (result.TrackCredits.Count > 0)
            {
                trackCredits = result.TrackCredits;
            }

            decision.ReleaseType ??= result.ReleaseType;
            if (mains is null)
            {
                if (albumMains.Count == 0)
                {
                    continue;
                }

                // Retenu même si une autre source passe avant : ses crédits de pistes servent toujours.
                provider = new ProviderPick(result, albumMains);
            }

            break;
        }

        foreach (var step in _settings.EffectiveSourceOrder())
        {
            if (mains is not null)
            {
                break;
            }

            mains = step switch
            {
                SourceKind.Providers => FromProvider(album, decision, provider),
                SourceKind.Tags => FromTags(album, decision, tags),
                _ => FromFolder(album, decision)
            };
        }

        if (mains is null)
        {
            var majority = TrackMajority(album);
            if (majority is not null)
            {
                decision.Source = DecisionSource.TrackArtists;
                decision.Confidence = Confidence.Low;
                mains = [_identity.Resolve(majority)];
            }
        }

        if (mains is null)
        {
            decision.Source = DecisionSource.None;
            decision.Confidence = Confidence.Low;
            decision.Flags |= DecisionFlags.NoSource;
            decision.Notes.Add(Messages.NoSource);
            mains = album.AlbumArtists.Select(n => _identity.Resolve(n)).ToList();
        }

        decision.MainArtists = mains;
        if (mains.Count > 1)
        {
            decision.Flags |= DecisionFlags.Collaboration;
        }

        AddDisagreements(album, decision, tags, provider);

        foreach (var track in album.Tracks)
        {
            if (track.IsLocked)
            {
                decision.Tracks.Add(Unchanged(track));
                continue;
            }

            var tagCredits = _parser.ParseTagValues(track.Artists);
            var creditSource = "Tags";
            var artists = new List<ArtistRef>();
            List<ArtistRef>? sourceArtists = null;
            if (trackCredits is not null && trackCredits.TryGetValue(track.Id, out var mbCredits))
            {
                // Crédit MusicBrainz d'abord (noms et rôles exacts), puis les invités des tags qu'il n'a pas :
                // aucun invité ne disparaît (ex. un invité présent dans les tags mais absent du crédit MusicBrainz).
                creditSource = MusicBrainzSource.SourceName;
                AddArtists(artists, mbCredits);
                sourceArtists = artists.ToList();
                var before = artists.Count;
                AddArtists(artists, tagCredits);
                if (artists.Count > before)
                {
                    creditSource = MusicBrainzSource.SourceName + "+Tags";
                }
            }
            else
            {
                AddArtists(artists, tagCredits);
            }

            decision.Tracks.Add(new TrackDecision
            {
                TrackId = track.Id,
                AlbumArtists = mains,
                Artists = artists.Count > 0 ? artists : mains.ToList(),
                SourceArtists = sourceArtists,
                CreditSource = creditSource
            });
        }

        return decision;
    }

    /// <summary>Empreinte des entrées d'une décision.</summary>
    public string InputHash(AlbumSnapshot album) => ComputeInputHash(_settings, _overrides, album);

    /// <summary>
    /// Empreinte des entrées d'une décision : réglages, dossier, verrou, identifiants, override, et pour chaque piste ses
    /// numéros, ses artistes et ses identifiants MusicBrainz. La tâche d'après scan la recalcule pour trouver les albums
    /// à résoudre de nouveau.
    /// </summary>
    public static string ComputeInputHash(ResolverSettings settings, IReadOnlyDictionary<string, OverrideEntry> overrides, AlbumSnapshot album)
    {
        var sb = new StringBuilder();
        sb.Append(settings.Hash()).Append('\n');
        sb.Append(album.FolderArtist).Append('|').Append(album.FolderArtistMbid).Append('|').Append(album.IsLocked).Append('\n');
        foreach (var (k, v) in album.ProviderIds.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            sb.Append(k).Append('=').Append(v).Append(';');
        }

        if (overrides.TryGetValue(album.Id, out var manual))
        {
            sb.Append("\noverride:").Append(string.Join("|", manual.MainArtists));
        }

        foreach (var t in album.Tracks.OrderBy(t => t.Id, StringComparer.Ordinal))
        {
            sb.Append('\n').Append(t.Id).Append('|').Append(t.Disc).Append('|').Append(t.Number).Append('|').Append(t.IsLocked)
                .Append('|').Append(string.Join(";", t.Artists)).Append('|').Append(string.Join(";", t.AlbumArtists))
                .Append('|').Append(t.GetProviderId(ProviderKeys.MusicBrainzTrack)).Append('|').Append(t.GetProviderId(ProviderKeys.MusicBrainzRecording));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))[..16];
    }

    private List<ArtistRef> CompilationArtists(AlbumSnapshot album)
        => _settings.Compilation switch
        {
            CompilationMode.FolderArtist when album.FolderArtist is not null => [_identity.Resolve(album.FolderArtist, album.FolderArtistMbid)],
            CompilationMode.None => [],
            _ => [new ArtistRef(_settings.VariousArtistsName, ArtistNames.VariousArtistsMbid)]
        };

    private List<ArtistRef>? FromProvider(AlbumSnapshot album, AlbumDecision decision, ProviderPick? provider)
    {
        if (provider is null)
        {
            return null;
        }

        decision.Source = DecisionSource.Provider;
        decision.SourceName = provider.Result.SourceName;
        decision.Confidence = provider.Result.Confidence;
        if (provider.Mains.All(c => ArtistNames.IsVariousArtists(c.Name, c.Mbid)))
        {
            decision.Flags |= DecisionFlags.Compilation;
            return CompilationArtists(album);
        }

        return provider.Mains.Select(_identity.Resolve).ToList();
    }

    private List<ArtistRef>? FromTags(AlbumSnapshot album, AlbumDecision decision, TagEvidence tags)
    {
        if (tags.Mains is { Count: > 0 })
        {
            decision.Source = DecisionSource.Tags;
            decision.Confidence = Confidence.Medium;
            if (tags.Mains.All(c => ArtistNames.IsVariousArtists(c.Name, c.Mbid)))
            {
                decision.Flags |= DecisionFlags.Compilation;
                return CompilationArtists(album);
            }

            return tags.Mains.Select(_identity.Resolve).ToList();
        }

        if (tags.Heterogeneous)
        {
            // Pistes aux artistes d'album tous différents : dossier compilation / playlist.
            decision.Flags |= DecisionFlags.Compilation;
            decision.Source = album.FolderArtist is not null && _settings.Compilation == CompilationMode.FolderArtist ? DecisionSource.Folder : DecisionSource.Tags;
            decision.Confidence = Confidence.Medium;
            return CompilationArtists(album);
        }

        return null;
    }

    private List<ArtistRef>? FromFolder(AlbumSnapshot album, AlbumDecision decision)
    {
        if (album.FolderArtist is null)
        {
            return null;
        }

        decision.Source = DecisionSource.Folder;
        decision.Confidence = Confidence.Medium;
        return [_identity.Resolve(album.FolderArtist, album.FolderArtistMbid)];
    }

    private TagEvidence AnalyzeTags(AlbumSnapshot album)
    {
        var votes = new Dictionary<string, (IReadOnlyList<Credit> Mains, int Count)>(StringComparer.Ordinal);
        var mbidVotes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var voted = 0;
        foreach (var track in album.Tracks)
        {
            var mains = CreditParser.MainCredits(_parser.ParseTagValues(track.AlbumArtists), _settings.Collaboration);
            if (mains.Count == 0)
            {
                continue;
            }

            voted++;
            var key = string.Join("|", mains.Select(m => ArtistNames.Key(m.Name)).Order(StringComparer.Ordinal));
            votes[key] = votes.TryGetValue(key, out var v) ? (v.Mains, v.Count + 1) : (mains, 1);
            if (track.GetProviderId(ProviderKeys.MusicBrainzAlbumArtist) is { } mbid)
            {
                mbidVotes[mbid] = mbidVotes.GetValueOrDefault(mbid) + 1;
            }
        }

        if (voted == 0)
        {
            return new TagEvidence(null, false);
        }

        var top = votes.Values.OrderByDescending(v => v.Count).First();
        if (top.Count * 2 <= voted && votes.Count > 1)
        {
            return new TagEvidence(null, true);
        }

        var result = top.Mains;
        if (result.Count == 1 && mbidVotes.Count > 0)
        {
            var mbid = mbidVotes.OrderByDescending(m => m.Value).First();
            if (mbid.Value * 2 > voted)
            {
                result = [result[0] with { Mbid = mbid.Key }];
            }
        }

        return new TagEvidence(result, false);
    }

    private string? TrackMajority(AlbumSnapshot album)
        => album.Tracks
            .Select(t => _parser.ParseTagValues(t.Artists).FirstOrDefault()?.Name)
            .Where(n => n is not null)
            .GroupBy(n => ArtistNames.Key(n), StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .Select(g => g.First())
            .FirstOrDefault();

    private void AddDisagreements(AlbumSnapshot album, AlbumDecision decision, TagEvidence tags, ProviderPick? provider)
    {
        var compilation = decision.Flags.HasFlag(DecisionFlags.Compilation);
        if (!compilation && album.FolderArtist is not null && decision.Source is not (DecisionSource.Folder or DecisionSource.Override)
            && !decision.MainArtists.Any(m => SameArtist(m, album.FolderArtist, album.FolderArtistMbid)))
        {
            decision.Notes.Add(Messages.FolderDisagrees(album.FolderArtist));
            decision.Flags |= DecisionFlags.Disagreement;
        }

        if (decision.Source is DecisionSource.Provider or DecisionSource.Folder && tags.Mains is { Count: > 0 } && !compilation
            && !tags.Mains.All(t => decision.MainArtists.Any(m => SameArtist(m, t.Name, t.Mbid))))
        {
            decision.Notes.Add(Messages.TagsDisagree(string.Join(", ", tags.Mains.Select(t => t.Name))));
            decision.Flags |= DecisionFlags.Disagreement;
        }

        // Fournisseur passé après les tags ou le dossier (ordre des sources) et en désaccord : on le dit.
        if (decision.Source is DecisionSource.Tags or DecisionSource.Folder && provider is not null && !compilation
            && !provider.Mains.All(c => ArtistNames.IsVariousArtists(c.Name, c.Mbid))
            && !provider.Mains.All(c => decision.MainArtists.Any(m => SameArtist(m, c.Name, c.Mbid))))
        {
            decision.Notes.Add(Messages.ProviderDisagrees(provider.Result.SourceName, string.Join(", ", provider.Mains.Select(c => _identity.Resolve(c).Name))));
            decision.Flags |= DecisionFlags.Disagreement;
        }
    }

    /// <summary>
    /// Release qui ne reconnaît pas la moitié des pistes locales ET dont l'artiste ne correspond ni au dossier ni aux
    /// tags : c'est une mauvaise identification, pas une source.
    /// </summary>
    private bool IsUnrelatedRelease(AlbumSnapshot album, SourceResult result, IReadOnlyList<Credit> albumMains, TagEvidence tags)
    {
        if (result.MatchedTracks is not int matched || album.Tracks.Count == 0 || matched * 2 >= album.Tracks.Count)
        {
            return false;
        }

        var mains = albumMains.Select(_identity.Resolve).ToList();
        var matchesFolder = album.FolderArtist is not null && mains.Any(m => SameArtist(m, album.FolderArtist, album.FolderArtistMbid));
        var matchesTags = tags.Mains is not null && tags.Mains.Any(t => mains.Any(m => SameArtist(m, t.Name, t.Mbid)));
        return !matchesFolder && !matchesTags;
    }

    private bool SameArtist(ArtistRef artist, string otherName, string? otherMbid)
    {
        otherMbid ??= _identity.MbidForName(otherName);
        if (artist.Mbid is not null && otherMbid is not null)
        {
            return string.Equals(artist.Mbid, otherMbid, StringComparison.OrdinalIgnoreCase);
        }

        var key = ArtistNames.Key(artist.Name);
        return key == ArtistNames.Key(otherName) || key == ArtistNames.Key(_identity.Resolve(otherName, otherMbid).Name);
    }

    /// <summary>Ajoute les crédits résolus qui ne sont pas déjà là (même MBID ou même clé de nom).</summary>
    private void AddArtists(List<ArtistRef> artists, IEnumerable<Credit> credits)
    {
        foreach (var credit in credits)
        {
            var artist = _identity.Resolve(credit);
            var key = ArtistNames.Key(artist.Name);
            if (!artists.Any(a => ArtistNames.Key(a.Name) == key || (a.Mbid is not null && string.Equals(a.Mbid, artist.Mbid, StringComparison.OrdinalIgnoreCase))))
            {
                artists.Add(artist);
            }
        }
    }

    private static TrackDecision Unchanged(TrackSnapshot track) => new()
    {
        TrackId = track.Id,
        AlbumArtists = track.AlbumArtists.Select(n => new ArtistRef(n)).ToList(),
        Artists = track.Artists.Select(n => new ArtistRef(n)).ToList(),
        CreditSource = "Locked"
    };

    private sealed record TagEvidence(IReadOnlyList<Credit>? Mains, bool Heterogeneous);

    /// <summary>Premier fournisseur qui donne des artistes d'album (release acceptée).</summary>
    private sealed record ProviderPick(SourceResult Result, IReadOnlyList<Credit> Mains);
}
