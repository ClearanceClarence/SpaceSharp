"""Generates every SpaceSharp brand asset from one definition of the mark.

Outputs (relative to the repo root):
  SpaceSharp/Assets/SpaceSharp.svg, SpaceSharp-small.svg, SpaceSharp.ico, SpaceSharp-256.png
  docs/icon.png, docs/social-preview.png, docs/wordmark.svg, docs/header.png
  installer/banner.bmp, installer/logo.bmp
"""
import io
import struct
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageEnhance

ROOT = Path(__file__).resolve().parent.parent
FONTS = Path(__file__).resolve().parent / "fonts"


def ensure_fonts():
    """Downloads Bricolage Grotesque (OFL) from GitHub on first run."""
    if (FONTS / "BricolageGrotesque96pt-ExtraBold.ttf").exists():
        return
    import urllib.request
    import zipfile
    url = "https://github.com/ateliertriay/bricolage/archive/refs/heads/main.zip"
    buf = io.BytesIO(urllib.request.urlopen(url).read())
    FONTS.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(buf) as z:
        for name in z.namelist():
            if name.endswith(".ttf") and "/fonts/ttf/BricolageGrotesque" in name:
                (FONTS / Path(name).name).write_bytes(z.read(name))

AMBER = (0xF5, 0xB8, 0x2E)
AMBER_DEEP = (0xD9, 0x9A, 0x12)
CHARCOAL = (0x1E, 0x1E, 0x24)
BASE = (0x16, 0x16, 0x1B)
PANEL = (0x2A, 0x2A, 0x33)
LINE = (0x44, 0x44, 0x4F)
OFFWHITE = (0xF4, 0xF4, 0xF7)
MUTED = (0xB9, 0xB9, 0xC4)
HINT = (0x8C, 0x8C, 0x99)

# The nine cells of the mark, in a 100 x 100 box: (x, y, w, h, alpha). Largest first from the top left.
CELLS = [
    (16, 16, 28, 28, 1.00), (48, 16, 18, 28, 0.55), (70, 16, 14, 28, 0.30),
    (16, 48, 28, 18, 0.55), (48, 48, 18, 18, 0.30), (70, 48, 14, 18, 0.18),
    (16, 70, 28, 14, 0.30), (48, 70, 18, 14, 0.18), (70, 70, 14, 14, 0.12),
]
# Four-cell version for 16 and 24 px, where nine cells would blur.
CELLS_SMALL = [
    (14, 14, 40, 40, 1.00), (60, 14, 26, 40, 0.55),
    (14, 60, 40, 26, 0.55), (60, 60, 26, 26, 0.30),
]


def blend(fg, bg, a):
    return tuple(round(f * a + b * (1 - a)) for f, b in zip(fg, bg))


def hexc(rgb):
    return "#%02X%02X%02X" % rgb


# ------------------------------------------------------------------ SVG

def mark_svg(cells, tile, radius, cell_radius, size=None):
    attrs = f' width="{size}" height="{size}"' if size else ""
    out = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"{attrs}>',
           f'  <rect width="100" height="100" rx="{radius}" fill="{hexc(tile)}"/>']
    for x, y, w, h, a in cells:
        op = "" if a == 1 else f' opacity="{a:g}"'
        out.append(f'  <rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{cell_radius}" fill="{hexc(AMBER)}"{op}/>')
    out.append("</svg>\n")
    return "\n".join(out)


def wordmark_svg():
    # Text as outlines would need a font engine; ship it as text with the font stack so it renders where the font exists.
    return f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 640 120" width="640" height="120">
  <style>text{{font-family:"Bricolage Grotesque","Segoe UI",sans-serif;letter-spacing:-0.035em}}</style>
  <rect width="100" height="100" x="10" y="10" rx="22" fill="{hexc(CHARCOAL)}"/>
''' + "".join(
        f'  <rect x="{10 + x}" y="{10 + y}" width="{w}" height="{h}" rx="3" fill="{hexc(AMBER)}"{"" if a == 1 else f" opacity={chr(34)}{a:g}{chr(34)}"}/>\n'
        for x, y, w, h, a in CELLS
    ) + f'''  <text x="136" y="92" font-size="84" font-weight="500" fill="{hexc(OFFWHITE)}">Space<tspan font-weight="800" fill="{hexc(AMBER)}">Sharp</tspan></text>
</svg>
'''


# ------------------------------------------------------------------ raster

SS = 4  # supersampling factor


def draw_mark(size, cells, tile, radius, cell_radius, tile_alpha=True):
    """Returns an RGBA image of the mark at `size` px."""
    big = size * SS
    im = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = big / 100
    d.rounded_rectangle([0, 0, big - 1, big - 1], radius=radius * s, fill=tile + (255,))
    for x, y, w, h, a in cells:
        col = blend(AMBER, tile, a)
        d.rounded_rectangle([x * s, y * s, (x + w) * s - 1, (y + h) * s - 1], radius=cell_radius * s, fill=col + (255,))
    return im.resize((size, size), Image.LANCZOS)


def icon(size):
    if size <= 24:
        return draw_mark(size, CELLS_SMALL, CHARCOAL, 24, 5)
    return draw_mark(size, CELLS, CHARCOAL, 22, 3)


def write_ico(path, sizes):
    """ICO with PNG-compressed entries (supported since Vista)."""
    entries = []
    for s in sizes:
        buf = io.BytesIO()
        icon(s).save(buf, "PNG")
        entries.append((s, buf.getvalue()))
    header = struct.pack("<HHH", 0, 1, len(entries))
    offset = 6 + 16 * len(entries)
    dir_entries, data = [], b""
    for s, png in entries:
        dim = 0 if s >= 256 else s
        dir_entries.append(struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(png), offset + len(data)))
        data += png
    path.write_bytes(header + b"".join(dir_entries) + data)


def font(weight, px, opsz96=True):
    name = {500: "Medium", 600: "SemiBold", 700: "Bold", 800: "ExtraBold"}[weight]
    prefix = "BricolageGrotesque96pt-" if opsz96 else "BricolageGrotesque-"
    return ImageFont.truetype(str(FONTS / f"{prefix}{name}.ttf"), px)


def text(d, xy, s, fnt, fill, tracking=0.0):
    """Draws text with letter-spacing (tracking in em)."""
    x, y = xy
    if not tracking:
        d.text((x, y), s, font=fnt, fill=fill)
        return x + d.textlength(s, font=fnt)
    step = fnt.size * tracking
    for ch in s:
        d.text((x, y), ch, font=fnt, fill=fill)
        x += d.textlength(ch, font=fnt) + step
    return x


def wordmark(d, x, y, px, light_color=OFFWHITE):
    """'Space' medium + 'Sharp' extrabold amber. Returns the end x."""
    f1, f2 = font(500, px), font(800, px)
    x = text(d, (x, y), "Space", f1, light_color, -0.035)
    return text(d, (x, y), "Sharp", f2, AMBER, -0.035)


def paste_mark(im, size, xy, cells=CELLS, tile=CHARCOAL, radius=22, cell_radius=3):
    im.alpha_composite(draw_mark(size, cells, tile, radius, cell_radius), xy)


def treemap(d, x0, y0, w, h, boxes, label_font=None):
    """boxes: (x, y, w, h, alpha or None for free space, label)."""
    for bx, by, bw, bh, a, label in boxes:
        col = PANEL if a is None else blend(AMBER, CHARCOAL, a)
        d.rounded_rectangle([x0 + bx, y0 + by, x0 + bx + bw - 1, y0 + by + bh - 1], radius=6, fill=col)
        if label and label_font:
            tc = CHARCOAL if a and a >= 0.9 else (HINT if a is None else OFFWHITE)
            d.text((x0 + bx + 14, y0 + by + 12), label, font=label_font, fill=tc)


def social_preview(w=1280, h=640, scale=1):
    W, H = w * scale, h * scale
    im = Image.new("RGBA", (W, H), BASE + (255,))
    d = ImageDraw.Draw(im)
    S = scale
    # header: mark + wordmark
    paste_mark(im, 56 * S, (64 * S, 72 * S))
    wordmark(d, 138 * S, 76 * S, 34 * S)
    # headline
    fh = font(800, 62 * S)
    d.text((64 * S, 214 * S), "See where your", font=fh, fill=OFFWHITE)
    d.text((64 * S, 282 * S), "disk space went.", font=fh, fill=OFFWHITE)
    fb = font(500, 22 * S)
    for i, line in enumerate(["A zoomable map of your drives for Windows.",
                              "Filter in plain words, fly into folders,",
                              "clean up from the map."]):
        d.text((64 * S, (372 + i * 32) * S), line, font=fb, fill=MUTED)
    # chips
    fc = font(500, 15 * S)
    x = 64 * S
    for chip in ["Free", "Open source", "Portable exe"]:
        tw = d.textlength(chip, font=fc)
        d.rounded_rectangle([x, 536 * S, x + tw + 24 * S, 566 * S], radius=6 * S, fill=CHARCOAL, outline=PANEL, width=S)
        d.text((x + 12 * S, 542 * S), chip, font=fc, fill=MUTED)
        x += tw + 34 * S
    d.text((64 * S, 588 * S), "github.com/ClearanceClarence/SpaceSharp", font=fc, fill=HINT)
    # map
    mx, my, mw, mh = 660 * S, 40 * S, 580 * S, 560 * S
    d.rounded_rectangle([mx, my, mx + mw - 1, my + mh - 1], radius=16 * S, fill=CHARCOAL)
    fl = font(600, 17 * S, opsz96=False)
    boxes = [
        (12, 12, 322, 322, 1.0, "Videos"), (342, 12, 226, 196, 0.55, "Games"),
        (342, 216, 142, 118, 0.38, "Photos"), (492, 216, 76, 118, 0.26, ""),
        (12, 342, 202, 206, 0.30, "Users"), (222, 342, 146, 116, 0.22, ""),
        (222, 466, 146, 82, 0.16, ""), (376, 342, 116, 206, 0.20, ""),
        (500, 342, 68, 90, 0.14, ""), (500, 440, 68, 108, None, "Free"),
    ]
    treemap(d, mx, my, mw, mh, [(bx * S, by * S, bw * S, bh * S, a, l) for bx, by, bw, bh, a, l in boxes], fl)
    return im


def header(w=1280, h=360):
    """README header: mark, wordmark and tagline, with a quiet abstract treemap on the right. No screenshot,
    no labels, only the shape the app is about, in the brand's amber steps."""
    S = 2
    im = Image.new('RGBA', (w * S, h * S), BASE + (255,))
    d = ImageDraw.Draw(im)
    # abstract map: a handful of boxes, largest first from the top left, brightness following size
    x0, y0, mw, mh = 720 * S, 0, 560 * S, h * S
    boxes = [
        (0, 0, 300, 220, 0.55), (306, 0, 254, 140, 0.34), (306, 146, 150, 74, 0.24), (462, 146, 98, 74, 0.17),
        (0, 226, 190, 134, 0.26), (196, 226, 140, 80, 0.18), (196, 312, 140, 48, 0.12), (342, 226, 120, 134, 0.14),
        (468, 226, 92, 64, 0.10), (468, 296, 92, 64, 0.07),
    ]
    for bx, by, bw, bh, a in boxes:
        d.rounded_rectangle([x0 + bx * S, y0 + by * S, x0 + (bx + bw) * S - 1, y0 + (by + bh) * S - 1], radius=6 * S,
                            fill=blend(AMBER, BASE, a * 0.34))
    # fade the map's left edge so the words sit on a clean surface
    fade = Image.new('RGBA', (w * S, h * S), (0, 0, 0, 0))
    fd = ImageDraw.Draw(fade)
    span = 300 * S
    for i in range(span):
        fd.line([(x0 + i, 0), (x0 + i, h * S)], fill=BASE + (int(255 * (1 - i / span) ** 1.4),))
    im.alpha_composite(fade)
    d = ImageDraw.Draw(im)
    paste_mark(im, 112 * S, (72 * S, (h // 2 - 56) * S), tile=PANEL)
    wordmark(d, 210 * S, (h // 2 - 62) * S, 84 * S)
    d.text((214 * S, (h // 2 + 40) * S), "See where your disk space went.", font=font(500, 27 * S), fill=MUTED)
    return im.resize((w, h), Image.LANCZOS)


def banner():
    """WixUIBannerBmp: the installer draws the page title in black on the left, so keep that side light
    and put the mark at the right edge."""
    S = 2
    im = Image.new("RGBA", (493 * S, 58 * S), OFFWHITE + (255,))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 58 * S - 2 * S, 493 * S, 58 * S], fill=AMBER)
    paste_mark(im, 40 * S, (493 * S - 52 * S, 8 * S))
    f = font(800, 17 * S)
    w = d.textlength("SpaceSharp", font=f)
    x = 493 * S - 62 * S - w
    x = text(d, (x, 17 * S), "Space", font(500, 17 * S), CHARCOAL, -0.03)
    text(d, (x, 17 * S), "Sharp", f, AMBER_DEEP, -0.03)
    return im.resize((493, 58), Image.LANCZOS)


def logo():
    """WixUIDialogBmp: the welcome and finish pages draw their text in black over the right part,
    so only the left 164 px column is branded and the rest stays light."""
    S = 2
    col = 164
    im = Image.new("RGBA", (493 * S, 312 * S), OFFWHITE + (255,))
    d = ImageDraw.Draw(im)
    d.rectangle([0, 0, col * S, 312 * S], fill=CHARCOAL)
    d.rectangle([col * S - 2 * S, 0, col * S, 312 * S], fill=AMBER)
    paste_mark(im, 88 * S, ((col - 88) // 2 * S, 40 * S), tile=PANEL)
    f1, f2 = font(500, 24 * S), font(800, 24 * S)
    w = d.textlength("Space", font=f1) + d.textlength("Sharp", font=f2) - 0.03 * 24 * S * 10
    x = (col * S - w) / 2
    x = text(d, (x, 150 * S), "Space", f1, OFFWHITE, -0.03)
    text(d, (x, 150 * S), "Sharp", f2, AMBER, -0.03)
    ft = font(500, 11 * S)
    for i, line in enumerate(["See where your", "disk space went."]):
        tw = d.textlength(line, font=ft)
        d.text(((col * S - tw) / 2, (186 + i * 16) * S), line, font=ft, fill=MUTED)
    # small map at the bottom of the column
    boxes = [
        (0, 0, 78, 64, 1.0, ""), (84, 0, 52, 36, 0.55, ""), (84, 42, 26, 22, 0.35, ""), (116, 42, 20, 22, 0.22, ""),
    ]
    treemap(d, 14 * S, 234 * S, 136 * S, 64 * S, [(bx * S, by * S, bw * S, bh * S, a, l) for bx, by, bw, bh, a, l in boxes])
    return im.resize((493, 312), Image.LANCZOS)


def main():
    ensure_fonts()
    assets = ROOT / "SpaceSharp" / "Assets"
    docs = ROOT / "docs"
    inst = ROOT / "installer"

    (assets / "SpaceSharp.svg").write_text(mark_svg(CELLS, CHARCOAL, 22, 3), encoding="utf-8")
    (assets / "SpaceSharp-small.svg").write_text(mark_svg(CELLS_SMALL, CHARCOAL, 24, 5), encoding="utf-8")
    (docs / "wordmark.svg").write_text(wordmark_svg(), encoding="utf-8")

    write_ico(assets / "SpaceSharp.ico", [16, 20, 24, 32, 40, 48, 64, 128, 256])
    icon(256).save(assets / "SpaceSharp-256.png")
    icon(256).save(docs / "icon.png")
    icon(512).save(docs / "icon-512.png")

    social_preview(scale=1).convert("RGB").save(docs / "social-preview.png", optimize=True)
    header().convert("RGB").save(docs / "header.png", optimize=True)

    banner().convert("RGB").save(inst / "banner.bmp")
    logo().convert("RGB").save(inst / "logo.bmp")
    print("done")


if __name__ == "__main__":
    main()
