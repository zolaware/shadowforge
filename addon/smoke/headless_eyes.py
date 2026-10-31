"""Imports one character and checks its staged eye materials: face under iris,
eyelid on top, and the iris alone on Base Color while a scene export runs.
Run with:

  blender --background --factory-startup --python addon/smoke/headless_eyes.py -- --id pc04 [--game-root <r>] [--sforge <exe>]
"""
import argparse
import os
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
import io_shadowforge as addon  # noqa: E402
from io_shadowforge import materials, scene  # noqa: E402
from smoke.common import bundled_exe, enable_gltf_io, fail, script_args  # noqa: E402


def base_color_source(mat):
    nt = mat.node_tree
    bsdf = materials._find_node(nt, 'BSDF_PRINCIPLED')
    return materials._link_into(nt, bsdf.inputs["Base Color"]).from_node


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--id", default="pc04")
    ap.add_argument("--game-root", default=None)
    ap.add_argument("--sforge", default=None)
    args = ap.parse_args(script_args())

    enable_gltf_io()
    addon.register()
    exe = args.sforge or bundled_exe()
    bpy.ops.wm.read_factory_settings(use_empty=True)

    workdir = tempfile.mkdtemp(prefix="eyes_" + args.id + "_")
    scene.open_blocking(args.id, workdir, exe, game_root=args.game_root, anims=False, textures=True)

    eyes = [m for m in bpy.data.materials if m.get(materials.STAGE2_PROP)]
    if not eyes:
        fail("EYES", args.id + " has no staged material that names an eyelid")
    for mat in eyes:
        if base_color_source(mat).name != materials.STAGE2_MIX_NODE:
            fail("EYES", mat.name + ": eyelid layer not on top of Base Color")
        eyelid = mat.node_tree.nodes[materials.STAGE2_TEX_NODE].image
        if eyelid is None or eyelid.name != mat[materials.STAGE2_PROP] or not eyelid.packed_file:
            fail("EYES", mat.name + ": eyelid image missing or not packed")

    with materials.contract_layout_for_export():
        for mat in eyes:
            if base_color_source(mat).type != 'TEX_IMAGE':
                fail("EYES", mat.name + ": iris not on Base Color during export")
    for mat in eyes:
        if base_color_source(mat).name != materials.STAGE2_MIX_NODE:
            fail("EYES", mat.name + ": layers not restored after export")

    addon.unregister()
    print("EYES-OK: " + ", ".join("%s (eyelid %s)" % (m.name, m[materials.STAGE2_PROP]) for m in eyes))


if __name__ == "__main__":
    main()
