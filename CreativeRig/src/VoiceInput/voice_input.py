"""Hands-free voice input for the studio.

Listens on the Focusrite (WASAPI shared, so REAPER can keep ASIO), segments speech with a
noise-adaptive energy gate (close mic >> music bleed), transcribes with faster-whisper on the
GPU, and types into the Copilot terminal.

  wake word  : "hey rig"  -> active until idle for IDLE_SECONDS or "go to sleep"
  submit     : say "submit three times" -> presses Enter
  other cmds : "clear that" (erase typed text), "go to sleep"

Env overrides: VOICE_DEVICE (name substring, default "default" = system default mic; e.g. Focusrite),
VOICE_CHANNEL (0-based, default 0),
VOICE_MODEL (default small.en), VOICE_IDLE (seconds).
Run with --probe to print live per-channel levels, --test to transcribe without typing.
"""
import os
import collections
import re
import sys
import time
import queue
import winsound

import numpy as np
import sounddevice as sd

for sub in ("cublas", "cudnn"):
    p = os.path.join(os.path.dirname(sys.executable), "Lib", "site-packages", "nvidia", sub, "bin")
    if os.path.isdir(p):
        os.add_dll_directory(p)
        os.environ["PATH"] = p + os.pathsep + os.environ["PATH"]

from faster_whisper import WhisperModel  # noqa: E402

DEVICE_HINT = os.environ.get("VOICE_DEVICE", "default")
CHANNEL = int(os.environ.get("VOICE_CHANNEL", "0"))
MODEL = os.environ.get("VOICE_MODEL", "small.en")
IDLE_SECONDS = float(os.environ.get("VOICE_IDLE", "30"))
SAMPLE_RATE = 48000
FRAME = 4800  # 100 ms
END_SILENCE = 0.9
MIN_SPEECH = 0.35
MAX_UTTERANCE = 25.0

WAKE_RE = re.compile(r"\b(hey|hay|hi|a|ok|okay)[\s,.!-]*(rig|rick|rg|ridge|rigg?s?)\b", re.I)
SUBMIT_RE = re.compile(r"\bsubmit[\s,.-]*(3|three)[\s,.-]*times?\b[\s.!]*$", re.I)
SLEEP_RE = re.compile(r"\b(go to sleep|stop listening)\b", re.I)
CLEAR_RE = re.compile(r"\bclear that\b", re.I)
DIRECT_EXE = os.environ.get("VOICE_DIRECT_EXE", os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "Voice.Commands", "bin", "Debug",
    "net10.0-windows", "Voice.Commands.exe"))
_direct = None
_direct_listening_until = 0.0
DIRECT_LISTEN_SECONDS = 25.0
ELMER_RE = re.compile(r"\b(elm[ae]r|elmore|ellmer|almer|el\s+(mer|more))\b", re.I)


def try_direct(text):
    """Send a transcript to the C# direct-command session ("Hey Elmer ..."). True if it consumed it."""
    global _direct, _direct_listening_until
    import subprocess
    listening = time.time() < _direct_listening_until
    if not os.path.exists(DIRECT_EXE) or not (listening or ELMER_RE.search(text)):
        return False
    if _direct is None or _direct.poll() is not None:
        _direct = subprocess.Popen([DIRECT_EXE], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                   text=True, bufsize=1)
    _direct.stdin.write(text.replace("\n", " ") + "\n")
    _direct.stdin.flush()
    reply = ""
    while True:  # launched apps can inherit our stdout and print noise; only accept protocol lines
        line = _direct.stdout.readline()
        if line == "":
            break
        line = line.strip()
        if re.match(r"^(PASS|LISTENING|HANDLED|CHAT)\b", line):
            reply = line
            break
    print(f"direct: {reply}", flush=True)
    _direct_listening_until = time.time() + (45.0 if reply.startswith("CHAT") else DIRECT_LISTEN_SECONDS) if (reply == "LISTENING" or reply.startswith("CHAT")) else 0.0
    return reply != "PASS"


JUNK = {"you", "thank you", "thanks for watching", "bye", "the", "uh", "um", "hmm", ""}


def find_device():
    apis = sd.query_hostapis()
    if DEVICE_HINT.lower() == "default":
        wasapi = next(h for h in apis if h["name"] == "Windows WASAPI")
        i = wasapi["default_input_device"]
        if i < 0:
            raise RuntimeError("No default WASAPI input device")
        return i, sd.query_devices(i)
    names = [h["name"] for h in apis]
    for i, d in enumerate(sd.query_devices()):
        if d["max_input_channels"] > 0 and DEVICE_HINT.lower() in d["name"].lower() \
                and names[d["hostapi"]] == "Windows WASAPI":
            return i, d
    raise RuntimeError(f"No WASAPI input matching '{DEVICE_HINT}'")


def beep(freq, ms=90):
    winsound.Beep(freq, ms)


def focus_copilot():
    try:
        import win32gui
        import win32con
        found = []

        def cb(h, _):
            if win32gui.IsWindowVisible(h) and "copilot" in win32gui.GetWindowText(h).lower():
                found.append(h)
        win32gui.EnumWindows(cb, None)
        if found:
            win32gui.ShowWindow(found[0], win32con.SW_SHOW)
            win32gui.SetForegroundWindow(found[0])
            time.sleep(0.15)
    except Exception:
        pass


def main():
    probe = "--probe" in sys.argv
    test = "--test" in sys.argv
    idx, dev = find_device()
    chans = min(dev["max_input_channels"], 2)
    print(f"Input: [{idx}] {dev['name']} ch{CHANNEL} @ {SAMPLE_RATE}", flush=True)
    q = queue.Queue()

    def on_audio(data, frames, t, status):
        q.put(data.copy())

    stream = sd.InputStream(device=idx, samplerate=SAMPLE_RATE, channels=chans, dtype="float32",
                            blocksize=FRAME, callback=on_audio)
    stream.start()

    if probe:
        t0 = time.time()
        while time.time() - t0 < 20:
            d = q.get()
            print("  ".join(f"ch{c}: {np.sqrt((d[:, c] ** 2).mean()):.4f}" for c in range(chans)), flush=True)
        return

    print(f"Loading {MODEL} on GPU...", flush=True)
    try:
        model = WhisperModel(MODEL, device="cuda", compute_type="float16")
    except Exception as e:
        print(f"GPU unavailable ({e}); using CPU", flush=True)
        model = WhisperModel(MODEL, device="cpu", compute_type="int8")

    # Median of the first 1.5 s so speech during startup doesn't inflate the gate.
    floor = max(0.003, float(np.median([np.sqrt((q.get()[:, CHANNEL] ** 2).mean()) for _ in range(15)])))
    print(f"Noise floor {floor:.4f}. Say 'hey rig'.", flush=True)
    beep(880)

    active = False
    last_activity = 0.0
    typed_len = 0
    buf, speech_t, silence_t, in_speech = [], 0.0, 0.0, False
    preroll = collections.deque(maxlen=4)  # 0.4 s so a quiet first word isn't clipped

    from pynput.keyboard import Controller, Key
    kb = Controller()

    def type_text(text):
        nonlocal typed_len
        if test:
            print("TYPE:", text, flush=True)
            return
        focus_copilot()
        kb.type(text)
        typed_len += len(text)

    def handle(audio):
        nonlocal active, last_activity, typed_len
        audio16 = audio[: len(audio) // 3 * 3].reshape(-1, 3).mean(axis=1).astype(np.float32)
        segs, _ = model.transcribe(audio16, language="en", beam_size=1, vad_filter=True,
                                   condition_on_previous_text=False, no_speech_threshold=0.5,
                                   initial_prompt="Hey Elmer, start REAPER. Hey Elmer, set the tempo to 120 BPM.")
        parts = [s for s in segs if s.avg_logprob > -1.0 and s.no_speech_prob < 0.6]
        text = " ".join(s.text.strip() for s in parts).strip()
        if re.fullmatch(r"(reaper|metronome|record|[\s,.])+", text.lower()):
            return  # Whisper parroting its prompt on unclear audio
        if text.lower().strip(" .,!?") in JUNK:
            return
        print(f"heard: {text}", flush=True)

        if try_direct(text):
            last_activity = time.time()
            while not q.empty():  # drop audio captured while the assistant was speaking
                q.get()
            preroll.clear()
            return

        if not active:
            m = WAKE_RE.search(text)
            if not m:
                return
            active = True
            last_activity = time.time()
            beep(1200)
            text = text[m.end():].strip(" ,.!?-")
            if not text:
                return

        last_activity = time.time()
        if SLEEP_RE.search(text):
            active = False
            beep(500, 200)
            print("sleeping", flush=True)
            return
        if CLEAR_RE.search(text):
            if not test:
                focus_copilot()
                for _ in range(typed_len):
                    kb.tap(Key.backspace)
            typed_len = 0
            beep(700)
            return

        submit = bool(SUBMIT_RE.search(text))
        if submit:
            text = SUBMIT_RE.sub("", text).strip()
        if text:
            type_text(text + (" " if not submit else ""))
        if submit:
            if not test:
                kb.tap(Key.enter)
            typed_len = 0
            beep(1500, 150)

    print("Listening...", flush=True)
    while True:
        d = q.get()[:, CHANNEL]
        rms = float(np.sqrt((d ** 2).mean()))
        loud = rms > max(floor * 3.0, 0.01)
        if not in_speech:
            if loud:
                in_speech, buf, speech_t, silence_t = True, list(preroll) + [d], 0.1, 0.0
            else:
                floor = 0.98 * floor + 0.02 * rms  # follow slow music/room level changes
            preroll.append(d)
        else:
            buf.append(d)
            speech_t += 0.1
            silence_t = 0.0 if loud else silence_t + 0.1
            if silence_t >= END_SILENCE or speech_t >= MAX_UTTERANCE:
                in_speech = False
                if speech_t - silence_t >= MIN_SPEECH:
                    try:
                        handle(np.concatenate(buf))
                    except Exception as e:
                        print(f"error: {e}", flush=True)
                    while q.qsize() > 50:  # drop backlog accumulated while transcribing
                        q.get()
        if active and not in_speech and time.time() - last_activity > IDLE_SECONDS:
            active = False
            beep(500, 200)
            print("idle -> sleeping", flush=True)


if __name__ == "__main__":
    main()
