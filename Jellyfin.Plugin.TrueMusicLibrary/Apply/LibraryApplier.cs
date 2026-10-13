using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueMusicLibrary.Persistence;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using Microsoft.Extensions.Logging;
using AudioItem = MediaBrowser.Controller.Entities.Audio.Audio;

namespace Jellyfin.Plugin.TrueMusicLibrary.Apply;

/// <summary>
/// Écrit les décisions dans Jellyfin et sait les défaire :
/// <list type="number">
/// <item>plan : liste des éléments à modifier avec leurs valeurs d'origine, sans rien écrire ;</item>
/// <item>instantané enregistré sur disque, puis application (albums, pistes, fiches renommées) ;</item>
/// <item>nettoyage des fiches artiste « par nom » plus référencées, même règle que l'<c>ArtistsValidator</c> de Jellyfin,
/// avec les fiches supprimées notées dans l'instantané avant suppression.</item>
/// </list>
/// Aucune écriture si la valeur est déjà la bonne ; éléments verrouillés ignorés ; aucun verrou posé ; aucun fichier
/// touché (ni audio, ni dossier de métadonnées).
/// </summary>
public sealed class LibraryApplier
{
    private readonly ILibraryManager _libraryManager;
    private readonly IItemRepository _itemRepository;
    private readonly ILogger<LibraryApplier> _logger;

    public LibraryApplier(ILibraryManager libraryManager, IItemRepository itemRepository, ILogger<LibraryApplier> logger)
    {
        _libraryManager = libraryManager;
        _itemRepository = itemRepository;
        _logger = logger;
    }

    /// <summary>Les People Artist / AlbumArtist ne sont stockés qu'à partir de Jellyfin 12 (ignorés par 10.11).</summary>
    private static bool ManagesArtistPeople => ArtistPeople.Managed;

    /// <param name="report">Analyse à appliquer.</param>
    /// <param name="snapshot">Instantané à remplir (valeurs d'origine).</param>
    /// <param name="persistSnapshot">Enregistre l'instantané, avant toute écriture.</param>
    /// <param name="progress">Progression (0 à 100).</param>
    /// <param name="cancellationToken">Annulation.</param>
    /// <param name="refreshCreatedArtists">
    /// Actualiser tout de suite les fiches créées (images, biographie), comme l'<c>ArtistsValidator</c> de Jellyfin : appel
    /// direct, jamais mis en file (une actualisation d'artiste en file relance celle de tous les albums de son dossier).
    /// </param>
    public async Task<RepairResult> RepairAsync(
        AnalysisReport report,
        RepairSnapshot snapshot,
        Action<RepairSnapshot> persistSnapshot,
        IProgress<double> progress,
        CancellationToken cancellationToken,
        bool refreshCreatedArtists = false)
    {
        var result = new RepairResult { Operation = "Repair" };
        var plan = BuildPlan(report, result);
        if (plan.Count > 0)
        {
            snapshot.Items = plan.Select(p => p.Original).ToList();
            persistSnapshot(snapshot);
            result.SnapshotId = snapshot.Id;
        }

        for (var i = 0; i < plan.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ApplyAsync(plan[i], cancellationToken).ConfigureAwait(false);
                switch (plan[i].Item)
                {
                    case MusicArtist:
                        result.ArtistsRenamed++;
                        break;
                    case MusicAlbum:
                        result.AlbumsUpdated++;
                        break;
                    default:
                        result.TracksUpdated++;
                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Réparation : échec sur {Item}", plan[i].Item.Path ?? plan[i].Item.Name);
                result.Errors.Add($"{plan[i].Item.Name} : {ex.Message}");
            }

            progress.Report(80.0 * (i + 1) / plan.Count);
        }

        var created = CleanupArtists(snapshot, s =>
        {
            persistSnapshot(s);
            result.SnapshotId = s.Id;
        }, result, cancellationToken);

        if (refreshCreatedArtists)
        {
            foreach (var artist in created)
            {
                try
                {
                    await artist.RefreshMetadata(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Actualisation de la fiche créée {Artist} impossible", artist.Name);
                }
            }
        }

        progress.Report(100);
        result.FinishedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Remet les éléments des instantanés pas encore restaurés. Pour chaque élément : la valeur native la plus récente
    /// connue, c'est-à-dire celle que Jellyfin lui donnerait seul.
    /// <list type="bullet">
    /// <item>valeur notée par un fournisseur quand la sonde venait de relire le fichier (« Remplacer toutes les métadonnées »,
    /// nouvelle piste) : c'est ce que le fichier dit aujourd'hui (un fichier retaggé depuis la réparation garde son nouveau tag) ;</item>
    /// <item>sinon la plus ancienne valeur notée : celle d'avant la première réparation (les suivantes sont des valeurs du plugin).</item>
    /// </list>
    /// Les albums sans valeur notée dont des pistes ont été remises sont recalculés depuis leurs pistes, comme Jellyfin le fait.
    /// Puis les fiches supprimées sont recréées et le nettoyage refait.
    /// </summary>
    public async Task<RepairResult> RestoreAsync(IReadOnlyList<RepairSnapshot> snapshots, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var result = new RepairResult { Operation = "Restore", SnapshotId = string.Join(", ", snapshots.Select(s => s.Id)) };
        var chosen = new Dictionary<string, ItemSnapshot>(StringComparer.Ordinal);
        foreach (var entry in snapshots.OrderBy(s => s.CreatedAt).SelectMany(s => s.Items))
        {
            if (!chosen.TryGetValue(entry.Id, out var current) || entry.Native)
            {
                chosen[entry.Id] = entry;
            }
        }

        var entries = chosen.Values.ToList();
        var albumsToRecompute = new Dictionary<Guid, MusicAlbum>();
        for (var i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = entries[i];
            var item = Guid.TryParse(original.Id, out var id) ? _libraryManager.GetItemById(id) : null;
            if (item is null)
            {
                result.Skipped++;
                continue;
            }

            try
            {
                var change = new PlannedChange
                {
                    Item = item,
                    Original = original,
                    Name = original.Kind == "MusicArtist" ? original.Name : null,
                    Artists = original.Artists,
                    AlbumArtists = original.AlbumArtists,
                    People = original.People?.Select(p => new PersonInfo
                    {
                        Name = p.Name,
                        Type = Enum.TryParse<PersonKind>(p.Type, out var kind) ? kind : PersonKind.Artist,
                        Role = p.Role,
                        SortOrder = p.SortOrder
                    }).ToList()
                };
                await ApplyAsync(change, cancellationToken).ConfigureAwait(false);
                switch (item)
                {
                    case MusicArtist:
                        result.ArtistsRenamed++;
                        break;
                    case MusicAlbum:
                        result.AlbumsUpdated++;
                        break;
                    case AudioItem audio:
                        result.TracksUpdated++;
                        if (audio.AlbumEntity is { } album && !chosen.ContainsKey(album.Id.ToString("N")))
                        {
                            albumsToRecompute[album.Id] = album;
                        }

                        break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Restauration : échec sur {Item}", item.Path ?? item.Name);
                result.Errors.Add($"{item.Name} : {ex.Message}");
            }

            progress.Report(70.0 * (i + 1) / Math.Max(1, entries.Count));
        }

        // Albums ajoutés après la réparation : leurs artistes viennent de leurs pistes, comme Jellyfin les calcule
        // (SetArtistsFromSongs / SetAlbumArtistFromSongs), et leurs People suivent (SetPeople).
        foreach (var album in albumsToRecompute.Values.Where(a => !a.IsLocked))
        {
            try
            {
                var songs = album.GetRecursiveChildren(i => i is AudioItem).OfType<AudioItem>().ToList();
                var artists = FromSongs(songs.SelectMany(s => s.Artists));
                var albumArtists = FromSongs(songs.SelectMany(s => s.AlbumArtists));
                if (!album.Artists.SequenceEqual(artists, StringComparer.Ordinal) || !album.AlbumArtists.SequenceEqual(albumArtists, StringComparer.Ordinal))
                {
                    await ApplyAsync(
                        new PlannedChange
                        {
                            Item = album,
                            Original = new ItemSnapshot { Id = Id(album), Kind = "MusicAlbum" },
                            Artists = artists,
                            AlbumArtists = albumArtists,
                            People = ManagesArtistPeople ? ArtistPeople.Wanted(albumArtists, artists) : null
                        },
                        cancellationToken).ConfigureAwait(false);
                    result.AlbumsUpdated++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Restauration : échec sur l'album {Album}", album.Path ?? album.Name);
                result.Errors.Add($"{album.Name} : {ex.Message}");
            }
        }

        progress.Report(80);

        // Fiches supprimées par les réparations : recréées par nom, avec leurs identifiants de fournisseurs. Celles qui ne
        // servent plus à rien repartent au nettoyage.
        foreach (var deleted in snapshots.SelectMany(s => s.DeletedArtists).DistinctBy(d => d.Name, StringComparer.Ordinal))
        {
            try
            {
                var artist = _libraryManager.GetArtist(deleted.Name);
                EnsurePresentationKey(artist, cancellationToken);
                if (artist.ProviderIds.Count == 0 && deleted.ProviderIds.Count > 0)
                {
                    foreach (var (key, value) in deleted.ProviderIds)
                    {
                        artist.SetProviderId(key, value);
                    }

                    await artist.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);
                }

                result.ArtistsCreated++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Restauration : impossible de recréer la fiche {Artist}", deleted.Name);
                result.Errors.Add($"{deleted.Name} : {ex.Message}");
            }
        }

        CleanupArtists(null, null, result, cancellationToken);
        progress.Report(100);
        result.FinishedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>Calcul de Jellyfin pour un album : noms des pistes, du plus fréquent au moins fréquent (casse ignorée).</summary>
    private static string[] FromSongs(IEnumerable<string> names)
        => names.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).Select(g => g.Key).ToArray();

    private List<PlannedChange> BuildPlan(AnalysisReport report, RepairResult result)
    {
        var plan = new List<PlannedChange>();
        foreach (var library in report.Libraries)
        {
            foreach (var album in library.Albums)
            {
                if (album.Flags.HasFlag(DecisionFlags.Locked))
                {
                    continue;
                }

                if (Get(album.Id) is not MusicAlbum albumItem)
                {
                    result.Skipped++;
                    continue;
                }

                var albumArtists = album.MainArtists.Select(m => m.Name).ToList();
                var albumTrackArtists = album.Tracks
                    .SelectMany(t => t.Artists)
                    .GroupBy(ArtistNames.Key, StringComparer.Ordinal)
                    .OrderByDescending(g => g.Count())
                    .Select(g => g.First())
                    .ToList();
                if (!albumItem.IsLocked)
                {
                    AddIfChanged(plan, albumItem, "MusicAlbum", albumTrackArtists, albumArtists);
                }

                foreach (var track in album.Tracks.Where(t => t.CreditSource != "Locked"))
                {
                    if (Get(track.Id) is not AudioItem audio)
                    {
                        result.Skipped++;
                        continue;
                    }

                    if (!audio.IsLocked && !audio.LockedFields.Contains(MetadataField.Cast))
                    {
                        AddIfChanged(plan, audio, "Audio", track.Artists.ToList(), track.AlbumArtists.ToList());
                    }
                }
            }

            foreach (var rename in library.Summary.Renames)
            {
                if (Get(rename.Id) is MusicArtist artist
                    && !artist.IsLocked
                    && !artist.LockedFields.Contains(MetadataField.Name)
                    && string.Equals(artist.Name, rename.From, StringComparison.Ordinal))
                {
                    plan.Add(new PlannedChange
                    {
                        Item = artist,
                        Original = new ItemSnapshot { Id = Id(artist), Kind = "MusicArtist", Name = artist.Name },
                        Name = rename.To
                    });
                }
            }
        }

        return plan;
    }

    private void AddIfChanged(List<PlannedChange> plan, BaseItem item, string kind, List<string> artists, List<string> albumArtists)
    {
        var (currentArtists, currentAlbumArtists) = item switch
        {
            MusicAlbum a => (a.Artists, a.AlbumArtists),
            AudioItem t => (t.Artists, t.AlbumArtists),
            _ => ((IReadOnlyList<string>)[], (IReadOnlyList<string>)[])
        };

        var people = ManagesArtistPeople ? ArtistPeople.Wanted(albumArtists, artists) : null;
        var currentPeople = ManagesArtistPeople ? ArtistPeople.Current(_libraryManager, item) : [];
        var peopleChanged = people is not null && !ArtistPeople.SameSet(currentPeople, people);

        // Les artistes d'un album (union de ceux de ses pistes) sont recalculés par Jellyfin à chaque actualisation, dans
        // son propre ordre : on compare l'ensemble, sinon chaque scan ferait réécrire l'album à la réparation suivante.
        var artistsUnchanged = item is MusicAlbum
            ? currentArtists.ToHashSet(StringComparer.Ordinal).SetEquals(artists)
            : currentArtists.SequenceEqual(artists, StringComparer.Ordinal);
        if (artistsUnchanged
            && currentAlbumArtists.SequenceEqual(albumArtists, StringComparer.Ordinal)
            && !peopleChanged)
        {
            return;
        }

        plan.Add(new PlannedChange
        {
            Item = item,
            Original = new ItemSnapshot
            {
                Id = Id(item),
                Kind = kind,
                Artists = currentArtists.ToList(),
                AlbumArtists = currentAlbumArtists.ToList(),
                People = ManagesArtistPeople ? ArtistPeople.Snapshot(currentPeople) : null
            },
            Artists = artists,
            AlbumArtists = albumArtists,
            People = people
        });
    }

    private async Task ApplyAsync(PlannedChange change, CancellationToken cancellationToken)
    {
        var item = change.Item;
        switch (item)
        {
            case MusicArtist artist when change.Name is not null:
                artist.Name = change.Name;
                artist.PresentationUniqueKey = artist.CreatePresentationUniqueKey();
                break;
            case MusicAlbum album:
                album.Artists = change.Artists ?? album.Artists;
                album.AlbumArtists = change.AlbumArtists ?? album.AlbumArtists;
                break;
            case AudioItem audio:
                audio.Artists = change.Artists ?? audio.Artists;
                audio.AlbumArtists = change.AlbumArtists ?? audio.AlbumArtists;
                break;
        }

        await item.UpdateToRepositoryAsync(ItemUpdateType.MetadataEdit, cancellationToken).ConfigureAwait(false);

        if (ManagesArtistPeople && change.People is not null && item is MusicAlbum or AudioItem)
        {
            // Seuls les People Artist / AlbumArtist sont remplacés : compositeurs et autres rôles restent.
            await ArtistPeople.ReplaceAsync(_libraryManager, item, change.People, cancellationToken).ConfigureAwait(false);
            if (!ArtistPeople.SameSet(ArtistPeople.Current(_libraryManager, item), change.People))
            {
                _logger.LogWarning(
                    "People non alignés après écriture sur {Item} : envoyés [{Sent}], relus [{Read}]",
                    item.Path ?? item.Name,
                    string.Join(" | ", change.People.Select(p => $"{p.Name}/{p.Type}")),
                    string.Join(" | ", _libraryManager.GetPeople(item).Select(p => $"{p.Name}/{p.Type}/{p.Role}/{p.SortOrder}")));
            }
        }
    }

    /// <summary>
    /// Une fiche par nom d'artiste encore utilisé (la fiche du dossier d'abord), création des fiches manquantes,
    /// suppression des fiches « par nom » qui ne servent plus. Une fiche est gardée si son nom exact est utilisé, ou si
    /// c'est une fiche de dossier de même clé (« Dr Dre » / « Dr. Dre ») : on garde plus de fiches que Jellyfin, jamais moins.
    /// </summary>
    private List<MusicArtist> CleanupArtists(RepairSnapshot? snapshot, Action<RepairSnapshot>? persistSnapshot, RepairResult result, CancellationToken cancellationToken)
    {
        var createdArtists = new List<MusicArtist>();
        var names = _itemRepository.GetAllArtistNames();
        var all = _libraryManager.GetItemList(new InternalItemsQuery { IncludeItemTypes = [BaseItemKind.MusicArtist] }).OfType<MusicArtist>().ToList();
        var byKey = all.GroupBy(a => ArtistNames.Key(a.Name), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var live = new HashSet<Guid>();
        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = ArtistNames.Key(name);
            if (key.Length == 0)
            {
                continue;
            }

            if (byKey.TryGetValue(key, out var group) && group.Count > 0)
            {
                foreach (var keep in group.Where(a => !a.IsAccessedByName || string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    live.Add(keep.Id);
                }

                if (group.All(a => !live.Contains(a.Id)))
                {
                    live.Add(group.OrderBy(a => a.IsAccessedByName ? 1 : 0).First().Id);
                }

                continue;
            }

            // Pas d'actualisation ici : mise en file, l'actualisation d'un artiste relance celle de tous les albums
            // du dossier artiste lié (ProviderManager.RefreshArtist), et la sonde réécrit alors les People des pistes
            // depuis les tags bruts. Jellyfin complète les fiches jamais actualisées au prochain scan (ArtistsValidator).
            var created = _libraryManager.GetArtist(name);
            EnsurePresentationKey(created, cancellationToken);
            byKey[key] = [created];
            live.Add(created.Id);
            createdArtists.Add(created);
            result.ArtistsCreated++;
        }

        // Doublon « par nom » d'une fiche du dossier au nom identique (casse près) : la fiche du dossier suffit,
        // comme dans l'ArtistsValidator de Jellyfin. Un nom seulement voisin (« Dr Dre » / « Dr. Dre ») ne suffit pas :
        // en 10.11, la ponctuation compte et la fiche « par nom » peut encore servir.
        var folderNames = all.Where(a => !a.IsAccessedByName).Select(a => a.Name ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var duplicate in all.Where(a => a.IsAccessedByName && folderNames.Contains(a.Name ?? string.Empty)))
        {
            live.Remove(duplicate.Id);
        }

        var dead = all.Where(a => a.IsAccessedByName && !a.IsLocked && !live.Contains(a.Id)).ToList();
        if (dead.Count == 0)
        {
            return createdArtists;
        }

        if (snapshot is not null && persistSnapshot is not null)
        {
            snapshot.DeletedArtists.AddRange(dead.Select(a => new ArtistSnapshot
            {
                Id = Id(a),
                Name = a.Name ?? string.Empty,
                ProviderIds = new Dictionary<string, string>(a.ProviderIds, StringComparer.OrdinalIgnoreCase)
            }));
            persistSnapshot(snapshot);
        }

        foreach (var artist in dead)
        {
            try
            {
                _libraryManager.DeleteItem(artist, new DeleteOptions { DeleteFileLocation = false }, false);
                result.ArtistsDeleted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Suppression impossible de la fiche {Artist}", artist.Name);
                result.Errors.Add($"{artist.Name} : {ex.Message}");
            }
        }

        return createdArtists;
    }

    /// <summary>
    /// Une fiche créée par <c>GetArtist</c> n'a pas de clé de présentation tant qu'elle n'a pas été actualisée. Or les
    /// listes d'artistes regroupent par cette clé : sans elle, toutes les fiches neuves se fondent en une seule entrée.
    /// </summary>
    private static void EnsurePresentationKey(MusicArtist artist, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(artist.PresentationUniqueKey))
        {
            artist.PresentationUniqueKey = artist.CreatePresentationUniqueKey();
            artist.UpdateToRepositoryAsync(ItemUpdateType.MetadataImport, cancellationToken).GetAwaiter().GetResult();
        }
    }

    private BaseItem? Get(string id) => Guid.TryParse(id, out var guid) ? _libraryManager.GetItemById(guid) : null;

    private static string Id(BaseItem item) => item.Id.ToString("N");

    private sealed class PlannedChange
    {
        public required BaseItem Item { get; init; }

        public required ItemSnapshot Original { get; init; }

        public string? Name { get; init; }

        public IReadOnlyList<string>? Artists { get; init; }

        public IReadOnlyList<string>? AlbumArtists { get; init; }

        public List<PersonInfo>? People { get; init; }
    }
}
