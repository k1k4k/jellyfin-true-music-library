using System.Reflection;
using Jellyfin.Plugin.TrueMusicLibrary.Web;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.TrueMusicLibrary.Api;

/// <summary>Sert le script de l'interface web. Anonyme : index.html le charge avant toute connexion.</summary>
[ApiController]
[Route(Plugin.RoutePrefix)]
public class TrueMusicLibraryWebController : ControllerBase
{
    /// <summary>GET /TrueMusicLibrary/true-music-library.js.</summary>
    [HttpGet(ScriptInjectionStartupFilter.ScriptFileName)]
    [AllowAnonymous]
    public ActionResult GetWebScript()
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Jellyfin.Plugin.TrueMusicLibrary." + ScriptInjectionStartupFilter.ScriptFileName);
        if (stream is null)
        {
            return NotFound();
        }

        // L'URL porte la version du plugin (?v=) : le navigateur peut le garder longtemps.
        Response.Headers["Cache-Control"] = "public, max-age=31536000, immutable";
        return new FileStreamResult(stream, "text/javascript; charset=utf-8");
    }
}
