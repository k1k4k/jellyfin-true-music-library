using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;

// Sous-ensemble de l'API MusicBrainz WS/2 (JSON) dont le plugin a besoin.

public sealed class MbRelease
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MbArtistCredit>? ArtistCredit { get; set; }

    [JsonPropertyName("release-group")]
    public MbReleaseGroup? ReleaseGroup { get; set; }

    [JsonPropertyName("media")]
    public List<MbMedium>? Media { get; set; }
}

public sealed class MbReleaseGroup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("primary-type")]
    public string? PrimaryType { get; set; }

    [JsonPropertyName("secondary-types")]
    public List<string>? SecondaryTypes { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MbArtistCredit>? ArtistCredit { get; set; }

    /// <summary>« Album », « Single », « EP »… suivi des types secondaires (« Album / Soundtrack »).</summary>
    public string? DescribeType()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(PrimaryType))
        {
            parts.Add(PrimaryType);
        }

        if (SecondaryTypes is { Count: > 0 })
        {
            parts.AddRange(SecondaryTypes);
        }

        return parts.Count == 0 ? null : string.Join(" / ", parts);
    }
}

public sealed class MbMedium
{
    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("tracks")]
    public List<MbTrack>? Tracks { get; set; }
}

public sealed class MbTrack
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("position")]
    public int Position { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("length")]
    public long? Length { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MbArtistCredit>? ArtistCredit { get; set; }

    [JsonPropertyName("recording")]
    public MbRecording? Recording { get; set; }
}

public sealed class MbRecording
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("artist-credit")]
    public List<MbArtistCredit>? ArtistCredit { get; set; }
}

public sealed class MbArtistCredit
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("joinphrase")]
    public string? JoinPhrase { get; set; }

    [JsonPropertyName("artist")]
    public MbArtist? Artist { get; set; }
}

public sealed class MbArtist
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("sort-name")]
    public string? SortName { get; set; }

    [JsonPropertyName("aliases")]
    public List<MbAlias>? Aliases { get; set; }
}

public sealed class MbAlias
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("primary")]
    public bool? Primary { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
