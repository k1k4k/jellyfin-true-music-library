using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>Une fiche artiste telle qu'elle existe aujourd'hui dans Jellyfin.</summary>
public sealed class ArtistEntity
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string? Mbid { get; init; }

    /// <summary>Fiche venant d'un dossier de la bibliothèque (et non créée « par nom »).</summary>
    public bool IsFolder { get; init; }

    /// <summary>Nombre d'utilisateurs qui l'ont en favori.</summary>
    public int Favorites { get; init; }

    public bool Played { get; init; }
}

/// <summary>État actuel d'une bibliothèque, pour le résumé « avant / après ».</summary>
public sealed class CurrentState
{
    /// <summary>Contenu actuel de l'onglet « Artistes » (<c>/Artists</c>).</summary>
    public IReadOnlyList<ArtistEntity> ArtistsTab { get; init; } = [];

    /// <summary>Contenu actuel de l'onglet « Artistes de l'album » (<c>/Artists/AlbumArtists</c>).</summary>
    public IReadOnlyList<ArtistEntity> AlbumArtistsTab { get; init; } = [];

    /// <summary>Fiches artiste venant des dossiers de la bibliothèque.</summary>
    public IReadOnlyList<ArtistEntity> FolderArtists { get; init; } = [];
}

/// <summary>Entrée de l'analyse d'une bibliothèque.</summary>
public sealed class LibraryInput
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Dossier racine, pour afficher des chemins relatifs.</summary>
    public string? Path { get; init; }

    public IReadOnlyList<AlbumSnapshot> Albums { get; init; } = [];

    public CurrentState Current { get; init; } = new();

    /// <summary>Fournisseurs d'album activés, dans l'ordre configuré (ex. « MusicBrainz », « TheAudioDB »).</summary>
    public IReadOnlyList<string> Fetchers { get; init; } = [];

    /// <summary>Sources autres que MusicBrainz, par nom de fournisseur.</summary>
    public IReadOnlyDictionary<string, IAlbumSource> ProviderSources { get; init; } = new Dictionary<string, IAlbumSource>();

    /// <summary>Langue de métadonnées de la bibliothèque (choix des alias).</summary>
    public string? PreferredLanguage { get; init; }
}

public sealed class AnalysisReport
{
    public DateTime GeneratedAt { get; init; }

    public string? PluginVersion { get; init; }

    public string? Target { get; init; }

    public ResolverSettings? Settings { get; init; }

    public MusicBrainz.MusicBrainzStats? MusicBrainz { get; init; }

    public List<LibraryReport> Libraries { get; init; } = [];
}

public sealed class LibraryReport
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public LibrarySummary Summary { get; init; } = new();

    public List<AlbumReport> Albums { get; init; } = [];

    /// <summary>Écritures connues → nom affiché (<see cref="ArtistIdentity.NameMap"/>). Pour la persistance, pas dans le rapport.</summary>
    [JsonIgnore]
    public Dictionary<string, string> NameMap { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Clés protégées du découpage (<see cref="ArtistIdentity.KnownKeys"/>). Pour la persistance, pas dans le rapport.</summary>
    [JsonIgnore]
    public HashSet<string> KnownKeys { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Fiches artiste de dossier qui sont des artistes principaux : Id → nom affiché voulu, y compris celles déjà au bon
    /// nom. Le fournisseur des fiches artiste remet ce nom si une actualisation le change. Pas dans le rapport.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string> FolderArtistNames { get; set; } = new(StringComparer.Ordinal);
}

public sealed class LibrarySummary
{
    public int Albums { get; set; }

    public int Tracks { get; set; }

    public int CurrentArtistsTab { get; set; }

    public int CurrentAlbumArtistsTab { get; set; }

    public List<string> Fetchers { get; set; } = [];

    public List<string> Notes { get; set; } = [];

    public List<ProposedArtist> ProposedArtists { get; set; } = [];

    /// <summary>
    /// Fiches qui quittent la liste : « Hidden » (invité, fiche gardée), « Merged » (même artiste sous un autre nom,
    /// fusionné dans <see cref="RemovedArtist.Into"/>) ou « Deleted » (plus référencée : chaîne parasite).
    /// </summary>
    public List<RemovedArtist> Removed { get; set; } = [];

    public List<RenamedArtist> Renames { get; set; } = [];

    public Dictionary<string, int> BySource { get; set; } = [];

    public Dictionary<string, int> ByFlag { get; set; } = [];

    public int ChangedAlbums { get; set; }

    public int ChangedTracks { get; set; }
}

public sealed record ProposedArtist(string Name, string? Mbid, int Albums);

public sealed record RemovedArtist(string Id, string Name, string Fate, int Favorites, bool Played, string? Into = null);

public sealed record RenamedArtist(string Id, string From, string To);

public sealed class AlbumReport
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Path { get; init; } = string.Empty;

    public IReadOnlyList<string> CurrentAlbumArtists { get; init; } = [];

    public IReadOnlyList<ArtistRef> MainArtists { get; init; } = [];

    public DecisionSource Source { get; init; }

    public string? SourceName { get; init; }

    public Confidence Confidence { get; init; }

    public DecisionFlags Flags { get; init; }

    public string? ReleaseType { get; init; }

    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool Changed { get; init; }

    public string InputHash { get; init; } = string.Empty;

    public List<TrackReport> Tracks { get; init; } = [];
}

public sealed class TrackReport
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public int? Disc { get; init; }

    public int? Number { get; init; }

    public IReadOnlyList<string> CurrentArtists { get; init; } = [];

    public IReadOnlyList<string> CurrentAlbumArtists { get; init; } = [];

    public IReadOnlyList<string> Artists { get; init; } = [];

    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    /// <summary>Crédit de la piste donné par MusicBrainz, avant l'ajout des invités des tags (<see cref="TrackDecision.SourceArtists"/>).</summary>
    public IReadOnlyList<string>? SourceArtists { get; init; }

    public string CreditSource { get; init; } = string.Empty;

    public bool Changed { get; init; }
}
