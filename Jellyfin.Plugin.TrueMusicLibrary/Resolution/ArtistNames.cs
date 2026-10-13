using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>Outils sur les noms d'artistes.</summary>
public static partial class ArtistNames
{
    /// <summary>MBID de l'artiste spécial « Various Artists » de MusicBrainz.</summary>
    public const string VariousArtistsMbid = "89ad4ac3-39f7-470e-963a-56509c546377";

    private static readonly HashSet<string> _variousArtistsKeys = new(StringComparer.Ordinal)
    {
        "various artists", "various", "va", "v a", "artistes varies", "artistes divers", "divers artistes",
        "verschiedene interpreten", "varios artistas", "artisti vari", "multi interpretes"
    };

    /// <summary>
    /// Clé de comparaison : minuscules, sans accents, ponctuation remplacée par des espaces, espaces fusionnés.
    /// Même règle que le <c>GetCleanValue</c> de Jellyfin 12.x, appliquée aussi en 10.11 (où Jellyfin garde
    /// la ponctuation) : « Dr Dre » et « Dr. Dre » ont la même clé.
    /// </summary>
    public static string Key(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        var cleaned = NonWordRegex().Replace(sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant(), " ");
        return SpacesRegex().Replace(cleaned, " ").Trim();
    }

    /// <summary>Vrai si toutes les lettres du nom sont en alphabet latin (accents compris).</summary>
    public static bool IsLatin(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var c in name)
        {
            if (char.IsLetter(c) && !(c <= 'ɏ' || (c >= 'Ḁ' && c <= 'ỿ') || (c >= 'Ⱡ' && c <= 'Ɀ') || (c >= '꜠' && c <= 'ꟿ')))
            {
                return false;
            }
        }

        return true;
    }

    public static bool IsVariousArtists(string? name, string? mbid = null)
        => string.Equals(mbid, VariousArtistsMbid, StringComparison.OrdinalIgnoreCase) || _variousArtistsKeys.Contains(Key(name));

    [GeneratedRegex(@"[^\p{L}\p{N}\s]")]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
