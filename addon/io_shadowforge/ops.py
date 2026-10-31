"""ShadowForge operators: refresh the asset catalog, import a character or
map, and deploy an edited character to a mod.

invoke() runs sforge as a SforgeJob polled from a modal timer, so the UI stays
live. execute() is the blocking path for headless and scripted callers, since
a modal operator needs a window and a timer.
"""
import re

import bpy

from . import bridge
from . import library
from . import scene

_TIMER_INTERVAL = 0.25

# sforge progress lines carry "i/N" counters ("baking model 3/24: x"), shown
# as a percentage so the status bar reads as a loading bar.
_FRACTION_RE = re.compile(r"\b(\d+)\s*/\s*(\d+)\b")


def _status_line(line):
    m = _FRACTION_RE.search(line)
    if m and int(m.group(2)) > 0:
        pct = 100 * int(m.group(1)) // int(m.group(2))
        return "ShadowForge: [%3d%%] %s" % (min(pct, 100), line)
    return "ShadowForge: " + line


def _prefs(context):
    return context.preferences.addons[__package__].preferences


def _game_root(context):
    return _prefs(context).game_root or None


def _selected_asset_id(context):
    asset = getattr(context, "asset", None)
    return asset.metadata.get("sf_entity_id") if asset else None


def _warning_text(w):
    return w.get("message", str(w)) if isinstance(w, dict) else str(w)


class _JobModal:
    """Modal plumbing shared by the operators: a status-bar timer, ESC to
    cancel the running SforgeJob, and progress lines from its stderr."""

    def _resolve_exe(self, context):
        """Returns the sforge path, or None after reporting the error."""
        try:
            return bridge.resolve_exe(_prefs(context).sforge_path or None)
        except bridge.SforgeError as ex:
            self.report({'ERROR'}, str(ex))
            return None

    def _begin_modal(self, context, status):
        wm = context.window_manager
        self._timer = wm.event_timer_add(_TIMER_INTERVAL, window=context.window)
        wm.modal_handler_add(self)
        context.workspace.status_text_set(status)
        return {'RUNNING_MODAL'}

    def _poll_job(self, context, event):
        """Returns the modal result to hand back while self._job runs, or
        None once it is done."""
        if event.type == 'ESC':
            self._job.cancel()
        if event.type != 'TIMER':
            return {'PASS_THROUGH'}
        for line in self._job.drain_progress():
            context.workspace.status_text_set(_status_line(line))
        if not self._job.done:
            return {'RUNNING_MODAL'}
        return None

    def _teardown(self, context):
        context.workspace.status_text_set(None)
        if getattr(self, "_timer", None) is not None:
            context.window_manager.event_timer_remove(self._timer)
            self._timer = None


def _report_catalog(op, summary):
    if summary.get("map_list_error"):
        op.report({'WARNING'}, "map list unavailable: " + summary["map_list_error"])
    unavailable = summary.get("maps_unavailable") or []
    op.report({'INFO'}, "ShadowForge catalog: %d characters, %d maps (%d unavailable)" % (
        summary.get("characters", 0), summary.get("maps", 0), len(unavailable)))
    if unavailable:
        op.report({'WARNING'}, "unavailable maps: " + ", ".join(unavailable))


class SF_OT_refresh_catalog(_JobModal, bpy.types.Operator):
    """Runs three jobs in turn: LIST (entity list), MAPS (map list) and GEN
    (background Blender library generation). A failed MAPS job is reported as
    a warning and GEN proceeds with entities only, as library.refresh() does."""
    bl_idname = "shadowforge.refresh_catalog"
    bl_label = "Refresh Character Catalog"
    bl_description = "Enumerate game characters and (re)build the Asset Browser library"
    bl_options = {"REGISTER", "UNDO"}

    def invoke(self, context, event):
        self._exe = self._resolve_exe(context)
        if self._exe is None:
            return {'CANCELLED'}
        self._game_root = _game_root(context)
        self._entity_dto = None
        self._summary = None
        self._out_dir = None
        self._start_job("LIST", self._exe,
                        bridge.list_entities_args(game_root=self._game_root), "dto")
        return self._begin_modal(context, "ShadowForge: listing characters...")

    def _start_job(self, state, exe, args, mode):
        self._state = state
        self._job = bridge.SforgeJob(exe, args, mode=mode)
        self._job.start()

    def _fail(self, context, message):
        self._teardown(context)
        self.report({'ERROR'}, message)
        return {'CANCELLED'}

    def modal(self, context, event):
        pending = self._poll_job(context, event)
        if pending is not None:
            return pending

        if self._state == "LIST":
            if self._job.error is not None:
                return self._fail(context, str(self._job.error))
            self._entity_dto = self._job.result
            context.workspace.status_text_set("ShadowForge: listing maps...")
            try:
                self._start_job("MAPS", self._exe,
                                bridge.list_maps_args(game_root=self._game_root), "dto")
            except Exception as ex:
                return self._fail(context, "catalog refresh failed: " + str(ex))
            return {'RUNNING_MODAL'}

        if self._state == "MAPS":
            if self._job.error is not None:
                self.report({'WARNING'}, "map list unavailable: " + str(self._job.error))
                map_dto = None
            else:
                map_dto = self._job.result
            context.workspace.status_text_set("ShadowForge: generating library...")
            try:
                self._out_dir = library.library_dir()
                dto_path, self._summary = library.write_generation_input(self._entity_dto, map_dto)
                gen_exe, gen_cmd_args = library.gen_args(dto_path, self._out_dir)
                self._start_job("GEN", gen_exe, gen_cmd_args, "text")
            except Exception as ex:
                return self._fail(context, "catalog refresh failed: " + str(ex))
            return {'RUNNING_MODAL'}

        if self._job.error is not None:
            return self._fail(context, str(self._job.error))
        self._teardown(context)
        try:
            library.register_library(self._out_dir)
        except Exception as ex:
            self.report({'ERROR'}, "catalog refresh failed: " + str(ex))
            return {'CANCELLED'}
        _report_catalog(self, self._summary)
        return {'FINISHED'}

    def execute(self, context):
        exe = self._resolve_exe(context)
        if exe is None:
            return {'CANCELLED'}
        try:
            summary = library.refresh(exe, _game_root(context))
        except bridge.SforgeError as ex:
            self.report({'ERROR'}, str(ex))
            return {'CANCELLED'}
        except Exception as ex:
            self.report({'ERROR'}, "catalog refresh failed: " + str(ex))
            return {'CANCELLED'}
        _report_catalog(self, summary)
        return {'FINISHED'}


class _ImportFromAsset(_JobModal):
    """Exports the selected Asset Browser asset with sforge, then imports the
    result. The Blender import runs one timer tick after the job finishes, so
    the status bar redraws before the import blocks the UI."""
    _kind = ""

    def _export_args(self, context, asset_id, workdir):
        raise NotImplementedError

    def _export_blocking(self, context, exe, asset_id, workdir):
        raise NotImplementedError

    def _import(self, context, workdir, asset_id, result):
        raise NotImplementedError

    def _report_import(self, result, imported):
        raise NotImplementedError

    def _no_selection(self):
        self.report({'ERROR'}, "select a ShadowForge %s asset first" % self._kind)
        return {'CANCELLED'}

    def invoke(self, context, event):
        self._asset_id = _selected_asset_id(context)
        if not self._asset_id:
            return self._no_selection()
        exe = self._resolve_exe(context)
        if exe is None:
            return {'CANCELLED'}
        self._workdir = scene.fresh_workdir(self._asset_id)
        args = self._export_args(context, self._asset_id, self._workdir)
        self._job = bridge.SforgeJob(exe, args, cwd=self._workdir)
        self._job.start()
        self._pending_import = False
        return self._begin_modal(context, "ShadowForge: exporting " + self._asset_id + "...")

    def modal(self, context, event):
        if self._pending_import:
            if event.type == 'ESC':
                self._teardown(context)
                self.report({'WARNING'}, "import of " + self._asset_id + " canceled")
                return {'CANCELLED'}
            if event.type != 'TIMER':
                return {'PASS_THROUGH'}
            self._teardown(context)
            try:
                imported = self._import(context, self._workdir, self._asset_id, self._job.result)
            except Exception as ex:
                self.report({'ERROR'}, "import failed: " + str(ex))
                return {'CANCELLED'}
            self._report_import(self._job.result, imported)
            return {'FINISHED'}
        pending = self._poll_job(context, event)
        if pending is not None:
            return pending
        if self._job.error is not None:
            self._teardown(context)
            self.report({'ERROR'}, str(self._job.error))
            return {'CANCELLED'}
        self._pending_import = True
        context.workspace.status_text_set(
            "ShadowForge: importing " + self._asset_id + " into Blender (UI will pause)...")
        return {'RUNNING_MODAL'}

    def execute(self, context):
        self._asset_id = _selected_asset_id(context)
        if not self._asset_id:
            return self._no_selection()
        exe = self._resolve_exe(context)
        if exe is None:
            return {'CANCELLED'}
        workdir = scene.fresh_workdir(self._asset_id)
        try:
            res = self._export_blocking(context, exe, self._asset_id, workdir)
            imported = self._import(context, workdir, self._asset_id, res)
        except bridge.SforgeError as ex:
            self.report({'ERROR'}, str(ex))
            return {'CANCELLED'}
        except Exception as ex:
            self.report({'ERROR'}, "import failed: " + str(ex))
            return {'CANCELLED'}
        self._report_import(res, imported)
        return {'FINISHED'}


class SF_OT_import_asset(_ImportFromAsset, bpy.types.Operator):
    bl_idname = "shadowforge.import_asset"
    bl_label = "Import Character"
    bl_description = "Import the selected Asset Browser character for editing"
    bl_options = {"REGISTER", "UNDO"}
    _kind = "character"

    @classmethod
    def poll(cls, context):
        asset = getattr(context, "asset", None)
        if asset is None or asset.metadata.get("sf_entity_id") is None:
            return False
        return asset.metadata.get("sf_category", "chara") == "chara"

    def _export_args(self, context, asset_id, workdir):
        props = context.scene.sf
        return bridge.export_entity_args(
            asset_id, workdir, game_root=_game_root(context),
            anims=props.include_animations, textures=props.include_textures)

    def _export_blocking(self, context, exe, asset_id, workdir):
        props = context.scene.sf
        return bridge.export_entity(exe, asset_id, workdir, game_root=_game_root(context),
                                    anims=props.include_animations,
                                    textures=props.include_textures)

    def _import(self, context, workdir, asset_id, result):
        exe = None
        if context.scene.sf.include_textures:
            exe = bridge.resolve_exe(_prefs(context).sforge_path or None)
        _, warnings = scene.import_entity(workdir, asset_id, result,
                                          exe=exe, game_root=_game_root(context))
        return warnings

    def _report_import(self, result, imported):
        for w in imported:
            self.report({'WARNING'}, w)
        for w in result.warnings:
            self.report({'WARNING'}, _warning_text(w))
        self.report({'INFO'}, "imported " + self._asset_id)


class SF_OT_import_map(_ImportFromAsset, bpy.types.Operator):
    bl_idname = "shadowforge.import_map"
    bl_label = "Import Map"
    bl_description = "Import the selected Asset Browser map for editing"
    bl_options = {"REGISTER", "UNDO"}
    _kind = "map"

    @classmethod
    def poll(cls, context):
        asset = getattr(context, "asset", None)
        return (asset is not None
                and asset.metadata.get("sf_entity_id") is not None
                and asset.metadata.get("sf_category") == "map")

    def _export_args(self, context, asset_id, workdir):
        return bridge.export_map_args(asset_id, workdir, game_root=_game_root(context),
                                      textures=context.scene.sf.include_textures)

    def _export_blocking(self, context, exe, asset_id, workdir):
        return bridge.export_map(exe, asset_id, workdir, game_root=_game_root(context),
                                 textures=context.scene.sf.include_textures)

    def _import(self, context, workdir, asset_id, result):
        return scene.import_map(context, workdir, asset_id, result)

    def _report_import(self, result, imported):
        man = imported
        n_ok = sum(1 for m in man.models if m.exported)
        n_missing = len(man.models) - n_ok
        if n_missing:
            self.report({'WARNING'}, "%d model(s) missing from region pack" % n_missing)
        if man.skipped:
            self.report({'WARNING'}, "%d sidecar(s) not imported (v1: geometry+textures only)" % len(man.skipped))
        self.report({'INFO'}, "imported %s: %d models" % (self._asset_id, n_ok))


class SF_OT_deploy_asset(_JobModal, bpy.types.Operator):
    """The scene GLTF export runs on the main thread and can block for minutes
    on animation-heavy characters, so invoke() defers it to the first timer
    tick and the status bar redraws first. Only the sforge deploy runs as a job."""
    bl_idname = "shadowforge.deploy_asset"
    bl_label = "Deploy to Mod"
    bl_description = "Cook and pack the active character's edits into a mod override tree"
    bl_options = {"REGISTER", "UNDO"}

    @classmethod
    def poll(cls, context):
        obj = context.active_object
        return (obj is not None and obj.get("sf_id") is not None
                and obj.get("sf_category") != "map")

    def _target(self, context):
        """Returns (entity_id, workdir). Reuses the directory the object was
        imported from, so deploy reads the same tree the user edited."""
        obj = context.active_object
        entity_id = obj.get("sf_id") if obj is not None else None
        if not entity_id:
            return None, None
        return entity_id, obj.get("sf_workdir") or scene.fresh_workdir(entity_id)

    def _no_target(self):
        self.report({'ERROR'}, "select an imported ShadowForge character first")
        return {'CANCELLED'}

    def _deploy_options(self, context):
        props = context.scene.sf
        return dict(enable=props.enable_mod, author=(_prefs(context).default_author or None),
                    game_root=_game_root(context))

    def invoke(self, context, event):
        self._entity_id, self._workdir = self._target(context)
        if not self._entity_id:
            return self._no_target()
        self._exe = self._resolve_exe(context)
        if self._exe is None:
            return {'CANCELLED'}
        self._args = bridge.deploy_entity_args(self._entity_id, self._workdir,
                                               context.scene.sf.mod_name,
                                               **self._deploy_options(context))
        self._job = None
        return self._begin_modal(
            context, "ShadowForge: exporting scene for " + self._entity_id + " (UI will pause)...")

    def modal(self, context, event):
        if self._job is None:
            if event.type == 'ESC':
                self._teardown(context)
                self.report({'WARNING'}, "deploy of " + self._entity_id + " canceled")
                return {'CANCELLED'}
            if event.type != 'TIMER':
                return {'PASS_THROUGH'}
            try:
                scene.export_scene_glb(self._workdir, self._entity_id)
                self._job = bridge.SforgeJob(self._exe, self._args, cwd=self._workdir)
                self._job.start()
            except Exception as ex:
                self._teardown(context)
                self.report({'ERROR'}, "scene export failed: " + str(ex))
                return {'CANCELLED'}
            context.workspace.status_text_set("ShadowForge: deploying " + self._entity_id + "...")
            return {'RUNNING_MODAL'}
        pending = self._poll_job(context, event)
        if pending is not None:
            return pending
        self._teardown(context)
        if self._job.error is not None:
            self.report({'ERROR'}, str(self._job.error))
            return {'CANCELLED'}
        self._report_result(self._job.result)
        return {'FINISHED'}

    def execute(self, context):
        entity_id, workdir = self._target(context)
        if not entity_id:
            return self._no_target()
        exe = self._resolve_exe(context)
        if exe is None:
            return {'CANCELLED'}
        try:
            res = scene.deploy_blocking(entity_id, workdir, context.scene.sf.mod_name, exe,
                                        **self._deploy_options(context))
        except bridge.SforgeError as ex:
            self.report({'ERROR'}, str(ex))
            return {'CANCELLED'}
        except Exception as ex:
            self.report({'ERROR'}, "deploy failed: " + str(ex))
            return {'CANCELLED'}
        self._report_result(res)
        return {'FINISHED'}

    def _report_result(self, res):
        mod_dir = res.outputs[0] if res.outputs else None
        for w in res.warnings:
            self.report({'WARNING'}, _warning_text(w))
        self.report({'INFO'}, "deployed to " + (mod_dir or "(unknown mod dir)"))


_CLASSES = (SF_OT_refresh_catalog, SF_OT_import_asset, SF_OT_import_map, SF_OT_deploy_asset)


def register():
    for cls in _CLASSES:
        bpy.utils.register_class(cls)


def unregister():
    for cls in reversed(_CLASSES):
        bpy.utils.unregister_class(cls)
