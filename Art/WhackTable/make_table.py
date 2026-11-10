"""Builds the Whack-a-Bunny table in Blender, renders previews in a white cloud, saves .blend and exports GLB.

Run (no UI):
  blender -b -P make_table.py
Outputs next to this file: WhackTable.blend, WhackTable.glb, renders/*.png
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
TEX = os.path.join(HERE, "textures")
REN = os.path.join(HERE, "renders")
os.makedirs(REN, exist_ok=True)

# ---- dimensions (metres) - keep in sync with gen_textures.py
TOP_W, TOP_D, TOP_T = 1.20, 0.75, 0.085
TABLE_H = 0.80                       # floor to top surface
CORNER_R = 0.085
EDGE_R = 0.022
HOLES = [(-0.40, 0.13), (0.0, 0.17), (0.40, 0.13), (-0.40, -0.17), (0.0, -0.13), (0.40, -0.17)]
HOLE_R = 0.075
TUBE_DEPTH = 0.15
APRON_H, APRON_INSET = 0.18, 0.07   # box under the top that hides the burrows
LEG_T, LEG_W_TOP, LEG_W_BOT = 0.045, 0.17, 0.13
LEG_SPLAY = math.radians(9)
LEG_INSET_X, LEG_INSET_Y = 0.17, 0.14

ONLY_RENDER = "--render-only" in sys.argv


def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def image(name):
    path = os.path.join(TEX, name)
    img = bpy.data.images.load(path, check_existing=True)
    return img


def material(name, tex=None, color=(1, 1, 1, 1), rough=0.7, spec=0.25):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = rough
    for key in ("Specular IOR Level", "Specular"):
        if key in bsdf.inputs:
            bsdf.inputs[key].default_value = spec
            break
    if tex:
        t = nt.nodes.new("ShaderNodeTexImage")
        t.image = image(tex)
        t.location = (-400, 200)
        nt.links.new(t.outputs["Color"], bsdf.inputs["Base Color"])
    else:
        bsdf.inputs["Base Color"].default_value = color
    return m


def link(obj, coll):
    for c in obj.users_collection:
        c.objects.unlink(obj)
    coll.objects.link(obj)


def apply_mods(obj):
    bpy.context.view_layer.objects.active = obj
    for mod in list(obj.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)


# ----------------------------------------------------------------- tabletop

def make_top(coll):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= TOP_W
        v.co.y *= TOP_D
        v.co.z *= TOP_T
    # round the four vertical corners
    vert_edges = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).normalized().z) > 0.99]
    bmesh.ops.bevel(bm, geom=vert_edges, offset=CORNER_R, segments=8, profile=0.5, affect='EDGES')
    me = bpy.data.meshes.new("WhackTable_Top")
    bm.to_mesh(me)
    bm.free()
    top = bpy.data.objects.new("WhackTable_Top", me)
    coll.objects.link(top)
    top.location = (0, 0, TABLE_H - TOP_T / 2)

    # soft rounded rim on top and bottom edges
    bev = top.modifiers.new("edge", 'BEVEL')
    bev.width = EDGE_R
    bev.segments = 4
    bev.limit_method = 'ANGLE'
    bev.angle_limit = math.radians(40)

    # holes
    cutters = []
    for i, (x, y) in enumerate(HOLES):
        bpy.ops.mesh.primitive_cylinder_add(vertices=48, radius=HOLE_R, depth=TOP_T * 4, location=(x, y, TABLE_H - TOP_T / 2))
        c = bpy.context.active_object
        c.name = f"cut{i}"
        cutters.append(c)
    bpy.ops.object.select_all(action='DESELECT')
    for c in cutters[1:]:
        c.select_set(True)
    cutters[0].select_set(True)
    bpy.context.view_layer.objects.active = cutters[0]
    bpy.ops.object.join()
    cutter = cutters[0]

    boo = top.modifiers.new("holes", 'BOOLEAN')
    boo.operation = 'DIFFERENCE'
    boo.object = cutter
    try:
        boo.solver = 'EXACT'
    except Exception:
        pass

    rim = top.modifiers.new("holeRim", 'BEVEL')
    rim.width = 0.008
    rim.segments = 3
    rim.limit_method = 'ANGLE'
    rim.angle_limit = math.radians(60)

    apply_mods(top)
    bpy.data.objects.remove(cutter, do_unlink=True)

    # materials: 0 painted top, 1 hole walls
    top.data.materials.clear()  # the boolean leaves an empty slot behind
    top.data.materials.append(material("WT_Paint", "top.png", rough=0.62, spec=0.3))
    top.data.materials.append(material("WT_Hole", "hole.png", rough=0.9, spec=0.1))

    bm = bmesh.new()
    bm.from_mesh(top.data)
    bm.normal_update()
    uv = bm.loops.layers.uv.verify()
    hole_faces = 0
    z_mid = TOP_T / 2 * 0  # local space centre
    for f in bm.faces:
        c = f.calc_center_median()
        n = f.normal
        in_hole = False
        for (hx, hy) in HOLES:
            if math.hypot(c.x - hx, c.y - hy) < HOLE_R + 0.004 and abs(n.z) < 0.6:
                in_hole = True
                break
        f.material_index = 1 if in_hole else 0
        hole_faces += in_hole
        for l in f.loops:
            co = l.vert.co
            if in_hole:
                a = math.atan2(co.y, co.x)
                l[uv].uv = (a / (2 * math.pi) % 1.0, (co.z + TOP_T / 2) / TOP_T)
            else:
                # side faces get a thin squeeze of the border colour; top/bottom use the top-down painting
                u = (co.x + TOP_W / 2) / TOP_W
                v = (co.y + TOP_D / 2) / TOP_D
                l[uv].uv = (min(max(u, 0.002), 0.998), min(max(v, 0.002), 0.998))
    print(f"[WhackTable] hole wall faces: {hole_faces}")
    bm.to_mesh(top.data)
    bm.free()
    for p in top.data.polygons:
        p.use_smooth = True
    try:
        top.data.set_sharp_from_angle(angle=math.radians(50))
    except Exception:
        pass
    return top


def make_tubes(coll):
    """dark burrows under each hole, so you never see through the table and bunnies rise out of darkness."""
    mat = material("WT_Burrow", color=(0.06, 0.045, 0.05, 1), rough=1.0, spec=0.0)
    bm = bmesh.new()
    top_z = TABLE_H - TOP_T + 0.004
    bot_z = top_z - TUBE_DEPTH
    r = HOLE_R - 0.001
    n = 32
    for (x, y) in HOLES:
        ring_t = [bm.verts.new((x + r * math.cos(a), y + r * math.sin(a), top_z)) for a in (2 * math.pi * k / n for k in range(n))]
        ring_b = [bm.verts.new((x + r * math.cos(a), y + r * math.sin(a), bot_z)) for a in (2 * math.pi * k / n for k in range(n))]
        for k in range(n):
            k2 = (k + 1) % n
            bm.faces.new((ring_t[k], ring_t[k2], ring_b[k2], ring_b[k]))   # wall, facing inward
        bm.faces.new(ring_b)                                               # floor, facing up
    bm.normal_update()
    me = bpy.data.meshes.new("WhackTable_Burrows")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("WhackTable_Burrows", me)
    coll.objects.link(ob)
    ob.data.materials.append(mat)
    return ob


def make_apron(coll, mat):
    """rounded box under the top; hides the burrows so the table reads as a toy box on legs."""
    w, d = TOP_W - 2 * APRON_INSET, TOP_D - 2 * APRON_INSET
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x *= w
        v.co.y *= d
        v.co.z *= APRON_H
    vert_edges = [e for e in bm.edges if abs((e.verts[0].co - e.verts[1].co).normalized().z) > 0.99]
    bmesh.ops.bevel(bm, geom=vert_edges, offset=CORNER_R * 0.6, segments=6, profile=0.5, affect='EDGES')
    # open top (it sits against the underside of the top)
    for f in list(bm.faces):
        if f.normal.z > 0.9:
            bm.faces.remove(f)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        for l in f.loops:
            co = l.vert.co
            a = math.atan2(co.y / d, co.x / w)
            l[uv].uv = (a / (2 * math.pi) % 1.0, co.z / APRON_H + 0.5)
    me = bpy.data.meshes.new("WhackTable_Apron")
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("WhackTable_Apron", me)
    coll.objects.link(ob)
    ob.location = (0, 0, TABLE_H - TOP_T - APRON_H / 2 + 0.01)
    bev = ob.modifiers.new("soft", 'BEVEL')
    bev.width = 0.012
    bev.segments = 3
    bev.limit_method = 'ANGLE'
    apply_mods(ob)
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.materials.append(mat)
    return ob


# ----------------------------------------------------------------- legs

def make_leg(coll, sx, sy, mat):
    h = TABLE_H - TOP_T + 0.01
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        top = v.co.z > 0
        w = LEG_W_TOP if top else LEG_W_BOT
        v.co.x *= LEG_T
        v.co.y *= w
        v.co.z = (v.co.z + 0.5) * h - h  # top at 0, bottom at -h
    me = bpy.data.meshes.new("leg")
    bm.to_mesh(me)
    bm.free()
    leg = bpy.data.objects.new(f"WhackTable_Leg_{'R' if sx > 0 else 'L'}{'B' if sy > 0 else 'F'}", me)
    coll.objects.link(leg)

    bev = leg.modifiers.new("soft", 'BEVEL')
    bev.width = 0.01
    bev.segments = 3
    apply_mods(leg)

    # wood UVs: grain runs along the leg
    bm = bmesh.new()
    bm.from_mesh(leg.data)
    uv = bm.loops.layers.uv.verify()
    k = 1.0 / 0.9
    off = (0.37 if sx > 0 else 0.0) + (0.21 if sy > 0 else 0.0)
    for f in bm.faces:
        n = f.normal
        for l in f.loops:
            co = l.vert.co
            if abs(n.x) >= abs(n.y) and abs(n.x) >= abs(n.z):
                l[uv].uv = (co.y * k + off, co.z * k)
            elif abs(n.y) >= abs(n.z):
                l[uv].uv = (co.x * k + off + 0.5, co.z * k)
            else:
                l[uv].uv = (co.x * k + off, co.y * k)
    bm.to_mesh(leg.data)
    bm.free()
    leg.data.materials.append(mat)

    # place under the corner, tilted outward along X (like the splayed toy-table legs)
    # against the outside of the apron's short ends, so they never cut into the burrows
    leg_x = TOP_W / 2 - APRON_INSET + LEG_T / 2 + 0.002
    leg.location = (sx * leg_x, sy * (TOP_D / 2 - LEG_INSET_Y), TABLE_H - TOP_T + 0.005)
    leg.rotation_euler = (sy * LEG_SPLAY * 0.35, -sx * LEG_SPLAY, 0)
    return leg


# ----------------------------------------------------------------- preview scene

def preview_scene():
    scn = bpy.context.scene
    for eng in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT"):
        try:
            scn.render.engine = eng
            break
        except Exception:
            continue
    scn.render.resolution_x, scn.render.resolution_y = 1280, 900
    try:
        scn.view_settings.view_transform = 'Standard'
        scn.view_settings.look = 'None'
    except Exception:
        pass
    try:
        scn.eevee.taa_render_samples = 64
        scn.eevee.use_shadows = True
    except Exception:
        pass

    world = bpy.data.worlds.new("Cloud")
    scn.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.93, 0.95, 0.98, 1)
    bg.inputs["Strength"].default_value = 0.55
    # (the cloud fog itself is done in Unity; here the floor just fades into the white background)

    # soft cloud floor that fades into the white
    bpy.ops.mesh.primitive_plane_add(size=40, location=(0, 0, 0))
    floor = bpy.context.active_object
    floor.name = "~CloudFloor"
    fm = bpy.data.materials.new("~Cloud")
    fm.use_nodes = True
    b = fm.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (0.95, 0.96, 0.99, 1)
    b.inputs["Roughness"].default_value = 1.0
    # fade to pure background white away from the table (fake cloud fog)
    nt = fm.node_tree
    tc = nt.nodes.new("ShaderNodeTexCoord")
    ln = nt.nodes.new("ShaderNodeVectorMath"); ln.operation = 'LENGTH'
    mr = nt.nodes.new("ShaderNodeMapRange")
    mr.inputs["From Min"].default_value = 1.2
    mr.inputs["From Max"].default_value = 4.0
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = (0.93, 0.95, 0.98, 1)
    em.inputs["Strength"].default_value = 0.55
    mix = nt.nodes.new("ShaderNodeMixShader")
    out = nt.nodes.get("Material Output")
    nt.links.new(tc.outputs["Object"], ln.inputs[0])
    nt.links.new(ln.outputs["Value"], mr.inputs["Value"])
    nt.links.new(mr.outputs["Result"], mix.inputs["Fac"])
    nt.links.new(b.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    floor.data.materials.append(fm)

    # sun + big soft fill
    bpy.ops.object.light_add(type='SUN', location=(2, -2, 4))
    sun = bpy.context.active_object
    sun.data.energy = 2.2
    sun.data.angle = math.radians(18)
    sun.rotation_euler = (math.radians(40), math.radians(18), math.radians(30))
    bpy.ops.object.light_add(type='AREA', location=(-2.5, -2.5, 3))
    fill = bpy.context.active_object
    fill.data.energy = 60
    fill.data.size = 4
    fill.rotation_euler = (math.radians(50), 0, math.radians(-45))

    cam_data = bpy.data.cameras.new("cam")
    cam = bpy.data.objects.new("~Cam", cam_data)
    scn.collection.objects.link(cam)
    scn.camera = cam
    return cam


def shoot(cam, name, loc, target, lens=45):
    cam.location = loc
    d = Vector(target) - Vector(loc)
    cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    cam.data.lens = lens
    bpy.context.scene.render.filepath = os.path.join(REN, name)
    bpy.ops.render.render(write_still=True)


def main():
    clear()
    coll = bpy.data.collections.new("WhackTable")
    bpy.context.scene.collection.children.link(coll)

    top = make_top(coll)
    tubes = make_tubes(coll)
    apron = make_apron(coll, material("WT_Apron", color=(0.30, 0.40, 0.63, 1), rough=0.7, spec=0.25))
    wood = material("WT_Wood", "wood.png", rough=0.7, spec=0.2)
    legs = [make_leg(coll, sx, sy, wood) for sx in (-1, 1) for sy in (-1, 1)]

    # one root so Unity gets a single prefab-like object
    root = bpy.data.objects.new("WhackTable", None)
    coll.objects.link(root)
    parts = [top, tubes, apron] + legs
    for ob in parts:
        ob.parent = root

    tris = sum(sum(len(p.vertices) - 2 for p in ob.data.polygons) for ob in parts)
    print(f"[WhackTable] triangles: {tris}")

    cam = preview_scene()
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "WhackTable.blend"))

    shoot(cam, "01_three_quarter.png", (1.7, -1.7, 1.65), (0, 0, 0.5), 38)
    shoot(cam, "02_player_view.png", (0.0, -0.95, 1.55), (0, 0.05, 0.78), 30)
    shoot(cam, "03_low_side.png", (1.9, -0.6, 0.55), (0, 0, 0.5), 40)

    if not ONLY_RENDER:
        bpy.ops.object.select_all(action='DESELECT')
        for ob in [root] + parts:
            ob.select_set(True)
        bpy.ops.export_scene.gltf(filepath=os.path.join(HERE, "WhackTable.glb"), export_format='GLB',
                                  use_selection=True, export_apply=True, export_yup=True)
        print("[WhackTable] exported GLB")


main()
