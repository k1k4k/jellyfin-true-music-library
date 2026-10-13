using System.Text.RegularExpressions;
using Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>
/// Découpe les crédits :
/// <list type="bullet">
/// <item>chaînes de tags : « A;B », « A / B », « A feat. B », « A &amp; B », « A, B and C »…, avec une liste blanche
/// et les artistes connus (MusicBrainz, dossiers) protégés ;</item>
/// <item>crédits MusicBrainz : les jointures disent qui est principal et qui est invité.</item>
/// </list>
/// </summary>
public sealed partial class CreditParser
{
    private static readonly string[] _articlesAfterSeparator = ["the ", "his ", "her ", "their ", "les ", "la ", "le "];

    private readonly HashSet<string> _whitelistKeys;
    private readonly Func<string, bool> _isKnownKey;
    private readonly Regex _guestRegex;

    /// <param name="settings">Réglages (jointures d'invité, liste blanche).</param>
    /// <param name="isKnownKey">Vrai si la clé (<see cref="ArtistNames.Key"/>) est un artiste connu : il n'est jamais coupé.</param>
    public CreditParser(ResolverSettings settings, Func<string, bool>? isKnownKey = null)
    {
        _whitelistKeys = settings.NameWhitelist.Select(ArtistNames.Key).Where(k => k.Length > 0).ToHashSet(StringComparer.Ordinal);
        _isKnownKey = isKnownKey ?? (_ => false);

        var alternatives = settings.GuestJoinPhrases
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p.Length > 0)
            .SelectMany(p => p.EndsWith('.') ? new[] { Regex.Escape(p.TrimEnd('.')) + @"\.?" } : new[] { Regex.Escape(p) })
            .Append(@"ft\.?")
            .Append(@"feat\.?")
            .Distinct();
        _guestRegex = new Regex(@"(?:^|\s|[\(\[])(?:" + string.Join("|", alternatives) + @")(?=\s|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>Découpe les valeurs d'artistes d'une piste (telles que Jellyfin les a stockées).</summary>
    public List<Credit> ParseTagValues(IEnumerable<string> values)
    {
        var credits = new List<Credit>();
        foreach (var value in values)
        {
            foreach (var (segment, index) in SplitHard(value).Select((s, i) => (s, i)))
            {
                var isFirstSegment = credits.Count == 0 && index == 0;
                var (main, guest) = SplitGuest(segment);
                foreach (var name in SplitCollab(main))
                {
                    Add(credits, name, isFirstSegment || credits.Count == 0 ? CreditRole.Main : CreditRole.Guest, null);
                }

                if (guest is not null)
                {
                    foreach (var name in SplitCollab(guest))
                    {
                        Add(credits, name, CreditRole.Guest, null);
                    }
                }
            }
        }

        return credits;
    }

    /// <summary>Crédits MusicBrainz : principaux jusqu'à la première jointure d'invité (« feat. », « with »…).</summary>
    public List<Credit> FromMusicBrainz(IReadOnlyList<MbArtistCredit>? credits)
    {
        var result = new List<Credit>();
        if (credits is null)
        {
            return result;
        }

        var guest = false;
        foreach (var credit in credits)
        {
            var name = credit.Artist?.Name ?? credit.Name;
            if (!string.IsNullOrWhiteSpace(name))
            {
                Add(result, name, guest ? CreditRole.Guest : CreditRole.Main, credit.Artist?.Id);
            }

            if (IsGuestJoinPhrase(credit.JoinPhrase))
            {
                guest = true;
            }
        }

        return result;
    }

    /// <summary>Vrai si la jointure annonce un invité (« feat. », « with »…).</summary>
    public bool IsGuestJoinPhrase(string? joinPhrase)
        => !string.IsNullOrWhiteSpace(joinPhrase) && _guestRegex.IsMatch(" " + joinPhrase.Trim() + " ");

    /// <summary>Artistes principaux selon le réglage des collaborations.</summary>
    public static IReadOnlyList<Credit> MainCredits(IReadOnlyList<Credit> credits, CollaborationMode mode)
    {
        var mains = credits.Where(c => c.Role == CreditRole.Main).ToList();
        if (mains.Count == 0 && credits.Count > 0)
        {
            mains.Add(credits[0]);
        }

        return mode == CollaborationMode.FirstOnly && mains.Count > 1 ? [mains[0]] : mains;
    }

    private bool IsProtected(string text)
    {
        var key = ArtistNames.Key(text);
        return key.Length > 0 && (_whitelistKeys.Contains(key) || _isKnownKey(key));
    }

    private IEnumerable<string> SplitHard(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        if (IsProtected(value))
        {
            yield return value.Trim();
            yield break;
        }

        foreach (var piece in HardSeparatorRegex().Split(value))
        {
            if (string.IsNullOrWhiteSpace(piece))
            {
                continue;
            }

            if (IsProtected(piece) || !piece.Contains('/', StringComparison.Ordinal))
            {
                yield return piece.Trim();
                continue;
            }

            foreach (var sub in piece.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return sub;
            }
        }
    }

    private (string Main, string? Guest) SplitGuest(string segment)
    {
        if (IsProtected(segment))
        {
            return (segment, null);
        }

        var match = _guestRegex.Match(segment);
        if (!match.Success || match.Index == 0)
        {
            return (segment, null);
        }

        var main = segment[..match.Index].TrimEnd(' ', '(', '[');
        var guest = segment[(match.Index + match.Length)..].Trim().TrimEnd(')', ']').Trim();
        return main.Length == 0 ? (segment, null) : (main, guest.Length == 0 ? null : guest);
    }

    private List<string> SplitCollab(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        if (IsProtected(text))
        {
            return [text];
        }

        var parts = CollabSeparatorRegex().Split(text);

        // parts = [nom, séparateur, nom, séparateur, nom…] (le groupe capturant garde les séparateurs)
        var result = new List<string>();
        var current = parts[0];
        for (var i = 1; i + 1 < parts.Length; i += 2)
        {
            var separator = parts[i];
            var next = parts[i + 1];
            if (ShouldSplit(current, next))
            {
                result.Add(current);
                current = next;
            }
            else
            {
                current = current + separator + next;
            }
        }

        result.Add(current);
        return result.Select(r => r.Trim()).Where(r => r.Length > 0).ToList();
    }

    private bool ShouldSplit(string left, string right)
    {
        var r = right.TrimStart();
        if (_articlesAfterSeparator.Any(a => r.StartsWith(a, StringComparison.OrdinalIgnoreCase)))
        {
            // « Florence and the Machine », « Tyler, The Creator » : on ne coupe que si la suite est un artiste connu.
            return _isKnownKey(ArtistNames.Key(r));
        }

        return left.Trim().Length > 0 && r.Length > 0;
    }

    private static void Add(List<Credit> credits, string rawName, CreditRole role, string? mbid)
    {
        var name = rawName.Trim().Trim('(', ')', '[', ']', '"').Trim();
        var key = ArtistNames.Key(name);
        if (key.Length == 0)
        {
            return;
        }

        var existing = credits.FindIndex(c => ArtistNames.Key(c.Name) == key);
        if (existing < 0)
        {
            credits.Add(new Credit(name, role, mbid));
        }
        else if (role == CreditRole.Main && credits[existing].Role == CreditRole.Guest)
        {
            credits[existing] = credits[existing] with { Role = CreditRole.Main };
        }
    }

    [GeneratedRegex(@"\s*[;|\\\u0000]\s*")]
    private static partial Regex HardSeparatorRegex();

    [GeneratedRegex(@"(\s+(?:&|and|et|x|X|vs\.?|VS\.?)\s+|\s*,\s+)")]
    private static partial Regex CollabSeparatorRegex();
}
