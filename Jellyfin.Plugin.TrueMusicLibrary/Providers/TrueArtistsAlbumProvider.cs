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
/// Fournisseur des albums : passe après la fusion MusicBrainz / TheAudioDB et remet
/// <list type="bullet">
/// <item>les artistes d'album aux artistes principaux décidés ;</item>
/// <item>les artistes à l'union de ceux des pistes, comme Jellyfin les calcule (« Remplacer toutes les métadonnées » y met
/// sinon tous les crédités MusicBrainz).</item>
/// </list>
/// <see cref="HasChanged"/> est évalué après le recalcul depuis les pistes (<c>AlbumMetadataService.UpdateMetadataFromChildren</c>) :
/// il signale un écart avec la décision, et l'album se corrige lors d'un scan ordinaire. Les People de l'album suivent :
/// Jellyfin les recalcule depuis ces deux champs en fin d'actualisation. Albums inconnus (nouveaux) : rien, la tâche d'après
/// scan les résout.
/// </summary>
public class TrueArtistsAlbumProvider : ICustomMetadataProvider<MusicAlbum>, IHasItemChangeMonitor, IHasOrder
{
    private const string Source = "album";

    private readonly ILibraryManager _libraryManager;
    private readonly PersistenceState _persistence;
    private readonly ProviderTrace _trace;

    public TrueArtistsAlbumProvider(ILibraryManager libraryManager, PersistenceState persistence, ProviderTrace trace)
    {
        _libraryManager = libraryManager;
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
        if (item is not MusicAlbum album || album.IsLocked || _persistence.Rules?.ForAlbum(album.Id.ToString("N")) is not { } mains)
        {
            return false;
        }

        var changed = !album.AlbumArtists.SequenceEqual(mains, StringComparer.Ordinal);
        if (changed)
        {
            _trace.Add(Source, "HasChanged", album.Id, album.Path, $"albumArtists=[{string.Join(" | ", album.AlbumArtists)}] wanted=[{string.Join(" | ", mains)}]");
        }

        return changed;
    }

    /// <inheritdoc />
    public Task<ItemUpdateType> FetchAsync(MusicAlbum item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        _trace.Add(
            Source,
            "Fetch",
            item.Id,
            item.Path,
            $"mode={options.MetadataRefreshMode} replaceAll={options.ReplaceAllMetadata} albumArtists=[{string.Join(" | ", item.AlbumArtists)}] mbAlbum={item.GetProviderId(MetadataProvider.MusicBrainzAlbum)}");

        if (item.IsLocked || _persistence.Rules?.ForAlbum(item.Id.ToString("N")) is not { } mains)
        {
            return Task.FromResult(ItemUpdateType.None);
        }

        // Même calcul que Jellyfin (SetArtistsFromSongs) : artistes des pistes, du plus fréquent au moins fréquent.
        var artists = item.GetRecursiveChildren(i => i is Audio).OfType<Audio>()
            .SelectMany(s => s.Artists)
            .GroupBy(a => a, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToArray();
        var artistsChanged = artists.Length > 0 && !item.Artists.ToHashSet(StringComparer.Ordinal).SetEquals(artists);
        var albumArtistsChanged = !item.AlbumArtists.SequenceEqual(mains, StringComparer.Ordinal);
        if (!artistsChanged && !albumArtistsChanged)
        {
            return Task.FromResult(ItemUpdateType.None);
        }

        var original = new ItemSnapshot
        {
            Id = item.Id.ToString("N"),
            Kind = "MusicAlbum",
            Artists = item.Artists.ToList(),
            AlbumArtists = item.AlbumArtists.ToList(),
            People = ArtistPeople.Managed ? ArtistPeople.Snapshot(ArtistPeople.Current(_libraryManager, item)) : null
        };
        _persistence.Record(original);

        if (artistsChanged)
        {
            item.Artists = artists;
        }

        item.AlbumArtists = mains.ToArray();
        _trace.Add(Source, "Applied", item.Id, item.Path, $"albumArtists=[{string.Join(" | ", mains)}] artists=[{string.Join(" | ", item.Artists)}]");
        return Task.FromResult(ItemUpdateType.MetadataEdit);
    }
}
