"""Tests for staged (eye) material layering.

Node-graph rewiring needs real link add/replace behavior, which MagicMock does
not model. The fakes below cover just enough of bpy.types.NodeTree: named
nodes, named sockets, and links where a new link into an occupied input
replaces the old one.
"""
import importlib
import sys
import types

import pytest


class FakeSocket:
    def __init__(self, node, name, identifier=None, is_output=False):
        self.node = node
        self.name = name
        self.identifier = identifier or name
        self.is_output = is_output


class FakeNode:
    _SOCKETS = {
        'BSDF_PRINCIPLED': (["Base Color", "Metallic", "Roughness"], ["BSDF"]),
        'TEX_IMAGE': (["Vector"], ["Color", "Alpha"]),
        'UVMAP': ([], ["UV"]),
        'OUTPUT_MATERIAL': (["Surface"], []),
    }

    def __init__(self, node_type, name):
        self.type = node_type
        self.name = name
        self.image = None
        self.uv_map = ""
        self.extension = 'REPEAT'
        self.location = (0.0, 0.0)
        self.label = ""
        if node_type == 'MIX':
            self.data_type = 'RGBA'
            ins = [FakeSocket(self, "Factor", "Factor_Float"),
                   FakeSocket(self, "Factor", "Factor_Vector"),
                   FakeSocket(self, "A", "A_Float"),
                   FakeSocket(self, "B", "B_Float"),
                   FakeSocket(self, "A", "A_Vector"),
                   FakeSocket(self, "B", "B_Vector"),
                   FakeSocket(self, "A", "A_Color"),
                   FakeSocket(self, "B", "B_Color")]
            outs = [FakeSocket(self, "Result", "Result_Float", True),
                    FakeSocket(self, "Result", "Result_Vector", True),
                    FakeSocket(self, "Result", "Result_Color", True)]
        else:
            in_names, out_names = self._SOCKETS[node_type]
            ins = [FakeSocket(self, n) for n in in_names]
            outs = [FakeSocket(self, n, is_output=True) for n in out_names]
        self.inputs = FakeSocketCollection(ins)
        self.outputs = FakeSocketCollection(outs)


class FakeSocketCollection:
    def __init__(self, sockets):
        self._sockets = sockets

    def __getitem__(self, key):
        if isinstance(key, int):
            return self._sockets[key]
        for s in self._sockets:
            if s.name == key:
                return s
        raise KeyError(key)

    def __iter__(self):
        return iter(self._sockets)


class FakeLink:
    def __init__(self, from_socket, to_socket):
        self.from_socket = from_socket
        self.to_socket = to_socket
        self.from_node = from_socket.node
        self.to_node = to_socket.node


class FakeLinks:
    def __init__(self):
        self._links = []

    def new(self, from_socket, to_socket):
        self._links = [l for l in self._links if l.to_socket is not to_socket]
        link = FakeLink(from_socket, to_socket)
        self._links.append(link)
        return link

    def remove(self, link):
        self._links.remove(link)

    def __iter__(self):
        return iter(self._links)


class FakeNodes:
    _TYPE_MAP = {
        'ShaderNodeTexImage': 'TEX_IMAGE',
        'ShaderNodeUVMap': 'UVMAP',
        'ShaderNodeMix': 'MIX',
    }

    def __init__(self):
        self._nodes = []

    def new(self, bl_idname):
        node = FakeNode(self._TYPE_MAP[bl_idname], bl_idname)
        base = node.name
        n = 1
        while self.get(node.name) is not None:
            node.name = "%s.%03d" % (base, n)
            n += 1
        self._nodes.append(node)
        return node

    def add(self, node):
        self._nodes.append(node)

    def get(self, name):
        for n in self._nodes:
            if n.name == name:
                return n
        return None

    def __iter__(self):
        return iter(self._nodes)


class FakeNodeTree:
    def __init__(self):
        self.nodes = FakeNodes()
        self.links = FakeLinks()


class FakeImage:
    def __init__(self, name):
        self.name = name


class FakeMaterial:
    def __init__(self, name, props=None):
        self.name = name
        self.use_nodes = True
        self.node_tree = FakeNodeTree()
        self._props = dict(props or {})

    def get(self, key, default=None):
        return self._props.get(key, default)

    def __setitem__(self, key, value):
        self._props[key] = value


class FakeUvLayer:
    def __init__(self, name):
        self.name = name


class FakeMesh:
    def __init__(self, uv_names):
        self.uv_layers = [FakeUvLayer(n) for n in uv_names]
        self.materials = []


class FakeSlot:
    def __init__(self, material):
        self.material = material


class FakeObject:
    def __init__(self, obj_type, materials=(), uv_names=()):
        self.type = obj_type
        self.material_slots = [FakeSlot(m) for m in materials]
        self.data = FakeMesh(uv_names) if obj_type == 'MESH' else None


class FakeImages:
    def __init__(self, names):
        self._images = {n: FakeImage(n) for n in names}

    def get(self, name):
        return self._images.get(name)


@pytest.fixture
def mock_bpy(monkeypatch):
    fake_bpy = types.ModuleType('bpy')
    fake_bpy.data = types.SimpleNamespace(images=FakeImages([]), materials=[])
    monkeypatch.setitem(sys.modules, 'bpy', fake_bpy)
    return fake_bpy


@pytest.fixture
def materials_module(mock_bpy):
    import io_shadowforge.materials
    return importlib.reload(io_shadowforge.materials)


def make_staged_material(name="np114_eye_r_01", stage0="np114_02"):
    """The node tree Blender's GLTF importer builds for a staged material:
    iris image (through a UV Map on layer 1) -> Principled Base Color."""
    mat = FakeMaterial(name, {"sfStage0Texture": stage0,
                              "sfStage2Texture": "np114_eyelid_l_0"})
    nt = mat.node_tree
    bsdf = FakeNode('BSDF_PRINCIPLED', "Principled BSDF")
    iris = FakeNode('TEX_IMAGE', "Image Texture")
    iris.image = FakeImage(name)
    uv1 = FakeNode('UVMAP', "UV Map")
    uv1.uv_map = "UVMap.001"
    nt.nodes.add(bsdf)
    nt.nodes.add(iris)
    nt.nodes.add(uv1)
    nt.links.new(uv1.outputs["UV"], iris.inputs["Vector"])
    nt.links.new(iris.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def link_into(nt, socket):
    for l in nt.links:
        if l.to_socket is socket:
            return l
    return None


def eye_object(mock_bpy, stage0="np114_02", images=("np114_02",)):
    mat = make_staged_material(stage0=stage0)
    mock_bpy.data.images = FakeImages(images)
    return mat, FakeObject('MESH', [mat], ["UVMap", "UVMap.001", "UVMap.002"])


def layered_material(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy)
    materials_module.layer_staged_materials([obj])
    mock_bpy.data.materials = [mat]
    return mat


def base_color_source(mat):
    nt = mat.node_tree
    return link_into(nt, nt.nodes.get("Principled BSDF").inputs["Base Color"]).from_node


def test_layer_builds_stage0_under_iris(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy)

    updated, warnings = materials_module.layer_staged_materials([obj])

    assert updated == [mat.name]
    assert warnings == []
    nt = mat.node_tree
    mix = nt.nodes.get(materials_module.MIX_NODE)
    tex0 = nt.nodes.get(materials_module.STAGE0_TEX_NODE)
    uv0 = nt.nodes.get(materials_module.STAGE0_UV_NODE)
    assert mix is not None and tex0 is not None and uv0 is not None
    assert tex0.image.name == "np114_02"
    assert uv0.uv_map == "UVMap"
    assert base_color_source(mat) is mix

    face = link_into(nt, materials_module._socket(mix.inputs, "A_Color"))
    iris = link_into(nt, materials_module._socket(mix.inputs, "B_Color"))
    factor = link_into(nt, materials_module._socket(mix.inputs, "Factor_Float"))
    assert face.from_node is tex0
    assert iris.from_node.image.name == mat.name
    assert factor.from_socket.name == "Alpha"
    assert factor.from_node is iris.from_node


def test_layer_twice_adds_one_mix(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy)

    materials_module.layer_staged_materials([obj])
    updated, warnings = materials_module.layer_staged_materials([obj])

    assert updated == []
    assert warnings == []
    mixes = [n for n in mat.node_tree.nodes if n.type == 'MIX']
    assert len(mixes) == 1


def test_layer_missing_stage0_image_warns_and_skips(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy, stage0="not_in_blend", images=())

    updated, warnings = materials_module.layer_staged_materials([obj])

    assert updated == []
    assert len(warnings) == 1
    assert "not_in_blend" in warnings[0]
    assert base_color_source(mat).type == 'TEX_IMAGE'


def test_layer_ignores_unstaged_materials(materials_module, mock_bpy):
    mat = FakeMaterial("np114_02")
    mat.node_tree.nodes.add(FakeNode('BSDF_PRINCIPLED', "Principled BSDF"))
    obj = FakeObject('MESH', [mat], ["UVMap"])

    updated, warnings = materials_module.layer_staged_materials([obj])

    assert updated == []
    assert warnings == []


def test_contract_layout_for_export_restores_after(materials_module, mock_bpy):
    mat = layered_material(materials_module, mock_bpy)

    with materials_module.contract_layout_for_export():
        iris = base_color_source(mat)
        assert iris.type == 'TEX_IMAGE'
        assert iris.image.name == mat.name
    assert base_color_source(mat) is mat.node_tree.nodes.get(materials_module.MIX_NODE)


def test_contract_layout_restores_on_exception(materials_module, mock_bpy):
    mat = layered_material(materials_module, mock_bpy)

    with pytest.raises(RuntimeError):
        with materials_module.contract_layout_for_export():
            raise RuntimeError("export failed")
    assert base_color_source(mat) is mat.node_tree.nodes.get(materials_module.MIX_NODE)


EYELID = "np114_eyelid_l_0"


def test_layer_puts_eyelid_over_face_and_iris(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy, images=("np114_02", EYELID))

    updated, warnings = materials_module.layer_staged_materials([obj])

    assert updated == [mat.name]
    assert warnings == []
    nt = mat.node_tree
    mix2 = nt.nodes.get(materials_module.STAGE2_MIX_NODE)
    tex2 = nt.nodes.get(materials_module.STAGE2_TEX_NODE)
    uv2 = nt.nodes.get(materials_module.STAGE2_UV_NODE)
    assert base_color_source(mat) is mix2
    assert tex2.image.name == EYELID
    assert tex2.extension == 'CLIP'
    assert uv2.uv_map == "UVMap.002"

    below = link_into(nt, materials_module._socket(mix2.inputs, "A_Color"))
    eyelid = link_into(nt, materials_module._socket(mix2.inputs, "B_Color"))
    factor = link_into(nt, materials_module._socket(mix2.inputs, "Factor_Float"))
    assert below.from_node is nt.nodes.get(materials_module.MIX_NODE)
    assert eyelid.from_node is tex2
    assert factor.from_node is tex2 and factor.from_socket.name == "Alpha"


def test_layer_adds_the_eyelid_once_its_image_arrives(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy)
    materials_module.layer_staged_materials([obj])
    mock_bpy.data.images = FakeImages(("np114_02", EYELID))

    updated, _ = materials_module.layer_staged_materials([obj])
    again, _ = materials_module.layer_staged_materials([obj])

    assert updated == [mat.name]
    assert again == []
    assert len([n for n in mat.node_tree.nodes if n.type == 'MIX']) == 2


def test_contract_layout_exports_iris_and_restores_eyelid(materials_module, mock_bpy):
    mat, obj = eye_object(mock_bpy, images=("np114_02", EYELID))
    materials_module.layer_staged_materials([obj])
    mock_bpy.data.materials = [mat]

    with materials_module.contract_layout_for_export():
        assert base_color_source(mat).image.name == mat.name
    assert base_color_source(mat) is mat.node_tree.nodes.get(materials_module.STAGE2_MIX_NODE)


def test_missing_stage2_images_lists_eyelids_not_in_the_blend(materials_module, mock_bpy):
    _, obj = eye_object(mock_bpy)

    assert materials_module.missing_stage2_images([obj]) == [EYELID]
    mock_bpy.data.images = FakeImages(("np114_02", EYELID))
    assert materials_module.missing_stage2_images([obj]) == []


def test_contract_layout_noop_without_layered_materials(materials_module, mock_bpy):
    mock_bpy.data.materials = [FakeMaterial("plain")]
    with materials_module.contract_layout_for_export():
        pass
