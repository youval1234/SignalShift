"""Render SonarShift's original vector mark into a Windows multi-resolution icon.
Only needed to regenerate assets: python -m pip install pillow
"""
from pathlib import Path
from PIL import Image, ImageDraw
import math

root = Path(__file__).resolve().parent.parent / "Assets"
root.mkdir(exist_ok=True)
size, scale = 256, 4
canvas = Image.new("RGBA", (size * scale, size * scale))
mask = Image.new("L", canvas.size)
draw = ImageDraw.Draw(mask)
draw.rounded_rectangle((0, 0, 256*scale-1, 256*scale-1), radius=60*scale, fill=255)
pixels = canvas.load()
for y in range(size*scale):
    for x in range(size*scale):
        t = (x+y)/(2*size*scale)
        pixels[x,y] = (int(151-51*t), int(119-42*t), int(247-33*t), 255)
canvas.putalpha(mask)
draw = ImageDraw.Draw(canvas)
# Two opposing routes: a single distinctive sound-switch symbol.
def route(points, color, width=18):
    p = [(int(x*scale), int(y*scale)) for x,y in points]
    draw.line(p, fill=color, width=width*scale, joint="curve")
    r=width*scale/2
    for x,y in (p[0],p[-1]): draw.ellipse((int(x-r),int(y-r),int(x+r),int(y+r)),fill=color)

route([(60,102),(60,92),(63,81),(72,73),(83,70),(177,70)], "#F5F1FF")
draw.polygon([(158*scale,46*scale),(190*scale,70*scale),(158*scale,94*scale)], fill="#F5F1FF")
route([(196,154),(196,164),(193,175),(184,183),(173,186),(79,186)], "#9FF4D8")
draw.polygon([(98*scale,162*scale),(66*scale,186*scale),(98*scale,210*scale)], fill="#9FF4D8")
for x,h in [(98,26),(128,48),(158,26)]:
    draw.rounded_rectangle(((x-6)*scale,(128-h/2)*scale,(x+6)*scale,(128+h/2)*scale),radius=6*scale,fill="#F5F1FF")
canvas = canvas.resize((256,256), Image.Resampling.LANCZOS)
canvas.save(root / "SonarShift.png")
canvas.save(root / "SonarShift.ico", sizes=[(16,16),(20,20),(24,24),(32,32),(40,40),(48,48),(64,64),(128,128),(256,256)])
print(root / "SonarShift.ico")
