<p align="center">
  <img src="icon.png" width="520" alt="Jellyfin True Music Library">
</p>

<p align="center">
  <a href="https://github.com/k1k4k/jellyfin-true-music-library/releases/latest"><img src="https://img.shields.io/github/v/release/k1k4k/jellyfin-true-music-library?label=version&color=00a4dc" alt="Latest release"></a>
  <img src="https://img.shields.io/badge/Jellyfin-12.x%20%7C%2010.11-00a4dc?logo=jellyfin&logoColor=white" alt="Jellyfin 12.x | 10.11">
  <a href="https://github.com/k1k4k/jellyfin-true-music-library/releases"><img src="https://img.shields.io/github/downloads/k1k4k/jellyfin-true-music-library/total?color=aa5cc3" alt="Downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/k1k4k/jellyfin-true-music-library?color=555" alt="MIT license"></a>
</p>

<p align="center">
  <b>One "Artists" list, with only the artists who made the albums.</b><br>
  Featured artists stay credited on their tracks, without cluttering the list or the search.
</p>

<p align="center">
  <a href="#installation">Installation</a> ·
  <a href="#features">Features</a> ·
  <a href="#troubleshooting">Troubleshooting</a> ·
  <a href="README.fr.md">Version française</a>
</p>

Out of the box, Jellyfin lists in "Artists" every name found in a tag: `Artist A;Artist B`, `Artist A feat. Artist C`, every guest of every compilation… "Album Artists" is not much better when album artist tags are inconsistent. On a large library, the list easily ends up several times longer than the number of actual artists.

## Features

### Analyze

- For each album, single or EP, the **main artists** are taken from the library's metadata providers, in their configured order (MusicBrainz release credits, then TheAudioDB…), then from the tags, then from the artist folder. This order can be changed, e.g. tags first to trust your own album artist tags.
- **Collaborations** (`A & B`) show up under each artist; **compilations and soundtracks** under the folder artist.
- A report shows, for every album, the current and proposed artists, the source, the confidence and the disagreements with the tags and the folder. Nothing is changed in Jellyfin.
- **Manual choice**: unfold an album in the report to impose its main artists.

### Repair

- Album artists = main artists; track artists split (`A;B`, `feat.`, `&`…), guests kept on their tracks.
- One entry per artist, named the way MusicBrainz credits it most often in your library; stray entries such as `A;B` are removed. Spellings differing only by case, accents or punctuation are merged, and so are names in another script when MusicBrainz links them to the same artist ("宇多田ヒカル" / "Utada Hikaru").
- Non-Latin names (Japanese, Chinese, Korean, Cyrillic…) are transliterated into Latin characters, or keep their original script: for every artist, or only for the ones you list.
- A snapshot is saved before anything is written. **Restore** puts everything back the way Jellyfin had it.

### Afterwards

- **The library stays repaired**: scans and "Replace all metadata" keep the decided artists, without network access during the scan, and new albums are resolved right after each scan.
- **One "Artists" list for every client**: the server serves "Artists" as "Album Artists", guests no longer show up in search results nor in the artist lists of other clients, and their page stays reachable from their tracks.
- The web interface hides the duplicate "Album Artists" tab.

### Everywhere

- No audio file is ever changed and no metadata is locked: "Restore", or uninstalling, gives you Jellyfin's own behavior back.
- English or French: the settings page follows the web interface language, the report and the scheduled tasks follow the server language.

## Installation

### Plugin (recommended)

1. **Dashboard → Plugins → Manage Repositories → `+`**, then the k1k4k plugin repository URL matching your Jellyfin:

   | Jellyfin | Repository URL |
   |---|---|
   | 12.x | `https://k1k4k.github.io/jellyfin-plugins/manifest.json` |
   | 10.11.x | `https://k1k4k.github.io/jellyfin-plugins/manifest-10.11.json` |

   This repository lists every k1k4k plugin (Background Play, Player Queue, True Music Library…). If you already added it, or the Player Queue repository, there's nothing to do.

2. **Plugins → Available → Jellyfin True Music Library → Install**, then restart Jellyfin.
3. **Back up Jellyfin** (12.x: Dashboard → Backups), then open **Dashboard → Plugins → Jellyfin True Music Library**: **Analyze**, read the report, **Repair**.

Until the first repair, the plugin changes nothing: neither the lists, nor the search, nor the scans. New versions show up in the dashboard.

<details>
<summary><b>Other methods (without the repository)</b></summary>

#### Plugin, by hand

Download the zip from the [latest release](https://github.com/k1k4k/jellyfin-true-music-library/releases/latest) (`_jf12` for Jellyfin 12, `_jf10` for 10.11), unzip it into `<config>/plugins/Jellyfin True Music Library/` and restart Jellyfin.

#### Install script

Copies the plugin into Jellyfin's `plugins` folder (latest release, a release zip, or built from these sources) and picks the right build for your server version.

```powershell
# Windows
.\install.ps1                                                                  # local Jellyfin
.\install.ps1 -PluginsDir "\\SERVER\plugins" -ServerUrl "http://SERVER:8096"   # network share
.\install.ps1 -Build                                                           # from the sources (.NET SDK 10, or 9 for 10.11)
.\install.ps1 -Uninstall
```

```bash
# Linux (plugins in /var/lib/jellyfin/plugins)
sudo ./install.sh --restart
sudo ./install.sh --uninstall
```

Then restart Jellyfin.

</details>

## Compatibility

| | |
|---|---|
| **Jellyfin 12.x** | tested (12.1.0) |
| **Jellyfin 10.11** | tested (10.11.11) |
| **Web interface** (classic and new layout) | yes |
| **Apps that show the web UI** (Jellyfin Media Player, Android app) | should work, untested |
| **Other clients** (Finamp, native apps…) | the lists and search they get from the server are cleaned up; untested |
| **Metadata providers** | MusicBrainz (credits read directly), and every album provider enabled in the library (TheAudioDB…) |

The first analysis queries MusicBrainz at one request per second (about 4 minutes for 200 albums); later ones use the cache.

## Troubleshooting

- **Nothing changed after "Analyze"**: normal, the analysis is read-only. "Repair" applies it.
- **Guests still show up in "Artists" or in the search**: the single list only applies once the library is repaired. The settings page shows whether the persistence is active; reload the web page (`Ctrl+F5`).
- **An album has the wrong main artist**: unfold it in the report to see the source and the disagreements, then impose its artists by hand. It applies at the next analysis and repair, and at the next scan once the library is repaired.
- **"The instance of entity type 'People' cannot be tracked…"** (Jellyfin 12, versions before 0.1.2): a track credits a composer or lyricist whose name has an accented capital letter. Update the plugin, then run "Repair" again.
- **Japanese, Chinese or Cyrillic names turned into Latin transliterations**: set "Non-Latin names" to "Original script", or keep the original script only for the artists you list, then analyze and repair again.
- **Jellyfin's custom tag delimiters**: not needed, the plugin splits `A;B`, `feat.`, `&`… itself and never splits the names it knows (MusicBrainz artists, folders, the "Names never to split" list). If Jellyfin's delimiters split a name like `AC/DC`, the plugin may not be able to put it back together, except on albums identified on MusicBrainz.
- **The "Album Artists" tab is still there**: reload the web page (`Ctrl+F5`) and check the setting "Hide the Album Artists tab".
- **To undo everything**: "Restore" on the settings page, then uninstall if you wish.

<details>
<summary><b>Development</b></summary>

#### Plugin

`Jellyfin.Plugin.TrueMusicLibrary/`: one source, two targets, `-p:JellyfinTarget=jf12` (Jellyfin 12, .NET 10, default) and `jf10` (Jellyfin 10.11, .NET 9). Without a local SDK:

```bash
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build Jellyfin.Plugin.TrueMusicLibrary/Jellyfin.Plugin.TrueMusicLibrary.csproj -c Release -o out/jf12
```

#### Releasing

1. Bump `<Version>` / `<AssemblyVersion>` in the csproj; add a `## X.Y.Z` section to `CHANGELOG.md`.
2. `git tag vX.Y.Z && git push origin vX.Y.Z`: the **Release** workflow builds both targets, publishes the release and updates the manifests on `main`. The central [`jellyfin-plugins`](https://github.com/k1k4k/jellyfin-plugins) repository picks them up on its next run (hourly, or right away with the `PLUGINS_REPO_TOKEN` secret).

</details>

## License

MIT, see [LICENSE](LICENSE).
