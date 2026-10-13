using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using MediaBrowser.Model.Search;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jellyfin.Plugin.TrueMusicLibrary.Web;

/// <summary>
/// Retire les invités des listes d'artistes que l'onglet unique ne couvre pas : résultats de <c>GET /Search/Hints</c> et de
/// <c>GET /Items</c> (et <c>/Users/{id}/Items</c>). Une fiche artiste est gardée si elle est artiste principal d'au moins une
/// sortie. Les requêtes par Id (<c>/Items/{id}</c>, <c>/Items?ids=</c>) ne sont pas filtrées : la page d'un invité reste
/// accessible depuis ses pistes. Rien n'est filtré tant que la médiathèque n'a pas été réparée.
/// Les totaux sont corrigés ; une page de liste peut contenir un peu moins d'éléments que demandé.
/// </summary>
public sealed class GuestArtistsResultFilter : IAsyncResultFilter
{
    private readonly PersistenceState _persistence;

    public GuestArtistsResultFilter(PersistenceState persistence)
    {
        _persistence = persistence;
    }

    /// <inheritdoc />
    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (Plugin.Instance?.Configuration.HideGuestArtists != false
            && context.Result is ObjectResult { Value: not null } result
            && IsFilteredAction(context)
            && !context.HttpContext.Request.Query.ContainsKey("ids")
            && _persistence.RepairedRules is { } rules)
        {
            switch (result.Value)
            {
                case QueryResult<BaseItemDto> items:
                    var kept = items.Items.Where(i => i.Type != BaseItemKind.MusicArtist || rules.IsMainArtist(i.Name)).ToArray();
                    if (kept.Length != items.Items.Count)
                    {
                        items.TotalRecordCount = Math.Max(0, items.TotalRecordCount - (items.Items.Count - kept.Length));
                        items.Items = kept;
                    }

                    break;
                case SearchHintResult hints:
                    var keptHints = hints.SearchHints.Where(h => h.Type != BaseItemKind.MusicArtist || rules.IsMainArtist(h.Name)).ToArray();
                    if (keptHints.Length != hints.SearchHints.Count)
                    {
                        result.Value = new SearchHintResult(keptHints, Math.Max(0, hints.TotalRecordCount - (hints.SearchHints.Count - keptHints.Length)));
                    }

                    break;
            }
        }

        return next();
    }

    /// <summary>Recherche et listes d'éléments de Jellyfin (pas les requêtes par Id ni les autres contrôleurs).</summary>
    private static bool IsFilteredAction(ResultExecutingContext context)
        => context.ActionDescriptor is ControllerActionDescriptor action
            && ((action.ControllerName == "Items" && action.ActionName is "GetItems" or "GetItemsByUserIdLegacy")
                || (action.ControllerName == "Search" && action.ActionName == "GetSearchHints"));
}
