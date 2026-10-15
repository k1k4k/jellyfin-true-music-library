using Jellyfin.Data.Enums;
using Jellyfin.Plugin.TrueMusicLibrary.Apply;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.TrueMusicLibrary.Persistence;

/// <summary>
/// People Artist / AlbumArtist d'un album ou d'une piste (12.x) : alignés sur ses champs Artists / AlbumArtists, les autres
/// rôles (compositeurs…) jamais touchés. Jellyfin 10.11 ne les stocke pas.
/// </summary>
internal static class ArtistPeople
{
    public static bool Managed =>
#if JF10
        false;
#else
        true;
#endif

    public static IReadOnlyList<PersonInfo> Current(ILibraryManager libraryManager, BaseItem item)
        => libraryManager.GetPeople(item).Where(p => p.Type is PersonKind.Artist or PersonKind.AlbumArtist).ToList();

    public static List<PersonInfo> Wanted(IReadOnlyList<string> albumArtists, IReadOnlyList<string> artists)
        => albumArtists.Select(n => new PersonInfo { Name = n, Type = PersonKind.AlbumArtist })
            .Concat(artists.Select(n => new PersonInfo { Name = n, Type = PersonKind.Artist }))
            .ToList();

    /// <summary>Comparaison sans ordre : Jellyfin ne rend pas toujours les People dans l'ordre où ils ont été écrits.</summary>
    public static bool SameSet(IEnumerable<PersonInfo> a, IEnumerable<PersonInfo> b)
        => a.Select(p => (p.Name, p.Type)).ToHashSet().SetEquals(b.Select(p => (p.Name, p.Type)));

    public static List<PersonSnapshot> Snapshot(IEnumerable<PersonInfo> people)
        => people.Select(p => new PersonSnapshot { Name = p.Name, Type = p.Type.ToString(), Role = p.Role, SortOrder = p.SortOrder }).ToList();

    /// <summary>Remplace les People Artist / AlbumArtist, garde les autres.</summary>
    public static Task ReplaceAsync(ILibraryManager libraryManager, BaseItem item, IEnumerable<PersonInfo> artistPeople, CancellationToken cancellationToken)
    {
        var people = libraryManager.GetPeople(item)
            .Where(p => p.Type is not (PersonKind.Artist or PersonKind.AlbumArtist))
            .Select(Fresh)
            .Concat(artistPeople)
            .ToList();
        return libraryManager.UpdatePeopleAsync(item, people, cancellationToken);
    }

    /// <summary>
    /// Copie d'un crédit gardé (compositeur…) sans son Id, comme les crédits que Jellyfin construit lui-même. Jellyfin 12
    /// cherche les noms existants avec le <c>lower()</c> de SQLite, qui ne met en minuscules que l'ASCII : un nom comme
    /// « Érick Benzi » n'est pas retrouvé, et le recréer avec l'Id de la ligne déjà chargée fait échouer toute l'écriture
    /// (« The instance of entity type 'People' cannot be tracked… »).
    /// </summary>
    private static PersonInfo Fresh(PersonInfo person) => new()
    {
        Name = person.Name,
        Role = person.Role,
        Type = person.Type,
        SortOrder = person.SortOrder,
        ImageUrl = person.ImageUrl,
        ProviderIds = new Dictionary<string, string>(person.ProviderIds ?? [], StringComparer.OrdinalIgnoreCase)
    };
}
