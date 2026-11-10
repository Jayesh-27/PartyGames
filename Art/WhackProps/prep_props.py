"""Prepares the downloaded bunny and hammer for Whack-a-Bunny and exports them for Unity.

blender -b -P prep_props.py -- <bunny.glb> <hammer.glb> <out_dir>

Bunny : keeps its rig (arms, fingers, ears, tail), removes the importer's helper sphere,
        scales it to fit through the table holes, feet at the origin.
Hammer: swaps the phone texture for the MOKIA version, scales it to a VR mallet,
        handle along +Z with the pivot at the grip.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Vector

args = sys.argv[sys.argv.index("--") + 1:]
BUNNY_SRC, HAMMER_SRC, OUT = args[0], args[1], args[2]
HERE = os.path.dirname(os.path.abspath(__file__))
REN = os.path.join(HERE, "renders")
os.makedirs(REN, exist_ok=True)

BUNNY_HEIGHT = 0.24     # metres, feet to ear tips
BUNNY_MAX_WIDTH = 0.13  # must fit through the 0.15 m holes
HAMMER_LENGTH = 0.55    # whole mallet, metres
GRIP_FROM_END = 0.07    # pivot this far up from the bottom of the handle


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def mesh_world_bounds(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for o in objs:
        if o.type != 'MESH':
            continue
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        mw = ev.matrix_world
        for v in me.vertices:
            p = mw @ v.co
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
        ev.to_mesh_clear()
    return lo, hi


def roots():
    return [o for o in bpy.data.objects if o.parent is None]


def wrap(name):
    """new empty that becomes the single root of everything imported."""
    root = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(root)
    for o in roots():
        if o is not root:
            o.parent = root
    return root


def export(path, objs):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objs:
        o.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, export_format='GLB', use_selection=True,
                              export_apply=False, export_yup=True, export_skins=True,
                              export_animations=False)


def everything_under(root):
    out = [root]
    for c in root.children_recursive:
        out.append(c)
    return out


def preview(name, target, dist, height):
    scn = bpy.context.scene
    try:
        scn.render.engine = 'BLENDER_EEVEE'
    except Exception:
        pass
    scn.render.resolution_x, scn.render.resolution_y = 900, 900
    scn.view_settings.view_transform = 'Standard'
    w = bpy.data.worlds.new("w")
    scn.world = w
    w.use_nodes = True
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0.93, 0.95, 0.98, 1)
    w.node_tree.nodes["Background"].inputs["Strength"].default_value = 0.9
    bpy.ops.object.light_add(type='SUN', location=(1, -1, 2))
    sun = bpy.context.active_object
    sun.data.energy = 2.5
    sun.rotation_euler = (math.radians(45), math.radians(15), math.radians(35))
    cam = bpy.data.objects.new("~cam", bpy.data.cameras.new("~cam"))
    scn.collection.objects.link(cam)
    scn.camera = cam
    for i, ang in enumerate((-35, 145)):
        a = math.radians(ang)
        cam.location = Vector((math.sin(a) * dist, -math.cos(a) * dist, height))
        d = Vector(target) - cam.location
        cam.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
        cam.data.lens = 50
        scn.render.filepath = os.path.join(REN, f"{name}_{i}.png")
        bpy.ops.render.render(write_still=True)
    for o in (sun, cam):
        bpy.data.objects.remove(o, do_unlink=True)


# ------------------------------------------------------------------ bunny

def do_bunny():
    reset()
    bpy.ops.import_scene.gltf(filepath=BUNNY_SRC)
    for o in list(bpy.data.objects):
        if o.type == 'MESH' and o.name.startswith("Icosphere") and o.parent is None:
            bpy.data.objects.remove(o, do_unlink=True)  # bone-display helper made by the importer
    for o in bpy.data.objects:
        if o.type == 'ARMATURE':
            o.name = "BunnyRig"
            o.data.name = "BunnyRig"
            o.show_in_front = False
    root = wrap("Bunny")
    bpy.context.view_layer.update()

    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    lo, hi = mesh_world_bounds(meshes)
    size = hi - lo
    print(f"[Props] bunny raw size {size.x:.3f} x {size.y:.3f} x {size.z:.3f}")
    s = min(BUNNY_HEIGHT / size.z, BUNNY_MAX_WIDTH / max(size.x, size.y))
    centre = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    root.matrix_world = Matrix.Translation(-centre * s) @ Matrix.Scale(s, 4) @ root.matrix_world
    bpy.context.view_layer.update()
    lo, hi = mesh_world_bounds(meshes)
    size = hi - lo
    print(f"[Props] bunny final size {size.x:.3f} x {size.y:.3f} x {size.z:.3f} (scale {s:.5f})")
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    print(f"[Props] bunny bones: {len(arm[0].data.bones) if arm else 0}")

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "Bunny.blend"))
    export(os.path.join(OUT, "Bunny.glb"), everything_under(root))
    preview("bunny", (0, 0, size.z * 0.5), size.z * 3.2, size.z * 0.75)


# ------------------------------------------------------------------ hammer

def do_hammer():
    reset()
    bpy.ops.import_scene.gltf(filepath=HAMMER_SRC)

    # swap the phone texture for the MOKIA one
    new_img = bpy.data.images.load(os.path.join(HERE, "textures", "front_back_mokia.png"))
    new_img.name = "mokia_front_back"
    swapped = 0
    for m in bpy.data.materials:
        if "front_back" not in m.name or not m.use_nodes:
            continue
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image is not None:
                links = [l for l in m.node_tree.links if l.from_node == n and l.to_socket.name == "Base Color"]
                if links:
                    n.image = new_img
                    swapped += 1
    print(f"[Props] hammer textures swapped: {swapped}")
    for m in bpy.data.materials:
        m.name = m.name.replace("nokia", "mokia")
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    names = {"nokia_front_back_material": "Head_FrontBack", "nokia_side_material": "Head_Side", "nokia_handle_material": "Handle"}
    for o in meshes:
        mat = o.data.materials[0].name if o.data.materials else ""
        for k, v in names.items():
            if k.replace("nokia", "mokia") == mat:
                o.name = v

    # bake every transform into the meshes, drop the Sketchfab empties
    bpy.context.view_layer.update()
    for o in meshes:
        mw = o.matrix_world.copy()
        o.parent = None
        o.matrix_world = mw
    for o in list(bpy.data.objects):
        if o.type == 'EMPTY':
            bpy.data.objects.remove(o, do_unlink=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    handle = next(o for o in meshes if o.name.startswith("Handle"))
    heads = [o for o in meshes if o is not handle]
    hlo, hhi = mesh_world_bounds([handle])
    axis = max(range(3), key=lambda i: hhi[i] - hlo[i])           # handle's long axis
    head_lo, head_hi = mesh_world_bounds(heads)
    head_c = (head_lo + head_hi) / 2
    handle_c = (hlo + hhi) / 2
    up = Vector((0, 0, 0))
    up[axis] = 1.0 if head_c[axis] > handle_c[axis] else -1.0      # from handle toward the head
    rot = up.rotation_difference(Vector((0, 0, 1))).to_matrix().to_4x4()

    alo, ahi = mesh_world_bounds(meshes)
    length = (ahi - alo)[axis]
    s = HAMMER_LENGTH / length
    bottom = handle_c.copy()
    bottom[axis] = hlo[axis] if up[axis] > 0 else hhi[axis]
    grip = bottom + up * (GRIP_FROM_END / s)
    xf = Matrix.Scale(s, 4) @ rot @ Matrix.Translation(-grip)
    for o in meshes:
        o.data.transform(xf)
        o.data.update()

    root = bpy.data.objects.new("MokiaHammer", None)
    bpy.context.scene.collection.objects.link(root)
    for o in meshes:
        o.parent = root
    lo, hi = mesh_world_bounds(meshes)
    print(f"[Props] hammer final size {hi.x - lo.x:.3f} x {hi.y - lo.y:.3f} x {hi.z - lo.z:.3f}, grip at origin")

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "MokiaHammer.blend"))
    export(os.path.join(OUT, "MokiaHammer.glb"), everything_under(root))
    mid = (lo + hi) / 2
    preview("hammer", (mid.x, mid.y, mid.z + 0.1), 1.3, mid.z + 0.25)


do_bunny()
do_hammer()
print("[Props] done")
