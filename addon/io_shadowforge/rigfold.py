"""Mirror-consistent bone rolls for Blender posing (the roll sandwich).

Game rigs build right-side bones as 180-degree rotations of the left side, not
reflections: heads and tails mirror across X but roll_R != -roll_L, which
inverts Blender's X-Axis Mirror posing. fold_armature() rolls each right-side
bone to roll_R = -roll_L. unfolded_for_export() restores the game rolls around
a scene export, because cooked HDB/MPK bone binds must match the game
convention bit for bit.

Rolling a bone changes its rest matrix, so each fold also retargets the
actions to preserve the deform matrix pose * rest^-1, not the world pose. With
R = rest^-1 * rest', that is basis' = R^-1 * basis * R. The parent's R cancels
out of child offsets, so only the rolled bone's own curves change, and an
identity (rest) basis stays identity.
"""
import json
import math

import bpy

from contextlib import contextmanager
from mathutils import Matrix, Quaternion, Vector

# Armature-data property holding {bone_name: [original_roll, folded_roll]}.
# Both endpoint values are stored, not a delta, so the export restores the
# game roll bit for bit: cooked HDB bind data feeds skinned vertex packing,
# where float drift crosses quantization boundaries and changes output bytes.
FOLD_PROP = "sf_roll_fold"

# Rolls closer than this to mirror-consistent are left alone (radians).
_MIN_DELTA = math.radians(0.2)
# Head/tail mirror tolerance for treating two bones as an L/R pair.
_PAIR_TOL = 1e-3


def flip_side_name(name):
    """Returns the left/right counterpart name, or None for center bones.
    Game rigs spell the side as plain 'left'/'right' inside dot-free names
    (leftarm / rightarm)."""
    low = name.lower()
    if "left" in low:
        i = low.index("left")
        return name[:i] + "right" + name[i + 4:]
    if "right" in low:
        i = low.index("right")
        return name[:i] + "left" + name[i + 5:]
    return None


def _wrap_angle(a):
    while a > math.pi:
        a -= 2 * math.pi
    while a <= -math.pi:
        a += 2 * math.pi
    return a


def _dist(a, b):
    return math.sqrt(sum((a[i] - b[i]) ** 2 for i in range(3)))


def _norm(a):
    return math.sqrt(sum(c * c for c in a))


def plan_folds(bones):
    """Chooses the per-bone roll delta that makes right-side rests mirrors of
    their left counterparts.

    bones: iterable of (name, head_xyz, tail_xyz, roll) tuples.
    Returns (deltas, warnings), where deltas maps bone name to the roll delta
    to add. Only the -X bone of each pair is folded. Pairs whose heads or
    tails are not mirror images produce a warning and are left alone."""
    info = {name: (tuple(head), tuple(tail), roll) for name, head, tail, roll in bones}
    deltas = {}
    warnings = []
    for name, (head, tail, roll) in info.items():
        other = flip_side_name(name)
        if other is None or other not in info:
            continue
        if head[0] >= 0:
            continue
        o_head, o_tail, o_roll = info[other]
        mirrored_head = (-o_head[0], o_head[1], o_head[2])
        mirrored_tail = (-o_tail[0], o_tail[1], o_tail[2])
        scale = max(1.0, _norm(head), _norm(tail))
        if (_dist(head, mirrored_head) > _PAIR_TOL * scale
                or _dist(tail, mirrored_tail) > _PAIR_TOL * scale):
            warnings.append(
                "bones '%s'/'%s' are not mirror-placed. X-mirror posing will "
                "stay unreliable for this pair" % (name, other))
            continue
        delta = _wrap_angle(-o_roll - roll)
        if abs(delta) >= _MIN_DELTA:
            deltas[name] = delta
    return deltas, warnings


def _rest_matrices(arm_obj, names):
    return {n: arm_obj.data.bones[n].matrix_local.copy() for n in names}


@contextmanager
def edit_bones(arm_obj):
    """Yields the armature's edit bones for one edit-mode round trip, then
    restores the previous mode and active object."""
    view_layer = bpy.context.view_layer
    prev_active = view_layer.objects.active
    view_layer.objects.active = arm_obj
    prev_mode = arm_obj.mode
    bpy.ops.object.mode_set(mode='EDIT')
    try:
        yield arm_obj.data.edit_bones
    finally:
        bpy.ops.object.mode_set(mode='OBJECT' if prev_mode == 'EDIT' else prev_mode)
        view_layer.objects.active = prev_active


def _edit_rolls(arm_obj, updates):
    """Applies {bone: fn(current_roll) -> new_roll} to edit bone rolls in one
    edit-mode round trip. Returns {bone: (roll_before, roll_after)}."""
    applied = {}
    with edit_bones(arm_obj) as ebs:
        for name, fn in updates.items():
            eb = ebs[name]
            before = eb.roll
            eb.roll = fn(before)
            applied[name] = (before, eb.roll)
    return applied


def _corrections(rest_before, rest_after):
    """Per-bone roll rotation R = rest^-1 @ rest' (bone-local, about the bone
    axis) for every given bone whose rest changed."""
    corrections = {}
    for name, before in rest_before.items():
        r = before.inverted() @ rest_after[name]
        if not _is_identity(r):
            corrections[name] = r
    return corrections


def _is_identity(m, eps=1e-6):
    ident = Matrix.Identity(4)
    return all(abs(m[i][j] - ident[i][j]) <= eps for i in range(4) for j in range(4))


def _bone_of_path(data_path):
    if not data_path.startswith('pose.bones["'):
        return None
    end = data_path.find('"]')
    return data_path[len('pose.bones["'):end] if end > 0 else None


def _channelbags(action):
    """Objects holding an action's .fcurves. Blender 5 actions are slotted,
    with curves in layer > strip > channelbag. Older Blenders keep .fcurves on
    the action itself."""
    layers = getattr(action, "layers", None)
    if layers is None:
        return [action]
    bags = []
    for layer in layers:
        for strip in layer.strips:
            if strip.type == 'KEYFRAME':
                bags.extend(strip.channelbags)
    return bags


def _action_bone_curves(bag):
    """Maps bone name -> {'rotation_quaternion': [fcurves], 'location': [...]}.
    Scale curves are omitted: the roll correction is rigid, so scale never
    changes."""
    out = {}
    for fc in bag.fcurves:
        bone = _bone_of_path(fc.data_path)
        if bone is None:
            continue
        if fc.data_path.endswith("rotation_quaternion"):
            key = "rotation_quaternion"
        elif fc.data_path.endswith("location"):
            key = "location"
        else:
            continue
        out.setdefault(bone, {}).setdefault(key, []).append(fc)
    for channels in out.values():
        for fcs in channels.values():
            fcs.sort(key=lambda f: f.array_index)
    return out


def _transform_action(action, corrections):
    for bag in _channelbags(action):
        _transform_bag(bag, corrections)


def _transform_bag(bag, corrections):
    """Conjugates each corrected bone's basis by its roll rotation. Every
    channel is re-keyed at the union of the bone's key times so rotation and
    location stay in sync."""
    per_bone = _action_bone_curves(bag)
    for bone, channels in per_bone.items():
        r = corrections.get(bone)
        if r is None:
            continue
        r_inv = r.inverted()
        quat_fcs = channels.get("rotation_quaternion", [])
        loc_fcs = channels.get("location", [])
        times = sorted({kp.co[0] for fc in quat_fcs + loc_fcs for kp in fc.keyframe_points})
        if not times:
            continue
        new_quats = []
        new_locs = []
        for t in times:
            if len(quat_fcs) == 4:
                rot = Quaternion([fc.evaluate(t) for fc in quat_fcs]).to_matrix().to_4x4()
            else:
                rot = Matrix.Identity(4)
            loc = Vector([fc.evaluate(t) for fc in loc_fcs]) if len(loc_fcs) == 3 else Vector((0, 0, 0))
            basis = Matrix.Translation(loc) @ rot
            new_basis = r_inv @ basis @ r
            new_quats.append(new_basis.to_quaternion())
            new_locs.append(new_basis.to_translation())
        _write_channel(bag, bone, "rotation_quaternion", quat_fcs, times,
                       [[q.w, q.x, q.y, q.z] for q in new_quats], 4)
        # R is a pure rotation, so a bone with no location keys keeps zero
        # translation and needs no location curves.
        if loc_fcs or any(v.length > 1e-6 for v in new_locs):
            _write_channel(bag, bone, "location", loc_fcs, times,
                           [[v.x, v.y, v.z] for v in new_locs], 3)


def _write_channel(bag, bone, prop, fcurves, times, values, ncomp):
    data_path = 'pose.bones["%s"].%s' % (bone, prop)
    if len(fcurves) != ncomp:
        fcurves = list(fcurves)
        have = {fc.array_index for fc in fcurves}
        for i in range(ncomp):
            if i not in have:
                fcurves.append(bag.fcurves.new(data_path, index=i))
        fcurves.sort(key=lambda f: f.array_index)
    for i, fc in enumerate(fcurves):
        kps = fc.keyframe_points
        if len(kps) != len(times):
            while len(kps) > 0:
                kps.remove(kps[0], fast=True)
            kps.add(len(times))
        for k, t in enumerate(times):
            kps[k].co = (t, values[k][i])
            kps[k].interpolation = 'LINEAR'
        fc.update()


def _correct_current_pose(arm_obj, corrections):
    """Applies basis' = R^-1 @ basis @ R to the live pose. Bones with no curves
    in the playing action keep their live basis, so without this they jump
    when their rest changes."""
    for pb in arm_obj.pose.bones:
        r = corrections.get(pb.name)
        if r is not None:
            pb.matrix_basis = r.inverted() @ pb.matrix_basis @ r


def _reroll(arm_obj, updates, actions):
    """Applies roll updates, then retargets actions and the live pose so the
    skin does not move. Only the rolled bones are retargeted: the edit-mode
    round trip also recomputes every other rest matrix, and that float noise
    is not a rest change. Returns {bone: (roll_before, roll_after)}."""
    rest_before = _rest_matrices(arm_obj, updates)
    applied = _edit_rolls(arm_obj, updates)
    corrections = _corrections(rest_before, _rest_matrices(arm_obj, updates))
    for action in actions:
        _transform_action(action, corrections)
    _correct_current_pose(arm_obj, corrections)
    return applied


def _fold_state(arm_obj):
    """{bone: (orig_roll, folded_roll)} recorded at fold time."""
    raw = arm_obj.data.get(FOLD_PROP)
    if not raw:
        return {}
    try:
        return {k: (float(v[0]), float(v[1])) for k, v in json.loads(raw).items()}
    except (ValueError, TypeError, IndexError):
        return {}


def fold_armature(arm_obj, actions):
    """Folds right-side bone rolls to the Blender mirror convention and
    retargets the given actions. An armature that already carries fold state
    is left as is. Returns (folded_bone_names, warnings)."""
    if _fold_state(arm_obj):
        return [], []
    bones = [(b.name, tuple(b.head_local), tuple(b.tail_local),
              _bone_roll(arm_obj, b.name)) for b in arm_obj.data.bones]
    deltas, warnings = plan_folds(bones)
    if not deltas:
        return [], warnings
    applied = _reroll(arm_obj, {n: (lambda r, d=d: r + d) for n, d in deltas.items()}, actions)
    arm_obj.data[FOLD_PROP] = json.dumps({n: [before, after]
                                          for n, (before, after) in applied.items()})
    return sorted(deltas), warnings


def _bone_roll(arm_obj, name):
    """Rest roll of a bone without entering edit mode, from the same
    matrix-to-roll decomposition Blender uses for edit-bone roll."""
    bone = arm_obj.data.bones[name]
    mat3 = bone.matrix_local.to_3x3()
    axis = (bone.tail_local - bone.head_local).normalized()
    _, roll = bpy.types.Bone.AxisRollFromMatrix(mat3, axis=axis)
    return roll


def _actions_for(arm_obj, folded):
    """Every action with curves on a folded bone of this armature. GLTF import
    binds each clip to one armature, so bone names identify the rig."""
    out = []
    for action in bpy.data.actions:
        bones = set()
        for bag in _channelbags(action):
            for fc in bag.fcurves:
                b = _bone_of_path(fc.data_path)
                if b is not None:
                    bones.add(b)
        if bones & folded:
            out.append(action)
    return out


def _save_keys(actions, bones):
    """Keyframes of every curve on the given bones, as
    [(bag, {(bone, prop): [[(co, interpolation), ...] per curve]})]."""
    saved = []
    for action in actions:
        for bag in _channelbags(action):
            keys = {}
            for bone, channels in _action_bone_curves(bag).items():
                if bone not in bones:
                    continue
                for prop, fcs in channels.items():
                    keys[(bone, prop)] = [[(tuple(kp.co), kp.interpolation)
                                           for kp in fc.keyframe_points] for fc in fcs]
            saved.append((bag, keys))
    return saved


def _restore_keys(saved, bones):
    """Writes saved keyframes back and removes bone curves added since."""
    for bag, keys in saved:
        for bone, channels in _action_bone_curves(bag).items():
            if bone not in bones:
                continue
            for prop, fcs in channels.items():
                old = keys.get((bone, prop))
                if old is None:
                    for fc in fcs:
                        bag.fcurves.remove(fc)
                    continue
                for fc, points in zip(fcs, old):
                    kps = fc.keyframe_points
                    if len(kps) != len(points):
                        while len(kps) > 0:
                            kps.remove(kps[0], fast=True)
                        kps.add(len(points))
                    for kp, (co, interpolation) in zip(kps, points):
                        kp.co = co
                        kp.interpolation = interpolation
                    fc.update()


def _save_pose(arm_obj, bones):
    return {pb.name: (tuple(pb.location), tuple(pb.rotation_quaternion),
                      tuple(pb.rotation_euler), tuple(pb.scale))
            for pb in arm_obj.pose.bones if pb.name in bones}


def _restore_pose(arm_obj, saved):
    for name, (loc, quat, euler, scale) in saved.items():
        pb = arm_obj.pose.bones[name]
        pb.location, pb.rotation_quaternion, pb.rotation_euler, pb.scale = loc, quat, euler, scale


@contextmanager
def unfolded_for_export():
    """Restores game-convention rolls and action curves around an export,
    then refolds. Each roll returns to its recorded original value plus any
    user edit made since folding, so an unedited bone's bind is restored bit
    for bit and the cooked HDB matches an export that never folded. The
    refold writes back the folded keys and pose saved before unfolding, so
    every export starts from the same scene."""
    folded_armatures = [o for o in bpy.data.objects
                        if o.type == 'ARMATURE' and _fold_state(o)]
    restore = []
    for arm_obj in folded_armatures:
        state = {n: rolls for n, rolls in _fold_state(arm_obj).items()
                 if n in arm_obj.data.bones}
        bones = set(state)
        actions = _actions_for(arm_obj, bones)
        saved = (_save_keys(actions, bones), _save_pose(arm_obj, bones))
        _reroll(arm_obj, {n: (lambda roll, o=orig, f=folded: o + (roll - f))
                          for n, (orig, folded) in state.items()}, actions)
        restore.append((arm_obj, state, bones, saved))
    try:
        yield
    finally:
        for arm_obj, state, bones, (keys, pose) in restore:
            _edit_rolls(arm_obj, {n: (lambda roll, o=orig, f=folded: f + (roll - o))
                                  for n, (orig, folded) in state.items()})
            _restore_keys(keys, bones)
            _restore_pose(arm_obj, pose)
