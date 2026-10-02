"""Builds docs/social-preview.png (1280x640): the message itself laid out as a treemap. The biggest boxes carry the
tagline, the smaller ones the facts, in amber steps on GitHub's dark page color. No screenshot needed."""
from pathlib import Path
import importlib.util
from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('ma', HERE / 'make-assets.py'); ma = importlib.util.module_from_spec(spec); spec.loader.exec_module(ma)
ma.ensure_fonts()

S = 2; W, H = 1280 * S, 640 * S
GITHUB = (0x0D, 0x11, 0x17)
im = Image.new('RGBA', (W, H), GITHUB + (255,))
d = ImageDraw.Draw(im)

def tone(a):  # amber over the page color, a = strength 0..1
    return ma.blend(ma.AMBER, GITHUB, a)

# the layout: (x, y, w, h, strength, text, size, weight, color, sub)
# coordinates in the 1280x640 frame, gutters of 10 px like the mark's cells
G = 10; M = 40
boxes = [
    (M, M, 586, 320, 1.00, "Your drive,", 92, 800, (0x1C, 0x15, 0x00), None),
    (M + 596, M, 604, 190, 0.78, "as a map.", 92, 800, (0x1C, 0x15, 0x00), None),
    (M + 596, M + 200, 298, 120, 0.46, "Scan C: in 2 s", 30, 800, ma.OFFWHITE, "reads the NTFS file table"),
    (M + 904, M + 200, 296, 120, 0.34, "See what grew", 30, 800, ma.OFFWHITE, "compared with last time"),
    (M, M + 330, 290, 230, 0.30, "Filter in words", 30, 800, ma.OFFWHITE, '"videos over 500MB older\nthan 1 year"'),
    (M + 300, M + 330, 286, 230, 0.22, "Clean up from\nthe map", 30, 800, ma.OFFWHITE, "straight to the Recycle Bin"),
    (M + 596, M + 330, 298, 108, 0.17, "English and Norwegian", 22, 800, ma.OFFWHITE, None),
    (M + 904, M + 330, 296, 108, 0.14, "Windows 10 and 11", 22, 800, ma.OFFWHITE, None),
    (M + 596, M + 448, 298, 112, 0.11, "Free and open source", 22, 800, ma.OFFWHITE, "MIT license"),
    (M + 904, M + 448, 296, 112, 0.08, None, 0, 0, None, None),  # the brand cell
]
for x, y, w, h, a, text, size, weight, color, sub in boxes:
    d.rounded_rectangle([x * S, y * S, (x + w) * S - 1, (y + h) * S - 1], radius=8 * S, fill=tone(a))
    if text is None: continue
    f = ma.font(weight, size * S)
    lines = text.split("\n")
    ty = (y + 18) * S
    for line in lines:
        d.text(((x + 20) * S, ty), line, font=f, fill=color)
        ty += int(size * 1.08 * S)
    if sub:
        fs = ma.font(500, 15 * S)
        subcol = (0x3A, 0x2E, 0x08) if a > 0.6 else ma.MUTED
        sy = ty + 6 * S
        for line in sub.split("\n"):
            d.text(((x + 20) * S, sy), line, font=fs, fill=subcol); sy += 20 * S

# the brand cell: mark + wordmark, bottom right
bx, by = M + 904, M + 448
ma.paste_mark(im, 44 * S, ((bx + 20) * S, (by + 34) * S), tile=ma.PANEL)
ma.wordmark(d, (bx + 76) * S, (by + 38) * S, 30 * S)
d.text(((bx + 78) * S, (by + 76) * S), "clearanceclarence.github.io/SpaceSharp", font=ma.font(500, 12 * S), fill=ma.HINT)

# size labels in the corner of the two tagline boxes, the way the app writes them
fl = ma.font(600, 14 * S)
d.text(((M + 586 - 20) * S - d.textlength("271.37 GB", font=fl), (M + 320 - 36) * S), "271.37 GB", font=fl, fill=(0x5A, 0x45, 0x0A))
d.text(((M + 596 + 604 - 20) * S - d.textlength("137.9 GB", font=fl), (M + 190 - 36) * S), "137.9 GB", font=fl, fill=(0x5A, 0x45, 0x0A))

out = im.resize((1280, 640), Image.LANCZOS).convert('RGB')
out.save(HERE.parent / 'docs' / 'social-preview.png', optimize=True)
print('ok')
