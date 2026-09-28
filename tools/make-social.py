"""Builds docs/social-preview.png (1280x640): wordmark and tagline on the left, the app window on the right.

Steps: open tools/screenshot-mockup.html in a browser at 200% zoom, Plain map scene, dark theme, Classic, Top folder,
Pastel, side panel on; capture the window and save it as tools/window.png; then run this script.
"""
from PIL import Image, ImageDraw, ImageFilter
import importlib.util
from pathlib import Path
HERE=Path(__file__).resolve().parent
spec=importlib.util.spec_from_file_location('ma', HERE / 'make-assets.py'); ma=importlib.util.module_from_spec(spec); spec.loader.exec_module(ma)
ma.ensure_fonts()
S=2; W,H=1280*S,640*S
im=Image.new('RGBA',(W,H),ma.BASE+(255,))
# soft amber glow behind the window, bottom right
glow=Image.new('RGBA',(W,H),(0,0,0,0)); gd=ImageDraw.Draw(glow)
gd.ellipse([W*0.45,H*0.05,W*1.15,H*1.05], fill=ma.AMBER+(70,))
glow=glow.filter(ImageFilter.GaussianBlur(160*S)); im.alpha_composite(glow)
d=ImageDraw.Draw(im)
# left: mark + wordmark + tagline
ma.paste_mark(im, 64*S, (72*S, 72*S), tile=ma.PANEL)
ma.wordmark(d, 152*S, 78*S, 40*S)
fh=ma.font(800, 56*S)
d.text((72*S, 214*S), "See where your", font=fh, fill=ma.OFFWHITE)
d.text((72*S, 278*S), "disk space went.", font=fh, fill=ma.OFFWHITE)
fb=ma.font(500, 19*S)
for i,line in enumerate(["Every file on the drive as a box sized by","the space it takes. Scan C: in seconds,","filter in plain words, clean up from the map."]):
    d.text((72*S,(366+i*28)*S), line, font=fb, fill=ma.MUTED)
fc=ma.font(500, 15*S)
d.text((72*S, 548*S), "Free and open source for Windows", font=fc, fill=ma.HINT)
d.text((72*S, 572*S), "github.com/ClearanceClarence/SpaceSharp", font=fc, fill=ma.HINT)
# right: the app window, cropped so the map dominates, bleeding off the right and bottom
win=Image.open(HERE / 'window.png').convert('RGBA')   # the app window from screenshot-mockup.html, captured at 2x (Plain map scene, dark, Classic, Top folder, Pastel)
scale=(900*S)/win.width
win=win.resize((int(win.width*scale), int(win.height*scale)), Image.LANCZOS)
# rounded corners + shadow
r=14*S
mask=Image.new('L',win.size,0); ImageDraw.Draw(mask).rounded_rectangle([0,0,win.width-1,win.height-1], radius=r, fill=255)
shadow=Image.new('RGBA',(W,H),(0,0,0,0)); sd=ImageDraw.Draw(shadow)
x,y=612*S, 96*S
sd.rounded_rectangle([x-6*S,y+18*S,x+win.width+6*S,y+win.height+30*S], radius=r, fill=(0,0,0,170))
shadow=shadow.filter(ImageFilter.GaussianBlur(28*S)); im.alpha_composite(shadow)
frame=Image.new('RGBA',win.size,(0,0,0,0)); frame.paste(win,(0,0),mask)
fd=ImageDraw.Draw(frame); fd.rounded_rectangle([0,0,win.width-1,win.height-1], radius=r, outline=ma.LINE+(255,), width=2*S)
im.alpha_composite(frame,(x,y))
out=im.resize((1280,640), Image.LANCZOS).convert('RGB')
out.save(HERE.parent / 'docs' / 'social-preview.png', optimize=True)
print('ok')
