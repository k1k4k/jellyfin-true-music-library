#!/usr/bin/env python3
"""Empaquète le plugin et met à jour le manifest du dépôt de plugins Jellyfin.

    python scripts/package.py --target jf12 --dll Jellyfin.Plugin.TrueMusicLibrary/bin/Release/net10.0/Jellyfin.Plugin.TrueMusicLibrary.dll \
        --repo k1k4k/jellyfin-true-music-library --tag v0.1.0 [--notes-out dist/release-notes.md]
    python scripts/package.py --refresh-changelogs

- crée dist/jellyfin-true-music-library_<version>_<target>.zip (la DLL à la racine du zip)
- calcule le MD5 attendu par Jellyfin
- ajoute/remplace l'entrée de version dans manifest.json (jf12) ou manifest-10.11.json (jf10)
Le sourceUrl pointe vers l'asset de la release GitHub du tag.

Les notes de version viennent de CHANGELOG.md (une section "## X.Y.Z" par version, écrite
pour les utilisateurs) : elles alimentent le champ "changelog" de chaque version des
manifestes et le texte de la release GitHub. Une version sans section est refusée.
"""
import argparse
import datetime as dt
import hashlib
import json
import os
import re
import zipfile

GUID = "541fb644-193a-4692-b8a4-2593356153a7"
NAME = "True Music Library"
TARGETS = {
    "jf12": {"manifest": "manifest.json", "targetAbi": "12.0.0.0", "framework": "net10.0"},
    "jf10": {"manifest": "manifest-10.11.json", "targetAbi": "10.11.0.0", "framework": "net9.0"},
}


def read_version(csproj):
    src = open(csproj, encoding="utf-8").read()
    m = re.search(r"<AssemblyVersion>([\d.]+)</AssemblyVersion>", src)
    if not m:
        raise SystemExit("AssemblyVersion introuvable dans " + csproj)
    return m.group(1)


def load_changelog(root):
    """{"0.7.0": "- ...\n- ..."} à partir des sections "## X.Y.Z" de CHANGELOG.md."""
    path = os.path.join(root, "CHANGELOG.md")
    sections, current = {}, None
    if not os.path.exists(path):
        return sections
    for line in open(path, encoding="utf-8"):
        m = re.match(r"^##\s+v?([\d.]+)\s*$", line)
        if m:
            current = m.group(1)
            sections[current] = []
        elif line.startswith("# "):
            current = None
        elif current is not None:
            sections[current].append(line.rstrip())
    texts = {k: "\n".join(v).strip() for k, v in sections.items()}
    return {k: v for k, v in texts.items() if v}


def changelog_for(changelog, version):
    """Notes d'une version ; "0.7.0.0" est cherché aussi sous la forme "0.7.0"."""
    short = re.sub(r"^(\d+\.\d+\.\d+)\.0$", r"\1", version)
    return changelog.get(version) or changelog.get(short)


def plugin_entry(manifest):
    """Entrée de ce plugin dans un manifeste (qui peut en lister plusieurs), par GUID."""
    for p in manifest:
        if p.get("guid") == GUID:
            return p
    return None


def write_manifest(path, manifest):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(manifest, f, indent=2, ensure_ascii=False)
        f.write("\n")


def refresh_changelogs(root, changelog):
    """Réécrit le champ changelog de toutes les versions déjà publiées dans les manifestes."""
    for t in TARGETS.values():
        path = os.path.join(root, t["manifest"])
        if not os.path.exists(path):
            continue
        manifest = json.load(open(path, encoding="utf-8"))
        plugin = plugin_entry(manifest)
        if plugin is None:
            continue
        for v in plugin["versions"]:
            text = changelog_for(changelog, v["version"])
            if text:
                v["changelog"] = text
        write_manifest(path, manifest)
        print("changelogs mis à jour : " + t["manifest"])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", choices=TARGETS)
    ap.add_argument("--dll")
    ap.add_argument("--repo", help="owner/name GitHub")
    ap.add_argument("--tag", help="tag de la release, ex. v0.3.0")
    ap.add_argument("--notes-out", help="écrit aussi les notes de la version dans ce fichier (texte de la release GitHub)")
    ap.add_argument("--refresh-changelogs", action="store_true",
                    help="met seulement à jour les notes des versions existantes depuis CHANGELOG.md")
    ap.add_argument("--out", default="dist")
    args = ap.parse_args()

    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    changelog = load_changelog(root)
    if args.refresh_changelogs:
        refresh_changelogs(root, changelog)
        return
    if not (args.target and args.dll and args.repo and args.tag):
        ap.error("--target, --dll, --repo et --tag sont requis")

    t = TARGETS[args.target]
    version = read_version(os.path.join(root, "Jellyfin.Plugin.TrueMusicLibrary", "Jellyfin.Plugin.TrueMusicLibrary.csproj"))
    notes = changelog_for(changelog, version)
    if not notes:
        short = re.sub(r"\.0$", "", version)
        raise SystemExit(f'Pas de notes pour la version {version} : ajoutez une section "## {short}" dans CHANGELOG.md')
    if args.notes_out:
        os.makedirs(os.path.dirname(os.path.abspath(args.notes_out)), exist_ok=True)
        with open(args.notes_out, "w", encoding="utf-8", newline="\n") as f:
            f.write(notes + "\n")

    os.makedirs(args.out, exist_ok=True)
    zip_name = f"jellyfin-true-music-library_{version}_{args.target}.zip"
    zip_path = os.path.join(args.out, zip_name)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
        z.write(args.dll, os.path.basename(args.dll))
    md5 = hashlib.md5(open(zip_path, "rb").read()).hexdigest()  # noqa: S324 - format imposé par Jellyfin

    manifest_path = os.path.join(root, t["manifest"])
    manifest = json.load(open(manifest_path, encoding="utf-8")) if os.path.exists(manifest_path) else []
    plugin = plugin_entry(manifest)
    if plugin is None:
        plugin = {
            "guid": GUID,
            "name": NAME,
            "description": "Un seul onglet « Artistes », avec seulement les artistes principaux des albums, singles et EP, choisis d'après les fournisseurs de métadonnées. "
                           "A single \"Artists\" tab, listing only the main artists of albums, singles and EPs, chosen from your metadata providers.",
            "overview": "Une médiathèque musicale sans artistes parasites. A music library without stray artists.",
            "owner": args.repo.split("/")[0],
            "category": "Metadata",
            "imageUrl": f"https://raw.githubusercontent.com/{args.repo}/main/icon.png",
            "versions": [],
        }
        manifest.append(plugin)

    entry = {
        "version": version,
        "changelog": notes,
        "targetAbi": t["targetAbi"],
        "sourceUrl": f"https://github.com/{args.repo}/releases/download/{args.tag}/{zip_name}",
        "checksum": md5,
        "timestamp": dt.datetime.now(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    versions = [v for v in plugin["versions"] if v["version"] != version]
    versions.insert(0, entry)
    versions.sort(key=lambda v: tuple(int(x) for x in v["version"].split(".")), reverse=True)
    plugin["versions"] = versions
    write_manifest(manifest_path, manifest)

    print(f"{zip_path}  md5={md5}  -> {t['manifest']} (targetAbi {t['targetAbi']})")


if __name__ == "__main__":
    main()
