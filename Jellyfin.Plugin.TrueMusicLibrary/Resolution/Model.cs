using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

// Modèle indépendant de Jellyfin : le plugin le remplit depuis la base, un outil peut le remplir depuis un export
// de l'API. Tout ce qui est dans ce dossier se teste sans serveur.

/// <summary>Photo d'un album (un dossier = un <c>MusicAlbum</c> Jellyfin).</summary>
public sealed class AlbumSnapshot
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Path { get; init; } = string.Empty;

    /// <summary>Artiste du dossier parent, quand l'album est rangé dans un dossier artiste (« Artiste/Album »).</summary>
    public string? FolderArtist { get; init; }

    /// <summary>Id Jellyfin de la fiche artiste du dossier.</summary>
    public string? FolderArtistId { get; init; }

    /// <summary>MBID de la fiche artiste du dossier, si Jellyfin l'a identifiée.</summary>
    public string? FolderArtistMbid { get; init; }

    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    public IReadOnlyList<string> Artists { get; init; } = [];

    /// <summary>Album verrouillé dans Jellyfin (<c>LockData</c>) : on n'y touche pas.</summary>
    public bool IsLocked { get; init; }

    public IReadOnlyList<TrackSnapshot> Tracks { get; init; } = [];

    public string? GetProviderId(string key) => ProviderIds.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>Photo d'une piste, avec les valeurs telles que Jellyfin les a stockées.</summary>
public sealed class TrackSnapshot
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Path { get; init; } = string.Empty;

    public int? Disc { get; init; }

    public int? Number { get; init; }

    public long? RunTimeTicks { get; init; }

    public IReadOnlyList<string> Artists { get; init; } = [];

    public IReadOnlyList<string> AlbumArtists { get; init; } = [];

    public IReadOnlyDictionary<string, string> ProviderIds { get; init; } = new Dictionary<string, string>();

    /// <summary>Piste verrouillée, ou champ « Cast » verrouillé : on n'y touche pas.</summary>
    public bool IsLocked { get; init; }

    public string? GetProviderId(string key) => ProviderIds.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}

/// <summary>Clés des identifiants de fournisseurs, telles que Jellyfin les nomme.</summary>
public static class ProviderKeys
{
    public const string MusicBrainzAlbum = "MusicBrainzAlbum";
    public const string MusicBrainzReleaseGroup = "MusicBrainzReleaseGroup";
    public const string MusicBrainzAlbumArtist = "MusicBrainzAlbumArtist";
    public const string MusicBrainzArtist = "MusicBrainzArtist";
    public const string MusicBrainzTrack = "MusicBrainzTrack";
    public const string MusicBrainzRecording = "MusicBrainzRecording";
}

/// <summary>Rôle d'un crédit : principal (y compris co-crédité d'une collaboration) ou invité.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CreditRole
{
    Main,
    Guest
}

/// <summary>Un artiste crédité, avec son MBID quand la source le donne.</summary>
public sealed record Credit(string Name, CreditRole Role, string? Mbid = null);

/// <summary>Un artiste résolu : nom affiché (canonique) et MBID éventuel.</summary>
public sealed record ArtistRef(string Name, string? Mbid = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionSource
{
    /// <summary>Album verrouillé dans Jellyfin : valeurs actuelles gardées.</summary>
    Locked,

    /// <summary>Choix manuel dans la page du plugin.</summary>
    Override,

    /// <summary>Un fournisseur de métadonnées (MusicBrainz, TheAudioDB, autre) : voir <see cref="AlbumDecision.SourceName"/>.</summary>
    Provider,

    /// <summary>Tags des pistes (artiste d'album majoritaire).</summary>
    Tags,

    /// <summary>Dossier artiste parent.</summary>
    Folder,

    /// <summary>Premier artiste majoritaire des pistes.</summary>
    TrackArtists,

    /// <summary>Rien d'exploitable : valeurs actuelles gardées.</summary>
    None
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Confidence
{
    Low,
    Medium,
    High
}

[Flags]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DecisionFlags
{
    None = 0,
    Compilation = 1,
    Collaboration = 2,
    NoSource = 4,
    Locked = 8,
    Disagreement = 16,
    Override = 32
}

/// <summary>Ce que le plugin propose pour une piste.</summary>
public sealed class TrackDecision
{
    public required string TrackId { get; init; }

    public IReadOnlyList<ArtistRef> AlbumArtists { get; init; } = [];

    public IReadOnlyList<ArtistRef> Artists { get; init; } = [];

    /// <summary>
    /// Crédit de la piste donné par la source (MusicBrainz), avant l'ajout des invités des tags. Gardé pour la
    /// persistance : pendant un scan, les artistes d'une piste = ce crédit + les tags relus, sans appel réseau.
    /// </summary>
    public IReadOnlyList<ArtistRef>? SourceArtists { get; init; }

    /// <summary>« MusicBrainz » (crédit de la piste) ou « Tags » (découpage des tags) ou « Locked ».</summary>
    public string CreditSource { get; init; } = string.Empty;
}

/// <summary>Ce que le plugin propose pour un album.</summary>
public sealed class AlbumDecision
{
    public required string AlbumId { get; init; }

    public IReadOnlyList<ArtistRef> MainArtists { get; set; } = [];

    public DecisionSource Source { get; set; }

    /// <summary>Nom du fournisseur quand <see cref="Source"/> vaut <see cref="DecisionSource.Provider"/>.</summary>
    public string? SourceName { get; set; }

    public Confidence Confidence { get; set; }

    public DecisionFlags Flags { get; set; }

    /// <summary>Type de sortie MusicBrainz (Album, Single, EP…) et types secondaires (Compilation, Soundtrack…).</summary>
    public string? ReleaseType { get; set; }

    /// <summary>Désaccords et remarques, lisibles tels quels dans le rapport.</summary>
    public List<string> Notes { get; } = [];

    public List<TrackDecision> Tracks { get; } = [];

    /// <summary>Empreinte des entrées (ids, tags, dossier, réglages) : si elle change, la décision est à refaire.</summary>
    public string InputHash { get; set; } = string.Empty;
}
