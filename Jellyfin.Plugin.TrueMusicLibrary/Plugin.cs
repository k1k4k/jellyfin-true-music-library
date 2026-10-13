using System.Globalization;
using Jellyfin.Plugin.TrueMusicLibrary.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.TrueMusicLibrary;

/// <summary>
/// Jellyfin True Music Library : un seul onglet « Artistes », qui ne liste que les artistes principaux
/// des sorties (albums, singles, EP), d'après les fournisseurs de métadonnées de la bibliothèque.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>Segment de route de l'API du plugin.</summary>
    public const string RoutePrefix = "TrueMusicLibrary";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "True Music Library";

    public override Guid Id => Guid.Parse("541fb644-193a-4692-b8a4-2593356153a7");

    public override string Description =>
        "Un seul onglet « Artistes », avec seulement les artistes principaux des albums, singles et EP. "
        + "A single \"Artists\" tab, listing only the main artists of albums, singles and EPs.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo
        {
            Name = Name,
            EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
        };
    }
}
