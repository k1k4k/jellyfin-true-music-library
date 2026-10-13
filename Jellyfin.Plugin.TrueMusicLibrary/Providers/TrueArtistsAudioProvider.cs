using Jellyfin.Data.Enums;
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
/// Fournisseur des pistes. Non <c>IPreRefreshProvider</c> : il passe après <c>AudioFileProber</c> et après la
/// fusion des autres fournisseurs. Il signale un changement dans les mêmes cas que la sonde (fichier modifié, piste jamais
/// sondée), donc il repasse chaque fois que la sonde relit les tags :
/// <list type="bullet">
/// <item>artistes et artistes d'album remis aux décisions en vigueur (<see cref="Resolution.LiveRules.ForTrack"/>) ;</item>
/// <item>People Artist / AlbumArtist réalignés à chaque passage (12.x) : la sonde les réécrit depuis les tags bruts, même
/// quand elle garde les anciens artistes.</item>
/// </list>
/// Rien tant que la médiathèque n'a pas été réparée. Pistes verrouillées (ou champ « Cast ») et pistes d'un album verrouillé
/// ignorées (pour un élément verrouillé, Jellyfin n'appelle d'ailleurs pas ce fournisseur). Aucun réseau.
/// </summary>
public class TrueArtistsAudioProvider : ICustomMetadataProvider<Audio>, IHasItemChangeMonitor, IHasOrder
{
    private const string Source = "audio";

    private readonly ILibraryManager _libraryManager;
    private readonly PersistenceState _persistence;
    private readonly ProviderTrace _trace;

    public TrueArtistsAudioProvider(ILibraryManager libraryManager, PersistenceState persistence, ProviderTrace trace)
    {
        _libraryManager = libraryManager;
        _persistence = persistence;
        _trace = trace;
    }

    /// <inheritdoc />
    public string Name => "Jellyfin True Music Library";

    /// <inheritdoc />
    public int Order => 1000;

    /// <inheritdoc />
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (_persistence.Rules is null || string.IsNullOrWhiteSpace(item.Path) || !item.IsFileProtocol)
        {
            return false;
        }

        // Mêmes conditions que la sonde (ProbeProvider.HasChanged) : fichier modifié, ou piste sans informations média.
        var file = directoryService.GetFile(item.Path);
        var changed = (file is not null && item.HasChanged(file.LastWriteTimeUtc))
            || (!item.RunTimeTicks.HasValue && !item.IsVirtualItem && !item.IsShortcut);
        if (changed)
        {
            _trace.Add(Source, "HasChanged", item.Id, item.Path, "file modified or never probed");
        }

        return changed;
    }

    /// <inheritdoc />
    public async Task<ItemUpdateType> FetchAsync(Audio item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        _trace.Add(
            Source,
            "Fetch",
            item.Id,
            item.Path,
            $"mode={options.MetadataRefreshMode} replaceAll={options.ReplaceAllMetadata} artists=[{string.Join(" | ", item.Artists)}] albumArtists=[{string.Join(" | ", item.AlbumArtists)}]");

        var rules = _persistence.Rules;
        if (rules is null || item.IsLocked || item.LockedFields.Contains(MetadataField.Cast))
        {
            return ItemUpdateType.None;
        }

        var id = item.Id.ToString("N");
        if (!rules.IsKnownTrack(id) && !IsInMusicLibrary(item))
        {
            return ItemUpdateType.None;
        }

        // Album verrouillé : ses pistes restent telles quelles, comme dans l'analyse.
        var album = item.AlbumEntity;
        if (album?.IsLocked == true)
        {
            return ItemUpdateType.None;
        }

        var folder = album?.GetParent() as MusicArtist;
        var target = rules.ForTrack(
            id,
            album?.Id.ToString("N"),
            item.Artists,
            item.AlbumArtists,
            folder is { IsAccessedByName: false } ? folder.Name : null);
        if (target is null)
        {
            return ItemUpdateType.None;
        }

        // Juste après la sonde qui a relu le fichier (« Remplacer toutes les métadonnées », nouvelle piste) : valeurs natives.
        var native = options.ReplaceAllMetadata || item.DateLastRefreshed == default;
        var original = new ItemSnapshot { Id = id, Kind = "Audio", Artists = item.Artists.ToList(), AlbumArtists = item.AlbumArtists.ToList(), Native = native };
        var update = ItemUpdateType.None;
        if (!item.Artists.SequenceEqual(target.Artists, StringComparer.Ordinal)
            || !item.AlbumArtists.SequenceEqual(target.AlbumArtists, StringComparer.Ordinal))
        {
            item.Artists = target.Artists.ToArray();
            item.AlbumArtists = target.AlbumArtists.ToArray();
            update = ItemUpdateType.MetadataEdit;
        }

        if (ArtistPeople.Managed)
        {
            var current = ArtistPeople.Current(_libraryManager, item);
            var wanted = ArtistPeople.Wanted(target.AlbumArtists, target.Artists);
            original.People = ArtistPeople.Snapshot(current);
            if (!ArtistPeople.SameSet(current, wanted))
            {
                await ArtistPeople.ReplaceAsync(_libraryManager, item, wanted, cancellationToken).ConfigureAwait(false);
                update |= ItemUpdateType.MetadataEdit;
            }
        }

        // Valeurs natives notées même sans changement : la restauration rendra ce que le fichier dit aujourd'hui.
        if (update != ItemUpdateType.None || native)
        {
            _persistence.Record(original);
        }

        if (update != ItemUpdateType.None)
        {
            _trace.Add(Source, "Applied", item.Id, item.Path, $"artists=[{string.Join(" | ", target.Artists)}] albumArtists=[{string.Join(" | ", target.AlbumArtists)}] known={target.Known}");
        }

        return update;
    }

    /// <summary>Une piste inconnue des décisions n'est traitée que dans une bibliothèque musique (pas un livre audio).</summary>
    private bool IsInMusicLibrary(BaseItem item)
        => _libraryManager.GetCollectionFolders(item).OfType<CollectionFolder>().Any(f => f.CollectionType == CollectionType.music);
}
