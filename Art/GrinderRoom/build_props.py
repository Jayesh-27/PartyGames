"""Builds the toon props for the grinder game and exports one GLB per prop.

Run headless:
  blender -b -P build_props.py -- <out_models_dir> <out_preview_dir>

Hiding props (lift or open them to look underneath / inside):
  Bucket, CardboardBox (open bottom, lift them), Crate (Lid), TrashCan (Lid),
  DrawerCabinet (Drawer_0..2 slide out along the front), Table (Drawer_0)
Items, one blue and one red version each: Wood (Y stick), Rope (coil), Stone
Grinder_Blue / Grinder_Red: Hopper (put things in), Chute (things come out), Crank (spins)

Every prop has its origin on the floor in the middle, front facing Blender -Y (Unity +Z).
Moving parts are separate child objects whose origin is their hinge / slide start.
"""
import bpy, bmesh, sys, os, math, random
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = args[0] if args else "."
PREV = args[1] if len(args) > 1 else OUT
os.makedirs(OUT, exist_ok=True)
os.makedirs(PREV, exist_ok=True)
random.seed(11)

BLUE = (0.25, 0.55, 1.00)
RED = (1.00, 0.33, 0.33)


def lin(c):
    return tuple((x / 12.92) if x <= 0.04045 else ((x + 0.055) / 1.055) ** 2.4 for x in c)


def shade(c, k):
    return tuple(min(1.0, x * k) for x in c)


MATS = {}


def mat(name, color, rough=0.8, metal=0.0):
    key = name
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = lin(color) + (1.0,)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    m.diffuse_color = lin(color) + (1.0,)
    MATS[key] = m
    return m


def link(o, parent=None):
    bpy.context.scene.collection.objects.link(o)
    if parent is not None:
        o.parent = parent
    return o


def empty(name, loc=(0, 0, 0), parent=None):
    o = bpy.data.objects.new(name, None)
    o.location = loc
    return link(o, parent)


def mesh_obj(name, bm, material, parent=None, loc=(0, 0, 0), smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if smooth:
        for p in me.polygons:
            p.use_smooth = True
    me.materials.append(material)
    o = bpy.data.objects.new(name, me)
    o.location = loc
    return link(o, parent)


def box(name, size, center, material, parent=None, bevel=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
    if bevel > 0:
        bmesh.ops.bevel(bm, geom=list(bm.edges), offset=bevel, segments=1, affect='EDGES')
    return mesh_obj(name, bm, material, parent)


def cyl(name, r0, r1, h, z0, material, parent=None, seg=16, center=(0, 0), cap_top=True, cap_bottom=True, axis='Z'):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r0, radius2=r1, depth=h)
    if not cap_top or not cap_bottom:
        kill = []
        for f in bm.faces:
            nz = f.normal.z
            if (not cap_top and nz > 0.99) or (not cap_bottom and nz < -0.99):
                kill.append(f)
        bmesh.ops.delete(bm, geom=kill, context='FACES_ONLY')
    for v in bm.verts:
        v.co.z += h / 2
    if axis == 'X':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'))
    elif axis == 'Y':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=(0, 0, 0), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    off = Vector((center[0], center[1], z0)) if axis == 'Z' else Vector((center[0], center[1], z0))
    for v in bm.verts:
        v.co += off
    return mesh_obj(name, bm, material, parent)


def solidify(o, t):
    m = o.modifiers.new("solid", 'SOLIDIFY')
    m.thickness = t
    m.offset = -1
    return o


def torus(name, R, r, center, material, parent=None, seg=20, rseg=8, rot=(0, 0, 0)):
    bm = bmesh.new()
    verts = []
    for i in range(seg):
        a = i / seg * math.pi * 2
        ring = []
        for j in range(rseg):
            b = j / rseg * math.pi * 2
            p = Vector(((R + r * math.cos(b)) * math.cos(a), (R + r * math.cos(b)) * math.sin(a), r * math.sin(b)))
            ring.append(bm.verts.new(p))
        verts.append(ring)
    for i in range(seg):
        for j in range(rseg):
            a, b = verts[i][j], verts[(i + 1) % seg][j]
            c, d = verts[(i + 1) % seg][(j + 1) % rseg], verts[i][(j + 1) % rseg]
            bm.faces.new((a, b, c, d))
    rm = (Matrix.Rotation(rot[0], 4, 'X') @ Matrix.Rotation(rot[1], 4, 'Y') @ Matrix.Rotation(rot[2], 4, 'Z'))
    bmesh.ops.transform(bm, matrix=rm, verts=bm.verts)
    for v in bm.verts:
        v.co += Vector(center)
    return mesh_obj(name, bm, material, parent, smooth=True)


def tube(name, pts, r, material, parent=None, seg=8, smooth=True):
    """A round tube through 3D points."""
    bm = bmesh.new()
    rings = []
    pts = [Vector(p) for p in pts]
    for i, p in enumerate(pts):
        d = (pts[min(i + 1, len(pts) - 1)] - pts[max(i - 1, 0)]).normalized()
        up = Vector((0, 0, 1)) if abs(d.z) < 0.9 else Vector((1, 0, 0))
        a = d.cross(up).normalized()
        b = d.cross(a).normalized()
        rings.append([bm.verts.new(p + (a * math.cos(t) + b * math.sin(t)) * r) for t in [k / seg * math.pi * 2 for k in range(seg)]])
    for i in range(len(rings) - 1):
        for k in range(seg):
            bm.faces.new((rings[i][k], rings[i + 1][k], rings[i + 1][(k + 1) % seg], rings[i][(k + 1) % seg]))
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    return mesh_obj(name, bm, material, parent, smooth=smooth)


def rock(name, r, center, material, parent=None, seed=0):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=r)
    for v in bm.verts:
        v.co *= rnd.uniform(0.8, 1.15)
        v.co.z *= 0.72
    for v in bm.verts:
        v.co += Vector(center)
    return mesh_obj(name, bm, material, parent)


# ============================================================== hiding props

def make_bucket():
    root = empty("Bucket")
    body = mat("BucketBody", (0.55, 0.78, 0.85), 0.5, 0.0)
    rim = mat("BucketRim", (0.38, 0.55, 0.65), 0.5)
    handle = mat("BucketHandle", (0.30, 0.30, 0.34), 0.5)
    b = solidify(cyl("Bucket_Body", 0.13, 0.165, 0.30, 0.0, body, root, 18, cap_top=False), 0.012)
    torus("Bucket_Rim", 0.165, 0.014, (0, 0, 0.30), rim, root, 24, 6)
    for z in (0.09, 0.2):
        rr = 0.13 + (0.165 - 0.13) * z / 0.30 + 0.004
        torus(f"Bucket_Band{int(z*100)}", rr, 0.008, (0, 0, z), rim, root, 24, 5)
    pts = [(0.17 * math.cos(math.radians(a)), 0, 0.29 + 0.15 * math.sin(math.radians(a))) for a in range(0, 181, 15)]
    tube("Bucket_Handle", pts, 0.008, handle, root, 6)
    return root


def make_cardboard():
    root = empty("CardboardBox")
    card = mat("Cardboard", (0.83, 0.62, 0.40), 0.95)
    tape = mat("Tape", (0.95, 0.85, 0.55), 0.6)
    w, d, h = 0.46, 0.36, 0.34
    # open bottom: build the 5 faces of a box and thicken
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * w, v.co.y * d, v.co.z * h + h / 2))
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.normal.z < -0.9], context='FACES_ONLY')
    o = solidify(mesh_obj("CardboardBox_Body", bm, card, root), 0.01)
    box("CardboardBox_Tape", (0.07, d + 0.004, 0.004), (0, 0, h + 0.002), tape, root)
    box("CardboardBox_TapeF", (0.07, 0.004, 0.1), (0, -d / 2 - 0.002, h - 0.05), tape, root)
    box("CardboardBox_TapeB", (0.07, 0.004, 0.1), (0, d / 2 + 0.002, h - 0.05), tape, root)
    # flap seam
    box("CardboardBox_Seam", (w + 0.003, 0.006, 0.003), (0, 0, h + 0.0015), mat("CardboardDark", (0.62, 0.44, 0.27), 0.95), root)
    return root


def make_crate():
    root = empty("Crate")
    wood = mat("CrateWood", (0.86, 0.62, 0.36), 0.85)
    dark = mat("CrateWoodDark", (0.62, 0.40, 0.22), 0.85)
    s, t = 0.52, 0.03
    h = 0.44
    # walls made of planks with gaps (toon look), open top
    ph = h / 3 - 0.02
    for k in range(3):
        z = 0.02 + k * (h / 3) + ph / 2
        box(f"Crate_PlankF{k}", (s, t, ph), (0, -s / 2 + t / 2, z), wood, root)
        box(f"Crate_PlankB{k}", (s, t, ph), (0, s / 2 - t / 2, z), wood, root)
        box(f"Crate_PlankL{k}", (t, s, ph), (-s / 2 + t / 2, 0, z), wood, root)
        box(f"Crate_PlankR{k}", (t, s, ph), (s / 2 - t / 2, 0, z), wood, root)
    box("Crate_Bottom", (s, s, 0.02), (0, 0, 0.01), dark, root)
    for x in (-1, 1):
        for y in (-1, 1):
            box(f"Crate_Corner{x}{y}", (0.05, 0.05, h), (x * (s / 2 - 0.01), y * (s / 2 - 0.01), h / 2), dark, root)
    lid = empty("Lid", (0, s / 2, h), root)  # hinge on the back edge, the lid reaches towards -Y
    for k in range(4):
        box(f"Lid_Plank{k}", (s + 0.02, s / 4 - 0.012, 0.025), (0, -s + s / 8 + k * s / 4, 0.0125), wood, lid)
    box("Lid_Brace", (0.06, s, 0.02), (0, -s / 2, 0.035), dark, lid)
    return root


def make_trashcan():
    root = empty("TrashCan")
    body = mat("TrashBody", (0.46, 0.72, 0.48), 0.55)
    rim = mat("TrashRim", (0.30, 0.50, 0.34), 0.55)
    r0, r1, h = 0.17, 0.2, 0.52
    solidify(cyl("TrashCan_Body", r0, r1, h, 0.0, body, root, 18, cap_top=False), 0.012)
    for z in (0.05, 0.26, 0.47):
        torus(f"TrashCan_Rib{int(z*100)}", r0 + (r1 - r0) * z / h + 0.006, 0.01, (0, 0, z), rim, root, 24, 5)
    lid = empty("Lid", (0, 0, h), root)
    cyl("Lid_Top", r1 + 0.02, r1 - 0.02, 0.05, 0.0, rim, lid, 18)
    cyl("Lid_Knob", 0.03, 0.03, 0.04, 0.05, body, lid, 10)
    torus("Lid_Handle", 0.05, 0.012, (0, 0, 0.1), rim, lid, 16, 6, rot=(math.radians(90), 0, 0))
    return root


def drawer(name, w, d, h, front_col, knob_col, parent, loc):
    """A drawer whose origin is the middle of its front face; it slides out along -Y."""
    dr = empty(name, loc, parent)
    inner = mat("DrawerInside", (0.80, 0.62, 0.42), 0.9)
    t = 0.015
    box(name + "_Front", (w, 0.025, h), (0, 0, 0), front_col, dr, bevel=0.006)
    box(name + "_Bottom", (w - 0.04, d, t), (0, d / 2, -h / 2 + 0.03), inner, dr)
    box(name + "_Back", (w - 0.04, t, h - 0.05), (0, d, -0.01), inner, dr)
    box(name + "_L", (t, d, h - 0.05), (-w / 2 + 0.02 + t / 2, d / 2, -0.01), inner, dr)
    box(name + "_R", (t, d, h - 0.05), (w / 2 - 0.02 - t / 2, d / 2, -0.01), inner, dr)
    cyl(name + "_Knob", 0.025, 0.02, 0.03, 0.0, knob_col, dr, 10, center=(0, 0), axis='Y').location = (0, -0.0125 - 0.015, 0)
    return dr


def make_cabinet():
    root = empty("DrawerCabinet")
    body = mat("CabinetBody", (0.72, 0.46, 0.27), 0.8)
    front = mat("CabinetFront", (0.95, 0.80, 0.55), 0.8)
    knob = mat("CabinetKnob", (0.85, 0.35, 0.25), 0.5)
    w, d, h, t = 0.62, 0.46, 0.92, 0.03
    box("Cabinet_Top", (w + 0.04, d + 0.03, 0.04), (0, 0.0, h - 0.02), body, root, bevel=0.01)
    box("Cabinet_L", (t, d, h - 0.1), (-w / 2 + t / 2, 0, (h - 0.1) / 2 + 0.06), body, root)
    box("Cabinet_R", (t, d, h - 0.1), (w / 2 - t / 2, 0, (h - 0.1) / 2 + 0.06), body, root)
    box("Cabinet_Back", (w, t, h - 0.1), (0, d / 2 - t / 2, (h - 0.1) / 2 + 0.06), body, root)
    box("Cabinet_Base", (w + 0.02, d + 0.01, 0.07), (0, 0, 0.035), mat("CabinetBase", (0.55, 0.34, 0.19), 0.8), root)
    n = 3
    dh = (h - 0.1 - 0.02) / n
    for k in range(n):
        z = 0.07 + dh * (k + 0.5)
        if k > 0:
            box(f"Cabinet_Shelf{k}", (w - 2 * t, d - t, 0.015), (0, -0.0, 0.07 + dh * k), body, root)
        drawer(f"Drawer_{k}", w - 2 * t - 0.01, d - 0.06, dh - 0.02, front, knob, root, (0, -d / 2 + 0.0125, z))
    return root


def make_table():
    root = empty("Table")
    top = mat("TableTop", (0.98, 0.76, 0.45), 0.75)
    leg = mat("TableLeg", (0.70, 0.42, 0.22), 0.8)
    front = mat("TableDrawer", (0.95, 0.88, 0.70), 0.8)
    knob = mat("CabinetKnob", (0.85, 0.35, 0.25), 0.5)
    w, d, h = 1.2, 0.7, 0.76
    box("Table_Top", (w, d, 0.06), (0, 0, h - 0.03), top, root, bevel=0.015)
    for x in (-1, 1):
        for y in (-1, 1):
            cyl(f"Table_Leg{x}{y}", 0.04, 0.03, h - 0.06, 0.0, leg, root, 10, center=(x * (w / 2 - 0.08), y * (d / 2 - 0.08)))
    # apron with a drawer in the front
    box("Table_ApronB", (w - 0.16, 0.025, 0.12), (0, d / 2 - 0.08, h - 0.12), leg, root)
    box("Table_ApronL", (0.025, d - 0.16, 0.12), (-w / 2 + 0.08, 0, h - 0.12), leg, root)
    box("Table_ApronR", (0.025, d - 0.16, 0.12), (w / 2 - 0.08, 0, h - 0.12), leg, root)
    box("Table_ApronFL", (0.25, 0.025, 0.12), (-w / 2 + 0.205, -d / 2 + 0.08, h - 0.12), leg, root)
    box("Table_ApronFR", (0.25, 0.025, 0.12), (w / 2 - 0.205, -d / 2 + 0.08, h - 0.12), leg, root)
    box("Table_DrawerShelf", (w - 0.6, d - 0.2, 0.012), (0, 0, h - 0.185), leg, root)
    drawer("Drawer_0", w - 0.6, d - 0.25, 0.1, front, knob, root, (0, -d / 2 + 0.08, h - 0.12))
    return root


# ============================================================== items

def team_mats(team, col):
    return (mat(f"{team}_Main", col, 0.7), mat(f"{team}_Dark", shade(col, 0.62), 0.75), mat(f"{team}_Light", tuple(min(1, x * 0.5 + 0.5) for x in col), 0.7))


def make_wood(team, col):
    main, dark, light = team_mats(team, col)
    root = empty(f"Wood_{team}")
    fork = Vector((0, 0, 0.13))
    tube(f"Wood_{team}_Handle", [(0, 0, 0.0), (0.003, 0, 0.07), fork], 0.017, main, root, 8, smooth=False)
    tube(f"Wood_{team}_ArmL", [fork, (-0.03, 0, 0.18), (-0.06, 0, 0.24)], 0.014, main, root, 8, smooth=False)
    tube(f"Wood_{team}_ArmR", [fork, (0.03, 0, 0.18), (0.06, 0, 0.24)], 0.014, main, root, 8, smooth=False)
    for z in (0.03, 0.09):
        torus(f"Wood_{team}_Band{int(z*100)}", 0.018, 0.004, (0, 0, z), light, root, 12, 4)
    cyl(f"Wood_{team}_TipL", 0.0145, 0.012, 0.012, 0.0, dark, root, 8).location = (-0.06, 0, 0.237)
    cyl(f"Wood_{team}_TipR", 0.0145, 0.012, 0.012, 0.0, dark, root, 8).location = (0.06, 0, 0.237)
    # lie it flat on the floor, arms spread sideways
    root.rotation_euler = (math.radians(-90), 0, 0)
    root.location = (0, -0.12, 0.018)
    return root


def make_rope(team, col):
    main, dark, light = team_mats(team, col)
    root = empty(f"Rope_{team}")
    for k in range(3):
        torus(f"Rope_{team}_Coil{k}", 0.075 - k * 0.006, 0.013, (0, 0, 0.013 + k * 0.022), main if k != 1 else dark, root, 22, 7)
    pts = [(0.075, 0, 0.06), (0.1, -0.02, 0.05), (0.13, -0.05, 0.02), (0.16, -0.07, 0.012)]
    tube(f"Rope_{team}_End", pts, 0.012, main, root, 7)
    torus(f"Rope_{team}_Tie", 0.015, 0.005, (0.16, -0.07, 0.012), light, root, 10, 4, rot=(0, math.radians(90), math.radians(35)))
    return root


def make_stone(team, col):
    main, dark, light = team_mats(team, col)
    root = empty(f"Stone_{team}")
    rock(f"Stone_{team}_Body", 0.05, (0, 0, 0.036), main, root, seed=5)
    rock(f"Stone_{team}_Speck", 0.012, (0.02, -0.03, 0.06), light, root, seed=2)
    return root


# ============================================================== grinder

def make_grinder(team, col):
    root = empty(f"Grinder_{team}")
    main, dark, light = team_mats(team, col)
    metal = mat("GrinderMetal", (0.80, 0.82, 0.86), 0.45, 0.2)
    yel = mat("GrinderYellow", (1.0, 0.80, 0.28), 0.6)
    black = mat("GrinderBlack", (0.18, 0.18, 0.22), 0.7)
    # chunky body on four stubby feet
    box(f"Grinder_{team}_Body", (0.7, 0.6, 0.62), (0, 0, 0.42), main, root, bevel=0.04)
    box(f"Grinder_{team}_Stripe", (0.72, 0.62, 0.08), (0, 0, 0.55), yel, root, bevel=0.01)
    for x in (-1, 1):
        for y in (-1, 1):
            cyl(f"Grinder_{team}_Foot{x}{y}", 0.06, 0.05, 0.11, 0.0, black, root, 10, center=(x * 0.26, y * 0.21))
    # hopper (funnel) on top: put things in here
    hopper = empty("Hopper", (0, 0.03, 0.73), root)
    solidify(cyl("Hopper_Funnel", 0.12, 0.3, 0.26, 0.0, metal, hopper, 16, cap_top=False, cap_bottom=False), 0.015)
    torus("Hopper_Rim", 0.3, 0.02, (0, 0, 0.26), dark, hopper, 28, 6)
    cyl("Hopper_Throat", 0.12, 0.12, 0.06, -0.05, black, hopper, 16, cap_top=False)
    # chute on the front: things come out here
    chute = empty("Chute", (0, -0.3, 0.2), root)
    bm = bmesh.new()
    pts = [(-0.14, 0, 0.06), (0.14, 0, 0.06), (0.14, -0.22, -0.06), (-0.14, -0.22, -0.06)]
    vs = [bm.verts.new(p) for p in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co.z -= 0.025
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh_obj("Chute_Slide", bm, metal, chute)
    box("Chute_SideL", (0.02, 0.25, 0.07), (-0.15, -0.11, 0.01), dark, chute).rotation_euler = (math.radians(28.6), 0, 0)
    box("Chute_SideR", (0.02, 0.25, 0.07), (0.15, -0.11, 0.01), dark, chute).rotation_euler = (math.radians(28.6), 0, 0)
    box("Chute_Mouth", (0.32, 0.04, 0.14), (0, 0.0, 0.08), black, chute)
    # crank on the right side: spins around X
    crank = empty("Crank", (0.36, 0, 0.5), root)
    cyl("Crank_Hub", 0.06, 0.06, 0.05, 0.0, dark, crank, 12, axis='X').location = (0.0, 0, 0)
    box("Crank_Arm", (0.03, 0.04, 0.24), (0.05, 0, 0.1), metal, crank)
    cyl("Crank_Handle", 0.025, 0.025, 0.12, 0.0, yel, crank, 10, axis='X').location = (0.05, 0, 0.22)
    # big gear badge on the front and two cartoon bolts
    gear = []
    for i in range(49):
        a = i / 48 * math.pi * 2
        rr = 0.11 if (i // 3) % 2 == 0 else 0.085
        gear.append(Vector((rr * math.cos(a), -0.305, 0.43 + rr * math.sin(a))))
    bm = bmesh.new()
    vs = [bm.verts.new(p) for p in gear[:-1]]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    for v in [e for e in r["geom"] if isinstance(e, bmesh.types.BMVert)]:
        v.co.y -= 0.015
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    mesh_obj(f"Grinder_{team}_Gear", bm, light, root)
    cyl(f"Grinder_{team}_GearHub", 0.035, 0.035, 0.02, 0.0, yel, root, 12, axis="Y").location = (0, -0.33, 0.43)
    for x in (-0.27, 0.27):
        cyl(f"Grinder_{team}_Bolt{x}", 0.02, 0.02, 0.012, 0.0, metal, root, 8, axis='Y').location = (x, -0.307, 0.68)
    return root


# ============================================================== export + previews

def export(root, filename):
    bpy.ops.object.select_all(action='DESELECT')
    stack = [root]
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack.extend(o.children)
    bpy.ops.export_scene.gltf(filepath=os.path.join(OUT, filename), export_format='GLB', use_selection=True, export_apply=True)


def preview(objs, filename):
    scn = bpy.context.scene
    engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
    scn.render.engine = 'BLENDER_EEVEE_NEXT' if 'BLENDER_EEVEE_NEXT' in engines else 'BLENDER_EEVEE'
    scn.render.resolution_x, scn.render.resolution_y = 1600, 900
    if scn.world is None:
        w = bpy.data.worlds.new("w")
        scn.world = w
        w.use_nodes = True
        w.node_tree.nodes["Background"].inputs[0].default_value = (1, 0.95, 0.86, 1)
        w.node_tree.nodes["Background"].inputs[1].default_value = 0.9
        sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", 'SUN'))
        sun.data.energy = 3.0
        sun.rotation_euler = (math.radians(45), 0, math.radians(-30))
        link(sun)
        me = bpy.data.meshes.new("ground")
        me.from_pydata([(-20, -20, 0), (20, -20, 0), (20, 20, 0), (-20, 20, 0)], [], [(0, 1, 2, 3)])
        me.materials.append(mat("PreviewFloor", (0.80, 0.55, 0.40), 0.9))
        link(bpy.data.objects.new("ground", me))
        cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam"))
        link(cam)
        scn.camera = cam
    cam = scn.camera
    for o in bpy.context.scene.objects:
        if o.type == 'EMPTY' and o.parent is None:
            o.hide_render = o not in objs
            stack = list(o.children)
            while stack:
                c = stack.pop()
                c.hide_render = o.hide_render
                stack.extend(c.children)
    xs = [o.location.x for o in objs]
    cx = (min(xs) + max(xs)) / 2
    span = max(xs) - min(xs) + 1.2
    cam.data.lens = 40
    dist = span * 1.25
    cam.location = (cx, -dist, dist * 0.55)
    target = Vector((cx, 0, 0.25))
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scn.render.filepath = os.path.join(PREV, filename)
    bpy.ops.render.render(write_still=True)


bpy.ops.wm.read_factory_settings(use_empty=True)

hide = [make_bucket(), make_cardboard(), make_crate(), make_trashcan(), make_cabinet(), make_table()]
items = []
for team, col in (("Blue", BLUE), ("Red", RED)):
    items += [make_wood(team, col), make_rope(team, col), make_stone(team, col)]
grinders = [make_grinder("Blue", BLUE), make_grinder("Red", RED)]

for o in hide + items + grinders:
    export(o, o.name + ".glb")

# lay them out in rows for the pictures (after export, so the files keep origin at 0)
x = 0.0
for o in hide:
    o.location.x = x
    x += 1.0 if o.name != "Table" else 1.6
# open a drawer and lids a bit for the picture
for o in hide:
    for c in o.children_recursive:
        if c.name == "Drawer_0":
            c.location.y -= 0.22
        if c.name == "Lid" and o.name == "Crate":
            c.rotation_euler.x = math.radians(-55)
        if c.name == "Lid" and o.name == "TrashCan":
            c.location.z += 0.12
            c.rotation_euler.y = math.radians(25)
hide[0].location.z = 0.18  # bucket lifted
preview(hide, "props_hiding.png")

for i, o in enumerate(items):
    o.location.x = (i % 3) * 0.35 + (0.0 if i < 3 else 1.3) + (o.location.x if o.name.startswith("Wood") else 0)
preview(items, "props_items.png")
for i, o in enumerate(grinders):
    o.location.x = i * 1.2
    o.rotation_euler.z = math.radians(-20)
preview(grinders, "props_grinders.png")
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(PREV, "Props.blend"))
print("[Props] exported to", OUT)
