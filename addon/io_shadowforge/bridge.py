"""Runs the bundled sforge CLI and parses its output.

Imports no bpy, so it loads under plain CPython for unit tests.

sforge verbs print one of two outputs on stdout. Envelope verbs (called with
--json) print {ok, verb, outputs, warnings, error}. DTO verbs (entity list,
map list) print a bare JSON document and signal failure by exit code.
"""
from __future__ import annotations

import json
import queue
import subprocess
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Sequence

CREATE_NO_WINDOW = 0x08000000


class SforgeError(Exception):
    def __init__(self, message, *, exit_code=None, stderr="", error=None):
        super().__init__(message)
        self.exit_code = exit_code
        self.stderr = stderr
        self.error = error


@dataclass
class SforgeResult:
    ok: bool
    verb: str
    outputs: list
    warnings: list
    error: Optional[dict]
    stdout: str = ""
    stderr: str = ""


def resolve_exe(override: Optional[str], package_dir: Optional[Path] = None) -> Path:
    if override:
        p = Path(override).expanduser()
        if not p.exists():
            raise SforgeError("sforge override does not exist: " + str(p))
        return p
    base = package_dir or Path(__file__).resolve().parent
    exe = Path(base) / "bin" / "sforge.exe"
    if not exe.exists():
        raise SforgeError("bundled sforge not found: " + str(exe))
    return exe


def _flags(options: dict) -> list:
    out = []
    for key, val in options.items():
        if val is None or val is False:
            continue
        if val is True:
            out.append(key)
        else:
            out.extend([key, str(val)])
    return out


def build_args(verb: Sequence[str], positional: Sequence[str] = (),
               options: Optional[dict] = None, json_flag: bool = True) -> list:
    args = list(verb) + [str(p) for p in positional]
    if options:
        args += _flags(options)
    if json_flag:
        args.append("--json")
    return args


def _parse_json(text):
    text = (text or "").strip()
    if not text:
        return None
    return json.loads(text)


def envelope_result(code, stdout, stderr) -> SforgeResult:
    try:
        data = _parse_json(stdout)
    except json.JSONDecodeError:
        data = None
    if data is None:
        raise SforgeError("sforge produced no JSON result", exit_code=code, stderr=stderr)
    res = SforgeResult(
        ok=bool(data.get("ok")),
        verb=data.get("verb", ""),
        outputs=list(data.get("outputs") or []),
        warnings=list(data.get("warnings") or []),
        error=data.get("error"),
        stdout=stdout, stderr=stderr,
    )
    if not res.ok:
        err = res.error or {}
        raise SforgeError(err.get("message") or "sforge reported failure",
                          exit_code=code, stderr=stderr, error=res.error)
    return res


def dto_result(code, stdout, stderr) -> dict:
    if code != 0:
        raise SforgeError("sforge command failed", exit_code=code, stderr=stderr)
    try:
        data = _parse_json(stdout)
    except json.JSONDecodeError as ex:
        raise SforgeError("unparseable output: " + str(ex), exit_code=code, stderr=stderr)
    if data is None:
        raise SforgeError("empty output", exit_code=code, stderr=stderr)
    return data


def text_result(code, stdout, stderr) -> str:
    if code != 0:
        raise SforgeError("sforge command failed", exit_code=code, stderr=stderr)
    return stdout


_RESULT_PARSERS = {"envelope": envelope_result, "dto": dto_result, "text": text_result}


def _default_run(cmd, cwd, timeout):
    return subprocess.run(cmd, cwd=cwd, timeout=timeout, capture_output=True,
                          text=True, creationflags=CREATE_NO_WINDOW)


def run(exe, args, *, cwd=None, timeout=None, _run=_default_run):
    cmd = [str(exe)] + [str(a) for a in args]
    return _run(cmd, str(cwd) if cwd else None, timeout)


def run_envelope(exe, args, *, cwd=None, timeout=None, _run=_default_run) -> SforgeResult:
    cp = run(exe, args, cwd=cwd, timeout=timeout, _run=_run)
    return envelope_result(cp.returncode, cp.stdout, cp.stderr)


def run_dto(exe, args, *, cwd=None, timeout=None, _run=_default_run) -> dict:
    cp = run(exe, args, cwd=cwd, timeout=timeout, _run=_run)
    return dto_result(cp.returncode, cp.stdout, cp.stderr)


def export_entity_args(entity_id, workdir, *, game_root=None, fmt="glb",
                       anims=True, textures=True):
    opts = {"-o": workdir, "--format": fmt, "--game-root": game_root,
            "--no-anims": not anims, "--no-textures": not textures}
    return build_args(["entity", "export"], [entity_id], opts)


def export_entity(exe, entity_id, workdir, *, game_root=None, fmt="glb",
                  anims=True, textures=True, timeout=None, _run=_default_run) -> SforgeResult:
    return run_envelope(exe, export_entity_args(entity_id, workdir, game_root=game_root,
                                                fmt=fmt, anims=anims, textures=textures),
                        cwd=workdir, timeout=timeout, _run=_run)


def export_textures_args(entity_id, outdir, *, game_root=None):
    return build_args(["dds", "export"], [entity_id],
                      {"-o": outdir, "--game-root": game_root}, json_flag=False)


def export_textures(exe, entity_id, outdir, *, game_root=None, timeout=None,
                    _run=_default_run) -> str:
    """Writes every texture of an entity to outdir as <texture name>.png."""
    cp = run(exe, export_textures_args(entity_id, outdir, game_root=game_root),
             timeout=timeout, _run=_run)
    return text_result(cp.returncode, cp.stdout, cp.stderr)


def deploy_entity_args(entity_id, workdir, mod_name, *, enable=False, author=None,
                       game_root=None):
    opts = {"--from": workdir, "--mod": mod_name, "--enable": enable,
            "--author": author, "--game-root": game_root}
    return build_args(["entity", "deploy"], [entity_id], opts)


def deploy_entity(exe, entity_id, workdir, mod_name, *, enable=False, author=None,
                  game_root=None, timeout=None, _run=_default_run) -> SforgeResult:
    return run_envelope(exe, deploy_entity_args(entity_id, workdir, mod_name,
                                                enable=enable, author=author,
                                                game_root=game_root),
                        cwd=workdir, timeout=timeout, _run=_run)


def list_entities_args(*, game_root=None, category=None):
    opts = {"--game-root": game_root, "--category": category}
    return build_args(["entity", "list"], [], opts, json_flag=False)


def list_entities(exe, *, game_root=None, category=None, timeout=None, _run=_default_run) -> dict:
    return run_dto(exe, list_entities_args(game_root=game_root, category=category),
                   timeout=timeout, _run=_run)


def list_maps_args(*, game_root=None):
    return build_args(["map", "list"], [], {"--game-root": game_root}, json_flag=False)


def list_maps(exe, *, game_root=None, timeout=None, _run=_default_run) -> dict:
    return run_dto(exe, list_maps_args(game_root=game_root), timeout=timeout, _run=_run)


def export_map_args(stage_id, workdir, *, game_root=None, textures=True):
    opts = {"-o": workdir, "--game-root": game_root, "--no-textures": not textures}
    return build_args(["map", "export"], [stage_id], opts)


def export_map(exe, stage_id, workdir, *, game_root=None, textures=True,
               timeout=None, _run=_default_run) -> SforgeResult:
    return run_envelope(exe, export_map_args(stage_id, workdir, game_root=game_root,
                                             textures=textures),
                        cwd=workdir, timeout=timeout, _run=_run)


class SforgeJob:
    """Runs a command on a worker thread while the main thread polls
    drain_progress() and done.

    mode picks the stdout parser: "envelope", "dto" or "text". `done` is set
    after result/error, so a reader that sees done == True also sees them.
    """

    def __init__(self, exe, args, *, cwd=None, mode="envelope", _popen=subprocess.Popen):
        if mode not in _RESULT_PARSERS:
            raise ValueError("mode must be envelope|dto|text")
        self._cmd = [str(exe)] + [str(a) for a in args]
        self._cwd = str(cwd) if cwd else None
        self._parse = _RESULT_PARSERS[mode]
        self._popen = _popen
        self._proc = None
        self._canceled = False
        self._q = queue.Queue()
        self._thread = None
        self.done = False
        self.result = None
        self.error = None

    def start(self):
        self._thread = threading.Thread(target=self._worker, daemon=True)
        self._thread.start()

    def join(self, timeout=None):
        if self._thread is not None:
            self._thread.join(timeout)

    def cancel(self):
        self._canceled = True
        proc = self._proc
        if proc is not None and proc.poll() is None:
            proc.terminate()

    def drain_progress(self):
        lines = []
        try:
            while True:
                lines.append(self._q.get_nowait())
        except queue.Empty:
            pass
        return lines

    def _worker(self):
        """Streams stderr lines as progress while a helper thread drains
        stdout. Reading one pipe to EOF before the other deadlocks once the
        child fills the OS pipe buffer on the unread one."""
        try:
            proc = self._popen(self._cmd, cwd=self._cwd, stdout=subprocess.PIPE,
                               stderr=subprocess.PIPE, text=True,
                               creationflags=CREATE_NO_WINDOW)
            self._proc = proc
            stdout_chunks = []

            def _drain_stdout():
                if proc.stdout is not None:
                    stdout_chunks.append(proc.stdout.read())

            out_thread = threading.Thread(target=_drain_stdout, daemon=True)
            out_thread.start()
            stderr_lines = []
            for line in proc.stderr:
                line = line.rstrip("\n")
                stderr_lines.append(line)
                self._q.put(line)
            out_thread.join()
            code = proc.wait()
            self._finish(code, "".join(stdout_chunks), "\n".join(stderr_lines))
        except Exception as ex:
            self.error = SforgeError(str(ex))
            self.done = True

    def _finish(self, code, stdout, stderr):
        if self._canceled:
            self.error = SforgeError("canceled", exit_code=code, stderr=stderr)
        else:
            try:
                self.result = self._parse(code, stdout, stderr)
            except SforgeError as ex:
                self.error = ex
        self.done = True
