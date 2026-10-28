"""CapCut bridge: builds CapCut drafts with pyCapCut. Usage: capcut_bridge.py <command> <json-args>

Edits accumulate in a sidecar spec (<specs>/<name>.json); save_draft renders the spec
into the CapCut drafts folder. CapCut must be closed when writing."""
import json
import os
import shutil
import subprocess
import sys
import time

import pycapcut as cc

DRAFTS = os.environ.get("CAPCUT_DRAFTS_DIR") or os.path.join(
    os.environ["LOCALAPPDATA"], "CapCut", "User Data", "Projects", "com.lveditor.draft")
SPECS = os.environ.get("CAPCUT_SPEC_DIR") or os.path.join(os.path.dirname(os.path.abspath(__file__)), "specs")


def capcut_running():
    out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq CapCut.exe", "/NH"], capture_output=True, text=True).stdout
    return "CapCut.exe" in out


def spec_path(name):
    return os.path.join(SPECS, name + ".json")


def load_spec(name):
    p = spec_path(name)
    if not os.path.exists(p):
        raise ValueError(f"No spec for draft '{name}'. Call create_draft first.")
    with open(p, encoding="utf-8") as f:
        return json.load(f)


def store_spec(name, spec):
    os.makedirs(SPECS, exist_ok=True)
    with open(spec_path(name), "w", encoding="utf-8") as f:
        json.dump(spec, f, indent=2)


def cmd_create_draft(a):
    spec = {"name": a["name"], "width": a.get("width", 1920), "height": a.get("height", 1080),
            "fps": a.get("fps", 30), "items": []}
    store_spec(a["name"], spec)
    return {"created": a["name"], "spec": spec_path(a["name"])}


def add_item(a, kind):
    spec = load_spec(a["name"])
    item = {k: v for k, v in a.items() if k != "name"}
    item["kind"] = kind
    spec["items"].append(item)
    store_spec(a["name"], spec)
    return {"items": len(spec["items"])}


def cmd_add_video(a):
    if not os.path.exists(a["path"]):
        raise ValueError(f"Media not found: {a['path']}")
    return add_item(a, "video")


def cmd_add_audio(a):
    if not os.path.exists(a["path"]):
        raise ValueError(f"Media not found: {a['path']}")
    return add_item(a, "audio")


def cmd_add_text(a):
    return add_item(a, "text")


def cmd_save_draft(a):
    if capcut_running() and not a.get("force"):
        raise RuntimeError("CapCut is running; close it before saving (autosave would overwrite), or pass force=true.")
    spec = load_spec(a["name"])
    folder = cc.DraftFolder(DRAFTS)
    target = os.path.join(DRAFTS, spec["name"])
    if os.path.isdir(target):
        shutil.copytree(target, os.path.join(SPECS, f"{spec['name']}.bak-{int(time.time())}"))
    script = folder.create_draft(spec["name"], spec["width"], spec["height"], spec["fps"], allow_replace=True)
    tracks = {}
    for it in spec["items"]:
        kind = it["kind"]
        ttype = {"video": cc.TrackType.video, "audio": cc.TrackType.audio, "text": cc.TrackType.text}[kind]
        track = it.get("track") or f"{kind}_main"
        if track not in tracks:
            script.add_track(ttype, track)
            tracks[track] = True
        rng = cc.trange(f"{it['start']}s", f"{it['duration']}s")
        if kind == "video":
            seg = cc.VideoSegment(it["path"], rng, volume=it.get("volume", 1.0))
        elif kind == "audio":
            seg = cc.AudioSegment(it["path"], rng, volume=it.get("volume", 1.0))
        else:
            style = cc.TextStyle(size=it.get("size", 8.0), color=tuple(it.get("color", [1, 1, 1])))
            seg = cc.TextSegment(it["text"], rng, style=style)
        script.add_segment(seg, track)
    script.save()
    return {"saved": spec["name"], "items": len(spec["items"]), "path": target}


def cmd_list_drafts(a):
    return {"drafts": cc.DraftFolder(DRAFTS).list_drafts(),
            "specs": [f[:-5] for f in os.listdir(SPECS) if f.endswith(".json")] if os.path.isdir(SPECS) else []}


def cmd_read_draft(a):
    p = os.path.join(DRAFTS, a["name"], "draft_content.json")
    with open(p, encoding="utf-8") as f:
        d = json.load(f)
    return {"canvas": d.get("canvas_config"), "fps": d.get("fps"), "duration_us": d.get("duration"),
            "tracks": [{"type": t.get("type"), "segments": len(t.get("segments", []))} for t in d.get("tracks", [])]}


def deep_merge(dst, src):
    for k, v in src.items():
        if isinstance(v, dict) and isinstance(dst.get(k), dict):
            deep_merge(dst[k], v)
        else:
            dst[k] = v


def cmd_patch_draft_json(a):
    if capcut_running() and not a.get("force"):
        raise RuntimeError("CapCut is running; close it first, or pass force=true.")
    p = os.path.join(DRAFTS, a["name"], "draft_content.json")
    shutil.copy2(p, p + f".mcpbak-{int(time.time())}")
    with open(p, encoding="utf-8") as f:
        d = json.load(f)
    deep_merge(d, a["patch"])
    with open(p + ".tmp", "w", encoding="utf-8") as f:
        json.dump(d, f, ensure_ascii=False)
    os.replace(p + ".tmp", p)
    return {"patched": a["name"]}


def main():
    cmd, raw = sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "{}"
    try:
        fn = globals().get("cmd_" + cmd)
        if fn is None:
            raise ValueError(f"Unknown command '{cmd}'")
        print(json.dumps({"success": True, "result": fn(json.loads(raw))}, default=str))
    except Exception as e:
        print(json.dumps({"success": False, "error": f"{type(e).__name__}: {e}"}))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    main()
