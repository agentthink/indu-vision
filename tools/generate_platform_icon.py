"""Rebuild the platform's geometric SVG, PNG and multi-resolution Windows ICO.

Optional design tool only; requires Python 3 and Pillow. Not needed to run WPF.
"""
from pathlib import Path
from PIL import Image, ImageDraw

assets = Path(__file__).resolve().parents[1] / "Assets"
assets.mkdir(exist_ok=True)
scale = 4
image = Image.new("RGBA", (256 * scale, 256 * scale))
draw = ImageDraw.Draw(image)


def points(items):
    return [(x * scale, y * scale) for x, y in items]


draw.rounded_rectangle((8 * scale, 8 * scale, 248 * scale, 248 * scale),
                       radius=54 * scale, fill="#2563EB")
draw.polygon(points([(128, 48), (202, 91), (128, 134), (54, 91)]), fill="#38BDF8")
paths = [
    [(128, 48), (202, 91), (202, 167), (128, 210), (54, 167), (54, 91), (128, 48)],
    [(54, 91), (128, 134), (202, 91)],
    [(128, 134), (128, 210)],
]
for path in paths:
    draw.line(points(path), fill="white", width=12 * scale, joint="curve")
    for x, y in path:
        draw.ellipse(((x - 6) * scale, (y - 6) * scale,
                      (x + 6) * scale, (y + 6) * scale), fill="white")
image = image.resize((256, 256), Image.Resampling.LANCZOS)
image.save(assets / "PlatformIcon.png")
image.save(assets / "PlatformIcon.ico", format="ICO",
           sizes=[(n, n) for n in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
(assets / "PlatformIcon.svg").write_text('''<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">
  <title>数字孪生工业自动化控制平台</title>
  <rect x="8" y="8" width="240" height="240" rx="54" fill="#2563EB"/>
  <path d="M128 48 202 91 128 134 54 91Z" fill="#38BDF8"/>
  <g fill="none" stroke="#FFFFFF" stroke-width="12" stroke-linejoin="round" stroke-linecap="round">
    <path d="M128 48 202 91V167L128 210 54 167V91Z"/>
    <path d="M54 91 128 134 202 91M128 134V210"/>
  </g>
</svg>
''', encoding="utf-8")
print("Created PlatformIcon.svg, PlatformIcon.png and PlatformIcon.ico")
