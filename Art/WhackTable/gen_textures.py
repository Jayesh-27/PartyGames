"""Paints the Whack-a-Bunny table textures (hand-painted toy look) with PIL.

top.png  - tabletop, UV = top view (u along X / width, v along Y / depth)
wood.png - legs
Run: python gen_textures.py
"""
import math
import os
import random

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(HERE, "textures")
os.makedirs(TEX, exist_ok=True)

# must match make_table.py
TOP_W, TOP_D = 1.20, 0.75
HOLES = [(-0.40, 0.13), (0.0, 0.17), (0.40, 0.13), (-0.40, -0.17), (0.0, -0.13), (0.40, -0.17)]
HOLE_R = 0.075

W, H = 2048, 1280
S = W / TOP_W  # pixels per metre

rng = random.Random(7)
nrng = np.random.default_rng(7)


def px(x, y):
    """table metres (x right, y away) -> pixel (origin top-left, y down)."""
    return (x + TOP_W / 2) * S, (TOP_D / 2 - y) * S


def smooth_noise(w, h, scale, octaves=4):
    out = np.zeros((h, w), np.float32)
    amp, total = 1.0, 0.0
    for o in range(octaves):
        gw, gh = max(2, int(w / scale) + 2), max(2, int(h / scale) + 2)
        g = nrng.random((gh, gw)).astype(np.float32)
        img = Image.fromarray((g * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC)
        out += np.asarray(img, np.float32) / 255.0 * amp
        total += amp
        amp *= 0.5
        scale /= 2
    return out / total


def paint_base(color, w, h, streak=True, var=0.06):
    """flat colour with soft blotches and brush streaks, so it looks painted not plastic."""
    base = np.ones((h, w, 3), np.float32) * np.array(color, np.float32) / 255.0
    n = smooth_noise(w, h, 260) - 0.5
    base *= 1.0 + n[..., None] * var * 2
    if streak:
        s = nrng.random((h // 40 + 1, 1)).astype(np.float32)
        s = np.asarray(Image.fromarray((s * 255).astype(np.uint8)).resize((w, h), Image.BICUBIC), np.float32) / 255.0
        fine = nrng.random((h, w)).astype(np.float32)
        fine = np.asarray(Image.fromarray((fine * 255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(1.2)), np.float32) / 255.0
        base *= 1.0 + (s[..., None] - 0.5) * 0.035 + (fine[..., None] - 0.5) * 0.03
    return Image.fromarray(np.clip(base * 255, 0, 255).astype(np.uint8))


def layer():
    return Image.new("RGBA", (W, H), (0, 0, 0, 0))


def blend(base, lay, blur=0.0):
    if blur:
        lay = lay.filter(ImageFilter.GaussianBlur(blur))
    return Image.alpha_composite(base.convert("RGBA"), lay)


def flower(d, cx, cy, r, color, petals=5, center=None, rot=0.0):
    for i in range(petals):
        a = rot + i * 2 * math.pi / petals
        pxc, pyc = cx + math.cos(a) * r * 0.55, cy + math.sin(a) * r * 0.55
        pr = r * 0.48
        d.ellipse([pxc - pr, pyc - pr, pxc + pr, pyc + pr], fill=color)
    if center:
        cr = r * 0.32
        d.ellipse([cx - cr, cy - cr, cx + cr, cy + cr], fill=center)


def outline_flower(d, cx, cy, r, color, width, petals=4, rot=0.0):
    for i in range(petals):
        a = rot + i * 2 * math.pi / petals
        pxc, pyc = cx + math.cos(a) * r * 0.5, cy + math.sin(a) * r * 0.5
        pr = r * 0.45
        d.ellipse([pxc - pr, pyc - pr, pxc + pr, pyc + pr], outline=color, width=width)


def leaf(lay, cx, cy, length, width, angle, color, dots=None):
    L = Image.new("RGBA", (int(length * 1.2), int(length * 1.2)), (0, 0, 0, 0))
    d = ImageDraw.Draw(L)
    c = L.size[0] / 2
    pts = []
    for i in range(41):
        t = i / 40
        x = c - length / 2 + t * length
        y = c - math.sin(t * math.pi) ** 0.8 * width / 2
        pts.append((x, y))
    for i in range(40, -1, -1):
        t = i / 40
        x = c - length / 2 + t * length
        y = c + math.sin(t * math.pi) ** 0.8 * width / 2
        pts.append((x, y))
    d.polygon(pts, fill=color)
    if dots:
        for _ in range(9):
            t = rng.uniform(0.2, 0.8)
            x = c - length / 2 + t * length
            y = c + rng.uniform(-0.25, 0.25) * width * math.sin(t * math.pi)
            dr = width * 0.06
            d.ellipse([x - dr, y - dr, x + dr, y + dr], fill=dots)
    L = L.rotate(angle, resample=Image.BICUBIC)
    lay.alpha_composite(L, (int(cx - L.size[0] / 2), int(cy - L.size[1] / 2)))


def rounded_dashes(d, inset, radius, dash, gap, width, color):
    """white stitch line around the top, following the rounded corners."""
    x0, y0, x1, y1 = inset, inset, W - inset, H - inset
    path = []
    steps = 2000

    def corner(cx, cy, a0):
        for i in range(60):
            a = a0 + i / 59 * math.pi / 2
            path.append((cx + math.cos(a) * radius, cy + math.sin(a) * radius))

    path.append((x0 + radius, y0))
    path.append((x1 - radius, y0))
    corner(x1 - radius, y0 + radius, -math.pi / 2)
    path.append((x1, y1 - radius))
    corner(x1 - radius, y1 - radius, 0)
    path.append((x0 + radius, y1))
    corner(x0 + radius, y1 - radius, math.pi / 2)
    path.append((x0, y0 + radius))
    corner(x0 + radius, y0 + radius, math.pi)
    path.append((x0 + radius, y0))

    # resample along length
    seg = []
    for (ax, ay), (bx, by) in zip(path, path[1:]):
        seg.append(((ax, ay), (bx, by), math.hypot(bx - ax, by - ay)))
    total = sum(s[2] for s in seg)
    pos, on = 0.0, True

    def point_at(dist):
        acc = 0.0
        for (a, b, l) in seg:
            if acc + l >= dist:
                t = (dist - acc) / max(l, 1e-6)
                return a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            acc += l
        return path[-1]

    while pos < total:
        ln = dash if on else gap
        if on:
            pts = [point_at(min(total, pos + ln * k / 6)) for k in range(7)]
            d.line(pts, fill=color, width=width, joint="curve")
        pos += ln
        on = not on


def make_top():
    img = paint_base((150, 190, 222), W, H)
    # soft darker band near the edge (painted shading)
    edge = np.zeros((H, W), np.float32)
    yy, xx = np.mgrid[0:H, 0:W]
    dx = np.minimum(xx, W - 1 - xx)
    dy = np.minimum(yy, H - 1 - yy)
    dist = np.minimum(dx, dy).astype(np.float32)
    edge = np.clip(1 - dist / 90.0, 0, 1) ** 2
    arr = np.asarray(img, np.float32)
    arr *= (1 - edge[..., None] * 0.12)
    img = Image.fromarray(np.clip(arr, 0, 255).astype(np.uint8)).convert("RGBA")

    # two soft diagonal ribbons (darker periwinkle), like painted paths
    lay = layer()
    d = ImageDraw.Draw(lay)
    for (yA, yB, w_) in [(0.62, 0.30, 120), (0.22, -0.02, 105)]:
        pts = []
        for i in range(60):
            t = i / 59
            x = t * W
            y = (yA + (yB - yA) * t + 0.05 * math.sin(t * 5.0)) * H
            pts.append((x, y))
        d.line(pts, fill=(98, 120, 196, 120), width=w_, joint="curve")
    img = blend(img, lay, blur=3)

    # pale yellow crescent swoosh
    lay = layer()
    d = ImageDraw.Draw(lay)
    cx, cy = px(-0.05, 0.02)
    d.ellipse([cx - 330, cy - 250, cx + 330, cy + 250], fill=(236, 230, 170, 120))
    d.ellipse([cx - 300, cy - 300, cx + 360, cy + 190], fill=(0, 0, 0, 0))
    img = blend(img, lay, blur=6)

    # decorations (kept away from the holes)
    lay = layer()
    d = ImageDraw.Draw(lay)
    leaf(lay, *px(-0.20, 0.06), 220, 115, 25, (60, 120, 130, 235), dots=(214, 196, 80, 255))
    leaf(lay, *px(0.22, -0.02), 240, 120, -15, (60, 120, 130, 235), dots=(214, 196, 80, 255))
    leaf(lay, *px(-0.20, -0.31), 150, 80, 160, (66, 130, 136, 220), dots=(214, 196, 80, 255))
    flower(d, *px(0.21, 0.21), 105, (220, 120, 40, 245), petals=5, center=(240, 170, 60, 255), rot=0.3)
    flower(d, *px(-0.21, -0.14), 95, (200, 70, 160, 245), petals=6, center=(230, 120, 200, 255), rot=0.1)
    flower(d, *px(0.50, 0.29), 52, (200, 70, 160, 235), petals=5, rot=0.5)
    outline_flower(d, *px(-0.20, 0.27), 100, (210, 150, 40, 240), 10, petals=4, rot=0.78)
    outline_flower(d, *px(0.22, -0.29), 70, (110, 120, 210, 240), 9, petals=4, rot=0.2)
    outline_flower(d, *px(-0.52, -0.28), 56, (110, 120, 210, 230), 8, petals=4, rot=0.6)
    # little heart-ish leaves and dot clusters
    for (x, y, c) in [(0.54, -0.04, (150, 90, 200, 230)), (-0.53, 0.02, (220, 120, 40, 220)), (0.12, -0.33, (200, 70, 160, 220)),
                      (-0.10, 0.31, (110, 120, 210, 220)), (0.56, 0.12, (110, 120, 210, 220)), (-0.40, 0.31, (200, 70, 160, 220))]:
        X, Y = px(x, y)
        for k in range(4):
            a = rng.uniform(0, 6.28)
            rr = rng.uniform(6, 14)
            ox, oy = math.cos(a) * rng.uniform(10, 34), math.sin(a) * rng.uniform(10, 34)
            if k == 0:
                rr, ox, oy = 22, 0, 0
            d.ellipse([X + ox - rr, Y + oy - rr, X + ox + rr, Y + oy + rr], outline=c, width=4 if k == 0 else 0, fill=None if k == 0 else c)
    # dotted trails between holes
    for (a, b) in [((-0.40, 0.13), (0.0, 0.17)), ((0.0, -0.13), (0.40, -0.17)), ((-0.40, -0.17), (0.0, -0.13))]:
        for i in range(1, 9):
            t = i / 9
            x = a[0] + (b[0] - a[0]) * t
            y = a[1] + (b[1] - a[1]) * t + math.sin(t * math.pi) * 0.04
            X, Y = px(x, y)
            d.ellipse([X - 6, Y - 6, X + 6, Y + 6], fill=(240, 244, 250, 210))
    img = blend(img, lay, blur=1.0)

    # stitch border
    lay = layer()
    d = ImageDraw.Draw(lay)
    rounded_dashes(d, inset=70, radius=120, dash=46, gap=30, width=11, color=(246, 248, 252, 245))
    img = blend(img, lay, blur=0.8)

    # dark soot ring right around each hole (shadow where the bunnies come out)
    lay = layer()
    d = ImageDraw.Draw(lay)
    for (hx, hy) in HOLES:
        X, Y = px(hx, hy)
        r = HOLE_R * S
        d.ellipse([X - r - 18, Y - r - 18, X + r + 18, Y + r + 18], fill=(60, 70, 110, 55))
    img = blend(img, lay, blur=14)

    img.convert("RGB").save(os.path.join(TEX, "top.png"), optimize=True)


def make_wood():
    w, h = 1024, 1024
    yy, xx = np.mgrid[0:h, 0:w].astype(np.float32)
    n = smooth_noise(w, h, 300)
    grain = np.sin((xx / w * 38 + n * 7.0) * math.pi) * 0.5 + 0.5
    fine = smooth_noise(w, h, 20, octaves=2)
    v = 0.78 + grain[..., None] * 0.14 + (fine[..., None] - 0.5) * 0.12
    base = np.array([176, 98, 78], np.float32) / 255.0
    arr = np.clip(base * v * 255, 0, 255).astype(np.uint8)
    img = Image.fromarray(arr)
    # a few darker grain streaks
    d = ImageDraw.Draw(img)
    for _ in range(26):
        x = rng.uniform(0, w)
        pts = [(x + math.sin(t / 60) * 6 + rng.uniform(-1, 1), t) for t in range(0, h + 20, 20)]
        d.line(pts, fill=(130, 66, 54), width=rng.choice([1, 2, 2, 3]))
    img = img.filter(ImageFilter.GaussianBlur(0.8))
    img.save(os.path.join(TEX, "wood.png"), optimize=True)


def make_hole():
    """inside of the holes: dark burrow, a bit warmer at the top."""
    w, h = 256, 256
    yy = np.mgrid[0:h, 0:w][0].astype(np.float32) / h
    col = np.array([52, 40, 46], np.float32) * (1 - yy[..., None] * 0.7)
    Image.fromarray(np.clip(col, 0, 255).astype(np.uint8)).save(os.path.join(TEX, "hole.png"))


if __name__ == "__main__":
    make_top()
    make_wood()
    make_hole()
    print("textures written to", TEX)
