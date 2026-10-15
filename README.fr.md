<p align="center">
  <img src="icon.png" width="520" alt="Jellyfin True Music Library">
</p>

<p align="center">
  <a href="https://github.com/k1k4k/jellyfin-true-music-library/releases/latest"><img src="https://img.shields.io/github/v/release/k1k4k/jellyfin-true-music-library?label=version&color=00a4dc" alt="Dernière version"></a>
  <img src="https://img.shields.io/badge/Jellyfin-12.x%20%7C%2010.11-00a4dc?logo=jellyfin&logoColor=white" alt="Jellyfin 12.x | 10.11">
  <a href="https://github.com/k1k4k/jellyfin-true-music-library/releases"><img src="https://img.shields.io/github/downloads/k1k4k/jellyfin-true-music-library/total?color=aa5cc3" alt="Téléchargements"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/k1k4k/jellyfin-true-music-library?color=555" alt="Licence MIT"></a>
</p>

<p align="center">
  <b>Une seule liste « Artistes », avec seulement les artistes qui ont fait les albums.</b><br>
  Les invités restent crédités sur leurs pistes, sans encombrer la liste ni la recherche.
</p>

<p align="center">
  <a href="#installation">Installation</a> ·
  <a href="#fonctionnalités">Fonctionnalités</a> ·
  <a href="#dépannage">Dépannage</a> ·
  <a href="README.md">English version</a>
</p>

D'origine, Jellyfin met dans « Artistes » tous les noms trouvés dans un tag : `Artiste A;Artiste B`, `Artiste A feat. Artiste C`, chaque invité de chaque compilation… « Artistes de l'album » ne vaut guère mieux quand les tags d'artiste d'album sont incohérents. Sur une grosse médiathèque, la liste devient vite plusieurs fois plus longue que le nombre de vrais artistes.

## Fonctionnalités

### Analyser

- Pour chaque album, single ou EP, les **artistes principaux** sont pris dans les fournisseurs de métadonnées de la bibliothèque, dans leur ordre (crédits de la release MusicBrainz, puis TheAudioDB…), puis dans les tags, puis dans le dossier de l'artiste. Cet ordre se règle, par exemple les tags d'abord pour faire confiance à vos propres artistes d'album.
- Les **collaborations** (`A & B`) apparaissent chez chaque artiste ; les **compilations et BO** chez l'artiste du dossier.
- Un rapport montre, pour chaque album, les artistes actuels et proposés, la source, la confiance et les désaccords avec les tags et le dossier. Rien n'est modifié dans Jellyfin.
- **Choix manuel** : dépliez un album du rapport pour imposer ses artistes principaux.

### Réparer

- Artistes d'album = artistes principaux ; artistes des pistes découpés (`A;B`, `feat.`, `&`…), invités gardés sur leurs pistes.
- Une seule fiche par artiste, au nom sous lequel MusicBrainz le crédite le plus souvent dans votre médiathèque ; les fiches parasites comme `A;B` sont supprimées. Les écritures qui ne diffèrent que par la casse, les accents ou la ponctuation sont fusionnées, tout comme les noms dans une autre écriture que MusicBrainz relie au même artiste (« 宇多田ヒカル » / « Utada Hikaru »).
- Les noms non latins (japonais, chinois, coréen, cyrillique…) sont transcrits en caractères latins, ou gardent leur écriture d'origine : pour tous les artistes, ou seulement pour ceux que vous listez.
- Un instantané est enregistré avant toute écriture. **Restaurer** remet tout comme Jellyfin l'avait.

### Ensuite

- **La médiathèque reste réparée** : les scans et « Remplacer toutes les métadonnées » gardent les artistes décidés, sans réseau pendant le scan, et les nouveaux albums sont résolus juste après chaque scan.
- **Une seule liste « Artistes » pour tous les clients** : le serveur sert « Artistes » comme « Artistes de l'album », les invités n'apparaissent plus dans la recherche ni dans les listes d'artistes des autres clients, et leur page reste accessible depuis leurs pistes.
- L'interface web cache l'onglet « Artistes de l'album », devenu un doublon.

### Partout

- Aucun fichier audio n'est modifié et aucune métadonnée n'est verrouillée : « Restaurer », ou la désinstallation, rend le comportement de Jellyfin.
- Français ou anglais : la page de configuration suit la langue de l'interface web, le rapport et les tâches planifiées celle du serveur.

## Installation

### Plugin (recommandé)

1. **Tableau de bord → Extensions → Gérer les dépôts → `+`**, puis l'adresse du dépôt de plugins k1k4k qui correspond à votre Jellyfin :

   | Jellyfin | Adresse du dépôt |
   |---|---|
   | 12.x | `https://k1k4k.github.io/jellyfin-plugins/manifest.json` |
   | 10.11.x | `https://k1k4k.github.io/jellyfin-plugins/manifest-10.11.json` |

   Ce dépôt liste tous les plugins k1k4k (Background Play, Player Queue, True Music Library…). Si vous l'avez déjà ajouté, ou le dépôt de Player Queue, rien à faire.

2. **Extensions → Disponible → Jellyfin True Music Library → Installer**, puis redémarrez Jellyfin.
3. **Faites une sauvegarde Jellyfin** (12.x : Tableau de bord → Sauvegardes), puis ouvrez **Tableau de bord → Extensions → Jellyfin True Music Library** : **Analyser**, lire le rapport, **Réparer**.

Tant que la médiathèque n'a pas été réparée, le plugin ne change rien : ni les listes, ni la recherche, ni les scans. Les nouvelles versions arrivent par le tableau de bord.

<details>
<summary><b>Autres méthodes (sans le dépôt)</b></summary>

#### Plugin, à la main

Téléchargez le zip de la [dernière version](https://github.com/k1k4k/jellyfin-true-music-library/releases/latest) (`_jf12` pour Jellyfin 12, `_jf10` pour 10.11), décompressez-le dans `<config>/plugins/Jellyfin True Music Library/` et redémarrez Jellyfin.

#### Script d'installation

Copie le plugin dans le dossier `plugins` de Jellyfin (dernière version publiée, un zip de version, ou compilé depuis ces sources), dans la variante qui correspond à la version du serveur.

```powershell
# Windows
.\install.ps1                                                                      # Jellyfin sur ce PC
.\install.ps1 -PluginsDir "\\SERVEUR\plugins" -ServerUrl "http://SERVEUR:8096"     # partage réseau
.\install.ps1 -Build                                                               # depuis les sources (SDK .NET 10, ou 9 pour 10.11)
.\install.ps1 -Uninstall
```

```bash
# Linux (plugins dans /var/lib/jellyfin/plugins)
sudo ./install.sh --restart
sudo ./install.sh --uninstall
```

Puis redémarrez Jellyfin.

</details>

## Compatibilité

| | |
|---|---|
| **Jellyfin 12.x** | testé (12.1.0) |
| **Jellyfin 10.11** | testé (10.11.11) |
| **Interface web** (classique et nouvelle) | oui |
| **Applications qui affichent l'interface web** (Jellyfin Media Player, appli Android) | devrait fonctionner, non testé |
| **Autres clients** (Finamp, applications natives…) | les listes et la recherche que leur sert le serveur sont nettoyées ; non testé |
| **Fournisseurs de métadonnées** | MusicBrainz (crédits lus directement), et tous les fournisseurs d'album activés dans la bibliothèque (TheAudioDB…) |

La première analyse interroge MusicBrainz à une requête par seconde (environ 4 minutes pour 200 albums) ; les suivantes utilisent le cache.

## Dépannage

- **Rien n'a changé après « Analyser »** : normal, l'analyse ne modifie rien. « Réparer » l'applique.
- **Des invités apparaissent encore dans « Artistes » ou dans la recherche** : la liste unique ne s'applique qu'une fois la médiathèque réparée. La page de configuration indique si la persistance est active ; rechargez la page web (`Ctrl+F5`).
- **Un album a le mauvais artiste principal** : dépliez-le dans le rapport pour voir la source et les désaccords, puis imposez ses artistes à la main. Le choix s'applique à la prochaine analyse et à la prochaine réparation, et au prochain scan une fois la médiathèque réparée.
- **« The instance of entity type 'People' cannot be tracked… »** (Jellyfin 12, versions avant la 0.1.2) : une piste crédite un compositeur ou un parolier dont le nom a une majuscule accentuée. Mettez le plugin à jour, puis relancez « Réparer ».
- **Des noms japonais, chinois ou cyrilliques transcrits en latin** : réglez « Noms non latins » sur « Écriture d'origine », ou gardez l'écriture d'origine pour les seuls artistes listés, puis analysez et réparez de nouveau.
- **Séparateurs de tags personnalisés de Jellyfin** : inutiles, le plugin découpe lui-même `A;B`, `feat.`, `&`… et ne coupe jamais les noms qu'il connaît (artistes MusicBrainz, dossiers, liste « Noms à ne jamais couper »). Si les séparateurs de Jellyfin coupent un nom comme `AC/DC`, le plugin risque de ne pas pouvoir le recoller, sauf sur les albums reconnus par MusicBrainz.
- **L'onglet « Artistes de l'album » est toujours là** : rechargez la page web (`Ctrl+F5`) et vérifiez le réglage « Cacher l'onglet Artistes de l'album ».
- **Pour tout défaire** : « Restaurer » dans la page de configuration, puis désinstallez si vous le souhaitez.

<details>
<summary><b>Développement</b></summary>

#### Plugin

`Jellyfin.Plugin.TrueMusicLibrary/` : une source, deux cibles, `-p:JellyfinTarget=jf12` (Jellyfin 12, .NET 10, par défaut) et `jf10` (Jellyfin 10.11, .NET 9). Sans SDK local :

```bash
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 dotnet build Jellyfin.Plugin.TrueMusicLibrary/Jellyfin.Plugin.TrueMusicLibrary.csproj -c Release -o out/jf12
```

#### Publier une version

1. Mettre à jour `<Version>` / `<AssemblyVersion>` dans le csproj ; ajouter une section `## X.Y.Z` dans `CHANGELOG.md`.
2. `git tag vX.Y.Z && git push origin vX.Y.Z` : le workflow **Release** compile les deux cibles, publie la release et met à jour les manifestes sur `main`. Le dépôt central [`jellyfin-plugins`](https://github.com/k1k4k/jellyfin-plugins) les reprend à son passage suivant (toutes les heures, ou tout de suite avec le secret `PLUGINS_REPO_TOKEN`).

</details>

## Licence

MIT, voir [LICENSE](LICENSE).
