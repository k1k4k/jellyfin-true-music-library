using Jellyfin.Plugin.TrueMusicLibrary.Resolution.MusicBrainz;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.TrueMusicLibrary.Resolution;

/// <summary>
/// Analyse d'une bibliothèque, en lecture seule, indépendante de Jellyfin.
/// 1. fiches des dossiers et écritures des tags ; 2. releases MusicBrainz de tous les albums (artistes et alias) ;
/// 3. fiches MusicBrainz des artistes de dossier ; 4. décisions ; 5. rapport.
/// </summary>
public sealed class AnalysisRunner
{
    private readonly ResolverSettings _settings;
    private readonly MusicBrainzClient? _musicBrainz;
    private readonly ILogger _logger;

    public AnalysisRunner(ResolverSettings settings, MusicBrainzClient? musicBrainz, ILogger logger)
    {
        _settings = settings;
        _musicBrainz = musicBrainz;
        _logger = logger;
    }

    public async Task<LibraryReport> AnalyzeAsync(
        LibraryInput input,
        IReadOnlyDictionary<string, OverrideEntry> overrides,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var settings = SettingsFor(_settings, input.PreferredLanguage);

        var identity = new ArtistIdentity(settings);
        var parser = new CreditParser(settings, identity.IsKnownKey);
        var notes = new List<string>();

        foreach (var folder in input.Current.FolderArtists)
        {
            identity.AddFolderArtist(folder.Name, folder.Mbid);
        }

        foreach (var value in input.Albums.SelectMany(a => a.Tracks).SelectMany(t => t.Artists.Concat(t.AlbumArtists)))
        {
            foreach (var credit in parser.ParseTagValues([value]))
            {
                identity.AddTagName(credit.Name);
            }
        }

        var sources = new List<IAlbumSource>();
        MusicBrainzSource? musicBrainzSource = null;
        foreach (var fetcher in input.Fetchers)
        {
            if (string.Equals(fetcher, MusicBrainzSource.SourceName, StringComparison.OrdinalIgnoreCase))
            {
                if (_musicBrainz is not null)
                {
                    musicBrainzSource = new MusicBrainzSource(_musicBrainz, parser, identity);
                    sources.Add(musicBrainzSource);
                }
                else
                {
                    notes.Add(Messages.MusicBrainzUnavailable);
                }
            }
            else if (input.ProviderSources.TryGetValue(fetcher, out var source))
            {
                sources.Add(source);
            }
            else
            {
                notes.Add(Messages.ProviderUnavailable(fetcher));
            }
        }

        var total = Math.Max(1, input.Albums.Count);
        if (musicBrainzSource is not null)
        {
            for (var i = 0; i < input.Albums.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await musicBrainzSource.PrefetchAsync(input.Albums[i], cancellationToken).ConfigureAwait(false);
                progress?.Report(60.0 * (i + 1) / total);
            }
        }

        if (_musicBrainz is not null)
        {
            var missing = identity.MissingMusicBrainzArtists().ToList();
            for (var i = 0; i < missing.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                identity.AddMusicBrainzArtist(await _musicBrainz.GetArtistAsync(missing[i], cancellationToken).ConfigureAwait(false));
                progress?.Report(60 + (10.0 * (i + 1) / Math.Max(1, missing.Count)));
            }
        }

        var resolver = new ReleaseArtistResolver(settings, identity, parser, sources, overrides);
        var decisions = new List<AlbumDecision>(input.Albums.Count);
        for (var i = 0; i < input.Albums.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                decisions.Add(await resolver.ResolveAsync(input.Albums[i], cancellationToken).ConfigureAwait(false));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Analyse de l'album {Album} impossible", input.Albums[i].Path);
                var failed = new AlbumDecision { AlbumId = input.Albums[i].Id, Source = DecisionSource.None, Flags = DecisionFlags.NoSource };
                failed.Notes.Add(Messages.AnalysisError(ex.Message));
                decisions.Add(failed);
            }

            progress?.Report(70 + (30.0 * (i + 1) / total));
        }

        notes.AddRange(identity.SuspiciousMbids.Order(StringComparer.CurrentCulture));
        var report = ReportBuilder.Build(input, decisions, notes, identity.MbidForName);
        report.NameMap = identity.NameMap();
        report.KnownKeys = identity.KnownKeys();
        return report;
    }

    /// <summary>Réglages d'une bibliothèque : ceux du plugin, avec la langue de métadonnées de la bibliothèque (choix des alias).</summary>
    public static ResolverSettings SettingsFor(ResolverSettings settings, string? preferredLanguage)
        => string.IsNullOrWhiteSpace(preferredLanguage)
            ? settings
            : new ResolverSettings
            {
                Collaboration = settings.Collaboration,
                Compilation = settings.Compilation,
                DisplayName = settings.DisplayName,
                GuestJoinPhrases = settings.GuestJoinPhrases,
                NameWhitelist = settings.NameWhitelist,
                VariousArtistsName = settings.VariousArtistsName,
                PreferredLanguage = preferredLanguage
            };
}

/// <summary>Met en forme le rapport d'une bibliothèque et calcule le résumé « avant / après ».</summary>
public static class ReportBuilder
{
    public static LibraryReport Build(
        LibraryInput input,
        IReadOnlyList<AlbumDecision> decisions,
        IReadOnlyList<string> notes,
        Func<string, string?>? mbidForName = null)
    {
        var byId = decisions.ToDictionary(d => d.AlbumId, StringComparer.Ordinal);
        var albums = new List<AlbumReport>(input.Albums.Count);
        var changedTracks = 0;

        foreach (var album in input.Albums.OrderBy(a => a.Path, StringComparer.OrdinalIgnoreCase))
        {
            var decision = byId[album.Id];
            var trackDecisions = decision.Tracks.ToDictionary(t => t.TrackId, StringComparer.Ordinal);
            var tracks = new List<TrackReport>();
            foreach (var track in album.Tracks.OrderBy(t => t.Disc ?? 1).ThenBy(t => t.Number ?? int.MaxValue).ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase))
            {
                var trackDecision = trackDecisions.GetValueOrDefault(track.Id);
                var artists = trackDecision?.Artists.Select(a => a.Name).ToList() ?? track.Artists.ToList();
                var albumArtists = trackDecision?.AlbumArtists.Select(a => a.Name).ToList() ?? track.AlbumArtists.ToList();
                var changed = !artists.SequenceEqual(track.Artists, StringComparer.Ordinal) || !albumArtists.SequenceEqual(track.AlbumArtists, StringComparer.Ordinal);
                if (changed)
                {
                    changedTracks++;
                }

                tracks.Add(new TrackReport
                {
                    Id = track.Id,
                    Name = track.Name,
                    Disc = track.Disc,
                    Number = track.Number,
                    CurrentArtists = track.Artists,
                    CurrentAlbumArtists = track.AlbumArtists,
                    Artists = artists,
                    AlbumArtists = albumArtists,
                    SourceArtists = trackDecision?.SourceArtists?.Select(a => a.Name).ToList(),
                    CreditSource = trackDecision?.CreditSource ?? string.Empty,
                    Changed = changed
                });
            }

            albums.Add(new AlbumReport
            {
                Id = album.Id,
                Name = album.Name,
                Path = Relative(album.Path, input.Path),
                CurrentAlbumArtists = album.AlbumArtists,
                MainArtists = decision.MainArtists,
                Source = decision.Source,
                SourceName = decision.SourceName,
                Confidence = decision.Confidence,
                Flags = decision.Flags,
                ReleaseType = decision.ReleaseType,
                Notes = decision.Notes,
                Changed = !decision.MainArtists.Select(m => m.Name).SequenceEqual(album.AlbumArtists, StringComparer.Ordinal),
                InputHash = decision.InputHash,
                Tracks = tracks
            });
        }

        var proposed = decisions
            .SelectMany(d => d.MainArtists)
            .GroupBy(a => ArtistNames.Key(a.Name), StringComparer.Ordinal)
            .Select(g => new ProposedArtist(g.First().Name, g.FirstOrDefault(a => a.Mbid is not null)?.Mbid, decisions.Count(d => d.MainArtists.Any(m => ArtistNames.Key(m.Name) == g.Key))))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var proposedByKey = proposed.ToDictionary(p => ArtistNames.Key(p.Name), StringComparer.Ordinal);
        var proposedByMbid = proposed.Where(p => p.Mbid is not null).GroupBy(p => p.Mbid!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var trackArtistKeys = decisions.SelectMany(d => d.Tracks).SelectMany(t => t.Artists.Concat(t.AlbumArtists))
            .Select(a => ArtistNames.Key(a.Name)).ToHashSet(StringComparer.Ordinal);

        var removed = new List<RemovedArtist>();
        var renames = new List<RenamedArtist>();
        var folderNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entity in input.Current.ArtistsTab.Concat(input.Current.AlbumArtistsTab).Concat(input.Current.FolderArtists).DistinctBy(e => e.Id))
        {
            var key = ArtistNames.Key(entity.Name);
            if (proposedByKey.TryGetValue(key, out var target))
            {
                if (entity.IsFolder)
                {
                    folderNames[entity.Id] = target.Name;
                    if (!string.Equals(entity.Name, target.Name, StringComparison.Ordinal))
                    {
                        renames.Add(new RenamedArtist(entity.Id, entity.Name, target.Name));
                    }
                }
            }
            else if (input.Current.ArtistsTab.Any(e => e.Id == entity.Id) || input.Current.AlbumArtistsTab.Any(e => e.Id == entity.Id))
            {
                var mbid = entity.Mbid ?? mbidForName?.Invoke(entity.Name);
                if (mbid is not null && proposedByMbid.TryGetValue(mbid, out var into))
                {
                    removed.Add(new RemovedArtist(entity.Id, entity.Name, "Merged", entity.Favorites, entity.Played, into.Name));
                }
                else
                {
                    removed.Add(new RemovedArtist(entity.Id, entity.Name, trackArtistKeys.Contains(key) ? "Hidden" : "Deleted", entity.Favorites, entity.Played));
                }
            }
        }

        var summary = new LibrarySummary
        {
            Albums = input.Albums.Count,
            Tracks = input.Albums.Sum(a => a.Tracks.Count),
            CurrentArtistsTab = input.Current.ArtistsTab.Count,
            CurrentAlbumArtistsTab = input.Current.AlbumArtistsTab.Count,
            Fetchers = input.Fetchers.ToList(),
            Notes = notes.ToList(),
            ProposedArtists = proposed,
            Removed = removed.OrderBy(r => r.Fate, StringComparer.Ordinal).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList(),
            Renames = renames.OrderBy(r => r.From, StringComparer.CurrentCultureIgnoreCase).ToList(),
            BySource = decisions.GroupBy(d => d.Source == DecisionSource.Provider ? d.SourceName ?? "Provider" : d.Source.ToString())
                .ToDictionary(g => g.Key, g => g.Count()),
            ByFlag = Enum.GetValues<DecisionFlags>().Where(f => f != DecisionFlags.None)
                .ToDictionary(f => f.ToString(), f => decisions.Count(d => d.Flags.HasFlag(f))),
            ChangedAlbums = albums.Count(a => a.Changed),
            ChangedTracks = changedTracks
        };

        return new LibraryReport { Id = input.Id, Name = input.Name, Summary = summary, Albums = albums, FolderArtistNames = folderNames };
    }

    private static string Relative(string path, string? root)
    {
        if (string.IsNullOrEmpty(root) || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        return path[root.Length..].TrimStart('/', '\\').Replace('\\', '/');
    }
}
