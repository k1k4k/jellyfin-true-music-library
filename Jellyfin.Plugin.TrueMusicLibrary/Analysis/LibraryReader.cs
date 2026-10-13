using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;
using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace Jellyfin.Plugin.TrueMusicLibrary.Analysis;

/// <summary>
/// Lit les bibliothèques musique de Jellyfin et en fait des <see cref="LibraryInput"/> (lecture seule) :
/// albums et pistes, fiches artiste des dossiers, contenu actuel des deux onglets (avec favoris et écoutes),
/// fournisseurs d'album activés dans l'ordre de la bibliothèque.
/// </summary>
public sealed class LibraryReader
{
    private readonly ILibraryManager _libraryManager;
    private readonly IProviderManager _providerManager;
    private readonly IUserManager _userManager;
    private readonly IUserDataManager _userDataManager;
    private readonly ILogger<LibraryReader> _logger;

    public LibraryReader(
        ILibraryManager libraryManager,
        IProviderManager providerManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILogger<LibraryReader> logger)
    {
        _libraryManager = libraryManager;
        _providerManager = providerManager;
        _userManager = userManager;
        _userDataManager = userDataManager;
        _logger = logger;
    }

    public List<LibraryInput> ReadMusicLibraries(CancellationToken cancellationToken)
    {
        var result = new List<LibraryInput>();
        foreach (var folder in _libraryManager.GetVirtualFolders().Where(v => v.CollectionType == CollectionTypeOptions.music))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Guid.TryParse(folder.ItemId, out var libraryId) && _libraryManager.GetItemById(libraryId) is { } root)
            {
                result.Add(Read(libraryId, root, folder.Name, folder.Locations.FirstOrDefault()));
            }
        }

        return result;
    }

    /// <summary>Albums des bibliothèques musique, sans le reste (tâche d'après scan : calcul des empreintes).</summary>
    public List<(string LibraryId, string? PreferredLanguage, List<AlbumSnapshot> Albums)> ReadAlbums(CancellationToken cancellationToken)
    {
        var result = new List<(string, string?, List<AlbumSnapshot>)>();
        foreach (var folder in _libraryManager.GetVirtualFolders().Where(v => v.CollectionType == CollectionTypeOptions.music))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Guid.TryParse(folder.ItemId, out var libraryId) && _libraryManager.GetItemById(libraryId) is { } root)
            {
                result.Add((libraryId.ToString("N"), _libraryManager.GetLibraryOptions(root).PreferredMetadataLanguage, Albums(libraryId).Select(Snapshot).ToList()));
            }
        }

        return result;
    }

    private List<MusicAlbum> Albums(Guid libraryId)
        => _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicAlbum],
            AncestorIds = [libraryId],
            Recursive = true
        }).OfType<MusicAlbum>().ToList();

    private LibraryInput Read(Guid libraryId, BaseItem root, string name, string? path)
    {
        var albums = Albums(libraryId);
        var albumsById = albums.ToDictionary(a => Id(a), StringComparer.Ordinal);

        var users = Compat.UserManagerCompat.GetUsers(_userManager);
        ArtistEntity Entity(BaseItem item) => new()
        {
            Id = Id(item),
            Name = item.Name ?? string.Empty,
            Mbid = item.GetProviderId(MetadataProvider.MusicBrainzArtist),
            IsFolder = item is MusicArtist { IsAccessedByName: false },
            Favorites = users.Count(u => _userDataManager.GetUserData(u, item)?.IsFavorite == true),
            Played = users.Any(u => (_userDataManager.GetUserData(u, item)?.PlayCount ?? 0) > 0)
        };

        var folderArtists = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.MusicArtist],
            AncestorIds = [libraryId],
            Recursive = true
        }).OfType<MusicArtist>().Where(a => !a.IsAccessedByName).ToList();

        var libraryOptions = _libraryManager.GetLibraryOptions(root);
        var reference = albums.FirstOrDefault(a => !a.IsLocked) ?? albums.FirstOrDefault();
        var remote = reference is null
            ? []
            : _providerManager.GetMetadataProviders<MusicAlbum>(reference, libraryOptions)
                .OfType<IRemoteMetadataProvider<MusicAlbum, AlbumInfo>>()
                .ToList();

        return new LibraryInput
        {
            Id = libraryId.ToString("N"),
            Name = name,
            Path = path,
            PreferredLanguage = libraryOptions.PreferredMetadataLanguage,
            Fetchers = remote.Select(p => p.Name).ToList(),
            ProviderSources = remote
                .Where(p => !string.Equals(p.Name, MusicBrainzSource.SourceName, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(p => p.Name, p => (IAlbumSource)new ProviderSource(p.Name, (snapshot, ct) => FetchFromProvider(p, albumsById, snapshot, ct))),
            Albums = albums.Select(Snapshot).ToList(),
            Current = new CurrentState
            {
                ArtistsTab = _libraryManager.GetArtists(new InternalItemsQuery { AncestorIds = [libraryId] }).Items.Select(i => Entity(i.Item)).ToList(),
                AlbumArtistsTab = _libraryManager.GetAlbumArtists(new InternalItemsQuery { AncestorIds = [libraryId] }).Items.Select(i => Entity(i.Item)).ToList(),
                FolderArtists = folderArtists.Select(Entity).ToList()
            }
        };
    }

    private async Task<ProviderAlbumResult?> FetchFromProvider(
        IRemoteMetadataProvider<MusicAlbum, AlbumInfo> provider,
        Dictionary<string, MusicAlbum> albumsById,
        AlbumSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!albumsById.TryGetValue(snapshot.Id, out var album))
        {
            return null;
        }

        try
        {
            // Appel en lecture : le fournisseur renvoie un objet résultat, rien n'est écrit sur l'album.
            var result = await provider.GetMetadata(album.GetLookupInfo(), cancellationToken).ConfigureAwait(false);
            return result is { HasMetadata: true, Item: not null }
                ? new ProviderAlbumResult(result.Item.AlbumArtists, result.Item.GetProviderId(MetadataProvider.MusicBrainzAlbumArtist))
                : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "{Provider} : échec pour l'album {Album}", provider.Name, album.Path);
            return null;
        }
    }

    private static AlbumSnapshot Snapshot(MusicAlbum album)
    {
        var folder = album.GetParent() as MusicArtist;
        if (folder is { IsAccessedByName: true })
        {
            folder = null;
        }

        return new AlbumSnapshot
        {
            Id = Id(album),
            Name = album.Name ?? string.Empty,
            Path = album.Path ?? string.Empty,
            FolderArtist = folder?.Name,
            FolderArtistId = folder is null ? null : Id(folder),
            FolderArtistMbid = folder?.GetProviderId(MetadataProvider.MusicBrainzArtist),
            ProviderIds = new Dictionary<string, string>(album.ProviderIds, StringComparer.OrdinalIgnoreCase),
            AlbumArtists = album.AlbumArtists.ToList(),
            Artists = album.Artists.ToList(),
            IsLocked = album.IsLocked,
            Tracks = album.GetRecursiveChildren(i => i is AudioItem).OfType<AudioItem>().Select(t => new TrackSnapshot
            {
                Id = Id(t),
                Name = t.Name ?? string.Empty,
                Path = t.Path ?? string.Empty,
                Disc = t.ParentIndexNumber,
                Number = t.IndexNumber,
                RunTimeTicks = t.RunTimeTicks,
                Artists = t.Artists.ToList(),
                AlbumArtists = t.AlbumArtists.ToList(),
                ProviderIds = new Dictionary<string, string>(t.ProviderIds, StringComparer.OrdinalIgnoreCase),
                IsLocked = t.IsLocked || t.LockedFields.Contains(MetadataField.Cast)
            }).ToList()
        };
    }

    private static string Id(BaseItem item) => item.Id.ToString("N");
}
