"""Scene-side import and export of sforge work directories.

Every function here calls bpy and must run on Blender's main thread. The
operators in ops.py and the headless smoke scripts both call into this module.
"""
import json
import os
import re
import shutil
import tempfile

import bpy

from . import bridge
from . import manifest as mf
from . import materials
from . import rigfold

# Blender dedups datablock names with a ".001"-style suffix. sforge group
# names contain no dots, so a trailing dot + 3 digits is always that suffix.
_DEDUP_RE = re.compile(r"\.\d{3}$")


def strip_dedup(name):
    return _DEDUP_RE.sub("", name)


def fresh_workdir(asset_id):
    d = os.path.join(tempfile.gettempdir(), "sforge_work", asset_id)
    if os.path.isdir(d):
        shutil.rmtree(d, ignore_errors=True)
    os.makedirs(d, exist_ok=True)
    return d


def _output_path(outputs, suffixes, fallback):
    for path in outputs:
        low = path.lower()
        if any(low.endswith(s) for s in suffixes):
            return path
    return fallback


def _store_manifest_text(asset_id, raw) -> str:
    name = "sf_manifest_" + asset_id
    txt = bpy.data.texts.get(name)
    if txt is None:
        txt = bpy.data.texts.new(name)
    txt.clear()
    txt.write(json.dumps(raw))
    return txt.name


def _new_names(collection, before):
    return [item for item in collection if item.name not in before]


def _load_eyelid_images(objs, entity_id, workdir, exe, game_root):
    """Exports the entity's textures with sforge when a staged material names
    an eyelid image the GLB does not carry, and loads the ones it names.
    Returns warnings."""
    names = materials.missing_stage2_images(objs)
    if not names:
        return []
    texdir = os.path.join(workdir, "textures")
    os.makedirs(texdir, exist_ok=True)
    try:
        bridge.export_textures(exe, entity_id, texdir, game_root=game_root)
    except bridge.SforgeError as ex:
        return ["eyelid textures not exported (%s); eyes drawn by the eyelid "
                "layer will be missing" % ex]
    missing = materials.load_images(names, texdir)
    return ["eyelid image '%s' not found in the exported textures" % n for n in missing]


def import_entity(workdir, entity_id, result, exe=None, game_root=None):
    """Imports an `entity export` result, tags the new objects, layers staged
    eye materials and folds right-side bone rolls. With exe, eyelid images the
    GLB lacks are exported with sforge first. Returns (manifest, warnings)."""
    glb = _output_path(result.outputs, (".glb", ".gltf"),
                       os.path.join(workdir, entity_id + ".glb"))
    man_path = _output_path(result.outputs, (".sfmod.json",),
                            os.path.join(workdir, entity_id + ".sfmod.json"))
    man = mf.Manifest.load(man_path)
    # Snapshot names, not ID references: bpy ID references go stale across
    # undo pushes and ID remaps, and touching one then crashes Blender.
    objects_before = {o.name for o in bpy.data.objects}
    actions_before = {a.name for a in bpy.data.actions}
    # FORTUNE places bone tips along the GLTF node chain. The default
    # heuristic misorients these rigs. A face texture bound only under eye
    # stages ships as a material no primitive uses, and without
    # import_unused_materials its image is dropped and layering cannot find it.
    bpy.ops.import_scene.gltf(filepath=glb, bone_heuristic='FORTUNE',
                              import_unused_materials=True)
    new_objs = _new_names(bpy.data.objects, objects_before)
    new_actions = _new_names(bpy.data.actions, actions_before)
    if not new_objs:
        raise RuntimeError("GLTF import produced no objects for " + entity_id + " (" + glb + ")")
    text_name = _store_manifest_text(entity_id, man.raw)
    for obj in new_objs:
        obj["sf_id"] = entity_id
        obj["sf_rig_id"] = man.entity.rig_id
        obj["sf_workdir"] = workdir
        obj["sf_manifest_text"] = text_name
    warnings = []
    if exe is not None:
        warnings.extend(_load_eyelid_images(new_objs, entity_id, workdir, exe, game_root))
    warnings.extend(materials.layer_staged_materials(new_objs)[1])
    for obj in new_objs:
        if obj.type == 'ARMATURE':
            _, fold_warnings = rigfold.fold_armature(obj, new_actions)
            warnings.extend(fold_warnings)
    return man, warnings


def _move_to(obj, coll):
    for c in obj.users_collection:
        c.objects.unlink(obj)
    coll.objects.link(obj)


def _move_subtree(obj, coll):
    _move_to(obj, coll)
    for child in obj.children:
        _move_subtree(child, coll)


def import_map(context, workdir, stage_id, result):
    """Imports a `map export` result into collections SF_<stage>/AREA_<n>/PRI_<p>,
    following the GLB node hierarchy AREA_<n> -> PRI_<p> -> model. Returns the
    map manifest."""
    glb = _output_path(result.outputs, (".glb",), os.path.join(workdir, stage_id + ".glb"))
    man_path = _output_path(result.outputs, (".sfmap.json",),
                            os.path.join(workdir, stage_id + ".sfmap.json"))
    man = mf.MapManifest.load(man_path)
    before = {o.name for o in bpy.data.objects}
    bpy.ops.import_scene.gltf(filepath=glb)
    new_objs = _new_names(bpy.data.objects, before)
    if not new_objs:
        raise RuntimeError("map import produced no objects for " + stage_id)

    root = bpy.data.collections.new("SF_" + stage_id)
    context.scene.collection.children.link(root)
    colls = {}

    def _coll_for(area_key, pri_key=None):
        area = colls.get(area_key)
        if area is None:
            area = bpy.data.collections.new(area_key)
            root.children.link(area)
            colls[area_key] = area
        if pri_key is None:
            return area
        full = area_key + "/" + pri_key
        pri = colls.get(full)
        if pri is None:
            pri = bpy.data.collections.new(pri_key)
            area.children.link(pri)
            colls[full] = pri
        return pri

    for obj in new_objs:
        if obj.parent is None and obj.name.startswith("AREA_"):
            area_key = strip_dedup(obj.name)
            for child in list(obj.children):
                if child.name.startswith("PRI_"):
                    _move_subtree(child, _coll_for(area_key, strip_dedup(child.name)))
                else:
                    _move_subtree(child, _coll_for(area_key))
            _move_to(obj, _coll_for(area_key))
    text_name = _store_manifest_text(stage_id, man.raw)
    for obj in new_objs:
        obj["sf_id"] = stage_id
        obj["sf_category"] = "map"
        obj["sf_manifest_text"] = text_name
    return man


def export_scene_glb(workdir, entity_id):
    """Exports the whole scene with staged materials in contract layout and
    bone rolls in the game convention, restoring both afterwards."""
    glb = os.path.join(workdir, entity_id + ".glb")
    with materials.contract_layout_for_export(), rigfold.unfolded_for_export():
        bpy.ops.export_scene.gltf(filepath=glb, export_format="GLB",
                                  use_selection=False, export_yup=True,
                                  export_animations=True, export_extras=True)
    return glb


def open_blocking(entity_id, workdir, exe, game_root=None, anims=True, textures=True):
    res = bridge.export_entity(exe, entity_id, workdir, game_root=game_root,
                               anims=anims, textures=textures)
    man, warnings = import_entity(workdir, entity_id, res,
                                  exe=exe if textures else None, game_root=game_root)
    for w in warnings:
        print("ShadowForge WARNING:", w)
    return man


def deploy_blocking(entity_id, workdir, mod_name, exe, *,
                    enable=False, author=None, game_root=None):
    export_scene_glb(workdir, entity_id)
    return bridge.deploy_entity(exe, entity_id, workdir, mod_name,
                                enable=enable, author=author, game_root=game_root)
