"""Short display tails for bones the GLTF importer draws far too long.

The importer points each bone's tail at its child, and a bone without a usable
child takes its parent's length. Game rigs hold locators and pivot nodes far
from the body (pc02's head_pointShape sits 13.8 units to the side and its child
head translates back), so those bones and their parents are drawn several
times the character's width. trim_tails() shortens them along their own
direction, which leaves every bone's head and orientation, and so the skin,
where it was.

A pivot node also draws its own joint far from the body. move_pivots() moves
such a bone onto its child's joint and shifts its location keys to match, so
it deforms exactly as before.

A tail edit also nudges the rest matrix in its last bits, and cooked HDB/MPK
bind data must match the game bit for bit. full_tails_for_export() puts the
original pivots and tails back around a scene export.
"""
import json
import math

import bpy

from contextlib import contextmanager
from mathutils import Quaternion, Vector

from . import rigfold
from .rigfold import edit_bones

# Armature-data property holding {bone_name: [original_tail, trimmed_tail]}.
TRIM_PROP = "sf_tail_trim"

# Armature-data property holding
# {bone_name: [[original_head, original_tail], [moved_head, moved_tail]]}.
PIVOT_PROP = "sf_pivot_move"

# Bones longer than this fraction of the model's largest mesh dimension are
# trimmed. Real limbs stay under about 0.3; above 0.4 are the root, hips,
# locators and prop pivots.
_MAX_FRACTION = 0.4


def plan_trims(lengths, model_size):
    """Chooses new lengths for overlong bones.

    lengths: {bone_name: length}. Returns {bone_name: new_length}, where each
    bone longer than _MAX_FRACTION of model_size gets the rig's median bone
    length."""
    if not lengths or model_size <= 0:
        return {}
    ordered = sorted(lengths.values())
    median = ordered[len(ordered) // 2]
    limit = _MAX_FRACTION * model_size
    return {name: median for name, length in lengths.items() if length > limit}


def plan_pivots(heads, parents, model_size):
    """Finds pivot bones to move onto their child.

    heads: {bone_name: head}, parents: {bone_name: parent_name or None}.
    Returns {bone_name: child_name} for each bone with a parent and one child
    whose head lies more than _MAX_FRACTION of model_size from both."""
    if model_size <= 0:
        return {}
    limit = _MAX_FRACTION * model_size
    children = {}
    for name, parent in parents.items():
        if parent is not None:
            children.setdefault(parent, []).append(name)
    pivots = {}
    for name, kids in children.items():
        parent = parents[name]
        if parent is None or len(kids) != 1:
            continue
        head = heads[name]
        if (math.dist(head, heads[parent]) > limit
                and math.dist(head, heads[kids[0]]) > limit):
            pivots[name] = kids[0]
    return pivots


def model_size(objs):
    """Largest world-space bounding-box dimension of the mesh objects."""
    points = [o.matrix_world @ Vector(corner)
              for o in objs if o.type == 'MESH' for corner in o.bound_box]
    if not points:
        return 0.0
    return max(max(p[i] for p in points) - min(p[i] for p in points) for i in range(3))


def _trim_state(arm_obj):
    raw = arm_obj.data.get(TRIM_PROP)
    return json.loads(raw) if raw else {}


def trim_tails(arm_obj, size):
    """Shortens the armature's overlong bones and records their original
    tails. Returns the trimmed bone names."""
    plan = plan_trims({b.name: b.length for b in arm_obj.data.bones}, size)
    if not plan:
        return []
    state = {}
    with edit_bones(arm_obj) as ebs:
        for name, length in plan.items():
            eb = ebs[name]
            original = list(eb.tail)
            eb.length = length
            state[name] = [original, list(eb.tail)]
    arm_obj.data[TRIM_PROP] = json.dumps(state)
    return sorted(plan)


def _pivot_state(arm_obj):
    raw = arm_obj.data.get(PIVOT_PROP)
    return json.loads(raw) if raw else {}


def _shift_keys(arm_obj, offsets):
    """Re-expresses bones' animation against rests moved by a bone-local
    offset t. basis' = T(-t) @ basis @ T(t) keeps rotation and scale, and
    location becomes loc + rot @ (scale * t) - t."""
    for action in rigfold._actions_for(arm_obj, set(offsets)):
        for bag in rigfold._channelbags(action):
            curves = rigfold._action_bone_curves(bag)
            for bone, offset in offsets.items():
                channels = curves.get(bone, {})
                quat_fcs = channels.get("rotation_quaternion", [])
                loc_fcs = channels.get("location", [])
                scale_fcs = [fc for fc in bag.fcurves
                             if fc.data_path == 'pose.bones["%s"].scale' % bone]
                scale_fcs.sort(key=lambda f: f.array_index)
                times = sorted({kp.co[0] for fc in quat_fcs + loc_fcs + scale_fcs
                                for kp in fc.keyframe_points})
                if not times:
                    continue
                locs = []
                for t in times:
                    rot = (Quaternion([fc.evaluate(t) for fc in quat_fcs])
                           if len(quat_fcs) == 4 else Quaternion())
                    loc = (Vector([fc.evaluate(t) for fc in loc_fcs])
                           if len(loc_fcs) == 3 else Vector((0, 0, 0)))
                    scale = (Vector([fc.evaluate(t) for fc in scale_fcs])
                             if len(scale_fcs) == 3 else Vector((1, 1, 1)))
                    locs.append(list(loc + (rot @ (scale * offset) - offset)))
                rigfold._write_channel(bag, bone, "location", loc_fcs, times, locs, 3)
    for name, offset in offsets.items():
        pb = arm_obj.pose.bones[name]
        pb.location = pb.location + (pb.matrix_basis.to_3x3() @ offset - offset)


def _set_pivots(arm_obj, state, from_index, to_index):
    """Moves each recorded pivot from one [head, tail] of its pair to the
    other and shifts its keys to match. Pivots the user has moved since are
    left alone."""
    bones = arm_obj.data.bones
    rests = {name: bones[name].matrix_local.copy() for name in state
             if name in bones}
    offsets = {}
    with edit_bones(arm_obj) as ebs:
        for name, ends in state.items():
            eb = ebs.get(name)
            if eb is None or [list(eb.head), list(eb.tail)] != ends[from_index]:
                continue
            head, tail = ends[to_index]
            offsets[name] = (rests[name].to_3x3().inverted()
                             @ (Vector(head) - Vector(ends[from_index][0])))
            eb.head = head
            eb.tail = tail
    if offsets:
        _shift_keys(arm_obj, offsets)


def move_pivots(arm_obj, size):
    """Moves the armature's pivot bones onto their child's joint, keeping
    their direction and length, and records where they were. Returns the
    moved bone names."""
    bones = arm_obj.data.bones
    plan = plan_pivots({b.name: tuple(b.head_local) for b in bones},
                       {b.name: b.parent.name if b.parent else None for b in bones},
                       size)
    if not plan:
        return []
    state = {}
    for name, child in plan.items():
        bone = bones[name]
        shift = bones[child].head_local - bone.head_local
        state[name] = [[list(bone.head_local), list(bone.tail_local)],
                       [list(bone.head_local + shift), list(bone.tail_local + shift)]]
    _set_pivots(arm_obj, state, 0, 1)
    arm_obj.data[PIVOT_PROP] = json.dumps(state)
    return sorted(plan)


def _set_tails(arm_obj, state, from_index, to_index):
    """Moves each recorded tail from one end of its [original, trimmed] pair
    to the other. Tails the user has moved since are left alone."""
    with edit_bones(arm_obj) as ebs:
        for name, tails in state.items():
            eb = ebs.get(name)
            if eb is not None and list(eb.tail) == tails[from_index]:
                eb.tail = tails[to_index]


@contextmanager
def full_tails_for_export():
    """Restores original tails and pivots around an export, then trims and
    moves them again."""
    armatures = [(o, _trim_state(o), _pivot_state(o)) for o in bpy.data.objects
                 if o.type == 'ARMATURE']
    for arm_obj, tails, pivots in armatures:
        _set_tails(arm_obj, tails, 1, 0)
        _set_pivots(arm_obj, pivots, 1, 0)
    try:
        yield
    finally:
        for arm_obj, tails, pivots in armatures:
            _set_pivots(arm_obj, pivots, 0, 1)
            _set_tails(arm_obj, tails, 0, 1)
