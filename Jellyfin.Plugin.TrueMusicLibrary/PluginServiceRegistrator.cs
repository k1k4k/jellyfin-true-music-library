using Jellyfin.Plugin.TrueMusicLibrary.Analysis;
using Jellyfin.Plugin.TrueMusicLibrary.Diagnostics;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using Jellyfin.Plugin.TrueMusicLibrary.Store;
using Jellyfin.Plugin.TrueMusicLibrary.Web;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.TrueMusicLibrary;

/// <summary>
/// Enregistre les services du plugin dans le conteneur de Jellyfin (10.11 et 12).
/// Les fournisseurs de métadonnées (<c>ICustomMetadataProvider</c>), les tâches planifiées et la tâche d'après scan
/// sont découverts tout seuls par Jellyfin.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ProviderTrace>();
        serviceCollection.AddSingleton<DecisionStore>();
        serviceCollection.AddSingleton<LibraryReader>();
        serviceCollection.AddSingleton<AnalysisService>();
        serviceCollection.AddSingleton<Apply.LibraryApplier>();
        serviceCollection.AddSingleton<PersistenceState>();
        serviceCollection.AddSingleton<IStartupFilter, ArtistsRouteRewriteStartupFilter>();
    }
}
