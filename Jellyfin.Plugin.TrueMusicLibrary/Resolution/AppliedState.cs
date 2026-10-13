namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>
/// Décisions en vigueur depuis la dernière réparation, dans <c>applied.json</c>. Les fournisseurs les appliquent
/// pendant les scans et les actualisations, sans réseau ; la tâche d'après scan les met à jour. Elles viennent de l'analyse
/// de contrôle faite juste après l'écriture : leurs empreintes sont celles de la médiathèque réparée.
/// Absent = persistance inactive (jamais réparé, ou restauré).
/// </summary>
public sealed class AppliedState
{
    /// <summary>Première réparation de la série (la date ne change pas quand la tâche d'après scan met à jour).</summary>
    public DateTime ActivatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public string? PluginVersion { get; set; }

    /// <summary>Réglages des décisions (découpage des tags pendant un scan).</summary>
    public ResolverSettings Settings { get; set; } = new();

    /// <summary>Par Id d'album Jellyfin.</summary>
    public Dictionary<string, AppliedAlbum> Albums { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Écritures connues (clé <see cref="ArtistNames.Key"/>) → nom affiché.</summary>
    public Dictionary<string, string> Names { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Clés que le découpeur ne coupe jamais (artistes MusicBrainz, dossiers).</summary>
    public List<string> KnownKeys { get; set; } = [];

    /// <summary>Fiches artiste de dossier principales : Id → nom affiché.</summary>
    public Dictionary<string, string> FolderArtists { get; set; } = new(StringComparer.Ordinal);

    public static AppliedState FromReport(AnalysisReport report, DateTime activatedAt)
    {
        var state = new AppliedState
        {
            ActivatedAt = activatedAt,
            UpdatedAt = DateTime.UtcNow,
            PluginVersion = report.PluginVersion,
            Settings = report.Settings ?? new ResolverSettings()
        };

        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var library in report.Libraries)
        {
            foreach (var (key, name) in library.NameMap)
            {
                state.Names.TryAdd(key, name);
            }

            known.UnionWith(library.KnownKeys);
            foreach (var (id, name) in library.FolderArtistNames)
            {
                state.FolderArtists[id] = name;
            }

            foreach (var album in library.Albums)
            {
                state.Albums[album.Id] = new AppliedAlbum
                {
                    InputHash = album.InputHash,
                    Locked = album.Flags.HasFlag(DecisionFlags.Locked),
                    Mains = album.MainArtists.Select(m => m.Name).ToList(),
                    Tracks = album.Tracks.ToDictionary(
                        t => t.Id,
                        t => new AppliedTrack
                        {
                            Artists = t.Artists.ToList(),
                            AlbumArtists = t.AlbumArtists.ToList(),
                            SourceArtists = t.SourceArtists?.ToList(),
                            Locked = t.CreditSource == "Locked"
                        },
                        StringComparer.Ordinal)
                };
            }
        }

        state.KnownKeys = known.Order(StringComparer.Ordinal).ToList();
        return state;
    }
}

public sealed class AppliedAlbum
{
    public string InputHash { get; set; } = string.Empty;

    /// <summary>Album verrouillé au moment de la décision : les fournisseurs n'y touchent pas.</summary>
    public bool Locked { get; set; }

    public List<string> Mains { get; set; } = [];

    /// <summary>Par Id de piste Jellyfin.</summary>
    public Dictionary<string, AppliedTrack> Tracks { get; set; } = new(StringComparer.Ordinal);
}

public sealed class AppliedTrack
{
    public List<string> Artists { get; set; } = [];

    public List<string> AlbumArtists { get; set; } = [];

    /// <summary>Crédit MusicBrainz de la piste, avant les invités des tags. <c>null</c> : crédit pris dans les tags.</summary>
    public List<string>? SourceArtists { get; set; }

    public bool Locked { get; set; }
}

/// <summary>Artistes voulus pour une piste.</summary>
public sealed record TrackTarget(IReadOnlyList<string> Artists, IReadOnlyList<string> AlbumArtists, bool Known);

/// <summary>
/// Les règles de la persistance, sans Jellyfin ni réseau : ce que les fournisseurs appliquent pendant un scan. Elles
/// donnent le même résultat que l'analyse (§4.4) :
/// <list type="bullet">
/// <item>piste connue : artistes = crédit MusicBrainz enregistré + tags relus découpés, artistes d'album = principaux de l'album ;</item>
/// <item>nouvelle piste d'un album connu : tags découpés, principaux de l'album ;</item>
/// <item>album inconnu (nouveau) : décision provisoire tirée des tags et du dossier, corrigée par la tâche d'après scan.</item>
/// </list>
/// Appliquées à des valeurs déjà réparées, elles les rendent telles quelles.
/// </summary>
public sealed class LiveRules
{
    private readonly CreditParser _parser;
    private readonly Dictionary<string, (AppliedAlbum Album, AppliedTrack Track)> _tracks = new(StringComparer.Ordinal);

    public LiveRules(AppliedState state)
    {
        State = state;
        var known = state.KnownKeys.ToHashSet(StringComparer.Ordinal);
        _parser = new CreditParser(state.Settings, known.Contains);
        foreach (var album in state.Albums.Values)
        {
            foreach (var (id, track) in album.Tracks)
            {
                _tracks[id] = (album, track);
            }
        }
    }

    public AppliedState State { get; }

    /// <summary>Nom affiché d'une écriture (elle-même si elle est inconnue).</summary>
    public string Name(string raw)
        => State.Names.TryGetValue(ArtistNames.Key(raw), out var name) ? name : raw.Trim();

    public bool IsKnownTrack(string trackId) => _tracks.ContainsKey(trackId);

    /// <summary>Artistes voulus pour une piste, ou <c>null</c> si elle (ou son album) est verrouillée dans la décision.</summary>
    /// <param name="trackId">Id Jellyfin de la piste.</param>
    /// <param name="albumId">Id de son album, s'il y en a un.</param>
    /// <param name="artists">Artistes actuels (ceux que la sonde vient de lire, ou ceux déjà réparés).</param>
    /// <param name="albumArtists">Artistes d'album actuels.</param>
    /// <param name="folderArtist">Artiste du dossier parent de l'album, s'il y en a un.</param>
    public TrackTarget? ForTrack(string trackId, string? albumId, IReadOnlyList<string> artists, IReadOnlyList<string> albumArtists, string? folderArtist)
    {
        AppliedAlbum? album = null;
        AppliedTrack? track = null;
        if (_tracks.TryGetValue(trackId, out var known))
        {
            (album, track) = known;
        }
        else if (albumId is not null)
        {
            album = State.Albums.GetValueOrDefault(albumId);
        }

        if (album?.Locked == true || track?.Locked == true)
        {
            return null;
        }

        var tagNames = Names(_parser.ParseTagValues(artists).Select(c => c.Name));
        var mains = album?.Mains ?? ProvisionalMains(albumArtists, folderArtist, tagNames);

        var result = new List<string>();
        Add(result, track?.SourceArtists ?? []);
        Add(result, tagNames);
        if (result.Count == 0)
        {
            result.AddRange(mains);
        }

        return new TrackTarget(result, mains, track is not null);
    }

    /// <summary>Artistes principaux voulus pour un album connu et pas verrouillé, sinon <c>null</c>.</summary>
    public IReadOnlyList<string>? ForAlbum(string albumId)
        => State.Albums.TryGetValue(albumId, out var album) && !album.Locked ? album.Mains : null;

    /// <summary>Nom voulu pour une fiche artiste de dossier principale, sinon <c>null</c>.</summary>
    public string? ForFolderArtist(string artistId) => State.FolderArtists.GetValueOrDefault(artistId);

    /// <summary>
    /// Décision provisoire d'un album inconnu (nouvel album) : artistes d'album des tags découpés, compilation
    /// → réglage des compilations, sinon dossier, sinon premier artiste de la piste. La tâche d'après scan la remplace.
    /// </summary>
    private List<string> ProvisionalMains(IReadOnlyList<string> albumArtists, string? folderArtist, IReadOnlyList<string> tagNames)
    {
        var mains = CreditParser.MainCredits(_parser.ParseTagValues(albumArtists), State.Settings.Collaboration);
        if (mains.Count > 0 && mains.All(c => ArtistNames.IsVariousArtists(c.Name, c.Mbid)))
        {
            return State.Settings.Compilation switch
            {
                CompilationMode.FolderArtist when folderArtist is not null => [Name(folderArtist)],
                CompilationMode.None => [],
                _ => [State.Settings.VariousArtistsName]
            };
        }

        if (mains.Count > 0)
        {
            return Names(mains.Select(c => c.Name));
        }

        if (folderArtist is not null)
        {
            return [Name(folderArtist)];
        }

        return tagNames.Take(1).ToList();
    }

    private List<string> Names(IEnumerable<string> raw)
    {
        var names = new List<string>();
        Add(names, raw.Select(Name));
        return names;
    }

    /// <summary>Ajoute les noms absents (même clé = même artiste, comme dans l'analyse).</summary>
    private static void Add(List<string> names, IEnumerable<string> more)
    {
        foreach (var name in more)
        {
            var key = ArtistNames.Key(name);
            if (key.Length > 0 && !names.Any(n => ArtistNames.Key(n) == key))
            {
                names.Add(name);
            }
        }
    }
}
