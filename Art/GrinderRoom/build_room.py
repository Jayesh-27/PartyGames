"""Builds the toon hide-and-seek room for the grinder game and exports it as a GLB.

Run headless:
  blender -b -P build_room.py -- <out_glb> <out_dir_for_textures_and_previews>

Everything here is made from scratch (shapes, colours, textures, chalk drawings), so the room is ours to ship.
Blender is Z-up; the glTF exporter turns it into Unity's Y-up. Units are metres.
"""
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_GLB = args[0] if args else "ToonRoom.glb"
OUT_DIR = args[1] if len(args) > 1 else os.path.dirname(os.path.abspath(OUT_GLB))
os.makedirs(OUT_DIR, exist_ok=True)
random.seed(7)

R = 6.0            # centre to wall (flat), metres
H = 4.2            # ceiling height
T = 0.3            # wall thickness
TAN = math.tan(math.radians(22.5))
FONT_PATH = "C:/Windows/Fonts/segoeprb.ttf"


def lin(c):
    """sRGB 0..1 -> linear, for colours typed the way they look."""
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


# ============================================================== chalkboard drawings (rendered to textures)

BOARD_W, BOARD_H = 3.2, 1.8
CHALK = (0.95, 0.96, 0.92)
CH_BLUE = (0.50, 0.75, 1.00)
CH_RED = (1.00, 0.52, 0.48)
CH_YEL = (1.00, 0.88, 0.45)
CH_GRN = (0.60, 0.95, 0.60)
BOARD_COL = (0.20, 0.38, 0.37)

_font = None


def font():
    global _font
    if _font is None:
        _font = bpy.data.fonts.load(FONT_PATH) if os.path.exists(FONT_PATH) else None
    return _font


def _obj(name, data, color):
    o = bpy.data.objects.new(name, data)
    bpy.context.scene.collection.objects.link(o)
    o.color = lin(color) + (1.0,)
    return o


def line(pts, color=CHALK, w=0.014, closed=False, z=0.0):
    cu = bpy.data.curves.new("ln", 'CURVE')
    cu.dimensions = '3D'
    cu.bevel_depth = w
    cu.bevel_resolution = 1
    sp = cu.splines.new('POLY')
    sp.points.add(len(pts) - 1)
    for i, p in enumerate(pts):
        sp.points[i].co = (p[0], p[1], z, 1.0)
    sp.use_cyclic_u = closed
    return _obj("ln", cu, color)


def dashed(pts, color=CHALK, w=0.012, dash=0.07, gap=0.05):
    # walk the polyline and emit short pieces
    seg, acc, on, cur = [], 0.0, True, [pts[0]]
    for a, b in zip(pts, pts[1:]):
        a, b = Vector((a[0], a[1])), Vector((b[0], b[1]))
        d = (b - a).length
        t = 0.0
        while t < d:
            left = (dash if on else gap) - acc
            step = min(left, d - t)
            t += step
            acc += step
            p = a + (b - a) * (t / d)
            if on:
                cur.append((p.x, p.y))
            if acc >= (dash if on else gap) - 1e-6:
                if on and len(cur) > 1:
                    seg.append(cur)
                on, acc, cur = not on, 0.0, [(p.x, p.y)]
    for s in seg:
        line(s, color, w)


def circle(cx, cy, r, color=CHALK, w=0.014, a0=0.0, a1=360.0, n=40, wobble=0.0):
    pts = []
    for i in range(n + 1):
        a = math.radians(a0 + (a1 - a0) * i / n)
        rr = r * (1 + wobble * math.sin(a * 5 + cx * 3))
        pts.append((cx + rr * math.cos(a), cy + rr * math.sin(a)))
    return line(pts, color, w)


def text(s, x, y, size=0.16, color=CHALK, align='CENTER', rot=0.0):
    cu = bpy.data.curves.new("tx", 'FONT')
    cu.body = s
    cu.size = size
    cu.align_x = align
    cu.align_y = 'CENTER'
    if font():
        cu.font = font()
    o = _obj("tx", cu, color)
    o.location = (x, y, 0.05)
    o.rotation_euler = (0, 0, math.radians(rot))
    return o


def arrow(x0, y0, x1, y1, color=CHALK, w=0.016, head=0.09, curve=0.0):
    a, b = Vector((x0, y0)), Vector((x1, y1))
    mid = (a + b) / 2 + Vector((-(b - a).y, (b - a).x)) * curve
    pts = [tuple(a.lerp(mid, t).lerp(mid.lerp(b, t), t)) for t in [i / 12 for i in range(13)]]
    line(pts, color, w)
    d = (b - Vector(pts[-3])).normalized()
    n = Vector((-d.y, d.x))
    line([tuple(b - d * head + n * head * 0.6), tuple(b), tuple(b - d * head - n * head * 0.6)], color, w)


# ---- little icons, each centred on (x, y) with size s

def ico_wood(x, y, s=0.3, c=CH_YEL):
    line([(x, y - s), (x, y + s * 0.05)], c, 0.022)
    line([(x, y + s * 0.05), (x - s * 0.55, y + s * 0.85)], c, 0.022)
    line([(x, y + s * 0.05), (x + s * 0.55, y + s * 0.85)], c, 0.022)
    for k in (-0.6, -0.25):
        line([(x - s * 0.08, y + s * k), (x + s * 0.08, y + s * k + 0.03)], c, 0.008)


def ico_rope(x, y, s=0.3, c=CH_YEL):
    pts = []
    for i in range(90):
        a = i / 90 * math.pi * 2 * 3.2
        r = s * (0.15 + 0.75 * i / 90)
        pts.append((x + r * math.cos(a), y + r * math.sin(a) * 0.8))
    line(pts, c, 0.014)
    line([pts[-1], (pts[-1][0] + s * 0.4, pts[-1][1] - s * 0.5)], c, 0.014)


def ico_stone(x, y, s=0.3, c=CHALK):
    pts = [(x + s * math.cos(a) * (0.85 + 0.2 * math.sin(a * 3 + 1)), y + s * 0.7 * math.sin(a) * (0.9 + 0.15 * math.cos(a * 2)))
           for a in [i / 14 * math.pi * 2 for i in range(14)]]
    line(pts, c, 0.016, closed=True)
    line([(x - s * 0.3, y + s * 0.2), (x - s * 0.05, y + s * 0.35)], c, 0.008)


def ico_slingshot(x, y, s=0.4, c=CHALK):
    ico_wood(x, y, s, c)
    tl, tr = (x - s * 0.55, y + s * 0.85), (x + s * 0.55, y + s * 0.85)
    pouch = (x, y + s * 0.35)
    line([tl, (x - s * 0.2, y + s * 0.45), pouch, (x + s * 0.2, y + s * 0.45), tr], CH_YEL, 0.012)
    circle(pouch[0], pouch[1] + 0.02, s * 0.12, CHALK, 0.012)
    for k in range(3):
        line([(x + s * (0.75 + 0.1 * k), y + s * (1.0 + 0.12 * k)), (x + s * (0.95 + 0.1 * k), y + s * (1.15 + 0.12 * k))], CHALK, 0.008)


def ico_bucket(x, y, s=0.3, c=CHALK):
    line([(x - s * 0.6, y + s * 0.5), (x - s * 0.4, y - s * 0.5), (x + s * 0.4, y - s * 0.5), (x + s * 0.6, y + s * 0.5)], c, 0.016)
    circle(x, y + s * 0.5, s * 0.6, c, 0.012, 0, 360, 30)
    circle(x, y + s * 0.5, s * 0.6, c, 0.012, 20, 160, 20)


def ico_drawer(x, y, s=0.35, c=CHALK):
    line([(x - s, y - s), (x - s, y + s), (x + s * 0.5, y + s), (x + s * 0.5, y - s)], c, 0.016, closed=True)
    line([(x - s * 0.9, y + s * 0.1), (x + s * 0.4, y + s * 0.1)], c, 0.01)
    # drawer pulled out to the right
    line([(x - s * 0.2, y - s * 0.15), (x + s * 1.1, y - s * 0.15), (x + s * 1.1, y - s * 0.75), (x - s * 0.2, y - s * 0.75)], c, 0.016)
    line([(x + s * 0.6, y - s * 0.45), (x + s * 0.8, y - s * 0.45)], c, 0.02)


def ico_grinder(x, y, s=0.4, c=CHALK):
    line([(x - s * 0.6, y - s * 0.8), (x - s * 0.6, y + s * 0.3), (x + s * 0.6, y + s * 0.3), (x + s * 0.6, y - s * 0.8)], c, 0.018, closed=True)
    line([(x - s * 0.9, y + s * 0.9), (x - s * 0.3, y + s * 0.3)], c, 0.016)
    line([(x + s * 0.9, y + s * 0.9), (x + s * 0.3, y + s * 0.3)], c, 0.016)
    # gear
    gx, gy, gr = x, y - s * 0.25, s * 0.3
    pts = []
    for i in range(49):
        a = i / 48 * math.pi * 2
        rr = gr * (1.0 if (i // 3) % 2 == 0 else 0.78)
        pts.append((gx + rr * math.cos(a), gy + rr * math.sin(a)))
    line(pts, CH_YEL, 0.012)
    circle(gx, gy, gr * 0.3, CH_YEL, 0.01)


def ico_box(x, y, s=0.3, c=CHALK):
    line([(x - s, y - s * 0.6), (x + s, y - s * 0.6), (x + s, y + s * 0.6), (x - s, y + s * 0.6)], c, 0.016, closed=True)
    line([(x - s, y + s * 0.6), (x - s * 0.6, y + s * 0.9), (x + s * 1.4, y + s * 0.9), (x + s, y + s * 0.6)], c, 0.012)
    line([(x + s * 1.4, y + s * 0.9), (x + s * 1.4, y - s * 0.3), (x + s, y - s * 0.6)], c, 0.012)


def ico_elephant(x, y, s=0.3, c=CHALK):
    circle(x, y, s, c, 0.016, 0, 360, 36)
    circle(x - s * 0.95, y + s * 0.15, s * 0.55, c, 0.014, 60, 300, 24)
    circle(x + s * 0.95, y + s * 0.15, s * 0.55, c, 0.014, -120, 120, 24)
    line([(x, y - s * 0.2), (x + s * 0.05, y - s * 0.8), (x + s * 0.3, y - s * 1.05), (x + s * 0.45, y - s * 0.9)], c, 0.016)
    circle(x - s * 0.35, y + s * 0.25, 0.02, c, 0.015, n=8)
    circle(x + s * 0.35, y + s * 0.25, 0.02, c, 0.015, n=8)


def board_bg():
    me = bpy.data.meshes.new("bg")
    me.from_pydata([(-2, -1.2, -0.5), (2, -1.2, -0.5), (2, 1.2, -0.5), (-2, 1.2, -0.5)], [], [(0, 1, 2, 3)])
    _obj("bg", me, BOARD_COL)
    # faint old chalk smudges
    for _ in range(7):
        cx, cy = random.uniform(-1.4, 1.4), random.uniform(-0.7, 0.7)
        r = random.uniform(0.15, 0.45)
        pts = [(cx + r * math.cos(a) * 1.6, cy + r * math.sin(a) * 0.5) for a in [i / 20 * math.pi * 2 for i in range(21)]]
        line(pts, (0.225, 0.405, 0.395), 0.035, z=-0.2)


def board_recipe():
    text("SLINGSHOT RECIPE", 0, 0.68, 0.2, CH_YEL)
    line([(-1.05, 0.52), (1.05, 0.52)], CH_YEL, 0.01)
    ico_wood(-1.15, -0.05, 0.28)
    text("+", -0.72, 0.0, 0.24)
    ico_rope(-0.3, 0.0, 0.26)
    text("+", 0.12, 0.0, 0.24)
    ico_stone(0.48, 0.0, 0.2)
    text("=", 0.85, 0.0, 0.26)
    ico_slingshot(1.28, -0.12, 0.3)
    text("WOOD", -1.15, -0.5, 0.11)
    text("ROPE", -0.3, -0.5, 0.11)
    text("STONE", 0.48, -0.5, 0.11)
    text("BONK!", 1.28, -0.55, 0.13, CH_RED, rot=-8)
    text("find all 3 of YOUR colour", 0, -0.76, 0.1, CHALK)


def board_colours():
    text("FIND YOUR COLOUR!", 0, 0.7, 0.2, CH_YEL)
    text("BLUE TEAM", -0.85, 0.42, 0.13, CH_BLUE)
    text("RED TEAM", 0.85, 0.42, 0.13, CH_RED)
    line([(0, 0.45), (0, -0.45)], CHALK, 0.01)
    text("VS", 0, 0.0, 0.15)
    for side, col in ((-1, CH_BLUE), (1, CH_RED)):
        ico_wood(side * 1.25, -0.05, 0.18, col)
        ico_rope(side * 0.85, -0.0, 0.16, col)
        ico_stone(side * 0.42, -0.02, 0.12, col)
    text("wrong colour?  HIDE IT!  ;)", 0, -0.62, 0.12, CHALK)
    arrow(0.95, -0.62, 1.35, -0.35, CH_YEL, curve=-0.4)


def board_search():
    text("LOOK UNDER EVERYTHING", 0, 0.7, 0.18, CH_YEL)
    # bucket lifted up, a question mark under it
    ico_bucket(-1.0, 0.18, 0.22)
    for k in (-1, 0, 1):
        line([(-1.0 + k * 0.12, -0.12), (-1.0 + k * 0.12, -0.02)], CHALK, 0.008)
    text("?", -1.0, -0.38, 0.22, CH_GRN)
    line([(-1.35, -0.55), (-0.65, -0.55)], CHALK, 0.01)
    ico_drawer(-0.05, 0.05, 0.22)
    text("!", 0.18, -0.08, 0.13, CH_GRN)
    ico_box(0.95, 0.0, 0.2)
    text("?", 1.0, 0.02, 0.15, CH_GRN)
    text("buckets  drawers  boxes  tables...", 0, -0.72, 0.1, CHALK)


def board_grinder():
    text("THE GRINDER", 0, 0.7, 0.2, CH_YEL)
    ico_stone(-1.3, 0.05, 0.12)
    ico_wood(-1.0, 0.0, 0.14)
    arrow(-0.82, 0.05, -0.4, 0.15, CHALK, curve=-0.3)
    ico_grinder(0.0, 0.0, 0.32)
    arrow(0.35, -0.15, 0.85, -0.15, CHALK)
    text("???", 1.2, -0.12, 0.17, CH_GRN)
    for a in range(0, 360, 45):
        r0, r1 = 0.3, 0.4
        line([(1.2 + r0 * math.cos(math.radians(a)), -0.12 + r0 * math.sin(math.radians(a))),
              (1.2 + r1 * math.cos(math.radians(a)), -0.12 + r1 * math.sin(math.radians(a)))], CH_YEL, 0.009)
    text("feed it.  see what comes out.", 0, -0.66, 0.11, CHALK)


def board_throw():
    text("THROW IT FAR", -0.15, 0.7, 0.2, CH_YEL)
    ico_elephant(-1.15, -0.25, 0.17)
    pts = [(-0.95 + 2.1 * t, -0.15 + 1.05 * t - 1.1 * t * t) for t in [i / 30 for i in range(31)]]
    dashed(pts, CHALK)
    ico_stone(1.15, -0.25, 0.08, CH_BLUE)
    text("bye bye!", 1.05, 0.15, 0.1, CH_BLUE, rot=-10)
    text("BLUE  ||||  ||", -0.95, -0.72, 0.11, CH_BLUE)
    text("RED  ||||  |||", 0.85, -0.72, 0.11, CH_RED)


def board_plan():
    text("TOP SECRET PLAN", 0, 0.72, 0.18, CH_RED)
    pts = [(0.62 * math.cos(math.radians(22.5 + 45 * i)), 0.05 + 0.5 * math.sin(math.radians(22.5 + 45 * i))) for i in range(8)]
    line(pts, CHALK, 0.014, closed=True)
    for (x, y) in [(-0.35, 0.25), (0.3, -0.2), (0.4, 0.3), (-0.2, -0.3)]:
        line([(x - 0.05, y - 0.05), (x + 0.05, y + 0.05)], CH_RED, 0.014)
        line([(x - 0.05, y + 0.05), (x + 0.05, y - 0.05)], CH_RED, 0.014)
    dashed([(-0.55, 0.0), (-0.35, 0.25), (0.05, 0.1), (0.4, 0.3), (0.3, -0.2), (-0.2, -0.3)], CH_YEL)
    text("X = maybe?", -1.15, 0.2, 0.11, CHALK)
    text("check the\ndrawers!!", 1.15, 0.15, 0.11, CHALK)
    text("don't tell the other team", 0, -0.7, 0.11, CHALK)
    line([(-0.95, -0.82), (0.95, -0.82)], CH_RED, 0.008)


BOARDS = [board_recipe, board_colours, board_search, board_grinder, board_throw, board_plan]


def render_boards():
    paths = []
    for i, fn in enumerate(BOARDS):
        reset()
        global _font
        _font = None
        scn = bpy.context.scene
        scn.render.engine = 'BLENDER_WORKBENCH'
        scn.display.shading.light = 'FLAT'
        scn.display.shading.color_type = 'OBJECT'
        scn.view_settings.view_transform = 'Standard'
        scn.render.resolution_x, scn.render.resolution_y = 1280, 720
        scn.render.film_transparent = False
        board_bg()
        fn()
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        scn.collection.objects.link(cam)
        scn.camera = cam
        cam.data.type = 'ORTHO'
        cam.data.ortho_scale = BOARD_W
        cam.location = (0, 0, 5)
        p = os.path.join(OUT_DIR, f"chalkboard_{i}.png")
        scn.render.filepath = p
        bpy.ops.render.render(write_still=True)
        paths.append(p)
    return paths


# ============================================================== room

def plank_texture(path, size=1024):
    """Warm toon planks. The texture covers 4 m x 4 m: 16 rows of 25 cm planks."""
    rng = np.random.default_rng(3)
    img = np.zeros((size, size, 3), np.float32)
    rows = 16
    rh = size // rows
    base = np.array([0.80, 0.52, 0.36])
    for r in range(rows):
        x = -rng.integers(0, size // 2)
        while x < size:
            ln = int(rng.integers(size // 3, size // 1.6))
            tint = base * rng.uniform(0.88, 1.08) + rng.uniform(-0.02, 0.02, 3)
            a, b = max(0, x), min(size, x + ln)
            if b > a:
                img[r * rh:(r + 1) * rh, a:b] = tint
                # soft grain stripes
                yy = np.arange(rh)[:, None]
                xx = np.arange(b - a)[None, :]
                grain = 0.035 * np.sin(xx * 0.035 + yy * 0.6 + rng.uniform(0, 6))
                img[r * rh:(r + 1) * rh, a:b] *= (1 + grain)[:, :, None]
                img[r * rh:(r + 1) * rh, a:min(size, a + 3)] = base * 0.55
            x += ln
        img[r * rh:r * rh + 3, :] = base * 0.55
    img = np.clip(img, 0, 1)
    im = bpy.data.images.new("FloorPlanks", size, size, alpha=False)
    rgba = np.ones((size, size, 4), np.float32)
    rgba[:, :, :3] = img[::-1]
    im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw = path
    im.file_format = 'PNG'
    im.save()
    return im


MATS = {}


def mat(name, color, rough=0.85, emit=0.0, image=None):
    if name in MATS:
        return MATS[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = lin(color) + (1.0,)
    bsdf.inputs["Roughness"].default_value = rough
    bsdf.inputs["Metallic"].default_value = 0.0
    if emit > 0:
        bsdf.inputs["Emission Color"].default_value = lin(color) + (1.0,)
        bsdf.inputs["Emission Strength"].default_value = emit
    if image is not None:
        tex = m.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = image
        m.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    MATS[name] = m
    return m


def box(name, size, loc, rot_z=0.0, material=None, parent=None):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2]))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    o.rotation_euler = (0, 0, rot_z)
    if material:
        me.materials.append(material)
    if parent:
        o.parent = parent
    return o


def octagon(name, radius, z, material, uv_scale=None, flip=False):
    rc = radius / math.cos(math.radians(22.5))
    vs = [(rc * math.cos(math.radians(22.5 + 45 * i)), rc * math.sin(math.radians(22.5 + 45 * i)), z) for i in range(8)]
    bm = bmesh.new()
    verts = [bm.verts.new(v) for v in vs]
    f = bm.faces.new(verts if not flip else verts[::-1])
    if uv_scale:
        uv = bm.loops.layers.uv.new()
        for loop in f.loops:
            loop[uv].uv = (loop.vert.co.x / uv_scale, loop.vert.co.y / uv_scale)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(material)
    return o


def disc(name, r, loc, material, n=24):
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=n)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc
    me.materials.append(material)
    return o


def wall_dir(i):
    a = math.radians(45 * (i + 1))
    return Vector((math.cos(a), math.sin(a), 0)), a


def strip(name, i, inset, depth, z0, z1, material):
    """A band along the inside of wall i, from height z0 to z1, sticking out 'depth' from the wall face."""
    d, a = wall_dir(i)
    r = R - inset - depth / 2
    length = 2 * (R - inset - depth) * TAN + 0.002
    return box(name, (depth, length + 2 * depth * TAN, z1 - z0), tuple(d * r + Vector((0, 0, (z0 + z1) / 2))), a, material)


def board(i, image_path, wood):
    d, a = wall_dir(i)
    n = -d
    side = Vector((-d.y, d.x, 0))
    cz = 2.05
    root = bpy.data.objects.new(f"Chalkboard_{i}", None)
    bpy.context.scene.collection.objects.link(root)
    img = bpy.data.images.load(image_path)
    face = mat(f"Chalkboard_{i}", BOARD_COL, 0.95, image=img)
    # picture plane, facing into the room
    me = bpy.data.meshes.new(f"Board_{i}")
    c = d * (R - 0.07) + Vector((0, 0, cz))
    hw, hh = BOARD_W / 2, BOARD_H / 2
    vs = [c - side * hw - Vector((0, 0, hh)), c + side * hw - Vector((0, 0, hh)), c + side * hw + Vector((0, 0, hh)), c - side * hw + Vector((0, 0, hh))]
    # looking at the wall from inside, +side is to the left, so the image runs from +side to -side
    vs = [c + side * hw - Vector((0, 0, hh)), c - side * hw - Vector((0, 0, hh)), c - side * hw + Vector((0, 0, hh)), c + side * hw + Vector((0, 0, hh))]
    me.from_pydata([tuple(v) for v in vs], [], [(0, 1, 2, 3)])
    uv = me.uv_layers.new()
    for li, coord in enumerate([(0, 0), (1, 0), (1, 1), (0, 1)]):
        uv.data[li].uv = coord
    me.materials.append(face)
    o = bpy.data.objects.new(f"Board_{i}", me)
    bpy.context.scene.collection.objects.link(o)
    o.parent = root
    # chunky toon frame and chalk tray
    f = 0.12
    for nm, sz, off in [
        ("Top", (0.1, BOARD_W + 2 * f, f), Vector((0, 0, hh + f / 2))),
        ("Bot", (0.1, BOARD_W + 2 * f, f), Vector((0, 0, -hh - f / 2))),
        ("L", (0.1, f, BOARD_H), side * (hw + f / 2)),
        ("R", (0.1, f, BOARD_H), -side * (hw + f / 2)),
        ("Tray", (0.16, BOARD_W * 0.9, 0.05), Vector((0, 0, -hh - f - 0.02)) + n * 0.06),
    ]:
        b = box(f"Frame{nm}_{i}", sz, tuple(c + off - n * 0.02), a, wood)
        b.parent = root
    # a few chalk sticks on the tray
    chalk = mat("Chalk", (0.97, 0.96, 0.92), 1.0)
    for k, col in enumerate([(0.97, 0.96, 0.92), (0.55, 0.75, 1.0), (1.0, 0.55, 0.5)]):
        cm = mat(f"Chalk{k}", col, 1.0)
        s = box(f"ChalkStick{k}_{i}", (0.03, 0.12, 0.03), tuple(c + side * (0.9 - k * 0.2) + n * 0.06 + Vector((0, 0, -hh - f + 0.02))), a + 0.2 * k, cm)
        s.parent = root


def banner(i, color, name):
    d, a = wall_dir(i)
    side = Vector((-d.y, d.x, 0))
    c = d * (R - 0.04) + Vector((0, 0, 2.3))
    w, h = 1.6, 2.4
    pts = [c + side * w / 2 + Vector((0, 0, h / 2)), c - side * w / 2 + Vector((0, 0, h / 2)),
           c - side * w / 2 - Vector((0, 0, h / 2 - 0.5)), c - Vector((0, 0, h / 2)), c + side * w / 2 - Vector((0, 0, h / 2 - 0.5))]
    bm = bmesh.new()
    vs = [bm.verts.new(tuple(p)) for p in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co -= d * 0.04
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(mat(name, color, 0.9))
    # rod and a white ring emblem
    wood = MATS["TrimWood"]
    box(name + "_Rod", (0.08, w + 0.4, 0.08), tuple(c + Vector((0, 0, h / 2 + 0.05)) - d * 0.06), a, wood)
    em = disc(name + "_Emblem", 0.45, tuple(c + Vector((0, 0, 0.35)) - d * 0.07), mat("BannerWhite", (0.98, 0.96, 0.9), 0.9), 28)
    em.rotation_euler = (math.radians(90), 0, a + math.radians(90))
    em2 = disc(name + "_EmblemIn", 0.3, tuple(c + Vector((0, 0, 0.35)) - d * 0.075), MATS[name], 28)
    em2.rotation_euler = em.rotation_euler


def build_room(board_images):
    reset()
    floor_img = plank_texture(os.path.join(OUT_DIR, "floor_planks.png"))
    floor = mat("FloorWood", (1, 1, 1), 0.8, image=floor_img)
    wall = mat("WallCream", (1.0, 0.86, 0.62), 0.95)
    lower = mat("WallLower", (0.86, 0.60, 0.40), 0.95)
    trim = mat("TrimWood", (0.80, 0.45, 0.18), 0.7)
    ceil = mat("Ceiling", (0.96, 0.92, 0.84), 1.0)
    lamp = mat("Lamp", (1.0, 0.95, 0.78), 0.5, emit=2.5)

    octagon("Floor", R, 0.0, floor, uv_scale=4.0)
    octagon("Ceiling", R, H, ceil, flip=True)
    for i in range(8):
        d, a = wall_dir(i)
        L = 2 * R * TAN
        box(f"Wall_{i}", (T, L + 2 * T * TAN + 0.02, H), tuple(d * (R + T / 2) + Vector((0, 0, H / 2))), a, wall)
        strip(f"Wainscot_{i}", i, 0.0, 0.03, 0.0, 1.05, lower)
        strip(f"ChairRail_{i}", i, 0.0, 0.07, 1.02, 1.12, trim)
        strip(f"Baseboard_{i}", i, 0.0, 0.06, 0.0, 0.22, trim)
        strip(f"Crown_{i}", i, 0.0, 0.1, H - 0.22, H, trim)
        # corner posts hide the seams between walls
        ca = math.radians(22.5 + 45 * (i + 1))
        rc = R / math.cos(math.radians(22.5)) - 0.06
        box(f"Post_{i}", (0.22, 0.22, H), (rc * math.cos(ca), rc * math.sin(ca), H / 2), ca, trim)

    # boards on the six walls that are not team walls (+X and -X are the team walls)
    board_walls = [0, 1, 2, 4, 5, 6]
    for k, i in enumerate(board_walls):
        board(i, board_images[k % len(board_images)], trim)
    banner(7, (0.25, 0.55, 1.0), "BannerBlue")
    banner(3, (1.0, 0.33, 0.33), "BannerRed")

    # ceiling lamps
    disc("Lamp_C", 0.7, (0, 0, H - 0.02), lamp, 28).rotation_euler = (math.radians(180), 0, 0)
    for k in range(4):
        a = math.radians(45 + 90 * k)
        disc(f"Lamp_{k}", 0.45, (3.4 * math.cos(a), 3.4 * math.sin(a), H - 0.02), lamp, 24).rotation_euler = (math.radians(180), 0, 0)

    # round rug in the middle
    disc("Rug", 2.6, (0, 0, 0.006), mat("RugTeal", (0.35, 0.65, 0.62), 1.0), 40)
    disc("RugRing", 2.1, (0, 0, 0.009), mat("RugCream", (0.98, 0.88, 0.62), 1.0), 40)
    disc("RugIn", 1.9, (0, 0, 0.012), MATS["RugTeal"], 40)


def previews():
    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 1280, 720
    w = bpy.data.worlds.new("w")
    scn.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (1, 0.97, 0.9, 1)
    w.node_tree.nodes["Background"].inputs[1].default_value = 0.6
    for k, p in enumerate([(0, 0, H - 0.3), (3, -3, H - 0.3), (-3, 3, H - 0.3)]):
        L = bpy.data.objects.new(f"pl{k}", bpy.data.lights.new(f"pl{k}", 'POINT'))
        L.data.energy = 900
        L.data.shadow_soft_size = 1.5
        L.location = p
        scn.collection.objects.link(L)
    cam = bpy.data.objects.new("pcam", bpy.data.cameras.new("pcam"))
    scn.collection.objects.link(cam)
    scn.camera = cam
    cam.data.lens = 16
    shots = [("room_view_a.png", (0, -4.6, 1.6), (0, 3, 1.8)), ("room_view_b.png", (-3.4, 2.6, 1.6), (4, -3, 1.4)),
             ("room_board.png", (0, -2.5, 1.7), None)]
    for name, pos, tgt in shots:
        cam.location = pos
        if tgt is None:
            d, a = wall_dir(1)  # board facing -Y... look at wall 1
            tgt = tuple(d * R + Vector((0, 0, 2.3)))
            cam.location = tuple(d * (R - 4.2) + Vector((0, 0, 1.7)))
            cam.data.lens = 24
        cam.rotation_euler = (Vector(tgt) - Vector(cam.location)).to_track_quat('-Z', 'Y').to_euler()
        scn.render.filepath = os.path.join(OUT_DIR, name)
        bpy.ops.render.render(write_still=True)
    for o in list(scn.objects):
        if o.type in ('LIGHT', 'CAMERA'):
            bpy.data.objects.remove(o)


images = render_boards()
build_room(images)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT_DIR, "ToonRoom.blend"))
previews()
bpy.ops.export_scene.gltf(filepath=OUT_GLB, export_format='GLB', export_apply=True, export_lights=False, export_cameras=False)
print("[Room] exported", OUT_GLB)
