"""Small always-on-top status pill showing what Elmer is doing (sleeping, listening, working...)."""
import itertools
import threading

STATES = {
    "loading": ("Elmer: starting...", "#555555"),
    "sleeping": ("Elmer: say \"Hey Elmer\"", "#3a3f47"),
    "listening": ("Elmer: listening", "#1e8e3e"),
    "hearing": ("Elmer: hearing you", "#1a73e8"),
    "transcribing": ("Elmer: transcribing", "#b06000"),
    "working": ("Elmer: working", "#d93025"),
}

_state = "loading"
_ready = threading.Event()


def set_state(state):
    global _state
    _state = state


def _run():
    try:
        import tkinter as tk
    except Exception:
        return
    root = tk.Tk()
    root.overrideredirect(True)
    root.attributes("-topmost", True)
    root.attributes("-alpha", 0.9)
    label = tk.Label(root, text="", fg="white", font=("Segoe UI", 11, "bold"), padx=14, pady=6, anchor="w")
    # Fixed width (in characters) fits the longest state plus the animated dots.
    label.config(width=max(len(t) for t, _ in STATES.values()) + 4)
    label.pack()
    spinner = itertools.cycle(["", ".", "..", "..."])
    shown = [None]
    placed = [False]

    def place():
        root.update_idletasks()
        w = root.winfo_width()
        root.geometry(f"+{root.winfo_screenwidth() - w - 20}+{root.winfo_screenheight() - 90}")
        placed[0] = True

    def tick():
        text, color = STATES.get(_state, STATES["sleeping"])
        if _state in ("transcribing", "working"):
            text += next(spinner)
        if shown[0] != (text, color):
            label.config(text=text, bg=color)
            root.config(bg=color)
            shown[0] = (text, color)
            if not placed[0]:
                place()
        root.after(300, tick)

    tick()
    root.mainloop()


def start():
    threading.Thread(target=_run, daemon=True, name="overlay").start()
