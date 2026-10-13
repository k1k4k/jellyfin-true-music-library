# Jellyfin True Music Library

**En développement : pas encore de version publiée.** L'analyse, la réparation, la restauration et la persistance fonctionnent (testées sur Jellyfin 12.1 et 10.11). Reste à faire : retirer les invités de la recherche, adapter l'interface web, et les choix manuels par album dans l'interface.

Un seul onglet « Artistes », avec seulement les artistes principaux des albums, singles et EP, choisis d'après les fournisseurs de métadonnées de la bibliothèque (MusicBrainz, TheAudioDB…). Les invités restent crédités sur leurs pistes sans encombrer la liste. Une action « Réparer » corrige la médiathèque existante, et les scans suivants gardent le résultat.

[English version](README.md)

## Pourquoi

D'origine, Jellyfin met dans « Artistes » tous les noms trouvés dans un tag : `Artiste A;Artiste B`, `Artiste A feat. Artiste C`, chaque invité de chaque compilation… « Artistes de l'album » ne vaut guère mieux quand les tags d'artiste d'album sont incohérents. Sur une grosse médiathèque, la liste devient vite plusieurs fois plus longue que le nombre de vrais artistes principaux.

## Ce qu'il fait

- **Analyser** (lecture seule) : pour chaque sortie, les artistes principaux, pris dans les fournisseurs de métadonnées d'album de la bibliothèque, dans leur ordre (crédits de la release MusicBrainz, puis TheAudioDB…), puis dans les tags, puis dans le dossier de l'artiste. Un rapport montre l'avant / après, les sources, la confiance et les désaccords.
- **Réparer** : applique l'analyse. Artistes d'album = artistes principaux ; artistes des pistes découpés (`A;B`, `feat.`, `&`…), invités gardés ; une seule fiche par artiste, au nom sous lequel MusicBrainz le crédite le plus souvent dans ta médiathèque ; fiches parasites `A;B` supprimées. Un instantané est enregistré avant toute écriture.
- **Persistance** : une fois réparée, la médiathèque le reste. Les scans et « Remplacer toutes les métadonnées » gardent les artistes décidés (aucun réseau pendant le scan), et les nouveaux albums sont résolus juste après chaque scan.
- **Restaurer** : remet chaque élément comme avant la première réparation, ou comme son fichier le dit aujourd'hui s'il a été relu depuis, et arrête la persistance.
- **Un seul onglet « Artistes »** : le serveur sert `/Artists` comme `/Artists/AlbumArtists`, pour tous les clients.

Aucun fichier audio n'est modifié. Aucune métadonnée n'est verrouillée : désinstaller le plugin puis « Remplacer toutes les métadonnées » rend le comportement de Jellyfin.

## Installation

**Faites une sauvegarde Jellyfin avant la première réparation** (12.x : Tableau de bord → Sauvegardes).

Dès qu'une version sera publiée, le plugin sera dans le dépôt de plugins k1k4k : **Tableau de bord → Extensions → Gérer les dépôts → `+`**

| Jellyfin | Adresse |
|---|---|
| 12.x | `https://k1k4k.github.io/jellyfin-plugins/manifest.json` |
| 10.11.x | `https://k1k4k.github.io/jellyfin-plugins/manifest-10.11.json` |

Sans le catalogue, les scripts d'installation copient le plugin dans le dossier `plugins` de Jellyfin (dernière version publiée, un zip de version, ou compilé depuis ces sources), dans la variante qui correspond à la version du serveur :

```powershell
.\install.ps1                                   # Windows, Jellyfin sur ce PC
.\install.ps1 -PluginsDir "\\SERVEUR\jellyfin\plugins" -ServerUrl "http://SERVEUR:8096"
.\install.ps1 -Build                            # depuis les sources (SDK .NET 10, ou 9 pour 10.11)
.\install.ps1 -Uninstall
```

```bash
sudo ./install.sh --restart                     # serveur Linux (/var/lib/jellyfin/plugins)
sudo ./install.sh --uninstall
```

Puis redémarrez Jellyfin.

## Utilisation

**Tableau de bord → Extensions → True Music Library** : **Analyser**, lire le rapport, **Réparer**. « Restaurer » revient sur tout. Les trois actions existent aussi en tâches planifiées.

Réglages : collaborations (`A & B` chez les deux artistes, ou le premier seulement), compilations et BO (artiste du dossier, « Artistes variés », ou aucun), écriture des noms (MusicBrainz, dossier, tags), jointures d'invité, noms à ne jamais couper (`AC/DC`, `Earth, Wind & Fire`…), persistance, onglet « Artistes » unique.

## Limites

- Deux artistes différents qui portent le même nom se retrouvent sur une seule fiche.
- Une mauvaise correspondance MusicBrainz donne un mauvais artiste principal : le rapport affiche la confiance et les désaccords avec les tags et le dossier.
- Un fichier retaggé n'est relu que par « Remplacer toutes les métadonnées », comme dans Jellyfin sans le plugin.

Jellyfin 12.x et 10.11. Licence MIT.
