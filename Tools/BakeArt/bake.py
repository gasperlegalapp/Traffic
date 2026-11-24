#!/usr/bin/env python3
"""
Turns the source models in Art/ into the one file the game loads: Assets/Resources/Art/models.bytes.

The game builds its meshes from that file in code (ArtKit.cs), so nothing depends on how Unity
imports a model. This script scales every vehicle by the same amount, keeping its proportions,
points it along +x, and sorts its faces into "paint" (takes the car's own color) and everything else.

    python3 Tools/BakeArt/bake.py                 write the file
    python3 Tools/BakeArt/bake.py preview.png     also draw every model to a picture, to check them

Needs Python 3 with numpy and Pillow. Run it from the top of the repository.
"""
import colorsys
import os
import struct
import sys

import numpy as np
from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
CARS = os.path.join(ROOT, "Art", "kenney-car-kit")
NATURE = os.path.join(ROOT, "Art", "kenney-nature-kit")
OUT = os.path.join(ROOT, "Assets", "Resources", "Art", "models.bytes")

PAINT, LEAF = 65535, 65534

# One scale for the whole car kit, so every vehicle keeps the proportions it was drawn with and
# its size next to the others. It makes the longest car about as long as the simulation's car.
KIT_SCALE = 1.5

# name in the game, source file.
VEHICLES = [
    ("car/sedan", "sedan"),
    ("car/sports", "sedan-sports"),
    ("car/hatchback", "hatchback-sports"),
    ("car/suv", "suv"),
    ("car/wagon", "suv-luxury"),
    ("car/van", "van"),
    ("car/pickup", "truck"),
    ("truck/box", "delivery"),
    ("truck/flatbed", "delivery-flat"),
]

# name in the game, source file, height in meters.
PROPS = [
    ("tree/default", "tree_default", 9.0),
    ("tree/oak", "tree_oak", 7.5),
    ("tree/detailed", "tree_detailed", 8.0),
    ("tree/fat", "tree_fat", 6.5),
    ("tree/tall", "tree_tall", 10.0),
    ("tree/thin", "tree_thin", 9.0),
    ("tree/plateau", "tree_plateau", 7.5),
    ("tree/simple", "tree_simple", 8.0),
    ("pine/round", "tree_pineRoundA", 9.0),
    ("pine/wide", "tree_pineRoundC", 8.0),
    ("pine/tall", "tree_pineTallA", 11.0),
    ("pine/default", "tree_pineDefaultA", 10.0),
    ("bush/small", "plant_bush", 1.1),
    ("bush/large", "plant_bushLarge", 1.4),
    ("bush/detailed", "plant_bushDetailed", 1.3),
    ("rock/small", "rock_smallA", 0.5),
    ("rock/pebble", "rock_smallC", 0.4),
    ("rock/large", "rock_largeA", 0.9),
    ("rock/boulder", "rock_largeC", 1.1),
]

# The nature kit's own colors are a stylized teal and orange. These replace them, by material.
TRUNK = (124, 94, 66)
NATURE_COLORS = {
    "leafsGreen": LEAF, "leafsDark": LEAF, "leafsFall": LEAF,
    "woodBark": TRUNK, "woodBarkDark": TRUNK, "woodBirch": TRUNK,
    "_defaultMat": (232, 232, 226),
}


def load_obj(path):
    """Read an OBJ file: positions, texture coordinates, normals, triangles and materials."""
    folder = os.path.dirname(path)
    mats, pos, tex, nor, faces = {}, [], [], [], []
    group = mat = ""
    for line in open(path):
        p = line.split()
        if not p:
            continue
        if p[0] == "mtllib":
            cur = None
            for l in open(os.path.join(folder, " ".join(p[1:]))):
                q = l.split()
                if not q:
                    continue
                if q[0] == "newmtl":
                    cur = q[1]
                    mats[cur] = {"Kd": (1, 1, 1), "map": None}
                elif q[0] == "Kd":
                    mats[cur]["Kd"] = tuple(float(x) for x in q[1:4])
                elif q[0] == "map_Kd":
                    mats[cur]["map"] = os.path.join(folder, " ".join(q[1:]))
        elif p[0] == "v":
            pos.append([float(x) for x in p[1:4]])
        elif p[0] == "vt":
            tex.append([float(x) for x in p[1:3]])
        elif p[0] == "vn":
            nor.append([float(x) for x in p[1:4]])
        elif p[0] in ("g", "o"):
            group = " ".join(p[1:])
        elif p[0] == "usemtl":
            mat = p[1]
        elif p[0] == "f":
            corners = []
            for t in p[1:]:
                a = (t.split("/") + ["", ""])[:3]
                corners.append((int(a[0]) - 1, int(a[1]) - 1 if a[1] else -1, int(a[2]) - 1 if a[2] else -1))
            for i in range(1, len(corners) - 1):
                faces.append((group, mat, corners[0], corners[i], corners[i + 1]))
    return np.array(pos), np.array(tex), np.array(nor), faces, mats


_textures = {}


def face_color(face, tex, mats):
    """The color of a face: looked up in its material's picture, or the material's flat color."""
    m = mats[face[1]]
    if m["map"]:
        if m["map"] not in _textures:
            _textures[m["map"]] = np.array(Image.open(m["map"]).convert("RGB"))
        img = _textures[m["map"]]
        uv = np.mean([tex[face[k][1]] for k in (2, 3, 4)], axis=0)
        h, w = img.shape[:2]
        x = min(w - 1, max(0, int(uv[0] % 1 * w)))
        y = min(h - 1, max(0, int((1 - uv[1] % 1) * h)))
        return tuple(int(c) for c in img[y, x])
    return tuple(int(round(c * 255)) for c in m["Kd"])


class Model:
    def __init__(self, name):
        self.name = name
        self.verts = []      # (x, y, z, nx, ny, nz, index, shade)
        self.lookup = {}
        self.paint = []
        self.rest = []

    def vertex(self, p, n, index, shade):
        key = (round(p[0], 4), round(p[1], 4), round(p[2], 4), round(n[0], 3), round(n[1], 3), round(n[2], 3), index, shade)
        if key not in self.lookup:
            self.lookup[key] = len(self.verts)
            self.verts.append((p[0], p[1], p[2], n[0], n[1], n[2], index, shade))
        return self.lookup[key]

    def triangle(self, pts, normals, index, shade):
        a, b, c = pts
        # Unity wants cross(b - a, c - a) to point out of the surface. Put the corners in that order.
        if np.dot(np.cross(b - a, c - a), normals[0] + normals[1] + normals[2]) < 0:
            pts = [a, c, b]
            normals = [normals[0], normals[2], normals[1]]
        ids = [self.vertex(p, n, index, shade) for p, n in zip(pts, normals)]
        (self.paint if index == PAINT else self.rest).extend(ids)


palette = []
palette_lookup = {}


def palette_index(color):
    """A slot in the shared palette for a fixed color. Close colors share a slot."""
    key = tuple(min(255, int(round(c / 6.0) * 6)) for c in color)
    if key not in palette_lookup:
        palette_lookup[key] = len(palette)
        palette.append(key)
    return palette_lookup[key]


def unit(v):
    n = np.linalg.norm(v)
    return v / n if n > 0 else np.array([0.0, 1.0, 0.0])


def bake_vehicle(name, source):
    pos, tex, nor, faces, mats = load_obj(os.path.join(CARS, source + ".obj"))
    lo, hi = pos.min(0), pos.max(0)
    mid = (lo + hi) / 2

    # The paint is the strongest color on the body: every face close to that hue takes the car's color.
    colors = [face_color(f, tex, mats) for f in faces]
    weight = {}
    for f, col in zip(faces, colors):
        if f[0].startswith("wheel"):
            continue
        h, s, v = colorsys.rgb_to_hsv(*(c / 255.0 for c in col))
        if s < 0.3:
            continue
        a, b, c = (pos[f[k][0]] for k in (2, 3, 4))
        bucket = round(h * 24) % 24
        weight[bucket] = weight.get(bucket, 0) + np.linalg.norm(np.cross(b - a, c - a))
    hue = max(weight, key=weight.get) / 24.0

    def is_paint(col):
        h, s, v = colorsys.rgb_to_hsv(*(c / 255.0 for c in col))
        return s >= 0.3 and min(abs(h - hue), 1 - abs(h - hue)) < 0.07

    brightest = max(max(col) for col in colors if is_paint(col))

    model = Model(name)
    for f, col in zip(faces, colors):
        # In the source x is the car's left, y is up and z is forward. In the game a model faces +x
        # with its left toward +z, so source (x, y, z) becomes game (z, y, x). It is centered on the
        # ground and scaled evenly, nothing else.
        pts = [np.array([pos[f[k][0]][2] - mid[2], pos[f[k][0]][1] - lo[1], pos[f[k][0]][0] - mid[0]]) * KIT_SCALE for k in (2, 3, 4)]
        normals = [unit(np.array([nor[f[k][2]][2], nor[f[k][2]][1], nor[f[k][2]][0]])) for k in (2, 3, 4)]
        if is_paint(col) and not f[0].startswith("wheel"):
            model.triangle(pts, normals, PAINT, int(round(255 * max(col) / brightest)))
        else:
            model.triangle(pts, normals, palette_index(col), 255)
    return model


def bake_prop(name, source, height):
    pos, tex, nor, faces, mats = load_obj(os.path.join(NATURE, source + ".obj"))
    scale = height / (pos[:, 1].max() - pos[:, 1].min())
    kind = name.split("/")[0]
    model = Model(name)
    for f in faces:
        mat = f[1]
        if mat in NATURE_COLORS:
            color = NATURE_COLORS[mat]
        elif mat == "grass":
            # Moss on a rock is stone here; on a bush, grass means leaves.
            color = (166, 170, 172) if kind == "rock" else LEAF
        elif mat == "dirt":
            color = (138, 142, 146)
        else:
            color = tuple(int(round(c * 255)) for c in mats[mat]["Kd"])
        pts = [np.array([pos[f[k][0]][2], pos[f[k][0]][1], pos[f[k][0]][0]]) * scale for k in (2, 3, 4)]
        normals = [unit(np.array([nor[f[k][2]][2], nor[f[k][2]][1], nor[f[k][2]][0]])) for k in (2, 3, 4)]
        model.triangle(pts, normals, color if color == LEAF else palette_index(color), 255)
    return model


def write(models):
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "wb") as out:
        out.write(b"TART")
        out.write(struct.pack("<ii", 1, len(palette)))
        for c in palette:
            out.write(bytes(c))
        out.write(struct.pack("<i", len(models)))
        for m in models:
            assert len(m.verts) < 65000, m.name
            name = m.name.encode("ascii")
            v = np.array([x[:3] for x in m.verts])
            size = v.max(0) - v.min(0)
            out.write(struct.pack("<B", len(name)) + name)
            out.write(struct.pack("<fffi", size[0], size[1], size[2], len(m.verts)))
            for x in m.verts:
                out.write(struct.pack("<ffffffHBB", x[0], x[1], x[2], x[3], x[4], x[5], x[6], x[7], 0))
            for tris in (m.paint, m.rest):
                out.write(struct.pack("<i", len(tris)))
                out.write(struct.pack("<%dH" % len(tris), *tris))


# ------------------------------------------------------------------ preview

SAMPLE_PAINT = [(242, 95, 92), (78, 168, 222), (255, 224, 102), (233, 236, 239), (112, 193, 179), (179, 136, 235)]
SAMPLE_LEAF = [(98, 158, 82), (78, 138, 74), (214, 138, 62), (224, 178, 74), (64, 116, 76), (196, 98, 52)]


def draw(model, number, px, view):
    """Draw one model with a simple renderer: flat light from above and to one side."""
    v = np.array([x[:3] for x in model.verts])
    n = np.array([x[3:6] for x in model.verts])
    paint = np.array(SAMPLE_PAINT[number % len(SAMPLE_PAINT)]) / 255.0
    leaf = np.array(SAMPLE_LEAF[number % len(SAMPLE_LEAF)]) / 255.0
    col = np.zeros((len(v), 3))
    for i, x in enumerate(model.verts):
        col[i] = paint * (0.55 + 0.45 * x[7] / 255.0) if x[6] == PAINT else leaf if x[6] == LEAF else np.array(palette[x[6]]) / 255.0
    if view == "top":
        # Looking down as the game does: the model's nose points right, tilted a little toward the viewer.
        tilt = 0.4
        right, upv = np.array([1.0, 0, 0]), np.array([0, np.sin(tilt), np.cos(tilt)])
        depth = np.array([0, np.cos(tilt), -np.sin(tilt)])
    else:
        # From a front corner and above, the way the kit's own pictures show them.
        turn, tilt = np.radians(215), np.radians(32)
        right = np.array([np.cos(turn), 0, np.sin(turn)])
        ahead = np.array([-np.sin(turn), 0, np.cos(turn)])
        upv = np.array([0, np.cos(tilt), 0]) + ahead * np.sin(tilt)
        depth = np.array([0, np.sin(tilt), 0]) - ahead * np.cos(tilt)
    sx, sy, sz = v @ right, v @ upv, v @ depth
    span = max(sx.max() - sx.min(), sy.max() - sy.min()) * 1.12
    cx, cy = (sx.max() + sx.min()) / 2, (sy.max() + sy.min()) / 2
    X = (sx - cx) / span * px + px / 2
    Y = px / 2 - (sy - cy) / span * px
    light = unit(np.array([0.45, 0.8, -0.35]))
    shade = 0.5 + 0.5 * np.clip(n @ light, 0, 1)
    img = np.full((px, px, 3), 0.42)
    img[:, :, 1] = 0.6
    img[:, :, 2] = 0.36
    zbuf = np.full((px, px), -1e9)
    tris = np.array(model.paint + model.rest).reshape(-1, 3)
    for a, b, c in tris:
        x0, x1 = int(max(0, np.floor(min(X[a], X[b], X[c])))), int(min(px - 1, np.ceil(max(X[a], X[b], X[c]))))
        y0, y1 = int(max(0, np.floor(min(Y[a], Y[b], Y[c])))), int(min(px - 1, np.ceil(max(Y[a], Y[b], Y[c]))))
        if x1 < x0 or y1 < y0:
            continue
        gx, gy = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        den = (Y[b] - Y[c]) * (X[a] - X[c]) + (X[c] - X[b]) * (Y[a] - Y[c])
        if abs(den) < 1e-9:
            continue
        wa = ((Y[b] - Y[c]) * (gx - X[c]) + (X[c] - X[b]) * (gy - Y[c])) / den
        wb = ((Y[c] - Y[a]) * (gx - X[c]) + (X[a] - X[c]) * (gy - Y[c])) / den
        wc = 1 - wa - wb
        z = wa * sz[a] + wb * sz[b] + wc * sz[c]
        inside = (wa >= 0) & (wb >= 0) & (wc >= 0) & (z > zbuf[y0:y1 + 1, x0:x1 + 1])
        if not inside.any():
            continue
        color = col[a] * (shade[a] + shade[b] + shade[c]) / 3
        zbuf[y0:y1 + 1, x0:x1 + 1][inside] = z[inside]
        img[y0:y1 + 1, x0:x1 + 1][inside] = color
    return (np.clip(img, 0, 1) * 255).astype(np.uint8)


def preview(models, path):
    px, per_row = 200, 8
    cells = []
    for i, m in enumerate(models):
        cells.append(draw(m, i, px, "top"))
        if m.name.startswith(("car", "truck")):
            cells.append(draw(m, i, px, "corner"))
    rows = (len(cells) + per_row - 1) // per_row
    sheet = np.zeros((rows * px, per_row * px, 3), np.uint8)
    for i, cell in enumerate(cells):
        sheet[i // per_row * px:(i // per_row + 1) * px, i % per_row * px:(i % per_row + 1) * px] = cell
    Image.fromarray(sheet).save(path)


def main():
    models = [bake_vehicle(*v) for v in VEHICLES] + [bake_prop(*p) for p in PROPS]
    write(models)
    print("%d models, %d palette colors, %d bytes" % (len(models), len(palette), os.path.getsize(OUT)))
    for m in models:
        v = np.array([x[:3] for x in m.verts])
        print("  %-16s %5d verts  paint %5d  rest %5d  size %s" % (m.name, len(m.verts), len(m.paint) // 3, len(m.rest) // 3, np.round(v.max(0) - v.min(0), 2)))
    if len(sys.argv) > 1:
        preview(models, sys.argv[1])


if __name__ == "__main__":
    main()
