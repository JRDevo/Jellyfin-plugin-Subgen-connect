"""Make a Jellyfin catalog banner from the Subgen icon: white icon + name on dark grey."""
import sys
from PIL import Image, ImageDraw, ImageFont, ImageOps

src, out, font_path = sys.argv[1], sys.argv[2], sys.argv[3]
W, H = 1280, 720

# Dark grey diagonal gradient, like the Open Subtitles tile
bg = Image.new("RGB", (W, H))
px = bg.load()
for y in range(H):
    for x in range(W):
        t = (x / W + y / H) / 2
        v = int(78 - 34 * t)
        px[x, y] = (v, v, v)
bg = bg.convert("RGBA")

# Icon: dark lines -> white, white background -> transparent
raw = Image.open(src).convert("RGBA")
flat = Image.new("RGBA", raw.size, (255, 255, 255, 255))
flat.alpha_composite(raw)
icon = flat.convert("L")
icon = icon.crop(ImageOps.invert(icon).getbbox())
alpha = ImageOps.invert(icon).point(lambda a: 0 if a < 40 else min(255, int(a * 1.15)))
white = Image.new("RGBA", icon.size, (255, 255, 255, 0))
white.putalpha(alpha)
size = 300
white = white.resize((size, size), Image.LANCZOS)

font = ImageFont.truetype(font_path, 150)
text = "Subgen"
d = ImageDraw.Draw(bg)
tb = d.textbbox((0, 0), text, font=font)
tw, th = tb[2] - tb[0], tb[3] - tb[1]
gap = 60
total = size + gap + tw
x0 = (W - total) // 2
bg.alpha_composite(white, (x0, (H - size) // 2))
d.text((x0 + size + gap - tb[0], (H - th) // 2 - tb[1]), text, font=font, fill=(255, 255, 255, 255))

bg.convert("RGB").save(out, "PNG", optimize=True)
print("saved", out)
