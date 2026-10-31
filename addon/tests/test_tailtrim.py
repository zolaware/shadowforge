"""Tests for tailtrim's trim planning. Edit-bone changes and the export
restore need Blender and are checked by smoke/headless_tailtrim.py."""
import importlib
import sys
import types

import pytest


@pytest.fixture
def tailtrim(monkeypatch, stub_mathutils):
    monkeypatch.setitem(sys.modules, 'bpy', types.ModuleType('bpy'))
    import io_shadowforge.rigfold
    importlib.reload(io_shadowforge.rigfold)
    import io_shadowforge.tailtrim
    return importlib.reload(io_shadowforge.tailtrim)


def test_plan_trims_pc02(tailtrim):
    # pc02 (Jiro) after a FORTUNE import: mesh size 17.0, median bone 0.634.
    lengths = {
        "referenceShape": 9.582,
        "hips": 9.582,
        "neck": 13.806,
        "head_pointShape": 13.766,
        "leftupleg": 4.1,
        "spine": 0.634,
        "head": 0.5,
    }
    lengths.update({"finger%d" % i: 0.3 for i in range(5)})
    plan = tailtrim.plan_trims(lengths, 17.0)
    assert set(plan) == {"referenceShape", "hips", "neck", "head_pointShape"}
    assert all(length == 0.634 for length in plan.values())


def test_plan_trims_keeps_limbs(tailtrim):
    # np209 (King Jibral): the longest limb is 0.13 of the model.
    lengths = {"spine": 6.191, "rightarm": 5.661, "tail": 2.768, "tin": 1.679}
    assert tailtrim.plan_trims(lengths, 47.29) == {}


def test_plan_trims_without_meshes(tailtrim):
    assert tailtrim.plan_trims({"referenceShape": 9.5}, 0.0) == {}
    assert tailtrim.plan_trims({}, 10.0) == {}


def test_plan_pivots_pc02(tailtrim):
    # pc02 (Jiro): head_pointShape sits 13.8 units to the side of neck and head.
    heads = {
        "spine1": (0.0, 0.2, 11.5),
        "neck": (0.0, 0.22, 12.74),
        "head_pointShape": (13.77, 0.0, 13.77),
        "head": (0.0, -0.01, 13.77),
        "nose": (0.0, -0.9, 14.0),
    }
    parents = {"spine1": None, "neck": "spine1", "head_pointShape": "neck",
               "head": "head_pointShape", "nose": "head"}
    assert tailtrim.plan_pivots(heads, parents, 17.0) == {"head_pointShape": "head"}


def test_plan_pivots_pc05(tailtrim):
    # pc05 (Zola): head_pointShape sits at the origin, between the feet.
    heads = {"neck": (0.0, 0.6, 14.94), "head_pointShape": (0.0, 0.0, 0.0),
             "head": (0.0, 0.48, 15.63)}
    parents = {"neck": None, "head_pointShape": "neck", "head": "head_pointShape"}
    assert tailtrim.plan_pivots(heads, parents, 17.0) == {"head_pointShape": "head"}


def test_plan_pivots_keeps_chains_and_roots(tailtrim):
    # A long thigh is near its child's head, and a root has no parent.
    heads = {"referenceShape": (0.0, 0.0, 0.0), "hips": (0.0, 0.0, 11.0),
             "leftupleg": (0.8, 0.2, 9.4), "leftleg": (0.8, 0.1, 5.6)}
    parents = {"referenceShape": None, "hips": "referenceShape",
               "leftupleg": "hips", "leftleg": "leftupleg"}
    assert tailtrim.plan_pivots(heads, parents, 17.0) == {}
    assert tailtrim.plan_pivots(heads, parents, 0.0) == {}
