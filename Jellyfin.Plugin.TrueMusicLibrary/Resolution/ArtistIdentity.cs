using Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>
/// Une fiche par artiste réel : relie MBID, noms MusicBrainz, alias, noms de dossiers et écritures
/// vues dans les tags, et choisit le nom affiché. Toutes les variantes d'écriture d'un même artiste
/// (« Dr Dre », « Dr. Dre ») donnent le même nom.
/// </summary>
public sealed class ArtistIdentity
{
    private readonly ResolverSettings _settings;
    private readonly Dictionary<string, MbArtist> _musicBrainz = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _mbidByNameKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _mbidByAliasKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _folderByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _folderByMbid = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, int>> _tagSpellings = new(StringComparer.Ordinal);
    private readonly HashSet<string> _suspicious = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, int>> _creditedNames = new(StringComparer.OrdinalIgnoreCase);

    public ArtistIdentity(ResolverSettings settings)
    {
        _settings = settings;
    }

    /// <summary>Artiste MusicBrainz (vu dans un crédit, ou lu par MBID). Ses alias d'artiste servent aussi de clés.</summary>
    public void AddMusicBrainzArtist(MbArtist? artist)
    {
        if (artist is null || !Guid.TryParse(artist.Id, out _) || string.IsNullOrWhiteSpace(artist.Name))
        {
            return;
        }

        if (!_musicBrainz.TryGetValue(artist.Id, out var known) || (known.Aliases is null && artist.Aliases is not null))
        {
            _musicBrainz[artist.Id] = artist;
        }

        MapKey(_mbidByNameKey, artist.Name, artist.Id);
        foreach (var alias in artist.Aliases ?? [])
        {
            // Les noms légaux (le nom civil d'un artiste) ne sont pas des noms d'artiste : pas de fusion.
            if (!string.Equals(alias.Type, "Legal name", StringComparison.OrdinalIgnoreCase))
            {
                MapKey(_mbidByAliasKey, alias.Name, artist.Id);
            }
        }
    }

    /// <summary>Artistes de tous les crédits d'une release (album et pistes), avec les noms tels que crédités.</summary>
    public void AddRelease(MbRelease? release)
    {
        foreach (var credit in release?.ArtistCredit ?? [])
        {
            AddCredit(credit);
        }

        foreach (var track in release?.Media?.SelectMany(m => m.Tracks ?? []) ?? [])
        {
            foreach (var credit in track.ArtistCredit ?? track.Recording?.ArtistCredit ?? [])
            {
                AddCredit(credit);
            }
        }
    }

    /// <summary>Un crédit MusicBrainz : l'artiste, et le nom sous lequel il est crédité (« Snoop Doggy Dogg » sur une release d'un artiste devenu « Snoop Dogg »).</summary>
    public void AddCredit(MbArtistCredit credit)
    {
        AddMusicBrainzArtist(credit.Artist);
        if (credit.Artist is null || !Guid.TryParse(credit.Artist.Id, out _) || string.IsNullOrWhiteSpace(credit.Name))
        {
            return;
        }

        if (!_creditedNames.TryGetValue(credit.Artist.Id, out var names))
        {
            _creditedNames[credit.Artist.Id] = names = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        names[credit.Name.Trim()] = names.GetValueOrDefault(credit.Name.Trim()) + 1;
        MapKey(_mbidByAliasKey, credit.Name, credit.Artist.Id);
    }

    /// <summary>Fiche artiste d'un dossier (« Artiste/Album »), avec le MBID que Jellyfin lui a trouvé.</summary>
    public void AddFolderArtist(string name, string? mbid)
    {
        var key = ArtistNames.Key(name);
        if (key.Length == 0)
        {
            return;
        }

        _folderByKey.TryAdd(key, name);
        if (Guid.TryParse(mbid, out _))
        {
            MapKey(_mbidByNameKey, name, mbid!);
            _folderByMbid.TryAdd(mbid!, name);
        }
    }

    /// <summary>Écriture vue dans un tag (pour choisir l'écriture la plus fréquente).</summary>
    public void AddTagName(string name)
    {
        var key = ArtistNames.Key(name);
        if (key.Length == 0)
        {
            return;
        }

        if (!_tagSpellings.TryGetValue(key, out var spellings))
        {
            _tagSpellings[key] = spellings = new Dictionary<string, int>(StringComparer.Ordinal);
        }

        spellings[name.Trim()] = spellings.GetValueOrDefault(name.Trim()) + 1;
    }

    /// <summary>MBID connus sans fiche MusicBrainz chargée (à lire avant la résolution).</summary>
    public IEnumerable<string> MissingMusicBrainzArtists()
        => _mbidByNameKey.Values.Distinct(StringComparer.OrdinalIgnoreCase).Where(id => !_musicBrainz.ContainsKey(id));

    /// <summary>Vrai si la clé est un artiste connu (MusicBrainz ou dossier) : le découpeur ne le coupe jamais.</summary>
    public bool IsKnownKey(string key) => _mbidByNameKey.ContainsKey(key) || _folderByKey.ContainsKey(key) || _mbidByAliasKey.ContainsKey(key);

    /// <summary>MBID d'un nom : d'abord par nom d'artiste (MusicBrainz, dossier), ensuite par alias. Un MBID incohérent avec le nom est ignoré.</summary>
    public string? MbidForName(string? name)
    {
        var key = ArtistNames.Key(name);
        return ValidMbid(name, _mbidByNameKey.GetValueOrDefault(key) ?? _mbidByAliasKey.GetValueOrDefault(key));
    }

    /// <summary>
    /// MBID rattachés par Jellyfin à un nom qui ne leur correspond pas (ex. fiche d'un dossier de bande originale identifiée
    /// comme un autre artiste) : ignorés, et signalés dans le rapport.
    /// </summary>
    public IReadOnlyCollection<string> SuspiciousMbids => _suspicious;

    /// <summary>Nom affiché et MBID d'un artiste.</summary>
    public ArtistRef Resolve(string name, string? mbid = null)
    {
        var key = ArtistNames.Key(name);
        mbid = Guid.TryParse(mbid, out _) ? ValidMbid(name, mbid) : MbidForName(name);

        var musicBrainz = mbid is not null && _musicBrainz.TryGetValue(mbid, out var artist) ? MusicBrainzDisplayName(artist) : null;
        var folder = _folderByKey.GetValueOrDefault(key) ?? (mbid is null ? null : _folderByMbid.GetValueOrDefault(mbid));
        var tag = MostFrequentSpelling(key);

        var display = _settings.DisplayName switch
        {
            DisplayNameMode.Folder => folder ?? musicBrainz ?? tag,
            DisplayNameMode.Tag => tag ?? musicBrainz ?? folder,
            _ => musicBrainz ?? tag ?? folder
        };

        return new ArtistRef(display ?? name.Trim(), mbid);
    }

    public ArtistRef Resolve(Credit credit) => Resolve(credit.Name, credit.Mbid);

    /// <summary>
    /// Toutes les écritures connues (par clé) et leur nom affiché : tags, dossiers, noms, alias et noms crédités
    /// MusicBrainz. Les fournisseurs s'en servent pendant un scan, sans réseau, pour donner aux tags relus le même nom
    /// que l'analyse (« Dr Dre » → « Dr. Dre », « 坂本龍一 » → « Ryuichi Sakamoto »).
    /// </summary>
    public Dictionary<string, string> NameMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in _tagSpellings.Keys.Concat(_folderByKey.Keys).Concat(_mbidByNameKey.Keys).Concat(_mbidByAliasKey.Keys))
        {
            if (map.ContainsKey(key))
            {
                continue;
            }

            // Toutes les écritures d'une même clé se résolvent pareil : on prend celle des tags, du dossier, ou la clé.
            var sample = MostFrequentSpelling(key) ?? _folderByKey.GetValueOrDefault(key);
            var name = Resolve(sample ?? key).Name;
            if (sample is not null || !string.Equals(name, key, StringComparison.Ordinal))
            {
                map[key] = name;
            }
        }

        return map;
    }

    /// <summary>Clés que le découpeur ne coupe jamais (<see cref="IsKnownKey"/>), pour le refaire à l'identique sans l'analyse.</summary>
    public HashSet<string> KnownKeys()
        => _mbidByNameKey.Keys.Concat(_folderByKey.Keys).Concat(_mbidByAliasKey.Keys).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Écriture MusicBrainz : le nom sous lequel l'artiste est le plus souvent crédité dans la médiathèque
    /// (« Snoop Doggy Dogg » plutôt que son nom MusicBrainz actuel « Snoop Dogg »), puis son nom MusicBrainz ; s'il n'est pas en
    /// alphabet latin : alias dans la langue préférée, puis alias anglais principal, puis tout alias latin.
    /// </summary>
    public string MusicBrainzDisplayName(MbArtist artist)
    {
        var name = artist.Name ?? string.Empty;
        if (_creditedNames.TryGetValue(artist.Id, out var credited))
        {
            var best = credited
                .Where(c => ArtistNames.IsLatin(c.Key))
                .OrderByDescending(c => c.Value)
                .ThenByDescending(c => string.Equals(c.Key, name, StringComparison.Ordinal))
                .ThenBy(c => c.Key, StringComparer.Ordinal)
                .Select(c => c.Key)
                .FirstOrDefault();
            if (best is not null)
            {
                return best;
            }
        }

        if (ArtistNames.IsLatin(name))
        {
            return name;
        }

        var aliases = (artist.Aliases ?? [])
            .Where(a => ArtistNames.IsLatin(a.Name) && !string.Equals(a.Type, "Legal name", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var chosen = aliases.FirstOrDefault(a => Lang(a.Locale) == Lang(_settings.PreferredLanguage) && a.Primary == true)
            ?? aliases.FirstOrDefault(a => Lang(a.Locale) == "en" && a.Primary == true)
            ?? aliases.FirstOrDefault(a => a.Primary == true)
            ?? aliases.FirstOrDefault(a => string.Equals(a.Type, "Artist name", StringComparison.OrdinalIgnoreCase));
        return chosen?.Name ?? name;
    }

    /// <summary>
    /// Le MBID est gardé si le nom correspond à l'artiste MusicBrainz : même clé que son nom ou qu'un de ses alias,
    /// ou écriture très proche (faute de frappe). Tant que la fiche MusicBrainz n'est pas chargée, on lui fait confiance.
    /// </summary>
    private string? ValidMbid(string? name, string? mbid)
    {
        if (mbid is null || string.IsNullOrWhiteSpace(name) || !_musicBrainz.TryGetValue(mbid, out var artist))
        {
            return mbid;
        }

        var key = ArtistNames.Key(name);
        var mbKey = ArtistNames.Key(artist.Name);
        if (key == mbKey
            || (artist.Aliases ?? []).Any(a => ArtistNames.Key(a.Name) == key)
            || (_creditedNames.TryGetValue(mbid, out var credited) && credited.Keys.Any(c => ArtistNames.Key(c) == key))
            || Similarity(key, mbKey) >= 0.85)
        {
            return mbid;
        }

        _suspicious.Add(Messages.SuspiciousMbid(name.Trim(), artist.Name ?? string.Empty, mbid));
        return null;
    }

    /// <summary>1 - distance de Levenshtein normalisée.</summary>
    private static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return 1.0 - ((double)previous[b.Length] / Math.Max(a.Length, b.Length));
    }

    private static string Lang(string? locale)
        => string.IsNullOrWhiteSpace(locale) ? string.Empty : locale.Split('_', '-')[0].ToLowerInvariant();

    private string? MostFrequentSpelling(string key)
        => _tagSpellings.TryGetValue(key, out var spellings) && spellings.Count > 0
            ? spellings.OrderByDescending(s => s.Value).ThenBy(s => s.Key, StringComparer.Ordinal).First().Key
            : null;

    private static void MapKey(Dictionary<string, string> map, string? name, string mbid)
    {
        var key = ArtistNames.Key(name);
        if (key.Length > 0)
        {
            // Premier arrivé gardé : une clé ambiguë (homonymes) ne doit pas basculer d'un artiste à l'autre.
            map.TryAdd(key, mbid);
        }
    }
}
