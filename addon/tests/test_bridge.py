import io as _io
import json
import sys
import types

import pytest

import bridge


def _cp(returncode=0, stdout="", stderr=""):
    return types.SimpleNamespace(returncode=returncode, stdout=stdout, stderr=stderr)


def test_build_args_positional_options_and_json():
    args = bridge.build_args(["entity", "export"], ["pc01"],
                             {"-o": "C:/wd", "--format": "glb", "--game-root": None})
    assert args == ["entity", "export", "pc01", "-o", "C:/wd", "--format", "glb", "--json"]


def test_build_args_bool_flag_and_no_json():
    args = bridge.build_args(["entity", "resolve"], ["pc01"],
                             {"--enable": True, "--author": None}, json_flag=False)
    assert args == ["entity", "resolve", "pc01", "--enable"]


def test_resolve_exe_override_missing_raises(tmp_path):
    with pytest.raises(bridge.SforgeError):
        bridge.resolve_exe(str(tmp_path / "nope.exe"))


def test_resolve_exe_override_present(tmp_path):
    exe = tmp_path / "sforge.exe"
    exe.write_text("x")
    assert bridge.resolve_exe(str(exe)) == exe


def test_resolve_exe_bundled(tmp_path):
    (tmp_path / "bin").mkdir()
    exe = tmp_path / "bin" / "sforge.exe"
    exe.write_text("x")
    assert bridge.resolve_exe(None, package_dir=tmp_path) == exe


def test_run_envelope_success():
    payload = {"ok": True, "verb": "entity.export",
               "outputs": ["a.glb", "a.sfmod.json"],
               "warnings": [{"code": "w", "message": "m"}], "error": None}
    fake = lambda cmd, cwd, timeout: _cp(0, json.dumps(payload), "log line\n")
    res = bridge.run_envelope("sforge", ["entity", "export", "pc01", "--json"], _run=fake)
    assert res.ok and res.verb == "entity.export"
    assert res.outputs == ["a.glb", "a.sfmod.json"]
    assert res.warnings == [{"code": "w", "message": "m"}]


def test_run_envelope_failure_raises_with_error():
    payload = {"ok": False, "verb": "entity.import", "outputs": [],
               "warnings": [], "error": {"code": "import", "message": "boom"}}
    fake = lambda cmd, cwd, timeout: _cp(1, json.dumps(payload), "err\n")
    with pytest.raises(bridge.SforgeError) as ei:
        bridge.run_envelope("sforge", ["entity", "import", "--json"], _run=fake)
    assert "boom" in str(ei.value)
    assert ei.value.error == {"code": "import", "message": "boom"}


def test_run_envelope_no_json_raises():
    fake = lambda cmd, cwd, timeout: _cp(1, "", "crash\n")
    with pytest.raises(bridge.SforgeError) as ei:
        bridge.run_envelope("sforge", ["x"], _run=fake)
    assert ei.value.stderr == "crash\n"


def test_run_dto_success_and_failure():
    dto = {"entity": {"id": "pc01"}, "install": {"source": "Install", "modsAvailable": True}}
    ok = lambda cmd, cwd, timeout: _cp(0, json.dumps(dto), "")
    assert bridge.run_dto("sforge", ["entity", "resolve", "pc01"], _run=ok)["install"]["source"] == "Install"
    bad = lambda cmd, cwd, timeout: _cp(1, "", "not found\n")
    with pytest.raises(bridge.SforgeError):
        bridge.run_dto("sforge", ["entity", "resolve", "zz"], _run=bad)


def test_export_entity_builds_expected_argv(monkeypatch):
    seen = {}
    def fake(cmd, cwd, timeout):
        seen["cmd"] = cmd
        return _cp(0, json.dumps({"ok": True, "verb": "entity.export",
                                  "outputs": [], "warnings": [], "error": None}), "")
    bridge.export_entity("sforge.exe", "pc01", "C:/wd", game_root=None, _run=fake)
    assert seen["cmd"][:4] == ["sforge.exe", "entity", "export", "pc01"]
    assert "--json" in seen["cmd"] and "-o" in seen["cmd"]
    assert "--game-root" not in seen["cmd"]


def test_list_entities_builds_expected_argv_and_returns_dto():
    seen = {}
    def fake(cmd, cwd, timeout):
        seen["cmd"] = cmd
        return _cp(0, json.dumps({"entities": [
            {"id": "pc01", "category": "chara", "class": "ply",
             "modelDefPath": "database\\model\\chara\\ply\\model_pc01.mdl",
             "displayName": "pc01"}]}), "")
    dto = bridge.list_entities("sforge.exe", game_root=None, _run=fake)
    assert seen["cmd"][:3] == ["sforge.exe", "entity", "list"]
    assert "--json" not in seen["cmd"]
    assert dto["entities"][0]["id"] == "pc01"


def test_run_real_subprocess_roundtrip():
    script = ("import sys;"
              "sys.stdout.write('{\"ok\": true, \"verb\": \"t\", \"outputs\": [], "
              "\"warnings\": [], \"error\": null}');"
              "sys.stderr.write('progress\\n')")
    cp = bridge.run(sys.executable, ["-c", script])
    assert '"ok"' in cp.stdout and cp.returncode == 0


class _FakePopen:
    def __init__(self, returncode, stdout_text, stderr_lines):
        self.returncode = returncode
        self.stdout = _io.StringIO(stdout_text)
        self.stderr = iter(stderr_lines)

    def wait(self):
        return self.returncode


def _popen_factory(returncode, stdout_text, stderr_lines):
    def factory(cmd, **kwargs):
        return _FakePopen(returncode, stdout_text, stderr_lines)
    return factory


def test_sforgejob_success_streams_progress_and_result():
    payload = {"ok": True, "verb": "entity.export", "outputs": ["a.glb"],
               "warnings": [], "error": None}
    job = bridge.SforgeJob("sforge", ["entity", "export", "pc01", "--json"],
                           _popen=_popen_factory(0, json.dumps(payload),
                                                 ["step 1\n", "step 2\n"]))
    job.start()
    job.join(timeout=5)
    assert job.done and job.error is None
    assert job.result.ok and job.result.outputs == ["a.glb"]
    assert _drain_all(job) == ["step 1", "step 2"]


def _drain_all(job):
    out = []
    while True:
        chunk = job.drain_progress()
        if not chunk:
            break
        out.extend(chunk)
    return out


def test_sforgejob_failure_sets_error():
    payload = {"ok": False, "verb": "entity.import", "outputs": [],
               "warnings": [], "error": {"code": "import", "message": "nope"}}
    job = bridge.SforgeJob("sforge", ["entity", "import", "--json"],
                           _popen=_popen_factory(1, json.dumps(payload), []))
    job.start()
    job.join(timeout=5)
    assert job.done and job.result is None
    assert isinstance(job.error, bridge.SforgeError) and "nope" in str(job.error)


def test_sforgejob_no_json_sets_error():
    job = bridge.SforgeJob("sforge", ["x", "--json"],
                           _popen=_popen_factory(139, "", ["crash\n"]))
    job.start()
    job.join(timeout=5)
    assert job.done and job.error is not None and job.result is None


def test_sforgejob_large_stdout_no_deadlock():
    # The stdout envelope is far larger than the OS pipe buffer (~64KB), so
    # reading stderr to EOF first would block both processes.
    script =("import sys, json; big = 'A' * 200000; "
              "sys.stderr.write('progress\\n'); "
              "sys.stdout.write(json.dumps({'ok': True, 'verb': 't', "
              "'outputs': [big], 'warnings': [], 'error': None}))")
    job = bridge.SforgeJob(sys.executable, ["-c", script])
    job.start()
    job.join(timeout=20)
    assert job.done, "SforgeJob deadlocked on large stdout"
    assert job.error is None and job.result.ok
    assert len(job.result.outputs[0]) == 200000


def test_export_entity_args_include_selective_flags():
    args = bridge.export_entity_args("pc01", "C:/wk", game_root="G:/game",
                                     anims=False, textures=False)
    assert args[:3] == ["entity", "export", "pc01"]
    assert "--no-anims" in args and "--no-textures" in args
    assert "--json" in args


def test_export_entity_args_default_flags_absent():
    args = bridge.export_entity_args("pc01", "C:/wk")
    assert "--no-anims" not in args and "--no-textures" not in args


def test_list_maps_builds_map_list_verb():
    seen = {}
    def fake(cmd, cwd, timeout):
        seen["cmd"] = cmd
        return _cp(0, '{"stages": []}')
    bridge.list_maps("sforge.exe", game_root="G:/game", _run=fake)
    assert seen["cmd"][1:3] == ["map", "list"]
    assert "--json" not in seen["cmd"]


def test_export_map_args_order_and_flags():
    args = bridge.export_map_args("bg01_01", "C:/wk", textures=False)
    assert args[:3] == ["map", "export", "bg01_01"]
    assert "--no-textures" in args and "--json" in args


def test_job_text_mode_success():
    job = bridge.SforgeJob("x.exe", ["map", "list"], mode="text",
                           _popen=_popen_factory(0, "GEN-OK 3\n", []))
    job.start()
    job.join(5)
    assert job.done and job.error is None
    assert "GEN-OK 3" in job.result


def test_job_text_mode_failure_sets_error():
    job = bridge.SforgeJob("x.exe", ["boom"], mode="text",
                           _popen=_popen_factory(2, "", []))
    job.start()
    job.join(5)
    assert job.done and job.error is not None


def test_export_textures_args_builds_dds_export_without_json():
    args = bridge.export_textures_args("pc04", "C:/wk/textures", game_root="G:/game")
    assert args == ["dds", "export", "pc04", "-o", "C:/wk/textures", "--game-root", "G:/game"]


def test_export_textures_failure_raises():
    def fake_run(cmd, cwd, timeout):
        return _cp(returncode=1, stderr="no such entity")
    with pytest.raises(bridge.SforgeError):
        bridge.export_textures("sforge.exe", "pc99", "C:/wk", _run=fake_run)


def test_deploy_entity_args_order_and_flags():
    args = bridge.deploy_entity_args("pc01", "C:/wk", "my-mod", enable=True,
                                     author="Tom", game_root="G:/game")
    assert args[:3] == ["entity", "deploy", "pc01"]
    assert "--from" in args and "C:/wk" in args
    assert "--mod" in args and "my-mod" in args
    assert "--enable" in args
    assert "--author" in args and "Tom" in args
    assert "--game-root" in args and "G:/game" in args
    assert "--json" in args


def test_deploy_entity_args_omits_falsy_options():
    args = bridge.deploy_entity_args("pc01", "C:/wk", "my-mod")
    assert "--enable" not in args
    assert "--author" not in args
    assert "--game-root" not in args
