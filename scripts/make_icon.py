#!/usr/bin/env python3
"""Dessine icon.png (1280x720, vignette du catalogue Jellyfin), dans le style des autres plugins k1k4k.

    python scripts/make_icon.py [--out icon.png]

Un onglet unique (carte blanche) avec une seule silhouette d'artiste et une note, « Jellyfin » en blanc,
« True Music Library » en bleu, bandeau dégradé en bas. Police Segoe UI Bold (Windows), sinon DejaVu Sans Bold.
"""
import argparse
import os

from PIL import Image, ImageDraw, ImageFont

W, H = 1280, 720
SCALE = 4  # dessin en grand puis réduction : bords lisses
BG = (11, 12, 14)
WHITE = (255, 255, 255)
BLUE = (13, 193, 245)
PURPLE = (155, 48, 255)


def font(size):
    for path in (r"C:\Windows\Fonts\segoeuib.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"):
        if os.path.exists(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "icon.png"))
    out = ap.parse_args().out

    s = SCALE
    img = Image.new("RGB", (W * s, H * s), BG)
    d = ImageDraw.Draw(img)

    # Carte (l'onglet « Artistes ») : contour blanc, languette d'onglet en haut à gauche.
    x0, y0, x1, y1 = 140 * s, 262 * s, 380 * s, 458 * s
    stroke = 22 * s
    d.rounded_rectangle((x0, y0 - 34 * s, x0 + 104 * s, y0 + 20 * s), radius=14 * s, fill=WHITE)
    d.rounded_rectangle((x0, y0, x1, y1), radius=18 * s, outline=WHITE, width=stroke)

    # Une seule silhouette d'artiste (bleue) et une note de musique.
    cx, head_y, head_r = 236 * s, 334 * s, 30 * s
    d.ellipse((cx - head_r, head_y - head_r, cx + head_r, head_y + head_r), fill=BLUE)
    d.chord((cx - 62 * s, 376 * s, cx + 62 * s, 496 * s), start=180, end=360, fill=BLUE)  # épaules jusqu'au bord intérieur
    nx, ny = 312 * s, 392 * s
    d.ellipse((nx - 15 * s, ny - 11 * s, nx + 15 * s, ny + 11 * s), fill=WHITE)
    d.rectangle((nx + 9 * s, ny - 66 * s, nx + 15 * s, ny), fill=WHITE)
    d.polygon([(nx + 15 * s, ny - 66 * s), (nx + 40 * s, ny - 50 * s), (nx + 40 * s, ny - 36 * s), (nx + 15 * s, ny - 50 * s)], fill=WHITE)

    # Texte : « Jellyfin » / « True Music Library », réduit si besoin pour tenir dans la largeur.
    left, right = 450 * s, (W - 70) * s
    size = 96
    while size > 40:
        title, name = font(130 * s * size // 96), font(size * s)
        if d.textlength("True Music Library", font=name) <= right - left:
            break
        size -= 2
    d.text((left, 360 * s), "Jellyfin", font=title, fill=WHITE, anchor="ls")
    d.text((left, 470 * s), "True Music Library", font=name, fill=BLUE, anchor="ls")

    # Bandeau dégradé en bas.
    band = 30 * s
    for x in range(W * s):
        t = x / (W * s - 1)
        color = tuple(round(BLUE[i] + (PURPLE[i] - BLUE[i]) * t) for i in range(3))
        d.line((x, H * s - band, x, H * s), fill=color)

    img.resize((W, H), Image.LANCZOS).save(out, optimize=True)
    print(out)


if __name__ == "__main__":
    main()
