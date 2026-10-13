# Jellyfin True Music Library

A single "Artists" tab, listing only the main artists of albums, singles and EPs, chosen from the library's metadata providers (MusicBrainz, TheAudioDB…). Featured artists stay credited on their tracks without cluttering the list. A "Repair" action fixes the existing library, and later scans keep the result.

[Version française](README.fr.md)

## Why

Out of the box, Jellyfin lists in "Artists" every name found in a tag: `Artist A;Artist B`, `Artist A feat. Artist C`, every guest of every compilation… "Album Artists" is not much better when album artist tags are inconsistent. On a large library, the list easily ends up several times longer than the number of actual main artists.

## What it does

- **Analyze** (read-only): for each release, the main artists, taken from the library's album metadata providers in their configured order (MusicBrainz release credits, then TheAudioDB…), then from tags, then from the artist folder. A report shows before / after, sources, confidence and disagreements.
- **Repair**: applies the analysis. Album artists = main artists; track artists split (`A;B`, `feat.`, `&`…) with guests kept; one entry per artist, named the way MusicBrainz credits it most often in your library; stray `A;B` entries removed. A snapshot is written before anything changes.
- **Persistence**: once repaired, the library stays repaired. Scans and "Replace all metadata" keep the decided artists (no network during the scan), and new albums are resolved right after each scan.
- **Restore**: puts every item back the way it was before the first repair, or the way its file reads today if the file was re-read since, and stops the persistence.
- **One "Artists" tab**, once the library is repaired:
  - the server serves `/Artists` as `/Artists/AlbumArtists`, for every client;
  - guests no longer show up in search results nor in the artist lists of other clients (`/Search/Hints`, `/Items`); their page stays reachable from their tracks;
  - the web interface hides the duplicate "Album Artists" tab.

Nothing is written to audio files. No metadata is locked: uninstalling the plugin and running "Replace all metadata" gives you Jellyfin's own behaviour back.

## Install

**Back up Jellyfin before the first repair** (12.x: Dashboard → Backups).

The plugin is in the k1k4k plugin repository: **Dashboard → Plugins → Manage Repositories → `+`**

| Jellyfin | URL |
|---|---|
| 12.x | `https://k1k4k.github.io/jellyfin-plugins/manifest.json` |
| 10.11.x | `https://k1k4k.github.io/jellyfin-plugins/manifest-10.11.json` |

Then **Plugins → Catalog → True Music Library → Install**, and restart Jellyfin.

Without the catalog, the install scripts copy the plugin into Jellyfin's `plugins` folder (latest release, a release zip, or built from these sources), picking the right build for your server version:

```powershell
.\install.ps1                                   # Windows, local Jellyfin
.\install.ps1 -PluginsDir "\\SERVER\jellyfin\plugins" -ServerUrl "http://SERVER:8096"
.\install.ps1 -Build                            # from the sources (.NET SDK 10, or 9 for 10.11)
.\install.ps1 -Uninstall
```

```bash
sudo ./install.sh --restart                     # Linux server (/var/lib/jellyfin/plugins)
sudo ./install.sh --uninstall
```

Then restart Jellyfin.

## Use

**Dashboard → Plugins → True Music Library**: **Analyze**, read the report, **Repair**. "Restore" undoes everything. The three actions also exist as scheduled tasks. In the report, unfold an album to impose its main artists by hand.

Settings: collaborations (`A & B` under both artists, or the first one only), compilations and soundtracks (folder artist, "Various Artists", or none), name spelling (MusicBrainz, folder, tags), guest join phrases, names never to split (`AC/DC`, `Earth, Wind & Fire`…), persistence, single "Artists" tab, guests hidden from search, "Album Artists" tab hidden in the web interface.

## Limits

- Two different artists with the same name end up on one entry.
- A wrong MusicBrainz match gives a wrong main artist: the report shows the confidence and the disagreements with the tags and the folder.
- A retagged file is only re-read by "Replace all metadata", as in Jellyfin without the plugin.

Jellyfin 12.x and 10.11. MIT license.
