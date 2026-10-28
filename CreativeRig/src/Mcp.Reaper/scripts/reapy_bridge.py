#!/usr/bin/env python3
"""
reapy_bridge.py - Thin CLI bridge between Mcp.Reaper (C#) and REAPER via
python-reapy's "distant API" (reapy.config.enable_dist_api(), run once
inside REAPER via Enable_Reapy.py).

Invocation contract (matches Mcp.Reaper's ReaperPythonBridge.cs):
    python reapy_bridge.py <command> <json-args>

Prints a single line of JSON to stdout:
    {"success": true, "result": {...}}
    {"success": false, "error": "..."}

Commands implement the tools from the design doc (section 6.3):
    analyze_audio_region, apply_fx_chain_for_mood, render_project,
    start_recording, stop_recording.

Mood -> FX chain mapping is intentionally simple and stock-REAPER-only
(ReaEQ/ReaDelay/ReaVerbate/ReaComp are bundled with every REAPER install),
so this works without any third-party plugin dependency.
"""
import json
import sys

MOOD_FX_CHAINS = {
    "dreamy": ["ReaEQ", "ReaDelay", "ReaVerbate"],
    "aggressive": ["ReaEQ", "ReaComp", "ReaXcomp"],
    "warm": ["ReaEQ", "ReaComp"],
    "spacious": ["ReaDelay", "ReaVerbate"],
    "clean": ["ReaEQ", "ReaComp"],
}


def _error(message):
    print(json.dumps({"success": False, "error": message}))
    sys.exit(0)


def _ok(result):
    print(json.dumps({"success": True, "result": result}))
    sys.exit(0)


def cmd_analyze_audio_region(args):
    import reapy

    project = reapy.Project()
    selected = project.selected_items
    if not selected:
        _error("No media item is selected in REAPER. Select an item/region first.")

    item = selected[0]
    take = item.active_take
    if take is None:
        _error("Selected item has no active take.")

    source_path = take.source.filename
    if not source_path:
        _error("Selected take has no underlying audio file (may be MIDI or in-project audio).")

    import librosa
    import numpy as np
    import pyloudnorm as pyln

    y, sr = librosa.load(source_path, sr=None, mono=True)
    duration_s = float(librosa.get_duration(y=y, sr=sr))
    peak_db = float(20.0 * np.log10(np.max(np.abs(y)) + 1e-12))
    rms = float(np.sqrt(np.mean(y ** 2)) + 1e-12)
    rms_db = float(20.0 * np.log10(rms))

    meter = pyln.Meter(sr)
    integrated_lufs = float(meter.integrated_loudness(y))

    tempo, _ = librosa.beat.beat_track(y=y, sr=sr)

    _ok({
        "sourcePath": source_path,
        "durationSeconds": duration_s,
        "peakDb": peak_db,
        "rmsDb": rms_db,
        "integratedLufs": integrated_lufs,
        "estimatedTempoBpm": float(tempo) if tempo else None,
    })


def cmd_apply_fx_chain_for_mood(args):
    import reapy

    track_index = int(args.get("trackIndex", 0))
    mood = str(args.get("mood", "")).strip().lower()

    chain = MOOD_FX_CHAINS.get(mood)
    if chain is None:
        _error(f"Unknown mood '{mood}'. Known moods: {', '.join(sorted(MOOD_FX_CHAINS))}")

    project = reapy.Project()
    if track_index < 0 or track_index >= project.n_tracks:
        _error(f"Track index {track_index} is out of range (0..{project.n_tracks - 1}).")

    track = project.tracks[track_index]
    added = []
    for fx_name in chain:
        fx = track.add_fx(fx_name, even_if_exists=False)
        added.append({"name": fx_name, "index": fx.index})

    _ok({"trackIndex": track_index, "trackName": track.name, "mood": mood, "fxAdded": added})


def cmd_render_project(args):
    import reapy
    from reapy import reascript_api as RPR

    project = reapy.Project()
    # Action 41824: "File: Render project, using the most recent render
    # settings, auto-close render dialog". Requires render settings to have
    # been configured at least once in the REAPER UI (File > Render...).
    RPR.Main_OnCommandEx(41824, 0, project.id)
    _ok({"project": project.name, "action": "render_started"})


def cmd_start_recording(args):
    import reapy
    from reapy import reascript_api as RPR

    project = reapy.Project()
    RPR.CSurf_OnRecord()
    _ok({"project": project.name, "transport": "recording"})


def cmd_stop_recording(args):
    import reapy
    from reapy import reascript_api as RPR

    project = reapy.Project()
    RPR.OnStopButton()
    _ok({"project": project.name, "transport": "stopped"})


def cmd_call_reascript_function(args):
    """
    Generic passthrough to any raw ReaScript API function, e.g.
    {"functionName": "GetTrack", "args": [0, 0]}
    {"functionName": "GetProjectName", "args": [0, "", 512]}

    reapy's raw API already handles the SWIG string-buffer marshalling: pass
    placeholder values (empty string, buffer size) for output parameters and
    read the filled-in values back from the returned list. Opaque object
    handles (MediaTrack*, MediaItem*, etc.) come back as strings like
    "(MediaTrack*)0x...' and can be passed into later calls unchanged.
    """
    from reapy import reascript_api as RPR

    function_name = str(args.get("functionName", "")).strip()
    if not function_name:
        _error("Missing required argument 'functionName'.")

    function = getattr(RPR, function_name, None)
    if function is None or not callable(function):
        _error(f"Unknown ReaScript function '{function_name}'.")

    call_args = args.get("args", [])
    if not isinstance(call_args, list):
        _error("'args' must be a JSON array of positional arguments.")

    result = function(*call_args)
    _ok(result)


def cmd_list_reascript_functions(args):
    """Lists available raw ReaScript function names, optionally filtered by substring."""
    from reapy import reascript_api as RPR

    name_filter = str(args.get("filter", "")).strip().lower()
    names = sorted(
        name for name in dir(RPR)
        if not name.startswith("_") and callable(getattr(RPR, name))
    )
    if name_filter:
        names = [name for name in names if name_filter in name.lower()]

    _ok({"count": len(names), "functions": names})


COMMANDS = {
    "analyze_audio_region": cmd_analyze_audio_region,
    "apply_fx_chain_for_mood": cmd_apply_fx_chain_for_mood,
    "render_project": cmd_render_project,
    "start_recording": cmd_start_recording,
    "stop_recording": cmd_stop_recording,
    "call_reascript_function": cmd_call_reascript_function,
    "list_reascript_functions": cmd_list_reascript_functions,
}


def main():
    if len(sys.argv) < 2:
        _error("Usage: reapy_bridge.py <command> [json-args]")

    command = sys.argv[1]
    raw_args = sys.argv[2] if len(sys.argv) > 2 else "{}"

    try:
        args = json.loads(raw_args) if raw_args else {}
    except json.JSONDecodeError as exc:
        _error(f"Invalid JSON arguments: {exc}")
        return

    handler = COMMANDS.get(command)
    if handler is None:
        _error(f"Unknown command '{command}'. Known commands: {', '.join(sorted(COMMANDS))}")
        return

    try:
        handler(args)
    except Exception as exc:  # noqa: BLE001 - bridge boundary, report all failures as JSON
        _error(f"{type(exc).__name__}: {exc}")


if __name__ == "__main__":
    main()
