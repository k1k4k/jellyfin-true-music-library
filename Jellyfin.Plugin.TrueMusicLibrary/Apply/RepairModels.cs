namespace Jellyfin.Plugin.TrueMusicLibrary.Apply;

/// <summary>
/// Valeurs d'avant une réparation : enregistré sur disque AVANT la première écriture, complété avant
/// chaque suppression de fiche. « Restaurer » le réapplique. La persistance en écrit aussi : valeurs remplacées par les
/// fournisseurs pendant les scans et les actualisations, puis par la tâche d'après scan.
/// </summary>
public sealed class RepairSnapshot
{
    public RepairSnapshot()
    {
        CreatedAt = DateTime.UtcNow;
        Id = CreatedAt.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string Id { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>« Repair » (tâche Réparer), « Providers » (fournisseurs pendant un scan ou une actualisation) ou « Scan » (tâche d'après scan).</summary>
    public string Origin { get; set; } = "Repair";

    public string? PluginVersion { get; set; }

    public string? Target { get; set; }

    public bool Restored { get; set; }

    public DateTime? RestoredAt { get; set; }

    /// <summary>Éléments modifiés, avec leurs valeurs d'origine.</summary>
    public List<ItemSnapshot> Items { get; set; } = [];

    /// <summary>Fiches artiste supprimées par le nettoyage (recréées par nom à la restauration).</summary>
    public List<ArtistSnapshot> DeletedArtists { get; set; } = [];
}

public sealed class ItemSnapshot
{
    public required string Id { get; set; }

    /// <summary>« MusicAlbum », « Audio » ou « MusicArtist ».</summary>
    public required string Kind { get; set; }

    /// <summary>Nom d'origine (fiches artiste renommées).</summary>
    public string? Name { get; set; }

    public List<string>? Artists { get; set; }

    public List<string>? AlbumArtists { get; set; }

    /// <summary>People Artist / AlbumArtist d'origine (les autres ne sont jamais touchés).</summary>
    public List<PersonSnapshot>? People { get; set; }

    /// <summary>
    /// Valeurs natives : notées par un fournisseur juste après la sonde qui relisait le fichier (« Remplacer toutes les
    /// métadonnées », nouvelle piste). À la restauration, la plus récente l'emporte : c'est ce que le fichier dit.
    /// </summary>
    public bool Native { get; set; }
}

public sealed class PersonSnapshot
{
    public required string Name { get; set; }

    public required string Type { get; set; }

    public string? Role { get; set; }

    public int? SortOrder { get; set; }
}

public sealed class ArtistSnapshot
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public Dictionary<string, string> ProviderIds { get; set; } = [];
}

/// <summary>Bilan d'une réparation, d'une restauration ou d'une mise à jour après scan.</summary>
public sealed class RepairResult
{
    /// <summary>« Repair », « Restore » ou « Scan ».</summary>
    public string Operation { get; set; } = "Repair";

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinishedAt { get; set; }

    public string? SnapshotId { get; set; }

    public int AlbumsUpdated { get; set; }

    public int TracksUpdated { get; set; }

    public int ArtistsRenamed { get; set; }

    public int ArtistsCreated { get; set; }

    public int ArtistsDeleted { get; set; }

    /// <summary>Éléments ignorés : verrouillés, ou disparus depuis l'analyse.</summary>
    public int Skipped { get; set; }

    /// <summary>Changements que l'analyse refaite après coup trouve encore (0 attendu après une réparation).</summary>
    public int? RemainingAlbumChanges { get; set; }

    public int? RemainingTrackChanges { get; set; }

    public List<string> Errors { get; set; } = [];
}
