# Changelog

What's new in each version, as shown in Jellyfin's plugin catalog.

## 0.2.1

- Update after additions: the plugin now waits until Jellyfin has read the added tracks and albums (tags, MusicBrainz identifiers, cover art) before deciding. When Jellyfin's refresh queue was busy, the plugin could decide from the folder name alone and do it again a few minutes later.
- Fixed (Jellyfin 12): albums and tracks rewritten at every repair, scan or update when Jellyfin keeps an artist under another capitalization (e.g. "CyberPriest" vs "CYBERPRIEST"). Nothing visible changed, but these writes were useless.

## 0.2.0

- New: albums added while Jellyfin's real time monitoring is on are handled without waiting for a library scan. About 2 minutes after the last change, new albums are resolved (MusicBrainz included), guests split, duplicate artist entries removed (Jellyfin can create the same artist twice while it is still recognizing a new artist folder), and removed or retagged albums are followed too. Setting "Update after additions", on by default; needs "Keep the library repaired" and real time monitoring on the library.

## 0.1.3

- Fixed: the analysis could take days when the server could not reach MusicBrainz properly (typically IPv6 announced but not working, in Docker or on a NAS): every request waited 100 seconds, four times. MusicBrainz is now reached through Jellyfin's own connection (the one its MusicBrainz provider uses, which follows Jellyfin's IPv6 setting and falls back to IPv4), a request gives up after 30 seconds, and after a few requests in a row without an answer the analysis goes on without MusicBrainz and says so in the report.

## 0.1.2

- Fixed (Jellyfin 12): "The instance of entity type 'People' cannot be tracked…" when a track also credits a composer, lyricist… whose name has an accented capital letter (e.g. "Érick Benzi"). The track was left unchanged. Run "Repair" again to fix the tracks that failed.
- New setting "Non-Latin names" (Japanese, Chinese, Korean, Cyrillic…): transliterated into Latin characters (as before), original script, or original script for a list of artists only.
- New setting "Order of the main artist sources": providers, tags and folder in the order you choose, e.g. tags first to trust your own album artist tags. Track credits still come from MusicBrainz whenever the album is identified there.

## 0.1.1

- Renamed "Jellyfin True Music Library", like the other k1k4k plugins.

## 0.1.0

- One "Artists" list with only the main artists of albums, singles and EPs, chosen from the library's metadata providers (MusicBrainz, TheAudioDB…), then the tags, then the artist folder. Collaborations show up under each artist; compilations and soundtracks under the folder artist.
- Featured artists stay credited on their tracks (`A;B`, `feat.`, `&`… are split), but no longer clutter the artist list nor the search. Their page stays reachable from their tracks.
- Analyze: a read-only report shows, for every album, the current and proposed artists, the source, the confidence and the disagreements. You can impose the main artists of an album by hand.
- Repair: applies the analysis after saving a snapshot. Stray entries such as `A;B` are removed, and artists are named the way MusicBrainz credits them most often.
- The library stays repaired: scans and "Replace all metadata" keep the decided artists, and new albums are resolved after each scan.
- Restore: puts everything back the way Jellyfin had it, and stops the plugin from fixing anything.
- Web interface: the duplicate "Album Artists" tab is hidden.
- No audio file is ever changed, and no metadata is locked. Back up Jellyfin before the first repair.
