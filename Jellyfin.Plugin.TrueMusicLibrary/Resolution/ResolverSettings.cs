using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>Collaborations « A &amp; B ».</summary>
public enum CollaborationMode
{
    /// <summary>Tous les crédités principaux sont artistes principaux : l'album apparaît chez chacun.</summary>
    Both,

    /// <summary>Seul le premier crédité est principal ; les autres passent invités.</summary>
    FirstOnly
}

/// <summary>Compilations, BO, playlists.</summary>
public enum CompilationMode
{
    /// <summary>Artiste du dossier, sinon « Artistes variés ».</summary>
    FolderArtist,

    /// <summary>Toujours « Artistes variés ».</summary>
    VariousArtists,

    /// <summary>La sortie n'ajoute aucun artiste à la liste.</summary>
    None
}

/// <summary>Écriture des noms.</summary>
public enum DisplayNameMode
{
    /// <summary>Nom MusicBrainz, alias latin si besoin.</summary>
    MusicBrainz,

    /// <summary>Nom du dossier quand il existe.</summary>
    Folder,

    /// <summary>Écriture la plus fréquente dans les tags.</summary>
    Tag
}

/// <summary>Réglages qui influencent les décisions.</summary>
public sealed class ResolverSettings
{
    public static readonly IReadOnlyList<string> DefaultGuestJoinPhrases = ["feat.", "ft.", "featuring", "with", "présente"];

    public static readonly IReadOnlyList<string> DefaultNameWhitelist =
    [
        "AC/DC", "Earth, Wind & Fire", "Simon & Garfunkel", "Crosby, Stills, Nash & Young", "Crosby, Stills & Nash",
        "Emerson, Lake & Palmer", "Peter, Paul and Mary", "Hall & Oates", "Daryl Hall & John Oates", "Brooks & Dunn",
        "Kool & the Gang", "Iron & Wine", "Mumford & Sons", "Of Monsters and Men", "Angus & Julia Stone",
        "Tyler, the Creator", "Earth Wind & Fire", "Sly & the Family Stone", "Belle and Sebastian", "Above & Beyond"
    ];

    public CollaborationMode Collaboration { get; init; } = CollaborationMode.Both;

    public CompilationMode Compilation { get; init; } = CompilationMode.FolderArtist;

    public DisplayNameMode DisplayName { get; init; } = DisplayNameMode.MusicBrainz;

    public IReadOnlyList<string> GuestJoinPhrases { get; init; } = DefaultGuestJoinPhrases;

    public IReadOnlyList<string> NameWhitelist { get; init; } = DefaultNameWhitelist;

    /// <summary>Langue des alias MusicBrainz préférés (langue de métadonnées de la bibliothèque).</summary>
    public string PreferredLanguage { get; init; } = "fr";

    /// <summary>Nom affiché des compilations sans dossier artiste.</summary>
    public string VariousArtistsName { get; init; } = "Artistes variés";

    /// <summary>Empreinte stable des réglages, pour l'empreinte des décisions.</summary>
    public string Hash()
    {
        var text = string.Join(
            "\n",
            Collaboration,
            Compilation,
            DisplayName,
            string.Join("|", GuestJoinPhrases),
            string.Join("|", NameWhitelist),
            PreferredLanguage,
            VariousArtistsName);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }
}
