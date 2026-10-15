# Changelog

What's new in each version, as shown in Jellyfin's plugin catalog.

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
