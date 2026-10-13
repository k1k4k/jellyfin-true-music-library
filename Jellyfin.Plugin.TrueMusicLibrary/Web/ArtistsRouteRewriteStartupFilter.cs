using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.TrueMusicLibrary.Web;

/// <summary>
/// Onglet unique côté serveur : <c>GET {BaseUrl}/Artists</c> est servi comme
/// <c>GET {BaseUrl}/Artists/AlbumArtists</c>. Les deux actions de <c>ArtistsController</c> prennent exactement
/// les mêmes paramètres, donc la query string est gardée telle quelle.
/// Middleware le plus externe (<see cref="IStartupFilter"/>) : la réécriture a lieu avant le routage,
/// l'authentification et le <c>Map(BaseUrl)</c> de Jellyfin. Seul le chemin exact est concerné :
/// <c>/Artists/{name}</c>, <c>/Artists/InstantMix</c>… ne sont pas touchés.
/// </summary>
public class ArtistsRouteRewriteStartupFilter : IStartupFilter
{
    /// <summary>En-tête ajouté aux réponses réécrites (diagnostic, tests).</summary>
    public const string MarkerHeader = "X-TrueMusicLibrary-Rewrite";

    private const string ArtistsSegment = "/Artists";
    private const string AlbumArtistsSegment = "/Artists/AlbumArtists";

    private readonly IServerConfigurationManager _configurationManager;

    public ArtistsRouteRewriteStartupFilter(IServerConfigurationManager configurationManager)
    {
        _configurationManager = configurationManager;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.Use(RewriteAsync);
            next(app);
        };
    }

    /// <summary>Chemin réécrit, ou null si la requête ne vise pas exactement <c>{baseUrl}/Artists</c>.</summary>
    internal static string? GetRewrittenPath(string? path, string? baseUrl)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var trimmed = path.Length > 1 ? path.TrimEnd('/') : path;
        var prefix = string.IsNullOrEmpty(baseUrl) ? string.Empty : "/" + baseUrl.Trim('/');
        return string.Equals(trimmed, prefix + ArtistsSegment, StringComparison.OrdinalIgnoreCase)
            ? prefix + AlbumArtistsSegment
            : null;
    }

    private Task RewriteAsync(HttpContext context, Func<Task> next)
    {
        if (HttpMethods.IsGet(context.Request.Method)
            && Plugin.Instance?.Configuration.EnableArtistsRouteRewrite == true)
        {
            var rewritten = GetRewrittenPath(context.Request.Path.Value, _configurationManager.GetNetworkConfiguration().BaseUrl);
            if (rewritten is not null)
            {
                context.Request.Path = new PathString(rewritten);
                context.Response.Headers[MarkerHeader] = "AlbumArtists";
            }
        }

        return next();
    }
}
