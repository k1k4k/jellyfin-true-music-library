using System.Text;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Web;

/// <summary>
/// Injecte la balise &lt;script&gt; de <c>true-music-library.js</c> dans l'index.html de jellyfin-web au moment de la requête
/// (middleware le plus externe, via <see cref="IStartupFilter"/>), quand le réglage « Cacher l'onglet Artistes de l'album »
/// est actif et que la médiathèque est réparée. Aucune écriture sur disque ; en cas d'erreur, la réponse d'origine est servie.
/// </summary>
public class ScriptInjectionStartupFilter : IStartupFilter
{
    /// <summary>Nom du script servi par le plugin (et marqueur d'idempotence dans index.html).</summary>
    public const string ScriptFileName = "true-music-library.js";

    private readonly PersistenceState _persistence;
    private readonly ILogger<ScriptInjectionStartupFilter> _logger;
    private int _loggedOnce;

    public ScriptInjectionStartupFilter(PersistenceState persistence, ILogger<ScriptInjectionStartupFilter> logger)
    {
        _persistence = persistence;
        _logger = logger;
    }

    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            // Middleware le plus externe : en retirant Accept-Encoding, la réponse à réécrire arrive non compressée.
            app.Use(InvokeAsync);
            next(app);
        };
    }

    /// <summary>Balise injectée. Chemin relatif à /web/ : fonctionne aussi derrière une base URL (/jellyfin/web/).</summary>
    internal static string BuildScriptTag()
        => $"<script defer=\"defer\" src=\"../{Plugin.RoutePrefix}/{ScriptFileName}?v={Analysis.AnalysisService.Version}\"></script>";

    private static bool IsIndexRequest(string? path)
        => !string.IsNullOrEmpty(path)
            && (path.EndsWith("/web/index.html", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web/", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/web", StringComparison.OrdinalIgnoreCase));

    private async Task InvokeAsync(HttpContext context, Func<Task> next)
    {
        if (!HttpMethods.IsGet(context.Request.Method)
            || !IsIndexRequest(context.Request.Path.Value)
            || Plugin.Instance?.Configuration.HideAlbumArtistsTab != true
            || _persistence.RepairedRules is null)
        {
            await next().ConfigureAwait(false);
            return;
        }

        // Réponse complète, en clair, en 200 : pas de compression, pas de partiel.
        context.Request.Headers.Remove("Accept-Encoding");
        context.Request.Headers.Remove("Range");
        context.Request.Headers.Remove("If-Range");

        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next().ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = originalBody;
        }

        buffer.Seek(0, SeekOrigin.Begin);
        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && (context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) ?? false);
        if (!isHtml)
        {
            await buffer.CopyToAsync(originalBody).ConfigureAwait(false);
            return;
        }

        string html;
        using (var reader = new StreamReader(buffer, Encoding.UTF8, true, 1024, leaveOpen: true))
        {
            html = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        try
        {
            var headClose = html.LastIndexOf("</head>", StringComparison.OrdinalIgnoreCase);
            if (!html.Contains(ScriptFileName, StringComparison.OrdinalIgnoreCase) && headClose >= 0)
            {
                html = string.Concat(html.AsSpan(0, headClose), BuildScriptTag(), html.AsSpan(headClose));
                if (Interlocked.Exchange(ref _loggedOnce, 1) == 0)
                {
                    _logger.LogInformation("True Music Library : script de l'interface web injecté dans index.html");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "True Music Library : injection du script impossible, index.html servi tel quel");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html;charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        // Le corps a changé : les validateurs du gestionnaire de fichiers statiques ne valent plus.
        context.Response.Headers.Remove("ETag");
        context.Response.Headers.Remove("Last-Modified");
        context.Response.Headers.Remove("Accept-Ranges");
        await originalBody.WriteAsync(bytes).ConfigureAwait(false);
    }
}
