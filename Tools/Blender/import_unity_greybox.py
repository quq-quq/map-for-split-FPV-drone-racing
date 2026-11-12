"""Builds a Blender copy of the Unity greybox exported by Tools > Blender > Export Greybox (BlenderGreyboxExporter.cs).

Headless (what the Unity menu runs):
    blender -b --factory-startup --python import_unity_greybox.py -- <greybox.json> <out.blend>
Inside an open Blender (adds to the current file, replaces an earlier copy), Python Console:
    import runpy; runpy.run_path(r"<project>/Tools/Blender/import_unity_greybox.py")
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Matrix

JSON_PATH = r""  # only used when run from Blender's text editor


def sock(sockets, identifier):
    return next(s for s in sockets if s.identifier == identifier)


def build_materials(info):
    images = {}

    def image(path, non_color=False):
        if not path:
            return None
        if not os.path.exists(path):
            print("missing texture:", path)
            return None
        key = (path, non_color)
        if key not in images:
            img = bpy.data.images.load(path, check_existing=False)
            if non_color:
                img.colorspace_settings.name = "Non-Color"
            images[key] = img
        return images[key]

    mats = []
    for m in info["materials"]:
        mat = bpy.data.materials.new(m["name"])
        if bpy.app.version < (5, 0, 0):  # node trees are always on from Blender 5
            mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
        col = m["color"]
        mat.diffuse_color = (col[0], col[1], col[2], col[3])
        bsdf.inputs["Base Color"].default_value = (col[0], col[1], col[2], 1.0)
        bsdf.inputs["Metallic"].default_value = m["metallic"]
        bsdf.inputs["Roughness"].default_value = 1.0 - m["smoothness"]

        mapping = []

        def uv():  # Unity tiling/offset: uv * scale + offset
            if not mapping:
                tc = nodes.new("ShaderNodeTexCoord")
                mp = nodes.new("ShaderNodeMapping")
                st = m["st"]
                mp.inputs["Scale"].default_value = (st[0], st[1], 1.0)
                mp.inputs["Location"].default_value = (st[2], st[3], 0.0)
                links.new(tc.outputs["UV"], mp.inputs["Vector"])
                mapping.append(mp)
            return mapping[0].outputs["Vector"]

        def tex_node(img):
            tn = nodes.new("ShaderNodeTexImage")
            tn.image = img
            links.new(uv(), tn.inputs["Vector"])
            return tn

        alpha_out = None
        img = image(m["tex"])
        if img:
            tn = tex_node(img)
            color_out = tn.outputs["Color"]
            if any(abs(c - 1.0) > 1e-4 for c in col[:3]):
                mix = nodes.new("ShaderNodeMix")
                mix.data_type = "RGBA"
                mix.blend_type = "MULTIPLY"
                sock(mix.inputs, "Factor_Float").default_value = 1.0
                links.new(color_out, sock(mix.inputs, "A_Color"))
                sock(mix.inputs, "B_Color").default_value = (col[0], col[1], col[2], 1.0)
                color_out = sock(mix.outputs, "Result_Color")
            links.new(color_out, bsdf.inputs["Base Color"])
            alpha_out = tn.outputs["Alpha"]

        if m["transparent"] or m["alphaClip"]:
            if alpha_out is not None and col[3] < 0.999:
                mul = nodes.new("ShaderNodeMath")
                mul.operation = "MULTIPLY"
                links.new(alpha_out, mul.inputs[0])
                mul.inputs[1].default_value = col[3]
                alpha_out = mul.outputs[0]
            if m["alphaClip"] and alpha_out is not None:
                gt = nodes.new("ShaderNodeMath")
                gt.operation = "GREATER_THAN"
                links.new(alpha_out, gt.inputs[0])
                gt.inputs[1].default_value = m["cutoff"]
                alpha_out = gt.outputs[0]
            if alpha_out is not None:
                links.new(alpha_out, bsdf.inputs["Alpha"])
            else:
                bsdf.inputs["Alpha"].default_value = col[3]
            if m["transparent"] and hasattr(mat, "surface_render_method"):
                mat.surface_render_method = "BLENDED"

        nimg = image(m["normalTex"], non_color=True)
        if nimg:
            nn = nodes.new("ShaderNodeNormalMap")
            nn.inputs["Strength"].default_value = m["normalScale"]
            links.new(tex_node(nimg).outputs["Color"], nn.inputs["Color"])
            links.new(nn.outputs["Normal"], bsdf.inputs["Normal"])

        mimg = image(m["metallicTex"], non_color=True)
        if mimg:  # URP: R = metallic, A = smoothness (scaled by _Smoothness)
            tn = tex_node(mimg)
            sep = nodes.new("ShaderNodeSeparateColor")
            links.new(tn.outputs["Color"], sep.inputs["Color"])
            links.new(sep.outputs[0], bsdf.inputs["Metallic"])
            rough = nodes.new("ShaderNodeMath")
            rough.operation = "MULTIPLY_ADD"  # 1 - a * smoothness
            links.new(tn.outputs["Alpha"], rough.inputs[0])
            rough.inputs[1].default_value = -m["smoothness"]
            rough.inputs[2].default_value = 1.0
            links.new(rough.outputs[0], bsdf.inputs["Roughness"])

        em = m["emission"]
        peak = max(em)
        if peak > 1e-4:
            strength = max(1.0, peak)
            bsdf.inputs["Emission Color"].default_value = (em[0] / strength, em[1] / strength, em[2] / strength, 1.0)
            bsdf.inputs["Emission Strength"].default_value = strength
            eimg = image(m["emissionTex"])
            if eimg:
                mix = nodes.new("ShaderNodeMix")
                mix.data_type = "RGBA"
                mix.blend_type = "MULTIPLY"
                sock(mix.inputs, "Factor_Float").default_value = 1.0
                links.new(tex_node(eimg).outputs["Color"], sock(mix.inputs, "A_Color"))
                sock(mix.inputs, "B_Color").default_value = bsdf.inputs["Emission Color"].default_value
                links.new(sock(mix.outputs, "Result_Color"), bsdf.inputs["Emission Color"])
        mats.append(mat)
    return mats


def build_meshes(info, data):
    meshes = []
    for mi in info["meshes"]:
        off = mi["offset"]
        nv, nl = mi["verts"], mi["loops"]
        nt = nl // 3

        def take(dtype, count):
            nonlocal off
            arr = np.frombuffer(data, dtype=dtype, count=count, offset=off)
            off += arr.nbytes
            return arr

        co = take(np.float32, nv * 3)
        idx = take(np.int32, nl)
        nrm = take(np.float32, nl * 3)
        uvs = take(np.float32, nl * 2)

        me = bpy.data.meshes.new(mi["name"])
        me.vertices.add(nv)
        me.vertices.foreach_set("co", co)
        me.loops.add(nl)
        me.loops.foreach_set("vertex_index", idx)
        me.polygons.add(nt)
        me.polygons.foreach_set("loop_start", np.arange(0, nl, 3, dtype=np.int32))
        try:
            me.polygons.foreach_set("loop_total", np.full(nt, 3, dtype=np.int32))
        except (AttributeError, TypeError, RuntimeError):
            pass  # read-only since Blender 4.0, derived from loop_start
        me.polygons.foreach_set("material_index", np.repeat(np.arange(len(mi["subTris"]), dtype=np.int32), mi["subTris"]))
        me.uv_layers.new(name="UVMap").data.foreach_set("uv", uvs)
        me.update(calc_edges=True)
        me.polygons.foreach_set("use_smooth", np.ones(nt, dtype=bool))
        try:
            me.normals_split_custom_set(nrm.reshape(-1, 3))  # Unity's exact normals (hard and soft edges)
        except Exception as e:  # noqa: BLE001
            print("custom normals skipped for", mi["name"], e)
        meshes.append(me)
    return meshes


def remove_previous(name):
    """Re-import replaces the earlier copy: its objects, collections and the data only they used."""
    old = bpy.data.collections.get(name)
    if old is None:
        return
    colls = [old] + list(old.children_recursive)
    obs = {ob for c in colls for ob in c.objects}
    meshes = {ob.data for ob in obs if ob.type == "MESH"}
    mats = {s.material for ob in obs for s in ob.material_slots if s.material} | {m for me in meshes for m in me.materials if m}
    for ob in obs:
        bpy.data.objects.remove(ob)
    for c in colls:
        bpy.data.collections.remove(c)
    for me in meshes:
        if me.users == 0:
            bpy.data.meshes.remove(me)
    images = set()
    for mat in mats:
        if mat.users == 0:
            if mat.node_tree:
                images |= {n.image for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n.image}
            bpy.data.materials.remove(mat)
    for img in images:
        if img.users == 0:
            bpy.data.images.remove(img)


def import_greybox(json_path):
    with open(json_path, "r", encoding="utf-8") as f:
        info = json.load(f)
    data = np.fromfile(os.path.join(os.path.dirname(json_path), info["binFile"]), dtype=np.uint8)

    remove_previous(info["scene"] + "_Greybox")
    mats = build_materials(info)
    meshes = build_meshes(info, data)
    missing = None

    root = bpy.data.collections.new(info["scene"] + "_Greybox")
    bpy.context.scene.collection.children.link(root)
    cols = {"": root}

    def coll(path):
        if path not in cols:
            parent, _, name = path.rpartition("/")
            c = bpy.data.collections.new(name)
            coll(parent).children.link(c)
            cols[path] = c
        return cols[path]

    for o in info["objects"]:
        me = meshes[o["mesh"]]
        ob = bpy.data.objects.new(o["name"], me)
        mtx = o["matrix"]
        ob.matrix_world = Matrix([mtx[0:4], mtx[4:8], mtx[8:12], mtx[12:16]])
        coll(o["path"]).objects.link(ob)
        for i, mid in enumerate(o["mats"]):
            if mid >= 0:
                mat = mats[mid]
            else:
                if missing is None:
                    missing = bpy.data.materials.new("Unity_NoMaterial")
                mat = missing
            if i >= len(me.materials):
                me.materials.append(mat)
            slot = ob.material_slots[i]
            slot.link = "OBJECT"  # meshes are shared, materials stay per object like Unity renderers
            slot.material = mat

    # the map is ~1 km across: open the viewport far enough to see it, show textures in Solid mode
    for screen in bpy.data.screens:
        for area in screen.areas:
            for space in area.spaces:
                if space.type == "VIEW_3D":
                    space.clip_start = 0.1
                    space.clip_end = 20000.0
                    space.shading.color_type = "TEXTURE"

    if not bpy.app.background:  # frame the whole map in every open 3D view
        for window in bpy.context.window_manager.windows:
            for area in window.screen.areas:
                if area.type == "VIEW_3D":
                    region = next((r for r in area.regions if r.type == "WINDOW"), None)
                    if region:
                        with bpy.context.temp_override(window=window, area=area, region=region):
                            bpy.ops.view3d.view_all()

    tris = sum(len(m.polygons) for m in meshes)
    verts = sum(len(m.vertices) for m in meshes)
    print("GREYBOX_IMPORTED objects=%d meshes=%d materials=%d tris=%d verts=%d"
          % (len(info["objects"]), len(meshes), len(mats), tris, verts))


def default_json():
    # <project>/Tools/Blender/this.py -> <project>/Exports/Blender/greybox.json
    here = os.path.dirname(os.path.abspath(globals().get("__file__", "")))
    path = os.path.normpath(os.path.join(here, "..", "..", "Exports", "Blender", "greybox.json"))
    return path if os.path.exists(path) else ""


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    json_path = argv[0] if argv else (JSON_PATH or default_json())
    if not json_path:
        raise SystemExit("Set JSON_PATH or pass the greybox.json path after --")
    if bpy.app.background:  # fresh file: drop the factory cube, light and camera
        for ob in list(bpy.data.objects):
            bpy.data.objects.remove(ob)
    import_greybox(json_path)
    if len(argv) > 1:
        bpy.ops.wm.save_as_mainfile(filepath=argv[1])
        print("GREYBOX_SAVED", argv[1])


main()
