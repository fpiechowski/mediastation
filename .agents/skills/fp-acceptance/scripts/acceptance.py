#!/usr/bin/env python3
# /// script
# requires-python = ">=3.11"
# ///
"""acceptance — the deterministic half of a guided acceptance session.

Log commands read JSON Lines logs (one object per line, an ISO-8601 UTC timestamp under --ts-key, a level
under --level-key) from every file matching --logs, sorted by timestamp. A window is [--since, --until):
`since` inclusive, `until` exclusive, `until` defaulting to now.

A SPEC selects log entries: comma-separated conditions, all of which must hold, with an optional count after
the last colon.
  path=value    the value at the dotted path equals value (bool as true/false, null as null)
  path!=value   the value differs or the path is absent
  path~=text    the value contains text (e.g. a commit in a version string)
  path          the path is present
  ...:N  ...:>=N  ...:<=N  ...:N-M   how many matching entries the window must hold (default >=1)
Example: "msg=action,data.kind=home,data.source=remote:1"

Commands:
  mark        print the current UTC time to use as --since
  launch      start an executable detached, mark the time, optionally wait for a ready entry
  focus       bring a process's window to the foreground and verify it (Windows)
  close       post WM_CLOSE to a process's windows and wait for it to exit (Windows)
  foreground  report the foreground window, optionally whether it belongs to --pid (Windows)
  wait        poll the logs from --since until every --for SPEC has a match, or --timeout; --say, --beep cues
  say         caption a line on the overlay and, with --voice, speak it
  overlay     start/stop the caption bar: topmost, never activated, click-through (Windows)
  check       assert a window: --expect counts, --forbid-level, --forbid-key, --ordered; an empty window fails
  inventory   list the messages and field paths the runs of one build wrote, to build SPECs from
  next        report the first empty row of a plan's results table (the last table whose first column is #),
              that row's scenario cells, and problems with the plan's structure
  fill        write one row of that table, from a saved check (--from-check) or a script's --exit-code;
              a filled row is never overwritten

row:ROW and row:ROW.until (with --plan) stand for the window `fill --from-check` recorded in that row's evidence.
  new-pass    append a fresh results table for a new pass over the plan
  meta        set keys in a plan's YAML frontmatter

Output: one JSON object on stdout. Exit codes: 0 found, passed or done; 1 not found or failed (the JSON
says why); 2 a usage or environment error, with a one-line reason on stderr.
"""

from __future__ import annotations

import argparse
import glob
import json
import os
import re
import subprocess
import sys
import time
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path


class ScriptError(Exception):
    """A failure the user can act on. Its message is the one line printed on exit."""


# ---------------------------------------------------------------------------
# Time and log reading
# ---------------------------------------------------------------------------


def now_utc() -> datetime:
    return datetime.now(timezone.utc)


def iso(moment: datetime) -> str:
    return moment.astimezone(timezone.utc).isoformat(timespec="milliseconds").replace("+00:00", "Z")


def parse_ts(value: object) -> datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        moment = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    if moment.tzinfo is None:
        moment = moment.replace(tzinfo=timezone.utc)
    return moment


def parse_moment(value: str, option: str) -> datetime:
    moment = parse_ts(value)
    if moment is None:
        raise ScriptError(f"{option} is not an ISO-8601 time: {value}")
    return moment


@dataclass
class Entry:
    ts: datetime
    data: dict
    raw: str
    file: str


def log_files(pattern: str) -> list[str]:
    expanded = os.path.expanduser(os.path.expandvars(pattern))
    return sorted(glob.glob(expanded))


def read_log(pattern: str, ts_key: str) -> tuple[list[Entry], dict[str, int]]:
    """Every parseable entry, sorted, and per file the count of lines skipped as unparseable."""
    files = log_files(pattern)
    if not files:
        raise ScriptError(f"no log file matches {pattern}")
    entries: list[Entry] = []
    skipped: dict[str, int] = {}
    for file in files:
        # The application may be writing; Python's open() shares read and write, and a torn last line is skipped.
        with open(file, encoding="utf-8-sig", errors="replace") as handle:
            for line in handle:
                line = line.strip()
                if not line:
                    continue
                try:
                    data = json.loads(line)
                except json.JSONDecodeError:
                    data = None
                ts = parse_ts(data.get(ts_key)) if isinstance(data, dict) else None
                if ts is None:
                    skipped[file] = skipped.get(file, 0) + 1
                    continue
                entries.append(Entry(ts, data, line, file))
    if not entries and skipped:
        # A wrong --ts-key or a non-JSON log would otherwise read as an empty, passing window.
        raise ScriptError(f"{sum(skipped.values())} lines in {len(skipped)} file(s), none with a parseable '{ts_key}'")
    entries.sort(key=lambda entry: entry.ts)
    return entries, skipped


def read_entries(pattern: str, ts_key: str) -> list[Entry]:
    return read_log(pattern, ts_key)[0]


def window(entries: list[Entry], since: datetime, until: datetime | None) -> list[Entry]:
    return [entry for entry in entries if entry.ts >= since and (until is None or entry.ts < until)]


# ---------------------------------------------------------------------------
# SPEC matching
# ---------------------------------------------------------------------------

COUNT = re.compile(r"^(?:(\d+)|>=(\d+)|<=(\d+)|(\d+)-(\d+))$")
MISSING = object()


@dataclass
class Spec:
    text: str
    conditions: list[tuple[str, str, str | None]]
    low: int = 1
    high: int | None = None
    explicit_count: bool = False

    def want(self) -> str:
        if self.high is None:
            return f">={self.low}"
        if self.low == self.high:
            return str(self.low)
        if self.low == 0:
            return f"<={self.high}"
        return f"{self.low}-{self.high}"

    def count_ok(self, count: int) -> bool:
        return count >= self.low and (self.high is None or count <= self.high)


def parse_spec(text: str) -> Spec:
    body, low, high, explicit = text, 1, None, False
    head, sep, tail = text.rpartition(":")
    if sep and COUNT.match(tail.strip()):
        match = COUNT.match(tail.strip())
        assert match is not None
        exact, at_least, at_most, range_low, range_high = match.groups()
        if exact is not None:
            low = high = int(exact)
        elif at_least is not None:
            low = int(at_least)
        elif at_most is not None:
            low, high = 0, int(at_most)
        else:
            low, high = int(range_low), int(range_high)
            if low > high:
                raise ScriptError(f"count range is reversed in {text}")
        body, explicit = head, True
    conditions: list[tuple[str, str, str | None]] = []
    for part in body.split(","):
        part = part.strip()
        if not part:
            continue
        if "!=" in part:
            path, value = part.split("!=", 1)
            conditions.append((path.strip(), "!=", value.strip()))
        elif "~=" in part:
            path, value = part.split("~=", 1)
            conditions.append((path.strip(), "~=", value.strip()))
        elif "=" in part:
            path, value = part.split("=", 1)
            conditions.append((path.strip(), "=", value.strip()))
        else:
            conditions.append((part, "exists", None))
    if not conditions:
        raise ScriptError(f"spec has no condition: {text}")
    return Spec(text, conditions, low, high, explicit)


def lookup(data: object, path: str) -> object:
    current = data
    for key in path.split("."):
        if isinstance(current, dict) and key in current:
            current = current[key]
        else:
            return MISSING
    return current


def render(value: object) -> str:
    if value is MISSING:
        return "(none)"
    if value is True:
        return "true"
    if value is False:
        return "false"
    if value is None:
        return "null"
    if isinstance(value, float) and value.is_integer():
        return str(int(value))
    if isinstance(value, (dict, list)):
        return json.dumps(value, ensure_ascii=False, separators=(",", ":"))
    return str(value)


def matches(spec: Spec, data: dict) -> bool:
    for path, op, value in spec.conditions:
        found = lookup(data, path)
        if op == "exists":
            if found is MISSING:
                return False
        elif op == "=":
            if found is MISSING or render(found) != value:
                return False
        elif op == "~=":
            if found is MISSING or value not in render(found):
                return False
        elif found is not MISSING and render(found) == value:
            return False
    return True


def has_key(data: object, names: set[str]) -> list[str]:
    hits: list[str] = []
    if isinstance(data, dict):
        for key, value in data.items():
            if key in names:
                hits.append(key)
            hits.extend(has_key(value, names))
    elif isinstance(data, list):
        for item in data:
            hits.extend(has_key(item, names))
    return hits


def split_list(values: list[str] | None) -> list[str]:
    return [item.strip() for value in values or [] for item in value.split(",") if item.strip()]


# ---------------------------------------------------------------------------
# Log commands
# ---------------------------------------------------------------------------


def cmd_mark(args: argparse.Namespace) -> tuple[int, dict]:
    return 0, {"mark": iso(now_utc())}


def cmd_check(args: argparse.Namespace) -> tuple[int, dict]:
    if args.out:
        # A check that errors must not leave the previous row's verdict behind for fill --from-check.
        Path(args.out).unlink(missing_ok=True)
    since = resolve_moment(args.since, "--since", args.plan)
    until = resolve_moment(args.until, "--until", args.plan) if args.until else now_utc()
    specs = [parse_spec(text) for text in args.expect or []]
    allows = [parse_spec(text) for text in args.allow or []]
    levels = {level.lower() for level in split_list(args.forbid_level)}
    keys = set(split_list(args.forbid_key))
    if not specs and not levels and not keys:
        raise ScriptError("nothing to check: give --expect, --forbid-level or --forbid-key")

    all_entries, skipped = read_log(args.logs, args.ts_key)
    entries = window(all_entries, since, until)
    result: dict = {
        "window": {"since": iso(since), "until": iso(until)},
        "files": sorted({entry.file for entry in entries}),
        "entries": len(entries),
        "skipped_lines": skipped,
        "assertion": {"expect": args.expect or [], "allow": args.allow or [], "forbid_level": sorted(levels),
                      "forbid_key": sorted(keys), "ordered": args.ordered, "allow_empty": args.allow_empty},
        "expects": [],
    }
    # A window the app wrote nothing into passes every forbid and every :0, so it fails unless that is the point.
    ok = bool(entries) or args.allow_empty
    if not entries:
        result["empty_window"] = True
    first_index: list[int | None] = []
    for spec in specs:
        hits = [index for index, entry in enumerate(entries) if matches(spec, entry.data)]
        passed = spec.count_ok(len(hits))
        ok &= passed
        first_index.append(hits[0] if hits else None)
        result["expects"].append({
            "spec": spec.text,
            "want": spec.want(),
            "count": len(hits),
            "ok": passed,
            "lines": [entries[index].raw for index in hits[: args.max_lines]],
        })

    if levels:
        bad = [
            entry for entry in entries
            if render(lookup(entry.data, args.level_key)).lower() in levels
            and not any(matches(allow, entry.data) for allow in allows)
        ]
        allowed = [
            entry for entry in entries
            if render(lookup(entry.data, args.level_key)).lower() in levels
            and any(matches(allow, entry.data) for allow in allows)
        ]
        ok &= not bad
        result["forbidden_levels"] = {
            "levels": sorted(levels),
            "allowed": [allow.text for allow in allows],
            "count": len(bad),
            "allowed_count": len(allowed),
            "ok": not bad,
            "lines": [entry.raw for entry in bad[: args.max_lines]],
        }

    if keys:
        bad_keys = [(entry, has_key(entry.data, keys)) for entry in entries]
        bad_keys = [(entry, hits) for entry, hits in bad_keys if hits]
        ok &= not bad_keys
        result["forbidden_keys"] = {
            "keys": sorted(keys),
            "count": len(bad_keys),
            "ok": not bad_keys,
            "lines": [entry.raw for entry, _ in bad_keys[: args.max_lines]],
        }

    if args.ordered:
        present = [(spec.text, index) for spec, index in zip(specs, first_index) if index is not None]
        in_order = all(earlier[1] < later[1] for earlier, later in zip(present, present[1:]))
        ok &= in_order
        result["order"] = {"ok": in_order, "first_seen": [text for text, _ in sorted(present, key=lambda item: item[1])]}

    result["verdict"] = "pass" if ok else "fail"
    if args.out:
        Path(args.out).write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    return (0 if ok else 1), result


def leaf_paths(value: object, prefix: str = "") -> list[str]:
    if isinstance(value, dict) and value:
        return [path for key, item in value.items() for path in leaf_paths(item, f"{prefix}{key}.")]
    return [prefix[:-1]] if prefix else []


def cmd_inventory(args: argparse.Namespace) -> tuple[int, dict]:
    """What the build under test writes: the messages, levels and field paths in the runs whose start names it."""
    entries, skipped = read_log(args.logs, args.ts_key)
    start = parse_spec(args.start)
    runs: list[list[Entry]] = []
    versions: list[str] = []
    current: list[Entry] | None = None
    for entry in entries:
        if matches(start, entry.data):
            version = render(lookup(entry.data, args.version_path))
            versions.append(version)
            current = [entry] if args.version_contains in version else None
            if current is not None:
                runs.append(current)
        elif current is not None:
            current.append(entry)
    if not runs:
        recent = list(dict.fromkeys(reversed(versions)))[:5]
        return 1, {"runs": 0, "reason": f"no {args.start} with {args.version_path} containing {args.version_contains}", "recent_versions": recent}
    messages: dict[str, dict] = {}
    for run in runs:
        for entry in run:
            name = render(lookup(entry.data, args.msg_key))
            info = messages.setdefault(name, {"count": 0, "levels": set(), "paths": set()})
            info["count"] += 1
            info["levels"].add(render(lookup(entry.data, args.level_key)))
            info["paths"].update(path for path in leaf_paths(entry.data) if path not in {args.ts_key, args.msg_key, args.level_key})
    return 0, {
        "runs": [{"start": iso(run[0].ts), "entries": len(run)} for run in runs],
        "skipped_lines": skipped,
        "messages": {name: {"count": info["count"], "levels": sorted(info["levels"]), "paths": sorted(info["paths"])}
                     for name, info in sorted(messages.items())},
    }


CUES = {"tick": [(660, 120)], "go": [(880, 450)], "found": [(1320, 120), (1760, 160)], "timeout": [(330, 700)]}


def cue(kind: str, enabled: bool) -> str | None:
    """Play a cue; return why it could not sound, so a silent channel is reported rather than raised."""
    # The user may face a full-screen app, not the terminal, so sound is the only cue they get.
    if not enabled:
        return None
    if sys.platform != "win32":
        return "sound cues need Windows"
    import winsound

    try:
        for frequency, duration in CUES[kind]:
            winsound.Beep(frequency, duration)
    except RuntimeError as error:
        return f"beep failed: {error}"
    return None


# ---------------------------------------------------------------------------
# Voice and captions: the channel to a user facing a full-screen app instead of the conversation
# ---------------------------------------------------------------------------

OVERLAY_STATE = Path(os.environ.get("TEMP", "/tmp")) / "fp-acceptance-overlay.json"


def read_state() -> dict:
    try:
        return json.loads(OVERLAY_STATE.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def write_state(state: dict) -> None:
    temporary = OVERLAY_STATE.with_suffix(".tmp")
    temporary.write_text(json.dumps(state, ensure_ascii=False), encoding="utf-8")
    os.replace(temporary, OVERLAY_STATE)


def pid_alive(pid: int) -> bool:
    if not pid:
        return False
    if sys.platform != "win32":
        try:
            os.kill(pid, 0)
            return True
        except OSError:
            return False
    import ctypes

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.OpenProcess.restype = ctypes.c_void_p
    handle = kernel32.OpenProcess(SYNCHRONIZE, False, pid)
    if not handle:
        return False
    try:
        return kernel32.WaitForSingleObject(ctypes.c_void_p(handle), 0) != WAIT_OBJECT_0
    finally:
        kernel32.CloseHandle(ctypes.c_void_p(handle))


def overlay_running() -> bool:
    return pid_alive(int(read_state().get("pid") or 0))


def caption(text: str, hold: float = 0) -> bool:
    if not overlay_running():
        return False
    state = read_state()
    state.update(text=text, until=time.time() + hold if hold > 0 else None)
    write_state(state)
    return True


SPEAK = (
    "Add-Type -AssemblyName System.Speech; $s = New-Object System.Speech.Synthesis.SpeechSynthesizer; "
    "$v = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and ($_.VoiceInfo.Culture.Name -eq $env:FPA_VOICE "
    "-or $_.VoiceInfo.Name -eq $env:FPA_VOICE) } | Select-Object -First 1; "
    "if (-not $v) { exit 3 }; $s.SelectVoice($v.VoiceInfo.Name); $s.Speak($env:FPA_TEXT)"
)


def say(text: str, voice: str | None, hold: float = 0) -> dict:
    result = {"captioned": caption(text, hold), "spoken": False}
    if not voice:
        return result
    if sys.platform != "win32":
        result["reason"] = "speech needs Windows"
        return result
    env = dict(os.environ, FPA_TEXT=text, FPA_VOICE=voice)
    code = subprocess.run(["powershell", "-NoProfile", "-NonInteractive", "-Command", SPEAK], env=env,
                          capture_output=True, check=False, creationflags=subprocess.CREATE_NO_WINDOW).returncode
    result["spoken"] = code == 0
    if code == 3:
        result["reason"] = f"no installed voice matches {voice}"
    elif code != 0:
        result["reason"] = f"speech failed with exit {code}"
    return result


def cmd_say(args: argparse.Namespace) -> tuple[int, dict]:
    result = say(args.text, args.voice, args.hold)
    ok = result["captioned"] or result["spoken"]
    if not ok and "reason" not in result:
        result["reason"] = "no overlay is running and no --voice was given"
    return (0 if ok else 1), result


def cmd_overlay(args: argparse.Namespace) -> tuple[int, dict]:
    if args.action == "status":
        return 0, {"running": overlay_running(), "state": str(OVERLAY_STATE)}
    if args.action == "stop":
        if not overlay_running():
            return 0, {"running": False}
        state = read_state()
        state["stop"] = True
        write_state(state)
        deadline = time.monotonic() + 5
        while time.monotonic() < deadline and overlay_running():
            time.sleep(0.1)
        if overlay_running():
            return 1, {"running": True, "reason": "the caption bar is still up after 5s"}
        return 0, {"running": False}
    win32()
    if overlay_running():
        return 0, {"running": True, "pid": read_state().get("pid"), "already": True}
    write_state({"text": "", "until": None, "stop": False, "pid": 0})
    # Under `uv run` sys.executable is a venv launcher that starts the real interpreter as a console child; without
    # a console Windows opens a terminal window that takes the foreground. pythonw.exe never gets a console.
    base = Path(getattr(sys, "_base_executable", sys.executable))
    interpreter = base.with_name("pythonw.exe") if base.with_name("pythonw.exe").is_file() else base
    process = start_detached([str(interpreter), str(Path(__file__).resolve()), "overlay-serve"], os.environ.copy(), str(Path.cwd()))
    state = read_state()
    state["pid"] = process.pid
    write_state(state)
    # A child that dies at once (no display, a broken interpreter) would otherwise read as a running bar.
    time.sleep(1)
    if not overlay_running():
        return 1, {"running": False, "pid": process.pid, "reason": f"the overlay process exited; try `{interpreter} {Path(__file__).resolve()} overlay-serve` to see why"}
    return 0, {"running": True, "pid": process.pid}


def serve_overlay() -> int:
    """A topmost caption bar that never activates and lets every click through, so no window changes state."""
    ctypes, wintypes, user32, kernel32, _ = win32()
    gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
    lresult = ctypes.c_ssize_t
    wndproc_type = ctypes.WINFUNCTYPE(lresult, wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM)

    class WNDCLASSW(ctypes.Structure):
        _fields_ = [("style", wintypes.UINT), ("lpfnWndProc", wndproc_type), ("cbClsExtra", ctypes.c_int),
                    ("cbWndExtra", ctypes.c_int), ("hInstance", wintypes.HINSTANCE), ("hIcon", wintypes.HICON),
                    ("hCursor", wintypes.HANDLE), ("hbrBackground", wintypes.HBRUSH), ("lpszMenuName", wintypes.LPCWSTR),
                    ("lpszClassName", wintypes.LPCWSTR)]

    class PAINTSTRUCT(ctypes.Structure):
        _fields_ = [("hdc", wintypes.HDC), ("fErase", wintypes.BOOL), ("rcPaint", wintypes.RECT),
                    ("fRestore", wintypes.BOOL), ("fIncUpdate", wintypes.BOOL), ("rgbReserved", ctypes.c_byte * 32)]

    user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.DefWindowProcW.restype = lresult
    user32.CreateWindowExW.argtypes = [wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD, ctypes.c_int,
                                       ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.HWND, wintypes.HMENU,
                                       wintypes.HINSTANCE, wintypes.LPVOID]
    user32.CreateWindowExW.restype = wintypes.HWND
    user32.SetWindowPos.argtypes = [wintypes.HWND, wintypes.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wintypes.UINT]
    user32.SetLayeredWindowAttributes.argtypes = [wintypes.HWND, wintypes.COLORREF, wintypes.BYTE, wintypes.DWORD]
    user32.SetTimer.argtypes = [wintypes.HWND, ctypes.c_size_t, wintypes.UINT, ctypes.c_void_p]
    user32.InvalidateRect.argtypes = [wintypes.HWND, ctypes.c_void_p, wintypes.BOOL]
    user32.BeginPaint.argtypes = [wintypes.HWND, ctypes.POINTER(PAINTSTRUCT)]
    user32.BeginPaint.restype = wintypes.HDC
    user32.EndPaint.argtypes = [wintypes.HWND, ctypes.POINTER(PAINTSTRUCT)]
    user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
    user32.FillRect.argtypes = [wintypes.HDC, ctypes.POINTER(wintypes.RECT), wintypes.HBRUSH]
    user32.DrawTextW.argtypes = [wintypes.HDC, wintypes.LPCWSTR, ctypes.c_int, ctypes.POINTER(wintypes.RECT), wintypes.UINT]
    user32.DestroyWindow.argtypes = [wintypes.HWND]
    user32.GetMessageW.argtypes = [ctypes.POINTER(wintypes.MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT]
    user32.DispatchMessageW.argtypes = [ctypes.POINTER(wintypes.MSG)]
    gdi32.CreateFontW.restype = wintypes.HFONT
    gdi32.CreateSolidBrush.restype = wintypes.HBRUSH
    gdi32.SelectObject.argtypes = [wintypes.HDC, wintypes.HGDIOBJ]
    gdi32.SetTextColor.argtypes = [wintypes.HDC, wintypes.COLORREF]
    gdi32.SetBkMode.argtypes = [wintypes.HDC, ctypes.c_int]

    user32.SetProcessDPIAware()
    screen_w, screen_h = user32.GetSystemMetrics(0), user32.GetSystemMetrics(1)
    width, height = int(screen_w * 0.8), int(screen_h * 0.16)
    font = gdi32.CreateFontW(-int(height * 0.32), 0, 0, 0, 600, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI")
    brush = gdi32.CreateSolidBrush(0x00202020)
    shown = {"text": None, "visible": False}

    WS_POPUP, WM_PAINT, WM_TIMER, WM_DESTROY = 0x80000000, 0x000F, 0x0113, 0x0002
    EX = 0x00000008 | 0x08000000 | 0x00000020 | 0x00080000 | 0x00000080  # topmost, noactivate, transparent, layered, toolwindow
    SWP = 0x0001 | 0x0002 | 0x0010 | 0x0200  # nosize, nomove, noactivate, noownerzorder
    HWND_TOPMOST = wintypes.HWND(-1)

    def current_text() -> str:
        state = read_state()
        if state.get("stop"):
            return "\0stop"
        until = state.get("until")
        if until and time.time() > until:
            return ""
        return str(state.get("text") or "")

    def window_proc(hwnd, msg, wparam, lparam):
        if msg == WM_TIMER:
            text = current_text()
            if text == "\0stop":
                user32.DestroyWindow(hwnd)
                return 0
            if text != shown["text"]:
                shown["text"] = text
                user32.InvalidateRect(hwnd, None, True)
            if text and not shown["visible"]:
                user32.ShowWindow(hwnd, 4)  # SW_SHOWNOACTIVATE
                shown["visible"] = True
            elif not text and shown["visible"]:
                user32.ShowWindow(hwnd, 0)
                shown["visible"] = False
            if shown["visible"]:
                # Re-assert topmost without activation: a full-screen app that is topmost itself would cover a stale order.
                user32.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP)
            return 0
        if msg == WM_PAINT:
            paint = PAINTSTRUCT()
            hdc = user32.BeginPaint(hwnd, ctypes.byref(paint))
            rect = wintypes.RECT()
            user32.GetClientRect(hwnd, ctypes.byref(rect))
            user32.FillRect(hdc, ctypes.byref(rect), brush)
            gdi32.SelectObject(hdc, font)
            gdi32.SetTextColor(hdc, 0x00FFFFFF)
            gdi32.SetBkMode(hdc, 1)
            rect.left, rect.right = rect.left + 24, rect.right - 24
            rect.top += int(height * 0.12)
            user32.DrawTextW(hdc, shown["text"] or "", -1, ctypes.byref(rect), 0x0001 | 0x0010)  # center, wordbreak
            user32.EndPaint(hwnd, ctypes.byref(paint))
            return 0
        if msg == WM_DESTROY:
            user32.PostQuitMessage(0)
            return 0
        return user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    proc = wndproc_type(window_proc)
    instance = kernel32.GetModuleHandleW(None)
    wndclass = WNDCLASSW(lpfnWndProc=proc, hInstance=instance, lpszClassName="FpAcceptanceOverlay")
    user32.RegisterClassW(ctypes.byref(wndclass))
    hwnd = user32.CreateWindowExW(EX, "FpAcceptanceOverlay", "fp-acceptance", WS_POPUP, (screen_w - width) // 2,
                                  screen_h - height - int(screen_h * 0.06), width, height, None, None, instance, None)
    user32.SetLayeredWindowAttributes(hwnd, 0, 225, 0x2)  # LWA_ALPHA
    user32.SetTimer(hwnd, 1, 250, None)
    message = wintypes.MSG()
    while user32.GetMessageW(ctypes.byref(message), None, 0, 0) > 0:
        user32.TranslateMessage(ctypes.byref(message))
        user32.DispatchMessageW(ctypes.byref(message))
    return 0


def cmd_wait(args: argparse.Namespace) -> tuple[int, dict]:
    since = resolve_moment(args.since, "--since", args.plan)
    specs = [parse_spec(text) for text in args.for_]
    # Whether the instruction reached a user who faces the app: a timeout after a silent channel is not a missed press.
    delivered = say(args.say, args.voice) if args.say else {"captioned": False, "spoken": False}
    sound_errors: list[str] = []
    for remaining in range(int(args.countdown), 0, -1):
        caption(str(remaining))
        sound_errors.append(cue("tick", args.beep) or "")
        time.sleep(0.88)
    caption(f"▶ {args.say}" if args.say else "▶")
    sound_errors.append(cue("go", args.beep) or "")
    delivered["beep"] = bool(args.beep) and not any(sound_errors)
    if any(sound_errors):
        delivered["beep_error"] = next(error for error in sound_errors if error)
    started = time.monotonic()
    deadline = started + args.timeout
    while True:
        entries = window(read_entries(args.logs, args.ts_key), since, None)
        found = {spec.text: [entry for entry in entries if matches(spec, entry.data)] for spec in specs}
        if all(found.values()):
            break
        if time.monotonic() >= deadline:
            caption(f"✗ {args.say}" if args.say else "✗")
            cue("timeout", args.beep)
            return 1, {
                "found": False,
                "waited_s": round(time.monotonic() - started, 1),
                "missing": [text for text, hits in found.items() if not hits],
                "seen": [entry.raw for entry in entries[-args.max_lines:]],
                "cue": delivered,
            }
        time.sleep(args.poll)
    caption(f"✓ {args.say}" if args.say else "✓")
    cue("found", args.beep)
    # Settle so a second, unwanted entry from the same act lands before the caller checks the window.
    if args.settle > 0:
        time.sleep(args.settle)
    first = min(hits[0].ts for hits in found.values())
    return 0, {
        "found": True,
        "waited_s": round(time.monotonic() - started, 1),
        "first": iso(first),
        "until": iso(now_utc()),
        "lines": [hits[0].raw for hits in found.values()],
        "cue": delivered,
    }


# ---------------------------------------------------------------------------
# Process and window commands (Windows)
# ---------------------------------------------------------------------------


def win32():
    if sys.platform != "win32":
        raise ScriptError("this command needs Windows")
    import ctypes
    from ctypes import wintypes

    user32 = ctypes.WinDLL("user32", use_last_error=True)
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    enum_proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    user32.EnumWindows.argtypes = [enum_proc, wintypes.LPARAM]
    user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
    user32.GetWindowThreadProcessId.restype = wintypes.DWORD
    user32.IsWindowVisible.argtypes = [wintypes.HWND]
    user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
    user32.GetForegroundWindow.restype = wintypes.HWND
    user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
    user32.IsIconic.argtypes = [wintypes.HWND]
    user32.ShowWindow.argtypes = [wintypes.HWND, ctypes.c_int]
    user32.SetForegroundWindow.argtypes = [wintypes.HWND]
    user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ctypes.c_void_p]
    kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenProcess.restype = wintypes.HANDLE
    kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
    kernel32.WaitForSingleObject.restype = wintypes.DWORD
    kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
    kernel32.QueryFullProcessImageNameW.argtypes = [wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
    kernel32.GetModuleHandleW.restype = wintypes.HMODULE
    return ctypes, wintypes, user32, kernel32, enum_proc


SYNCHRONIZE = 0x00100000
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
WM_CLOSE = 0x0010
WAIT_OBJECT_0 = 0


def window_pid(user32, wintypes, ctypes, hwnd) -> int:
    pid = wintypes.DWORD(0)
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return int(pid.value)


def window_text(user32, ctypes, hwnd, getter: str) -> str:
    buffer = ctypes.create_unicode_buffer(512)
    getattr(user32, getter)(hwnd, buffer, 512)
    return buffer.value


def process_name(ctypes, wintypes, kernel32, pid: int) -> str:
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return ""
    try:
        buffer = ctypes.create_unicode_buffer(1024)
        size = wintypes.DWORD(1024)
        if kernel32.QueryFullProcessImageNameW(handle, 0, buffer, ctypes.byref(size)):
            return Path(buffer.value).stem
        return ""
    finally:
        kernel32.CloseHandle(handle)


def cmd_foreground(args: argparse.Namespace) -> tuple[int, dict]:
    ctypes, wintypes, user32, kernel32, _ = win32()
    hwnd = user32.GetForegroundWindow()
    if not hwnd:
        return 1, {"hwnd": None, "reason": "no foreground window"}
    pid = window_pid(user32, wintypes, ctypes, hwnd)
    result = {
        "hwnd": hex(hwnd),
        "pid": pid,
        "process": process_name(ctypes, wintypes, kernel32, pid),
        "title": window_text(user32, ctypes, hwnd, "GetWindowTextW"),
        "class": window_text(user32, ctypes, hwnd, "GetClassNameW"),
    }
    if args.pid is None:
        return 0, result
    result["is_pid"] = pid == args.pid
    return (0 if result["is_pid"] else 1), result


def process_windows(ctypes, wintypes, user32, enum_proc, pid: int) -> list[int]:
    windows: list[int] = []

    def collect(hwnd, _):
        if user32.IsWindowVisible(hwnd) and window_pid(user32, wintypes, ctypes, hwnd) == pid:
            windows.append(hwnd)
        return True

    user32.EnumWindows(enum_proc(collect), 0)
    return windows


def cmd_focus(args: argparse.Namespace) -> tuple[int, dict]:
    ctypes, wintypes, user32, kernel32, enum_proc = win32()
    windows = [hwnd for hwnd in process_windows(ctypes, wintypes, user32, enum_proc, args.pid)
               if window_text(user32, ctypes, hwnd, "GetWindowTextW")]
    if not windows:
        raise ScriptError(f"pid {args.pid} has no visible titled window")
    hwnd = windows[0]
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, 9)
    # A process started from a terminal does not take the foreground by itself; Windows lets the caller hand it
    # over right after an Alt press.
    user32.keybd_event(0x12, 0, 0, None)
    user32.keybd_event(0x12, 0, 2, None)
    user32.SetForegroundWindow(hwnd)
    time.sleep(0.5)
    current = user32.GetForegroundWindow()
    on_top = bool(current) and window_pid(user32, wintypes, ctypes, current) == args.pid
    result = {"pid": args.pid, "hwnd": hex(hwnd), "title": window_text(user32, ctypes, hwnd, "GetWindowTextW"), "foreground": on_top}
    if not on_top:
        result["reason"] = "the window did not come to the foreground"
    return (0 if on_top else 1), result


def cmd_close(args: argparse.Namespace) -> tuple[int, dict]:
    ctypes, wintypes, user32, kernel32, enum_proc = win32()
    handle = kernel32.OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, False, args.pid)
    if not handle:
        raise ScriptError(f"no running process with pid {args.pid}")
    try:
        windows = process_windows(ctypes, wintypes, user32, enum_proc, args.pid)
        for hwnd in windows:
            user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
        exited = kernel32.WaitForSingleObject(handle, int(args.timeout * 1000)) == WAIT_OBJECT_0
        exit_code = None
        code = wintypes.DWORD(0)
        if exited and kernel32.GetExitCodeProcess(handle, ctypes.byref(code)):
            exit_code = int(code.value)
    finally:
        kernel32.CloseHandle(handle)
    result = {"pid": args.pid, "windows": len(windows), "exited": exited, "exit_code": exit_code}
    if not exited:
        result["reason"] = f"still running after {args.timeout}s; nothing was killed"
    return (0 if exited else 1), result


def cmd_launch(args: argparse.Namespace) -> tuple[int, dict]:
    exe = Path(args.exe)
    if not exe.is_file():
        raise ScriptError(f"executable not found: {exe}")
    env = os.environ.copy()
    for pair in args.env or []:
        key, sep, value = pair.partition("=")
        if not sep or not key:
            raise ScriptError(f"--env needs KEY=VALUE: {pair}")
        env[key] = value
    for key in args.unset or []:
        env.pop(key, None)
    ready = parse_spec(args.ready) if args.ready else None
    mark = now_utc()
    process = start_detached([str(exe.resolve()), *(args.arg or [])], env, str(exe.resolve().parent))
    result: dict = {"pid": process.pid, "mark": iso(mark), "exe": str(exe.resolve()), "args": args.arg or []}
    if ready is None:
        return 0, result
    deadline = time.monotonic() + args.timeout
    log_error = None
    while time.monotonic() < deadline:
        code = process.poll()
        try:
            entries = window(read_entries(args.logs, args.ts_key), mark, None)
            log_error = None
        except ScriptError as error:
            entries, log_error = [], str(error)
        hits = [entry for entry in entries if matches(ready, entry.data)]
        if hits:
            result["ready"] = hits[0].raw
            return 0, result
        if code is not None:
            result.update(exited=True, exit_code=code, seen=[entry.raw for entry in entries[-args.max_lines:]])
            result["reason"] = f"process exited with {code} before {ready.text}"
            if log_error:
                result["log_error"] = log_error
            return 1, result
        time.sleep(0.25)
    # Point at the glob when no log was readable, not at the app.
    result["reason"] = log_error or f"no {ready.text} within {args.timeout}s; the process is still running"
    return 1, result


def start_detached(command: list[str], env: dict, cwd: str) -> subprocess.Popen:
    if sys.platform != "win32":
        return subprocess.Popen(command, env=env, cwd=cwd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, start_new_session=True)
    # Break away from the job of `uv run` and the agent's shell, which may kill the tree when they exit.
    flags = subprocess.DETACHED_PROCESS | subprocess.CREATE_NEW_PROCESS_GROUP
    try:
        return subprocess.Popen(command, env=env, cwd=cwd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, creationflags=flags | subprocess.CREATE_BREAKAWAY_FROM_JOB)
    except OSError:
        return subprocess.Popen(command, env=env, cwd=cwd, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                stderr=subprocess.DEVNULL, creationflags=flags)


# ---------------------------------------------------------------------------
# Plan commands
# ---------------------------------------------------------------------------


def split_row(line: str) -> list[str]:
    body = line.strip()
    if body.startswith("|"):
        body = body[1:]
    if body.endswith("|") and not body.endswith("\\|"):
        body = body[:-1]
    cells = re.split(r"(?<!\\)\|", body)
    return [cell.strip() for cell in cells]


def is_separator(cells: list[str]) -> bool:
    return bool(cells) and all(re.fullmatch(r":?-{3,}:?", cell) for cell in cells)


@dataclass
class Table:
    start: int
    header: list[str]
    rows: list[tuple[int, list[str]]] = field(default_factory=list)


def find_tables(lines: list[str]) -> list[Table]:
    tables: list[Table] = []
    index = 0
    while index < len(lines):
        if lines[index].lstrip().startswith("|") and index + 1 < len(lines) and is_separator(split_row(lines[index + 1])):
            table = Table(index, split_row(lines[index]))
            index += 2
            while index < len(lines) and lines[index].lstrip().startswith("|"):
                table.rows.append((index, split_row(lines[index])))
                index += 1
            tables.append(table)
        else:
            index += 1
    return tables


def results_table(lines: list[str], plan: Path) -> Table:
    # The results table is the last one whose first column is `#`; columns are read by position, not by name,
    # so a plan in any language works: 1 id, 2 result, 3 evidence.
    candidates = [table for table in find_tables(lines) if table.header and table.header[0] == "#" and len(table.header) >= 3]
    if not candidates:
        raise ScriptError(f"no results table (first column '#', at least 3 columns) in {plan}")
    return candidates[-1]


def read_plan(path: str) -> tuple[Path, list[str]]:
    plan = Path(path)
    if not plan.is_file():
        raise ScriptError(f"plan not found: {plan}")
    return plan, plan.read_text(encoding="utf-8").splitlines()


def frontmatter(lines: list[str]) -> tuple[int, int] | None:
    if not lines or lines[0].strip() != "---":
        return None
    for index in range(1, len(lines)):
        if lines[index].strip() == "---":
            return 0, index
    return None


def front_value(lines: list[str], key: str) -> str | None:
    bounds = frontmatter(lines)
    if bounds is None:
        return None
    for line in lines[bounds[0] + 1: bounds[1]]:
        name, sep, value = line.partition(":")
        if sep and name.strip() == key:
            return value.strip()
    return None


ACTORS = {"agent", "user-hardware", "observation"}
# `row:S1`, not `@S1`: PowerShell reads an unquoted @name as splatting and passes nothing.
ROW_REF = re.compile(r"^row:([^.\s]+)(?:\.(since|until))?$")


def resolve_moment(value: str, option: str, plan_path: str | None) -> datetime:
    """An ISO time, or `row:S1` / `row:S1.until`: the window that `fill --from-check` recorded in that row's evidence."""
    ref = ROW_REF.match(value.strip())
    if not ref:
        return parse_moment(value, option)
    if not plan_path:
        raise ScriptError(f"{option} {value} names a row; give --plan")
    row, bound = ref.group(1), ref.group(2) or "since"
    plan, lines = read_plan(plan_path)
    rows = [cells for _, cells in results_table(lines, plan).rows if cells and (cells[0] == row or cells[0].startswith(f"{row} (rerun"))]
    if not rows:
        raise ScriptError(f"row {row} is not in the results table of {plan}")
    found = re.search(rf"\b{bound} (\d{{4}}-\d\d-\d\dT[\d:.]+Z)", rows[-1][2] if len(rows[-1]) > 2 else "")
    if not found:
        raise ScriptError(f"row {row} records no {bound} time (fill --from-check writes one)")
    return parse_moment(found.group(1), option)


PLAN_KEYS = ["epic", "type", "machine", "base_head", "created", "status"]
QUOTED_SPEC = re.compile(r"--(expect|for|allow|ready)[ =](\"[^\"]*\"|'[^']*')")


def row_problems(cells: list[str]) -> list[str]:
    """What keeps one row of a seven-column scenarios table from running as written."""
    row_id, actor = cells[0], cells[2] if len(cells) > 2 else ""
    assertion = cells[4].strip() if len(cells) > 4 else ""
    problems: list[str] = []
    if actor not in ACTORS:
        problems.append(f"{row_id}: Actor '{actor}' is not one of {', '.join(sorted(ACTORS))}")
    if actor != "observation" and assertion in {"", "—", "-"}:
        problems.append(f"{row_id}: no Assertion")
    # A check script that cues the user itself stands in for `wait --for`.
    if actor == "user-hardware" and "--for" not in assertion and not re.search(r"\.(ps1|py|sh|cmd|bat)\b", assertion):
        problems.append(f"{row_id}: a user-hardware row names no `wait --for` SPEC")
    for _, quoted in QUOTED_SPEC.findall(assertion.replace("\\|", "|")):
        try:
            parse_spec(quoted[1:-1])
        except ScriptError as error:
            problems.append(f"{row_id}: {error}")
    return problems


def scenario_table(lines: list[str], plan: Path) -> Table | None:
    # The scenarios table is the first `#` table with at least five columns; preparation and results tables have three.
    results = results_table(lines, plan)
    return next((table for table in find_tables(lines)
                 if table.header and table.header[0] == "#" and len(table.header) >= 5 and table.start != results.start), None)


def cmd_next(args: argparse.Namespace) -> tuple[int, dict]:
    plan, lines = read_plan(args.plan)
    table = results_table(lines, plan)
    filled = [cells[0] for _, cells in table.rows if len(cells) > 1 and cells[1]]
    empty = [cells[0] for _, cells in table.rows if len(cells) < 2 or not cells[1]]
    upcoming = empty[0] if empty else None
    scenarios = scenario_table(lines, plan)
    problems: list[str] = []
    row: dict | list | None = None
    if scenarios is None:
        problems.append("no scenarios table (first column '#', five or more columns)")
    else:
        header = scenarios.header
        if len(header) < 7:
            problems.append(f"the scenarios table has {len(header)} columns, without Actor and Assertion: derive both at each announcement")
        else:
            for _, cells in scenarios.rows:
                problems += row_problems(cells)
        result_ids = {cells[0] for _, cells in table.rows if cells}
        problems += [f"{cells[0]} has no row in the results table" for _, cells in scenarios.rows if cells and cells[0] not in result_ids]
        known = {cells[0] for _, cells in scenarios.rows if cells}
        steps = {cells[0].split(".")[0] for other in find_tables(lines) if other.header and other.header[0] == "#"
                 and other.start not in {table.start, scenarios.start} for _, cells in other.rows if cells}
        problems += [f"results row {row_id} has no scenario or preparation step" for row_id in result_ids
                     if " (rerun" not in row_id and row_id not in known | steps]
    missing_keys = [key for key in PLAN_KEYS if front_value(lines, key) is None]
    if missing_keys:
        problems.append(f"frontmatter lacks {', '.join(missing_keys)}")
        for _, cells in scenarios.rows:
            if cells and cells[0] == upcoming:
                row = dict(zip(header, cells))
    if row is None and upcoming:
        # A preparation row such as P0 stands for its steps P0.1, P0.2, ...
        steps = [dict(zip(other.header, cells)) for other in find_tables(lines)
                 if other.header and other.header[0] == "#" and other.start != table.start
                 for _, cells in other.rows if cells and cells[0].startswith(f"{upcoming}.")]
        row = steps or None
    result = {
        "plan": str(plan),
        "status": front_value(lines, "status"),
        "next": upcoming,
        "row": row,
        "filled": filled,
        "empty": empty,
        "problems": problems,
    }
    return 0, result


def cell(text: str) -> str:
    return text.strip().replace("\r\n", "\n").replace("|", "\\|").replace("\n", "<br>")


def write_lines(plan: Path, lines: list[str]) -> None:
    plan.write_text("\n".join(lines) + "\n", encoding="utf-8")


def clip(raw: str, limit: int = 240) -> str:
    return raw if len(raw) <= limit else raw[: limit - 1] + "…"


def check_evidence(check: dict) -> str:
    """The window, the assertion with its counts and up to three decisive lines, rendered the same way every time."""
    parts = [f"window since {check['window']['since']} until {check['window']['until']}"]
    decisive: list[str] = []
    if check.get("empty_window"):
        parts.append("empty window")
    for item in check.get("expects", []):
        parts.append(f"`{item['spec']}` want {item['want']}, got {item['count']}")
        if not item["ok"] or not decisive:
            decisive += item["lines"][:1]
    levels = check.get("forbidden_levels")
    if levels:
        allowed = f" (allowed: {'; '.join(levels['allowed'])}, {levels['allowed_count']})" if levels["allowed"] else ""
        parts.append(f"forbid {','.join(levels['levels'])}: {levels['count']}{allowed}")
        decisive = levels["lines"][:2] + decisive
    keys = check.get("forbidden_keys")
    if keys:
        parts.append(f"forbid keys {','.join(keys['keys'])}: {keys['count']}")
        decisive = keys["lines"][:1] + decisive
    order = check.get("order")
    if order:
        parts.append("order ok" if order["ok"] else f"order broken, first seen: {', '.join(order['first_seen'])}")
    lines = "\n".join(f"`{clip(line)}`" for line in decisive[:3])
    return "; ".join(parts) + (f"\n{lines}" if lines else "")


def frozen_assertion(lines: list[str], plan: Path, row: str) -> str | None:
    """The row's Assertion cell, when the plan has one (column 5 of a seven-column scenarios table)."""
    scenarios = scenario_table(lines, plan)
    if scenarios is None or len(scenarios.header) < 7:
        return None
    for _, cells in scenarios.rows:
        if cells and cells[0] == row and len(cells) > 4:
            text = cells[4].strip()
            return text if text and text not in {"—", "-"} else None
    return None


def codes(text: str, option: str) -> set[int]:
    try:
        return {int(item) for item in split_list([text])}
    except ValueError as error:
        raise ScriptError(f"{option} takes comma-separated integers: {text}") from error


def quoted_lines(log: str, pattern: str) -> list[str]:
    try:
        text = Path(log).read_text(encoding="utf-8-sig", errors="replace")
    except OSError as error:
        raise ScriptError(f"--log {log} cannot be read: {error}") from error
    try:
        matcher = re.compile(pattern)
    except re.error as error:
        raise ScriptError(f"--quote is not a regular expression: {error}") from error
    return [line.strip() for line in text.splitlines() if matcher.search(line)][-3:]


def verdict_cells(args: argparse.Namespace, lines: list[str], plan: Path) -> tuple[str, str]:
    """Result and evidence for a row: from a saved check, from a check script's exit code, or as given."""
    if not (args.from_check or args.exit_code is not None):
        if args.result is None or args.evidence is None:
            raise ScriptError("give --from-check or --exit-code, or both --result and --evidence")
        return args.result, args.evidence
    if args.from_check:
        try:
            check = json.loads(Path(args.from_check).read_text(encoding="utf-8"))
            verdict, evidence = check["verdict"], check_evidence(check)
            expects = check.get("assertion", {}).get("expect", [])
        except (OSError, json.JSONDecodeError, KeyError, TypeError, AttributeError) as error:
            raise ScriptError(f"--from-check {args.from_check} is not a saved check: {error}") from error
        frozen = frozen_assertion(lines, plan, args.row)
        if frozen is not None:
            # The assertion was frozen at the announcement; a SPEC edited after the outcome is a reinterpretation.
            stray = [spec for spec in expects if spec not in frozen]
            if stray:
                raise ScriptError(f"{', '.join(stray)} is not in the Assertion of {args.row}: {frozen}")
    else:
        passing, failing = codes(args.pass_codes, "--pass-codes"), codes(args.fail_codes, "--fail-codes")
        if args.exit_code in passing:
            verdict = "pass"
        elif args.exit_code in failing:
            verdict = "fail"
        else:
            raise ScriptError(f"exit {args.exit_code} is neither a pass ({args.pass_codes}) nor a fail ({args.fail_codes}) code: a script error, not a verdict")
        evidence = f"exit {args.exit_code}" + (f"; run log {args.log}" if args.log else "")
        if args.quote:
            if not args.log:
                raise ScriptError("--quote needs --log")
            evidence += "".join(f"\n`{clip(line)}`" for line in quoted_lines(args.log, args.quote))
    if args.measured:
        # A measurement records a value and fails nothing; the counts stay in the evidence.
        verdict = "measured"
    note = (args.result or "").strip()
    # Anything after "user:" is the user's own verdict, which may differ from the script's.
    claimed = set(re.findall(r"\b(pass|fail)\b", note.split("user:", 1)[0].lower()))
    if claimed - {verdict}:
        raise ScriptError(f"--result says '{', '.join(sorted(claimed))}' but the script's verdict is '{verdict}'; a user's rejection is written as 'user: ...'")
    return f"{verdict} {note}".strip(), evidence + (f"\n{args.evidence}" if args.evidence else "")


def cmd_fill(args: argparse.Namespace) -> tuple[int, dict]:
    plan, lines = read_plan(args.plan)
    table = results_table(lines, plan)
    rows = [(index, cells) for index, cells in table.rows if cells and cells[0] == args.row]
    if not rows:
        raise ScriptError(f"row {args.row} is not in the results table of {plan}")
    index, cells = rows[0]
    width = len(table.header)
    result_text, evidence_text = verdict_cells(args, lines, plan)
    values = [args.row, cell(result_text), cell(evidence_text)] + [""] * (width - 3)
    if len(cells) > 1 and cells[1]:
        if not args.rerun:
            raise ScriptError(f"row {args.row} is already filled; a result is never overwritten (use --rerun for an explicit repeat)")
        reruns = [cells for _, cells in table.rows if cells and cells[0].startswith(f"{args.row} (rerun")]
        values[0] = f"{args.row} (rerun {len(reruns) + 1})"
        insert_at = max(i for i, c in table.rows if c and (c[0] == args.row or c[0].startswith(f"{args.row} (rerun"))) + 1
        lines.insert(insert_at, "| " + " | ".join(values) + " |")
        result = {"plan": str(plan), "row": values[0], "line": insert_at + 1, "rerun": True}
    else:
        lines[index] = "| " + " | ".join(values) + " |"
        result = {"plan": str(plan), "row": args.row, "line": index + 1, "rerun": False}
    write_lines(plan, lines)
    if args.from_check:
        # A saved check fills one row; a later row that forgot --out then finds no file instead of this verdict.
        Path(args.from_check).unlink(missing_ok=True)
    return 0, result


def cmd_new_pass(args: argparse.Namespace) -> tuple[int, dict]:
    plan, lines = read_plan(args.plan)
    table = results_table(lines, plan)
    ids = [cells[0] for _, cells in table.rows if cells and " (rerun" not in cells[0]]
    width = len(table.header)
    while lines and not lines[-1].strip():
        lines.pop()
    lines += ["", f"## {args.heading}", ""]
    if args.note:
        lines += [args.note, ""]
    lines += ["| " + " | ".join(table.header) + " |", "|" + "|".join(["---"] * width) + "|"]
    lines += ["| " + " | ".join([row_id] + [""] * (width - 1)) + " |" for row_id in ids]
    write_lines(plan, lines)
    return 0, {"plan": str(plan), "heading": args.heading, "rows": ids}


def cmd_meta(args: argparse.Namespace) -> tuple[int, dict]:
    plan, lines = read_plan(args.plan)
    bounds = frontmatter(lines)
    if bounds is None:
        raise ScriptError(f"{plan} has no YAML frontmatter")
    changed: dict[str, str] = {}
    for pair in args.set:
        key, sep, value = pair.partition("=")
        key = key.strip()
        if not sep or not key:
            raise ScriptError(f"meta needs KEY=VALUE: {pair}")
        start, end = frontmatter(lines)  # type: ignore[misc]
        for index in range(start + 1, end):
            name, colon, _ = lines[index].partition(":")
            if colon and name.strip() == key:
                lines[index] = f"{key}: {value.strip()}"
                break
        else:
            lines.insert(end, f"{key}: {value.strip()}")
        changed[key] = value.strip()
    write_lines(plan, lines)
    return 0, {"plan": str(plan), "set": changed}


# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(prog="acceptance", description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    commands = parser.add_subparsers(dest="command", required=True)

    def logs(sub: argparse.ArgumentParser) -> None:
        sub.add_argument("--logs", required=True, help="glob of the JSONL logs; %%VAR%%, $VAR and ~ are expanded")
        sub.add_argument("--ts-key", default="ts", help="timestamp key (default ts)")
        sub.add_argument("--max-lines", type=int, default=5, help="log lines quoted per finding (default 5)")

    commands.add_parser("mark", help="print the current UTC time")

    launch = commands.add_parser("launch", help="start an executable detached and wait for a ready entry")
    launch.add_argument("--exe", required=True, help="path of the executable")
    launch.add_argument("--arg", action="append", help="one argument for the executable; repeat for more")
    launch.add_argument("--env", action="append", help="KEY=VALUE set for the process; repeat for more")
    launch.add_argument("--unset", action="append", help="environment variable removed for the process")
    launch.add_argument("--ready", help="SPEC of the entry that means the app is ready; omit to return at once")
    launch.add_argument("--timeout", type=float, default=30, help="seconds to wait for --ready (default 30)")
    logs(launch)

    close = commands.add_parser("close", help="post WM_CLOSE to a process's windows and wait for exit")
    close.add_argument("--pid", type=int, required=True)
    close.add_argument("--timeout", type=float, default=10, help="seconds to wait for exit (default 10)")

    focus = commands.add_parser("focus", help="bring a process's window to the foreground and verify it")
    focus.add_argument("--pid", type=int, required=True)

    foreground = commands.add_parser("foreground", help="report the foreground window")
    foreground.add_argument("--pid", type=int, help="exit 1 unless the foreground window belongs to this pid")

    wait = commands.add_parser("wait", help="poll the logs until every --for SPEC has a match")
    wait.add_argument("--since", required=True, help="ISO time, usually a mark, or row:ROW with --plan")
    wait.add_argument("--plan", help="plan whose results rows row:ROW refers to")
    wait.add_argument("--for", dest="for_", action="append", required=True, help="SPEC to wait for; repeat for more")
    wait.add_argument("--timeout", type=float, default=60, help="seconds before giving up (default 60)")
    wait.add_argument("--settle", type=float, default=2, help="seconds to keep waiting after the match (default 2)")
    wait.add_argument("--say", help="the instruction, spoken (with --voice) and captioned on the overlay before the countdown")
    wait.add_argument("--voice", help="installed voice culture or name, e.g. pl-PL; omit for captions only")
    wait.add_argument("--beep", action="store_true", help="sound cues: ticks, a long go tone, a rising tone on a match, a low tone on timeout")
    wait.add_argument("--countdown", type=float, default=0, help="seconds of ticks before the go tone (default 0)")
    wait.add_argument("--poll", type=float, default=0.25, help=argparse.SUPPRESS)
    logs(wait)

    check = commands.add_parser("check", help="assert a window of the logs")
    check.add_argument("--since", required=True, help="ISO time, inclusive; or row:ROW (that row's recorded since) with --plan")
    check.add_argument("--until", help="ISO time, exclusive (default now); or row:ROW.until with --plan")
    check.add_argument("--plan", help="plan whose results rows row:ROW refers to")
    check.add_argument("--allow-empty", action="store_true", help="a window without any entry may pass (it fails by default)")
    check.add_argument("--out", help="also save the JSON here, for fill --from-check")
    check.add_argument("--expect", action="append", help="SPEC with its count; repeat for more")
    check.add_argument("--forbid-level", action="append", help="levels that fail the window, e.g. warn,error")
    check.add_argument("--allow", action="append", help="SPEC exempt from --forbid-level (a deliberate warn)")
    check.add_argument("--forbid-key", action="append", help="keys that must not appear anywhere in an entry, e.g. vk,scan")
    check.add_argument("--ordered", action="store_true", help="the --expect specs must first appear in the given order")
    check.add_argument("--level-key", default="level", help="level key (default level)")
    logs(check)

    inventory = commands.add_parser("inventory", help="messages and field paths in the runs of one build")
    inventory.add_argument("--version-contains", required=True, help="text the start entry's version must contain, e.g. a short HEAD")
    inventory.add_argument("--start", default="msg=start", help="SPEC of the entry that starts a run (default msg=start)")
    inventory.add_argument("--version-path", default="data.version", help="path of the version in that entry (default data.version)")
    inventory.add_argument("--msg-key", default="msg", help="message key (default msg)")
    inventory.add_argument("--level-key", default="level", help="level key (default level)")
    logs(inventory)

    say_parser = commands.add_parser("say", help="caption a line on the overlay and speak it")
    say_parser.add_argument("text")
    say_parser.add_argument("--voice", help="installed voice culture or name, e.g. pl-PL; omit for the caption only")
    say_parser.add_argument("--hold", type=float, default=0, help="seconds the caption stays (default: until the next one)")

    overlay = commands.add_parser("overlay", help="start, stop or query the caption overlay (Windows)")
    overlay.add_argument("action", choices=["start", "stop", "status"])
    commands.add_parser("overlay-serve")

    nxt = commands.add_parser("next", help="first empty row of the results table")
    nxt.add_argument("plan")

    fill = commands.add_parser("fill", help="write one row of the results table")
    fill.add_argument("plan")
    fill.add_argument("row", help="the row id in the first column, e.g. S3")
    source = fill.add_mutually_exclusive_group()
    source.add_argument("--from-check", help="JSON saved by check --out: writes the verdict and the evidence from it, then deletes it")
    source.add_argument("--exit-code", type=int, help="a check script's exit code, judged by --pass-codes and --fail-codes")
    fill.add_argument("--pass-codes", default="0", help="with --exit-code: codes that mean pass (default 0)")
    fill.add_argument("--fail-codes", default="1", help="with --exit-code: codes that mean fail (default 1); any other code is a script error")
    fill.add_argument("--log", help="with --exit-code: the script's run log")
    fill.add_argument("--quote", help="with --log: regex of the run log's verdict lines to quote (the last three matches)")
    fill.add_argument("--measured", action="store_true", help="a measurement row: writes 'measured' instead of pass/fail")
    fill.add_argument("--result", help="the result; with --from-check or --exit-code, a note after the verdict")
    fill.add_argument("--evidence", help="the evidence; with --from-check or --exit-code, added after the rendered one")
    fill.add_argument("--rerun", action="store_true", help="the user asked to repeat a filled row: add a new row after it")

    new_pass = commands.add_parser("new-pass", help="append an empty results table for a new pass; next and fill then use it")
    new_pass.add_argument("plan")
    new_pass.add_argument("--heading", required=True, help="heading of the new section, e.g. the date and HEAD of the pass")
    new_pass.add_argument("--note", help="one line under the heading: why this pass starts")

    meta = commands.add_parser("meta", help="set frontmatter keys")
    meta.add_argument("plan")
    meta.add_argument("--set", action="append", required=True, help="KEY=VALUE; repeat for more")
    return parser


HANDLERS = {
    "mark": cmd_mark, "launch": cmd_launch, "focus": cmd_focus, "close": cmd_close, "foreground": cmd_foreground,
    "wait": cmd_wait, "check": cmd_check, "inventory": cmd_inventory, "say": cmd_say, "overlay": cmd_overlay, "next": cmd_next, "fill": cmd_fill,
    "new-pass": cmd_new_pass, "meta": cmd_meta,
}


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if args.command == "overlay-serve":
        return serve_overlay()
    try:
        code, result = HANDLERS[args.command](args)
    except ScriptError as error:
        print(f"acceptance {args.command}: {error}", file=sys.stderr)
        return 2
    except Exception as error:  # noqa: BLE001 - a crash must never read as exit 1, a failed verdict
        print(f"acceptance {args.command}: unexpected {type(error).__name__}: {error}", file=sys.stderr)
        return 2
    sys.stdout.reconfigure(encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
