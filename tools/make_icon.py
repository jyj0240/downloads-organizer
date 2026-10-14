"""Draws the app icon: a light arrow dropping into a tray on a rounded colored tile.

Each size is drawn on its own (8x supersampled, then reduced) so small tray sizes stay crisp,
with lines thickened at small sizes. Writes the multi-size .ico and a preview PNG.

usage:
  python tools/make_icon.py [--palette NAME]       write the .ico and docs/icon-preview.png
  python tools/make_icon.py --compare OUT.png      put every palette side by side, write nothing else
"""
import argparse
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ICO = ROOT / "src" / "DownloadsOrganizer.App" / "Assets" / "app.ico"
PREVIEW = ROOT / "docs" / "icon-preview.png"

# tile, slightly darker rim (contrast on light taskbars), mark
PALETTES = {
    "royal": ((37, 99, 235), (29, 78, 196), (248, 250, 252)),
    "sky": ((56, 152, 236), (36, 124, 206), (250, 252, 255)),
    "navy": ((30, 58, 138), (22, 44, 108), (226, 236, 255)),
}
DEFAULT_PALETTE = "royal"
SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]
SUPER = 8


def round_line(draw, points, width, fill):
    """Polyline with round caps and joins (Pillow lines have square ends)."""
    for a, b in zip(points, points[1:]):
        draw.line([a, b], fill=fill, width=round(width))
    r = width / 2
    for x, y in points:
        draw.ellipse([x - r, y - r, x + r, y + r], fill=fill)


def draw_icon(size, palette):
    tile, rim_color, mark = (c + (255,) for c in PALETTES[palette])
    s = size * SUPER
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    inset = s * 0.04
    radius = s * 0.24
    d.rounded_rectangle([inset, inset, s - inset, s - inset], radius=radius, fill=rim_color)
    rim = max(s * 0.02, SUPER * 0.6)
    d.rounded_rectangle([inset + rim, inset + rim, s - inset - rim, s - inset - rim], radius=radius - rim, fill=tile)

    # Lines at least ~2 px wide at the final size, so 16 px stays readable.
    w = max(s * 0.105, SUPER * 2.1)
    u = s
    if size <= 20:   # fewer, larger strokes at tiny sizes
        shaft = [(0.50 * u, 0.20 * u), (0.50 * u, 0.56 * u)]
        head = [(0.30 * u, 0.40 * u), (0.50 * u, 0.60 * u), (0.70 * u, 0.40 * u)]
        tray = [(0.22 * u, 0.58 * u), (0.22 * u, 0.80 * u), (0.78 * u, 0.80 * u), (0.78 * u, 0.58 * u)]
    else:
        shaft = [(0.50 * u, 0.20 * u), (0.50 * u, 0.57 * u)]
        head = [(0.33 * u, 0.42 * u), (0.50 * u, 0.59 * u), (0.67 * u, 0.42 * u)]
        tray = [(0.24 * u, 0.57 * u), (0.24 * u, 0.77 * u), (0.76 * u, 0.77 * u), (0.76 * u, 0.57 * u)]
    for part in (shaft, head, tray):
        round_line(d, part, w, mark)

    return img.resize((size, size), Image.LANCZOS)


def strip(images, sizes, pad=16, gap=12):
    """Icons in a row on a light band and a dark band, as on a light or dark taskbar."""
    tallest = max(sizes)
    width = pad * 2 + sum(sizes) + gap * (len(sizes) - 1)
    height = pad * 2 + 2 * (tallest + pad)
    sheet = Image.new("RGBA", (width, height), (255, 255, 255, 255))
    ImageDraw.Draw(sheet).rectangle([0, height // 2, width, height], fill=(32, 32, 32, 255))
    for top in (pad, height // 2 + pad):
        x = pad
        for n in sizes:
            sheet.alpha_composite(images[n], (x, top + (tallest - n)))
            x += n + gap
    return sheet


def compare(out):
    sizes = [16, 24, 32, 48, 128]
    rows = [strip({n: draw_icon(n, name) for n in sizes}, sizes) for name in PALETTES]
    label_w = 120
    sheet = Image.new("RGBA", (label_w + rows[0].width, sum(r.height for r in rows)), (255, 255, 255, 255))
    d = ImageDraw.Draw(sheet)
    y = 0
    for name, row in zip(PALETTES, rows):
        d.text((12, y + row.height // 2 - 6), name, fill=(0, 0, 0, 255))
        sheet.alpha_composite(row, (label_w, y))
        y += row.height
    sheet.save(out)
    print(f"wrote {out}")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--palette", choices=PALETTES, default=DEFAULT_PALETTE)
    parser.add_argument("--compare", metavar="OUT.png")
    args = parser.parse_args()
    if args.compare:
        compare(args.compare)
        return

    images = {size: draw_icon(size, args.palette) for size in SIZES}
    ICO.parent.mkdir(parents=True, exist_ok=True)
    images[SIZES[-1]].save(ICO, format="ICO", sizes=[(n, n) for n in SIZES],
                           append_images=[images[n] for n in SIZES[:-1]])
    PREVIEW.parent.mkdir(parents=True, exist_ok=True)
    strip(images, SIZES).save(PREVIEW)
    print(f"wrote {ICO} ({ICO.stat().st_size} bytes, palette {args.palette}) and {PREVIEW}")


if __name__ == "__main__":
    main()
