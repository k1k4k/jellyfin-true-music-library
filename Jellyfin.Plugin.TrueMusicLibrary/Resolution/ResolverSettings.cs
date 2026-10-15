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
    /// <summary>Nom MusicBrainz (écriture des noms non latins : <see cref="NonLatinNameMode"/>).</summary>
    MusicBrainz,

    /// <summary>Nom du dossier quand il existe.</summary>
    Folder,

    /// <summary>Écriture la plus fréquente dans les tags.</summary>
    Tag
}

/// <summary>Noms MusicBrainz en écriture non latine (japonais, chinois, cyrillique…).</summary>
public enum NonLatinNameMode
{
    /// <summary>Transcrits en caractères latins : nom crédité ou alias MusicBrainz.</summary>
    Latin,

    /// <summary>Écriture d'origine.</summary>
    Original,

    /// <summary>Écriture d'origine pour les artistes de <see cref="ResolverSettings.OriginalScriptArtists"/>, transcrits pour les autres.</summary>
    OriginalListed
}

/// <summary>Sources des artistes principaux d'un album, dans l'ordre réglé.</summary>
public enum SourceKind
{
    /// <summary>Fournisseurs de métadonnées de la bibliothèque, dans leur ordre (MusicBrainz, TheAudioDB…).</summary>
    Providers,

    /// <summary>Tags des fichiers (artistes d'album).</summary>
    Tags,

    /// <summary>Dossier artiste (« Artiste/Album »).</summary>
    Folder
}

/// <summary>Réglages qui influencent les décisions.</summary>
public sealed class ResolverSettings
{
    public static readonly IReadOnlyList<SourceKind> DefaultSourceOrder = [SourceKind.Providers, SourceKind.Tags, SourceKind.Folder];

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

    public NonLatinNameMode NonLatinNames { get; init; } = NonLatinNameMode.Latin;

    /// <summary>Artistes qui gardent leur écriture d'origine avec <see cref="NonLatinNameMode.OriginalListed"/> (n'importe quelle écriture).</summary>
    public IReadOnlyList<string> OriginalScriptArtists { get; init; } = [];

    /// <summary>Ordre des sources ; les sources absentes viennent ensuite, dans l'ordre par défaut.</summary>
    public IReadOnlyList<SourceKind> SourceOrder { get; init; } = DefaultSourceOrder;

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

        // Réglages ajoutés en 0.1.2 : comptés seulement s'ils ne sont pas à leur valeur par défaut, pour que les empreintes
        // d'avant restent valables (sinon la tâche d'après scan résoudrait de nouveau toute la médiathèque après la mise à jour).
        if (NonLatinNames != NonLatinNameMode.Latin)
        {
            text += "\n" + NonLatinNames + "\n" + string.Join("|", OriginalScriptArtists);
        }

        if (!EffectiveSourceOrder().SequenceEqual(DefaultSourceOrder))
        {
            text += "\nsources:" + string.Join(",", EffectiveSourceOrder());
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];
    }

    /// <summary>Ordre des sources complété : chaque source une fois, les absentes à la fin dans l'ordre par défaut.</summary>
    public IReadOnlyList<SourceKind> EffectiveSourceOrder()
        => (SourceOrder ?? []).Concat(DefaultSourceOrder).Distinct().ToList();

    /// <summary>Lit un ordre des sources écrit « Providers,Tags,Folder » ; les noms inconnus sont ignorés.</summary>
    public static IReadOnlyList<SourceKind> ParseSourceOrder(string? text)
        => (text ?? string.Empty)
            .Split([',', ';', ' ', '>'], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => Enum.TryParse<SourceKind>(p, true, out var kind) ? (SourceKind?)kind : null)
            .OfType<SourceKind>()
            .Concat(DefaultSourceOrder)
            .Distinct()
            .ToList();
}
