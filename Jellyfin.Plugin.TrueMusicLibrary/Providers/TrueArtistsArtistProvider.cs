using Jellyfin.Plugin.TrueMusicLibrary.Apply;
using Jellyfin.Plugin.TrueMusicLibrary.Diagnostics;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.TrueMusicLibrary.Providers;

/// <summary>
/// Fournisseur des fiches artiste : passe après TheAudioDB et MusicBrainz et remet le nom affiché décidé
/// sur les fiches de dossier principales (« Remplacer toutes les métadonnées » y met sinon le nom MusicBrainz actuel, qui
/// peut être différent). Aucun verrou. Les fiches « par nom » ne sont jamais renommées : leur Id dépend du nom.
/// </summary>
public class TrueArtistsArtistProvider : ICustomMetadataProvider<MusicArtist>, IHasItemChangeMonitor, IHasOrder
{
    private const string Source = "artist";

    private readonly PersistenceState _persistence;
    private readonly ProviderTrace _trace;

    public TrueArtistsArtistProvider(PersistenceState persistence, ProviderTrace trace)
    {
        _persistence = persistence;
        _trace = trace;
    }

    /// <inheritdoc />
    public string Name => "True Music Library";

    /// <inheritdoc />
    public int Order => 1000;

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        var changed = item is MusicArtist artist && WantedName(artist) is not null;
        if (changed)
        {
            _trace.Add(Source, "HasChanged", item.Id, item.Name, "rename pending");
        }

        return changed;
    }

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(MusicArtist item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        _trace.Add(Source, "Fetch", item.Id, item.Name, $"mode={options.MetadataRefreshMode} replaceAll={options.ReplaceAllMetadata} byName={item.IsAccessedByName} path={item.Path}");

        var name = WantedName(item);
        if (name is null)
        {
            return Task.FromResult(ItemUpdateType.None);
        }

        _persistence.Record(new ItemSnapshot { Id = item.Id.ToString("N"), Kind = "MusicArtist", Name = item.Name });
        _trace.Add(Source, "Applied", item.Id, item.Name, $"renamed to {name}");
        item.Name = name;

        // La clé de présentation (« Artist-<nom> ») est calculée avant les fournisseurs : on la met à jour ici, comme Jellyfin
        // le ferait à l'actualisation suivante. Favoris et historique suivent.
        item.PresentationUniqueKey = item.CreatePresentationUniqueKey();
        return Task.FromResult(ItemUpdateType.MetadataEdit);
    }

    private string? WantedName(MusicArtist artist)
    {
        if (artist.IsAccessedByName || artist.IsLocked || artist.LockedFields.Contains(MetadataField.Name))
        {
            return null;
        }

        var name = _persistence.Rules?.ForFolderArtist(artist.Id.ToString("N"));
        return name is not null && !string.Equals(artist.Name, name, StringComparison.Ordinal) ? name : null;
    }
}
