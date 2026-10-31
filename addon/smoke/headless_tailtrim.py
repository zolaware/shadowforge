"""Checks that moving pivots and trimming bone tails never moves the skin or
changes an export. Run with:

  blender --background --factory-startup --python addon/smoke/headless_tailtrim.py \
      -- --glb <path.glb> [--tol 0.001]

The GLB is imported and folded as the addon does, twice: one scene is exported
untrimmed, the other has its pivots moved and tails trimmed and is exported.
The two GLBs must hold byte-identical nodes and bind matrices and animation
keys within 1e-6: moving a pivot re-derives its child's rest through Blender's
parent-relative bone storage, which leaves last-bit noise in that child's keys.
The trimmed scene's deformed skin must match its untrimmed skin at rest and at
sampled clip frames. A third scene keys
every pivot with a turn and an offset first, so the key shift is checked on
animated pivots too, before and after an export.
"""
import argparse
import json
import os
import struct
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import bpy  # noqa: E402
from mathutils import Quaternion, Vector  # noqa: E402

from io_shadowforge import rigfold, scene, tailtrim  # noqa: E402
from smoke.common import enable_gltf_io, fail, script_args  # noqa: E402
from smoke.headless_rigfold import _cases, _max_delta, _snapshot  # noqa: E402


def _import_folded(glb):
    """Fresh scene holding the folded GLB. Each export unfolds and refolds the
    action curves, so every export gets a scene of its own. Returns the
    armature, the skin at the sampled cases, and the cases."""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=glb, bone_heuristic='FORTUNE')
    arm = next(o for o in bpy.data.objects if o.type == 'ARMATURE')
    actions = list(bpy.data.actions)
    rigfold.fold_armature(arm, actions)
    cases = _cases(actions)
    return arm, [_snapshot(arm, a, f) for a, f in cases], cases


def _export_bytes(workdir, name):
    with open(scene.export_scene_glb(workdir, name), "rb") as f:
        return f.read()


def _parse_glb(data):
    size = struct.unpack_from("<I", data, 12)[0]
    return json.loads(data[20:20 + size]), data[20 + size + 8:]


def _accessor(gltf, binary, index):
    acc = gltf["accessors"][index]
    view = gltf["bufferViews"][acc["bufferView"]]
    start = view.get("byteOffset", 0) + acc.get("byteOffset", 0)
    width = {"SCALAR": 1, "VEC3": 3, "VEC4": 4, "MAT4": 16}[acc["type"]]
    return binary[start:start + 4 * width * acc["count"]]


def _export_difference(a, b):
    """Returns why two exported GLBs differ beyond key noise, or None."""
    (ga, ba), (gb, bb) = _parse_glb(a), _parse_glb(b)
    if ga["nodes"] != gb["nodes"]:
        return "node transforms differ"
    for sa, sb in zip(ga["skins"], gb["skins"]):
        if _accessor(ga, ba, sa["inverseBindMatrices"]) != _accessor(gb, bb, sb["inverseBindMatrices"]):
            return "inverse bind matrices differ"
    worst = 0.0
    for aa, ab in zip(ga["animations"], gb["animations"]):
        for xa, xb in zip(aa["samplers"], ab["samplers"]):
            if _accessor(ga, ba, xa["input"]) != _accessor(gb, bb, xb["input"]):
                return "animation key times differ"
            va, vb = _accessor(ga, ba, xa["output"]), _accessor(gb, bb, xb["output"])
            if va != vb:
                worst = max(worst, max(abs(p - q) for p, q in
                                       zip(struct.unpack("<%df" % (len(va) // 4), va),
                                           struct.unpack("<%df" % (len(vb) // 4), vb))))
    if worst > 1e-6:
        return "animation keys differ by %g" % worst
    return None


def _key_pivots(arm, actions):
    """Gives every pivot bone a turn and an offset at each sampled frame."""
    plan = tailtrim.plan_pivots({b.name: tuple(b.head_local) for b in arm.data.bones},
                                {b.name: b.parent.name if b.parent else None
                                 for b in arm.data.bones},
                                tailtrim.model_size(bpy.data.objects))
    for action in actions:
        arm.animation_data.action = action
        start, end = (int(x) for x in action.frame_range)
        for frame in (start, (start + end) // 2, end):
            for name in plan:
                pb = arm.pose.bones[name]
                pb.rotation_quaternion = Quaternion(Vector((0.3, 0.5, 0.8)).normalized(),
                                                    0.2 + 0.001 * frame)
                pb.location = (0.1, -0.2, 0.05 * (frame % 3))
                pb.keyframe_insert("rotation_quaternion", frame=frame)
                pb.keyframe_insert("location", frame=frame)
    return sorted(plan)


def _check_animated_pivots(glb, workdir, tol):
    arm, _, cases = _import_folded(glb)
    keyed = _key_pivots(arm, list(bpy.data.actions))
    before = [_snapshot(arm, a, f) for a, f in cases]
    size = tailtrim.model_size(bpy.data.objects)
    tailtrim.move_pivots(arm, size)
    tailtrim.trim_tails(arm, size)
    moved = max(_max_delta(b, _snapshot(arm, a, f))[0] for (a, f), b in zip(cases, before))
    _export_bytes(workdir, "animated")
    exported = max(_max_delta(b, _snapshot(arm, a, f))[0] for (a, f), b in zip(cases, before))
    print("animated pivots %s: worst skin delta %.6f after move, %.6f after export"
          % (keyed, moved, exported))
    if max(moved, exported) > tol:
        fail("TAILTRIM", "moving an animated pivot moved the skin by %.6f"
             % max(moved, exported))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--glb", required=True)
    ap.add_argument("--tol", type=float, default=1e-3)
    args = ap.parse_args(script_args())

    enable_gltf_io()
    workdir = tempfile.mkdtemp(prefix="sf_tailtrim_")
    _import_folded(args.glb)
    untrimmed = _export_bytes(workdir, "untrimmed")

    arm, before, cases = _import_folded(args.glb)
    size = tailtrim.model_size(bpy.data.objects)
    moved = tailtrim.move_pivots(arm, size)
    trimmed = tailtrim.trim_tails(arm, size)
    print("moved pivots %s, trimmed %d bones: %s" % (moved, len(trimmed), trimmed))
    if not trimmed:
        fail("TAILTRIM", "nothing was trimmed, so this rig cannot verify the restore")
    difference = _export_difference(untrimmed, _export_bytes(workdir, "trimmed"))

    limit = tailtrim._MAX_FRACTION * size
    longest = max(arm.data.bones, key=lambda b: b.length)
    if longest.length > limit:
        fail("TAILTRIM", "bone '%s' is still %.3f long (limit %.3f)"
             % (longest.name, longest.length, limit))
    if difference:
        fail("TAILTRIM", "export after trimming: %s" % difference)

    worst = max(_max_delta(b, _snapshot(arm, a, f))[0]
                for (a, f), b in zip(cases, before))
    print("worst skin delta after trim = %.6f" % worst)
    if worst > args.tol:
        fail("TAILTRIM", "trimming moved the skin by %.6f" % worst)
    for name in moved:
        bone = arm.data.bones[name]
        if (bone.head_local - bone.children[0].head_local).length > 1e-4:
            fail("TAILTRIM", "pivot '%s' was not moved onto its child" % name)

    _check_animated_pivots(args.glb, workdir, args.tol)
    print("TAILTRIM-OK")


if __name__ == "__main__":
    main()
