"""Put each icon PNG onto a mint-green (#6ee7b7) background.

Strategy: flood-fill from the four corners, replacing connected near-white
pixels with mint. This converts the outer background without touching the
book's internal white pages (they're bounded by the blue book cover). Also
handles PNGs with alpha — transparent pixels become mint via composite first.
"""
import sys
from pathlib import Path
from PIL import Image, ImageDraw

MINT = (0x6E, 0xE7, 0xB7, 255)
ICONS_DIR = Path(__file__).resolve().parent.parent / "web-svelte" / "static" / "icons"

TARGETS = [
    "icon-192.png",
    "icon-512.png",
    "icon-192-maskable.png",
    "icon-512-maskable.png",
    "apple-touch-icon.png",
    "favicon-16.png",
    "favicon-32.png",
]


def composite_on_mint(img: Image.Image) -> Image.Image:
    img = img.convert("RGBA")
    bg = Image.new("RGBA", img.size, MINT)
    return Image.alpha_composite(bg, img).convert("RGBA")


def flood_fill_corners_to_mint(img: Image.Image, threshold: int = 230) -> Image.Image:
    """Flood-fill each corner pixel-cluster of 'near-white' with mint."""
    img = img.convert("RGBA")
    w, h = img.size
    # PIL floodfill operates on a single-mode image; using RGBA works with a
    # tolerance-based approximation via ImageDraw isn't exposed. Do our own
    # iterative flood fill using a stack + per-pixel threshold check.
    px = img.load()

    def is_bg(x: int, y: int) -> bool:
        r, g, b, a = px[x, y]
        return a < 10 or (r >= threshold and g >= threshold and b >= threshold)

    seeds = [(0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1)]
    visited = [[False] * h for _ in range(w)]
    stack: list[tuple[int, int]] = []
    for sx, sy in seeds:
        if is_bg(sx, sy):
            stack.append((sx, sy))

    while stack:
        x, y = stack.pop()
        if x < 0 or y < 0 or x >= w or y >= h:
            continue
        if visited[x][y]:
            continue
        visited[x][y] = True
        if not is_bg(x, y):
            continue
        px[x, y] = MINT
        stack.append((x + 1, y))
        stack.append((x - 1, y))
        stack.append((x, y + 1))
        stack.append((x, y - 1))

    return img


def main() -> int:
    if not ICONS_DIR.exists():
        print(f"Icons dir not found: {ICONS_DIR}", file=sys.stderr)
        return 1

    for name in TARGETS:
        p = ICONS_DIR / name
        if not p.exists():
            print(f"  skip (missing): {name}")
            continue
        img = Image.open(p)
        # First composite so any transparent pixels become mint, then flood
        # the remaining outer-white region to mint.
        img = composite_on_mint(img)
        img = flood_fill_corners_to_mint(img)
        img.save(p, format="PNG", optimize=True)
        print(f"  mint bg applied: {name} ({img.size[0]}x{img.size[1]})")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
