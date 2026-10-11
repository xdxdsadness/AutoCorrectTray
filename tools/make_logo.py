# App logo asset pipeline: full-size PNG, crisp small tray versions
# (LANCZOS + unsharp mask) and a multi-frame ICO for the exe.
#
# Usage: python tools/make_logo.py
import os
from PIL import Image, ImageFilter

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'assets', 'logo-source.png')
ASSETS = os.path.join(ROOT, 'src', 'AutoCorrectTray', 'Assets')

img = Image.open(SRC).convert('RGBA')

# Crop by alpha channel (getbbox is fooled by transparent px with non-zero channels)
alpha = img.getchannel('A')
w, h = img.size
minx, miny, maxx, maxy = w, h, -1, -1
apx = alpha.load()
for y in range(h):
    for x in range(w):
        if apx[x, y] >= 10:
            if x < minx: minx = x
            if x > maxx: maxx = x
            if y < miny: miny = y
            if y > maxy: maxy = y
pad = 8
img = img.crop((max(0, minx - pad), max(0, miny - pad), min(w, maxx + pad + 1), min(h, maxy + pad + 1)))

os.makedirs(ASSETS, exist_ok=True)
img.save(os.path.join(ASSETS, 'logo.png'))


def fit_square(size, dim=None, sharpen=False):
    side = max(img.size)
    canvas = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    canvas.paste(img, ((side - img.size[0]) // 2, (side - img.size[1]) // 2))
    out = canvas.resize((size, size), Image.LANCZOS)
    if dim is not None:
        a = out.getchannel('A').point(lambda v: int(v * dim))
        out.putalpha(a)
    if sharpen:
        out = out.filter(ImageFilter.UnsharpMask(radius=0.9, percent=90, threshold=2))
    return out


fit_square(32).save(os.path.join(ASSETS, 'tray.png'))
fit_square(32, dim=0.38, sharpen=True).save(os.path.join(ASSETS, 'tray_paused.png'))

ico_frames = []
for size in (256, 64, 48, 32, 24, 20, 16):
    ico_frames.append(fit_square(size, sharpen=size <= 32))
ico_frames[0].save(
    os.path.join(ASSETS, 'app.ico'),
    sizes=[(f.size[0], f.size[1]) for f in ico_frames],
    append_images=ico_frames[1:])
print('OK', img.size)
