"""Viewport preview for staged (eye) materials.

sforge exports a staged material in contract layout: baseColor is the stage-1
iris image on TEXCOORD_1, and the stage-0 face and stage-2 eyelid images are
named in material extras. Rendered as-is, the iris texture's transparent
padding paints the whole eye region dark.

layer_staged_materials() stacks the layers the way the game draws them: the
stage-0 face (TEXCOORD_0), the iris over it by iris alpha, then the stage-2
eyelid (TEXCOORD_2) over both by eyelid alpha. The eyelid is the bound blink
frame, and on some characters (Marumaro) it is the layer that draws the eyes.
It samples with CLIP addressing, because a UV of -32 marks vertices the eyelid
does not cover. contract_layout_for_export() rewires back to contract layout
around a scene export, because the GLTF exporter cannot trace baseColorTexture
through a Mix node.
"""
import os

import bpy

from contextlib import contextmanager

# Node names are unique per node tree, so MIX_NODE also marks a layered material.
MIX_NODE = "SF Stage Mix"
STAGE0_TEX_NODE = "SF Stage0 Texture"
STAGE0_UV_NODE = "SF Stage0 UV"
IRIS_MASK_NODE = "SF Stage1 Mask"
STAGE2_MIX_NODE = "SF Stage2 Mix"
STAGE2_TEX_NODE = "SF Stage2 Texture"
STAGE2_UV_NODE = "SF Stage2 UV"

STAGE0_PROP = "sfStage0Texture"
STAGE2_PROP = "sfStage2Texture"


def _socket(sockets, identifier):
    """Looks a socket up by identifier. The Mix node has several sockets
    named "A", "B" and "Result" (one per data type), so names are ambiguous."""
    for s in sockets:
        if s.identifier == identifier:
            return s
    raise KeyError(identifier)


def _find_node(nt, node_type):
    for n in nt.nodes:
        if n.type == node_type:
            return n
    return None


def _link_into(nt, socket):
    """bpy returns a fresh wrapper per RNA access, so sockets compare with ==
    (pointer equality), never `is`."""
    for l in nt.links:
        if l.to_socket == socket:
            return l
    return None


def _staged_materials(objs):
    """Yields (material, mesh) for each distinct staged material on objs."""
    seen = set()
    for obj in objs:
        if obj.type != 'MESH':
            continue
        for slot in obj.material_slots:
            mat = slot.material
            if mat is None or mat.name in seen:
                continue
            if mat.get(STAGE0_PROP) is None or not mat.use_nodes:
                continue
            seen.add(mat.name)
            yield mat, obj.data


def layer_staged_materials(objs):
    """Layers every staged material used by objs. Returns
    (updated_material_names, warnings). A material that cannot be layered
    stays in contract layout and produces a warning. The eyelid layer is added
    when its image is in this blend and the mesh has a third UV layer."""
    updated = []
    warnings = []
    for mat, mesh in _staged_materials(objs):
        nt = mat.node_tree
        bsdf = _find_node(nt, 'BSDF_PRINCIPLED')
        if bsdf is None:
            warnings.append("staged material '%s': no Principled BSDF node" % mat.name)
            continue
        changed = False
        if nt.nodes.get(MIX_NODE) is None:
            problem = _layer_face_under_iris(mat, mesh, nt, bsdf)
            if problem is not None:
                warnings.append(problem)
                continue
            changed = True
        if nt.nodes.get(STAGE2_MIX_NODE) is None and _layer_eyelid_on_top(mat, mesh, nt, bsdf):
            changed = True
        if changed:
            updated.append(mat.name)
    return updated, warnings


def _layer_face_under_iris(mat, mesh, nt, bsdf):
    """Puts the stage-0 face under the iris, mixed by iris alpha sampled with
    CLIP addressing. Returns a
    warning when the material cannot be layered, else None."""
    base_in = bsdf.inputs["Base Color"]
    iris_link = _link_into(nt, base_in)
    if iris_link is None or iris_link.from_node.type != 'TEX_IMAGE':
        return ("staged material '%s': Base Color is not an image "
                "texture, left as imported" % mat.name)
    iris = iris_link.from_node
    stage0_name = mat.get(STAGE0_PROP)
    stage0_img = bpy.data.images.get(stage0_name) if stage0_name else None
    if stage0_img is None:
        return ("staged material '%s': stage-0 image '%s' not found "
                "in this blend. The eye region will show the iris "
                "texture only" % (mat.name, stage0_name))
    if not mesh.uv_layers:
        return "staged material '%s': mesh has no UV layers" % mat.name

    uv0 = nt.nodes.new('ShaderNodeUVMap')
    uv0.name = STAGE0_UV_NODE
    uv0.uv_map = mesh.uv_layers[0].name
    uv0.location = (iris.location[0] - 250, iris.location[1] + 300)

    tex0 = nt.nodes.new('ShaderNodeTexImage')
    tex0.name = STAGE0_TEX_NODE
    tex0.label = "stage 0 (face)"
    tex0.image = stage0_img
    tex0.location = (iris.location[0], iris.location[1] + 300)

    mask = nt.nodes.new('ShaderNodeTexImage')
    mask.name = IRIS_MASK_NODE
    mask.label = "stage 1 (iris) coverage"
    mask.image = iris.image
    mask.extension = 'CLIP'
    mask.location = (iris.location[0], iris.location[1] - 450)

    mix = nt.nodes.new('ShaderNodeMix')
    mix.data_type = 'RGBA'
    mix.name = MIX_NODE
    mix.label = "face + iris decal"
    mix.location = (iris.location[0] + 300, iris.location[1] + 150)

    nt.links.new(_socket(uv0.outputs, "UV"), tex0.inputs["Vector"])
    nt.links.new(_socket(tex0.outputs, "Color"), _socket(mix.inputs, "A_Color"))
    nt.links.new(_socket(iris.outputs, "Color"), _socket(mix.inputs, "B_Color"))
    iris_uv = _link_into(nt, iris.inputs["Vector"])
    if iris_uv is not None:
        nt.links.new(iris_uv.from_socket, mask.inputs["Vector"])
    nt.links.new(_socket(mask.outputs, "Alpha"), _socket(mix.inputs, "Factor_Float"))
    nt.links.new(_socket(mix.outputs, "Result_Color"), base_in)
    return None


def _layer_eyelid_on_top(mat, mesh, nt, bsdf):
    """Draws the stage-2 eyelid over whatever feeds Base Color, mixed by
    eyelid alpha. Returns False when the eyelid image is not in this blend or
    the mesh has fewer than three UV layers."""
    eyelid_img = bpy.data.images.get(mat.get(STAGE2_PROP) or "")
    if eyelid_img is None or len(mesh.uv_layers) < 3:
        return False
    base_in = bsdf.inputs["Base Color"]
    below = _link_into(nt, base_in).from_socket
    anchor = nt.nodes.get(MIX_NODE).location

    uv2 = nt.nodes.new('ShaderNodeUVMap')
    uv2.name = STAGE2_UV_NODE
    uv2.uv_map = mesh.uv_layers[2].name
    uv2.location = (anchor[0] - 250, anchor[1] - 300)

    tex2 = nt.nodes.new('ShaderNodeTexImage')
    tex2.name = STAGE2_TEX_NODE
    tex2.label = "stage 2 (eyelid)"
    tex2.image = eyelid_img
    tex2.extension = 'CLIP'
    tex2.location = (anchor[0], anchor[1] - 300)

    mix2 = nt.nodes.new('ShaderNodeMix')
    mix2.data_type = 'RGBA'
    mix2.name = STAGE2_MIX_NODE
    mix2.label = "eyelid over face + iris"
    mix2.location = (anchor[0] + 300, anchor[1] - 150)

    nt.links.new(_socket(uv2.outputs, "UV"), tex2.inputs["Vector"])
    nt.links.new(below, _socket(mix2.inputs, "A_Color"))
    nt.links.new(_socket(tex2.outputs, "Color"), _socket(mix2.inputs, "B_Color"))
    nt.links.new(_socket(tex2.outputs, "Alpha"), _socket(mix2.inputs, "Factor_Float"))
    nt.links.new(_socket(mix2.outputs, "Result_Color"), base_in)
    return True


def missing_stage2_images(objs):
    """Names of the stage-2 eyelid images the staged materials on objs name
    and this blend does not have. The exported GLB carries only stage 0 and 1."""
    names = set()
    for mat, _mesh in _staged_materials(objs):
        name = mat.get(STAGE2_PROP)
        if name and bpy.data.images.get(name) is None:
            names.add(name)
    return sorted(names)


def load_images(names, folder):
    """Loads <folder>/<name>.png as an image called name for each name, packed
    into the blend so it outlives the folder. Returns the names with no file."""
    missing = []
    for name in names:
        path = os.path.join(folder, name + ".png")
        if not os.path.isfile(path):
            missing.append(name)
            continue
        image = bpy.data.images.load(path)
        image.name = name
        image.pack()
    return missing


def _iris_feeding(nt, base_in):
    """The iris image node behind Base Color through the layer mixes, or None
    when Base Color is wired some other way."""
    link = _link_into(nt, base_in)
    node = link.from_node if link else None
    if node is not None and node.name == STAGE2_MIX_NODE:
        link = _link_into(nt, _socket(node.inputs, "A_Color"))
        node = link.from_node if link else None
    if node is None or node.name != MIX_NODE:
        return None
    link = _link_into(nt, _socket(node.inputs, "B_Color"))
    return link.from_node if link and link.from_node.type == 'TEX_IMAGE' else None


@contextmanager
def contract_layout_for_export():
    """Links the iris image straight into Base Color on every layered
    material for the duration of an export, then restores the layer mixes.
    The C# importer reads stage 1 from the exported baseColorTexture.
    Materials whose Base Color no longer comes from the layer mixes were
    rewired by the user and are left alone."""
    restore = []
    for mat in bpy.data.materials:
        if not getattr(mat, "use_nodes", False) or mat.node_tree is None:
            continue
        nt = mat.node_tree
        bsdf = _find_node(nt, 'BSDF_PRINCIPLED')
        if bsdf is None:
            continue
        base_in = bsdf.inputs["Base Color"]
        iris = _iris_feeding(nt, base_in)
        if iris is None:
            continue
        layered_out = _link_into(nt, base_in).from_socket
        nt.links.new(_socket(iris.outputs, "Color"), base_in)
        restore.append((nt, layered_out, base_in))
    try:
        yield
    finally:
        for nt, out_sock, in_sock in restore:
            nt.links.new(out_sock, in_sock)
