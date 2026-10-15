using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TrueMusicLibrary.Configuration;

/// <summary>Configuration du plugin (Tableau de bord → Plugins → True Music Library).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Onglet unique côté serveur : <c>GET /Artists</c> est servi comme <c>GET /Artists/AlbumArtists</c>,
    /// pour tous les clients, une fois la médiathèque réparée. Pris en compte immédiatement, sans redémarrage.
    /// </summary>
    public bool EnableArtistsRouteRewrite { get; set; } = true;

    /// <summary>
    /// Une fois la médiathèque réparée, les fiches artiste qui ne sont artiste principal d'aucune sortie (les invités) sont
    /// retirées des résultats de la recherche (<c>/Search/Hints</c>) et des listes <c>/Items</c>. Leur page reste accessible.
    /// </summary>
    public bool HideGuestArtists { get; set; } = true;

    /// <summary>Interface web : cache l'onglet « Artistes de l'album » des bibliothèques musique, une fois la médiathèque réparée.</summary>
    public bool HideAlbumArtistsTab { get; set; } = true;

    /// <summary>
    /// Persistance : une fois la médiathèque réparée, les scans et les actualisations gardent les artistes
    /// décidés, et les nouveaux albums sont résolus après chaque scan. Activé par défaut ; sans effet avant « Réparer ».
    /// </summary>
    public bool EnablePersistence { get; set; } = true;

    /// <summary>Collaborations « A &amp; B » : les deux principaux (défaut) ou le premier seulement.</summary>
    public CollaborationMode Collaboration { get; set; } = CollaborationMode.Both;

    /// <summary>Compilations / BO : artiste du dossier (défaut), « Artistes variés », ou aucun.</summary>
    public CompilationMode Compilation { get; set; } = CompilationMode.FolderArtist;

    /// <summary>Écriture des noms : MusicBrainz (défaut), dossier, ou tags.</summary>
    public DisplayNameMode DisplayName { get; set; } = DisplayNameMode.MusicBrainz;

    /// <summary>Noms MusicBrainz non latins : transcrits (défaut), écriture d'origine, ou écriture d'origine pour la liste seulement.</summary>
    public NonLatinNameMode NonLatinNames { get; set; } = NonLatinNameMode.Latin;

    /// <summary>Artistes qui gardent leur écriture d'origine (mode liste), un par ligne, dans n'importe quelle écriture.</summary>
    public string OriginalScriptArtists { get; set; } = string.Empty;

    /// <summary>Ordre des sources des artistes principaux, ex. « Providers,Tags,Folder » (défaut) ou « Tags,Providers,Folder ».</summary>
    public string SourceOrder { get; set; } = string.Join(",", ResolverSettings.DefaultSourceOrder);

    /// <summary>Jointures qui annoncent un invité, une par ligne.</summary>
    public string GuestJoinPhrases { get; set; } = string.Join("\n", ResolverSettings.DefaultGuestJoinPhrases);

    /// <summary>Noms à ne jamais couper, un par ligne.</summary>
    public string NameWhitelist { get; set; } = string.Join("\n", ResolverSettings.DefaultNameWhitelist);

    /// <summary>Nom affiché des compilations sans dossier artiste. Vide : « Artistes variés » ou « Various Artists » selon la langue du serveur.</summary>
    public string VariousArtistsName { get; set; } = string.Empty;

    /// <summary>Réglages du résolveur.</summary>
    public ResolverSettings ToResolverSettings() => new()
    {
        Collaboration = Collaboration,
        Compilation = Compilation,
        DisplayName = DisplayName,
        NonLatinNames = NonLatinNames,
        OriginalScriptArtists = Lines(OriginalScriptArtists, []),
        SourceOrder = ResolverSettings.ParseSourceOrder(SourceOrder),
        GuestJoinPhrases = Lines(GuestJoinPhrases, ResolverSettings.DefaultGuestJoinPhrases),
        NameWhitelist = Lines(NameWhitelist, []),
        VariousArtistsName = string.IsNullOrWhiteSpace(VariousArtistsName) ? Messages.VariousArtists : VariousArtistsName.Trim()
    };

    private static IReadOnlyList<string> Lines(string? text, IReadOnlyList<string> fallback)
    {
        var lines = (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? fallback : lines;
    }
}
