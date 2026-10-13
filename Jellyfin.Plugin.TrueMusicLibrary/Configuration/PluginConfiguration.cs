using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.TrueMusicLibrary.Configuration;

/// <summary>Configuration du plugin (Tableau de bord → Plugins → True Music Library).</summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Onglet unique côté serveur : <c>GET /Artists</c> est servi comme <c>GET /Artists/AlbumArtists</c>,
    /// pour tous les clients. Pris en compte immédiatement, sans redémarrage.
    /// </summary>
    public bool EnableArtistsRouteRewrite { get; set; } = true;

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

    /// <summary>Jointures qui annoncent un invité, une par ligne.</summary>
    public string GuestJoinPhrases { get; set; } = string.Join("\n", ResolverSettings.DefaultGuestJoinPhrases);

    /// <summary>Noms à ne jamais couper, un par ligne.</summary>
    public string NameWhitelist { get; set; } = string.Join("\n", ResolverSettings.DefaultNameWhitelist);

    /// <summary>Nom affiché des compilations sans dossier artiste.</summary>
    public string VariousArtistsName { get; set; } = "Artistes variés";

    /// <summary>Réglages du résolveur.</summary>
    public ResolverSettings ToResolverSettings() => new()
    {
        Collaboration = Collaboration,
        Compilation = Compilation,
        DisplayName = DisplayName,
        GuestJoinPhrases = Lines(GuestJoinPhrases, ResolverSettings.DefaultGuestJoinPhrases),
        NameWhitelist = Lines(NameWhitelist, []),
        VariousArtistsName = string.IsNullOrWhiteSpace(VariousArtistsName) ? "Artistes variés" : VariousArtistsName.Trim()
    };

    private static IReadOnlyList<string> Lines(string? text, IReadOnlyList<string> fallback)
    {
        var lines = (text ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? fallback : lines;
    }
}
