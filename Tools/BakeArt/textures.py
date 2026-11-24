#!/usr/bin/env python3
"""
Makes the ground and road textures the game loads, from the photo scans in Art/ambientcg/.

A scan covers only a couple of meters, which from the height the game is seen from would
average out to one flat color. So each texture here repeats its scan small, for the detail
you see zoomed in, and lays over it what you see from further off: lighter and darker
stretches, relaid patches and cracks in the asphalt, bare and dry places in the grass.

    python3 Tools/BakeArt/textures.py                 write the textures
    python3 Tools/BakeArt/textures.py preview.png     also draw a sample of each, and a mock-up of a road

The files are written as JPEG data with the ending .bytes, so that Unity hands them over
untouched and the game decodes them itself (ArtKit.Photo). Needs numpy and Pillow.
"""
import os
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "Art", "ambientcg")
OUT = os.path.join(ROOT, "Assets", "Resources", "Art")
rng = np.random.default_rng(7)


def scan(name, size):
    """A scan, resized, as numbers from 0 to 1."""
    return np.asarray(Image.open(os.path.join(SRC, name + ".jpg")).convert("RGB").resize((size, size), Image.LANCZOS), dtype=np.float32) / 255


def tiled(image, times):
    return np.tile(image, (times, times, 1))


def hills(size, cycles, seed):
    """Smooth random hills from 0 to 1 that repeat at the edges, so the texture tiles."""
    r = np.random.default_rng(seed)
    grid = r.random((cycles, cycles)).astype(np.float32)
    big = Image.fromarray((np.tile(grid, (3, 3)) * 255).astype(np.uint8)).resize((size * 3, size * 3), Image.BICUBIC)
    out = np.asarray(big, dtype=np.float32)[size:2 * size, size:2 * size] / 255
    return (out - out.min()) / max(out.max() - out.min(), 1e-6)


def save(name, image, quality=88):
    os.makedirs(OUT, exist_ok=True)
    path = os.path.join(OUT, name + ".bytes")
    Image.fromarray((np.clip(image, 0, 1) * 255).astype(np.uint8)).save(path, "JPEG", quality=quality)
    return path


def asphalt(size=2048):
    """64 m of road: the scan sixteen times across, toned, patched and cracked."""
    stone = scan("Asphalt012", size // 16)
    # The scan has red paint in it; keep its grain, drop its color.
    gray = stone.mean(axis=2, keepdims=True)
    base = tiled(np.repeat(gray, 3, axis=2), 16)
    base = 0.33 + (base - base.mean()) * 0.9
    tone = (hills(size, 7, 1) - 0.5) * 0.08 + (hills(size, 31, 2) - 0.5) * 0.05
    marks = np.zeros((size, size), np.float32)
    for _ in range(16):
        # A relaid patch: a darker rectangle with a paler seam round it.
        x, y = rng.integers(0, size, 2)
        w, h = int(rng.uniform(0.02, 0.07) * size), int(rng.uniform(0.012, 0.035) * size)
        ys, xs = np.ix_(np.arange(y - 1, y + h + 1) % size, np.arange(x - 1, x + w + 1) % size)
        marks[ys, xs] = 0.025
        ys, xs = np.ix_(np.arange(y, y + h) % size, np.arange(x, x + w) % size)
        marks[ys, xs] = -rng.uniform(0.025, 0.05)
    for _ in range(70):
        # A crack: a dark line that wanders.
        x, y, heading = rng.uniform(0, size), rng.uniform(0, size), rng.uniform(0, 6.28)
        for _ in range(int(rng.uniform(0.03, 0.14) * size)):
            heading += rng.uniform(-0.4, 0.4)
            x += np.cos(heading)
            y += np.sin(heading)
            marks[int(y) % size, int(x) % size] = -0.08
            marks[int(y + 1) % size, int(x) % size] = -0.05
    image = base + (tone + marks)[:, :, None]
    # A little blue in the gray, as worn asphalt has.
    return image * np.array([0.97, 0.99, 1.04], np.float32)


def ground(size=2048):
    """128 m of open ground: mown-looking grass, rougher grass, and bare, mossy earth, in drifts."""
    lawn = tiled(scan("Grass004", size // 16), 16)
    rough = tiled(scan("Grass001", size // 16), 16)
    earth = tiled(scan("Ground037", size // 8), 8)
    # Tame the greens so they sit together, and take the edge off the brightest.
    lawn = lawn * np.array([0.92, 0.9, 0.88], np.float32)
    rough = rough * np.array([1.25, 1.2, 1.15], np.float32)
    a = np.clip((hills(size, 6, 11) * 0.7 + hills(size, 23, 12) * 0.3 - 0.42) * 3.5, 0, 1)[:, :, None]
    image = lawn * (1 - a) + rough * a
    b = np.clip((hills(size, 9, 13) * 0.6 + hills(size, 37, 14) * 0.4 - 0.62) * 5, 0, 1)[:, :, None]
    image = image * (1 - b * 0.85) + earth * (b * 0.85)
    return image * (0.94 + 0.12 * hills(size, 4, 15))[:, :, None]


def sidewalk(size=512):
    """One slab's length of sidewalk: across the picture is across the walk, with the curb stone at the left."""
    stone = scan("Concrete034", size)
    image = 0.69 + (stone - stone.mean()) * 0.8
    image = image * np.array([1.0, 0.985, 0.95], np.float32)
    image *= (0.95 + 0.1 * hills(size, 6, 21))[:, :, None]
    curb = int(size * 0.13)
    image[:, :curb] *= 1.05
    image[:, curb - 2:curb + 2] *= 0.72
    image[:3] *= 0.7
    image[-2:] *= 0.7
    return image


def gravel(size=512):
    """12 m of highway shoulder."""
    stones = tiled(scan("Gravel022", size // 4), 4)
    image = 0.5 + (stones - stones.mean()) * 0.55
    image = image * np.array([1.04, 1.0, 0.92], np.float32)
    return image * (0.9 + 0.2 * hills(size, 7, 31))[:, :, None]


def foliage(size=512):
    """
    The top of a tree, in grays for the game to tint: clumps of leaves, each lit from one side
    and dark where it meets the next, with the grain of real leaves over them.
    """
    yy, xx = np.mgrid[0:size, 0:size].astype(np.float32)
    light = np.full((size, size), 0.2, np.float32)
    height = np.zeros((size, size), np.float32)
    for _ in range(420):
        cx, cy, r = rng.uniform(0, size), rng.uniform(0, size), rng.uniform(0.035, 0.085) * size
        for ox in (-size, 0, size):
            for oy in (-size, 0, size):
                dx, dy = xx - cx - ox, yy - cy - oy
                d2 = (dx * dx + dy * dy) / (r * r)
                inside = d2 < 1
                if not inside.any():
                    continue
                dome = np.sqrt(np.clip(1 - d2, 0, 1))
                top = rng.uniform(0.2, 1.0) + dome * 0.5
                # Lit from the upper left.
                shade = 0.55 + 0.45 * dome + 0.22 * (-dx - dy) / (r * 1.4)
                higher = inside & (top > height)
                height[higher] = top[higher]
                light[higher] = shade[higher]
    leaves = scan("Grass001", size // 2).mean(axis=2)
    leaves = np.tile(leaves, (2, 2))
    image = np.clip(light, 0.12, 1.1) * (0.75 + 0.9 * (leaves - leaves.mean() + 0.28))
    image = np.clip(image * 0.56 / np.median(image), 0, 1)
    return np.repeat(image[:, :, None], 3, axis=2)


def mock(path, parts):
    """A sample of each texture, and a rough picture of how a road made of them should look from above."""
    asph, grnd, walk, grav, leaf = parts
    scale = 16  # pixels to the meter
    w, h = 1500, 700
    canvas = np.tile(grnd, (2, 2, 1))[:h, :w].copy()
    road = np.tile(asph, (1, 2, 1))  # 32 px to the meter in the file; use every other pixel
    road = road[::2, ::2]
    lanes, lane = 2, int(3.66 * scale)
    top = h // 2 - lanes * lane
    canvas[top:top + 2 * lanes * lane] = road[:2 * lanes * lane, :w]
    # Wear: darker wheel tracks and an oil line in each lane.
    xs = np.linspace(0, 1, lane)
    track = 0.10 * (np.exp(-((xs - 0.27) / 0.09) ** 2) + np.exp(-((xs - 0.73) / 0.09) ** 2)) + 0.05 * np.exp(-((xs - 0.5) / 0.06) ** 2)
    for k in range(2 * lanes):
        canvas[top + k * lane:top + (k + 1) * lane] *= (1 - track)[:, None, None]
    # Paint.
    white, yellow = np.array([0.86, 0.86, 0.83]), np.array([0.85, 0.68, 0.2])
    mid = top + lanes * lane
    canvas[mid - 4:mid - 2] = yellow
    canvas[mid + 2:mid + 4] = yellow
    for k in (1, 3):
        y = top + k * lane
        for x in range(0, w, int(12.2 * scale)):
            canvas[y - 1:y + 1, x:x + int(3.05 * scale)] = white
    canvas[top + 3:top + 5] = white
    canvas[top + 2 * lanes * lane - 5:top + 2 * lanes * lane - 3] = white
    # Sidewalks either side, with the curb stone against the road, and a soft shadow at the curb.
    walk_w = int(2.2 * scale)
    slab = np.asarray(Image.fromarray((np.clip(walk, 0, 1) * 255).astype(np.uint8)).resize((int(1.5 * scale), walk_w)), dtype=np.float32) / 255
    strip = np.tile(np.transpose(slab, (1, 0, 2)).transpose(1, 0, 2), (1, w // slab.shape[1] + 1, 1))[:, :w]
    canvas[top - walk_w:top] = strip[::-1]
    canvas[top + 2 * lanes * lane:top + 2 * lanes * lane + walk_w] = strip
    canvas[top:top + 6] *= 0.8
    # Trees: tinted foliage in rough circles, with shadows thrown to the lower right.
    yy, xx = np.mgrid[0:h, 0:w]
    tints = [(0.42, 0.62, 0.32), (0.33, 0.52, 0.28), (0.5, 0.66, 0.34), (0.8, 0.55, 0.24), (0.28, 0.45, 0.3)]
    rng2 = np.random.default_rng(99)
    for i in range(26):
        cx, cy, r = rng2.uniform(0, w), rng2.choice([rng2.uniform(20, top - walk_w - 60), rng2.uniform(top + 2 * lanes * lane + walk_w + 60, h - 20)]), rng2.uniform(40, 68)
        wob = 1 + 0.16 * np.sin(np.arctan2(yy - cy, xx - cx) * rng2.integers(3, 7) + rng2.uniform(0, 6))
        d = np.sqrt((xx - cx) ** 2 + (yy - cy) ** 2) / (r * wob)
        shadow = np.sqrt((xx - cx - 34) ** 2 + (yy - cy - 24) ** 2) / (r * wob) < 1
        canvas[shadow & (d >= 1)] *= 0.6
        inside = d < 1
        u = ((xx - cx) / (2.4 * r) * leaf.shape[1] + rng2.integers(0, 512)).astype(int) % leaf.shape[1]
        v = ((yy - cy) / (2.4 * r) * leaf.shape[0] + rng2.integers(0, 512)).astype(int) % leaf.shape[0]
        tint = np.array(tints[i % len(tints)], np.float32)
        crown = leaf[v, u] * tint * 1.35 * (1.05 - 0.35 * d[:, :, None] ** 2)
        canvas[inside] = crown[inside]
    samples = [np.asarray(Image.fromarray((np.clip(p, 0, 1) * 255).astype(np.uint8)).resize((300, 300)), dtype=np.float32) / 255 for p in parts]
    sheet = np.zeros((h + 310, w, 3), np.float32) + 0.12
    sheet[:h] = canvas
    for i, s in enumerate(samples):
        sheet[h + 5:h + 305, 5 + i * 298:305 + i * 298] = s
    Image.fromarray((np.clip(sheet, 0, 1) * 255).astype(np.uint8)).save(path)


def main():
    parts = [asphalt(), ground(), sidewalk(), gravel(), foliage()]
    names = ["asphalt", "ground", "sidewalk", "gravel", "foliage"]
    for name, image in zip(names, parts):
        path = save(name, image, 90 if name == "ground" else 88)
        print("%-9s %4d px  %7d bytes  mean %s" % (name, image.shape[0], os.path.getsize(path), np.round(image.reshape(-1, 3).mean(axis=0) * 255)))
    if len(sys.argv) > 1:
        mock(sys.argv[1], parts)


if __name__ == "__main__":
    main()
