"""Run a /delegate brief in a CLI agent session and print its handoff.

Starts a new Codex CLI (`codex exec`) or OpenCode (`opencode run`) session with the
brief, records the session id as soon as the CLI reports it, and when the session
ends prints the delegate's final message (its handoff) together with the session id
and the command that resumes the session. Run it in the background: the delegating
agent is told when it exits.

Files written to --out: session.json (as soon as the id is known), events.jsonl
(raw CLI events), handoff.md (the final message).

Exit code: the CLI's exit code, or 2 when the CLI cannot be started.
"""

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

OPENCODE_PROMPT = (
    "Your complete task is in the attached file {name}. Read it in full and carry it out; "
    "its last section says what your final message must contain."
)


def build_command(args, brief: Path):
    if args.adapter == "codex-cli":
        exe = shutil.which("codex")
        cmd = [exe, "exec", "--json", "-C", str(args.cwd), "-s", args.sandbox,
               "-o", str(args.out / "last-message.md")]
        if args.model:
            cmd += ["-m", args.model]
        if args.effort:
            cmd += ["-c", f'model_reasoning_effort="{args.effort}"']
        if args.worktree:
            cmd += ["--worktree"]
        cmd += ["-"]
        return exe, cmd, brief.read_bytes()
    exe = shutil.which("opencode")
    cmd = [exe, "run", "--format", "json", "--title", args.title, "-f", str(brief)]
    if args.model:
        cmd += ["-m", f"{args.model}#{args.effort}" if args.effort else args.model]
    if args.auto:
        cmd += ["--auto"]
    cmd += [OPENCODE_PROMPT.format(name=brief.name)]
    return exe, cmd, b""


def session_id(adapter, event):
    if adapter == "codex-cli":
        return event.get("thread_id") if event.get("type") == "thread.started" else None
    return event.get("sessionID")


def message_text(adapter, event):
    if adapter == "codex-cli":
        item = event.get("item") or {}
        if event.get("type") == "item.completed" and item.get("type") == "agent_message":
            return item.get("text")
        return None
    part = event.get("part") or {}
    return part.get("text") if event.get("type") == "text" else None


def resume_command(adapter, sid):
    return f"codex resume {sid}" if adapter == "codex-cli" else f"opencode -s {sid}"


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--adapter", required=True, choices=["codex-cli", "opencode"])
    p.add_argument("--brief", required=True, type=Path, help="file with the full brief")
    p.add_argument("--cwd", required=True, type=Path, help="working directory of the session")
    p.add_argument("--out", required=True, type=Path, help="directory for session.json, events.jsonl, handoff.md")
    p.add_argument("--title", default="delegate", help="session title (OpenCode)")
    p.add_argument("--model", help="model id as the CLI expects it")
    p.add_argument("--effort", help="reasoning effort (Codex) or model variant (OpenCode)")
    p.add_argument("--sandbox", default="workspace-write", help="Codex sandbox mode")
    p.add_argument("--worktree", action="store_true", help="Codex: run in a new managed git worktree")
    p.add_argument("--auto", action="store_true", help="OpenCode: auto-approve permissions not explicitly denied")
    args = p.parse_args()

    args.out.mkdir(parents=True, exist_ok=True)
    brief = args.brief.resolve()
    exe, cmd, stdin_data = build_command(args, brief)
    if exe is None:
        print(f"error: {args.adapter} is not installed (command not found)", file=sys.stderr)
        return 2

    sid, last = None, None
    texts = []
    with open(args.out / "events.jsonl", "w", encoding="utf-8") as log:
        proc = subprocess.Popen(cmd, cwd=args.cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                stderr=subprocess.PIPE)
        proc.stdin.write(stdin_data)
        proc.stdin.close()
        for raw in proc.stdout:
            line = raw.decode("utf-8", errors="replace").strip()
            if not line:
                continue
            log.write(line + "\n")
            log.flush()
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                continue
            if sid is None and (found := session_id(args.adapter, event)):
                sid = found
                meta = {"adapter": args.adapter, "session_id": sid, "resume": resume_command(args.adapter, sid)}
                (args.out / "session.json").write_text(json.dumps(meta, indent=2), encoding="utf-8")
                print(f"SESSION {args.adapter} {sid}", flush=True)
            if (text := message_text(args.adapter, event)):
                texts.append(text)
        stderr = proc.stderr.read().decode("utf-8", errors="replace")
        code = proc.wait()

    last_file = args.out / "last-message.md"
    if args.adapter == "codex-cli" and last_file.exists():
        last = last_file.read_text(encoding="utf-8").strip()
    last = last or (texts[-1].strip() if texts else "")
    (args.out / "handoff.md").write_text(last, encoding="utf-8")

    print("=== DELEGATE FINISHED ===")
    print(f"adapter: {args.adapter}")
    print(f"session: {sid or 'unknown'}")
    if sid:
        print(f"resume: {resume_command(args.adapter, sid)}")
    print(f"exit code: {code}")
    print(f"files: {args.out}")
    print("=== HANDOFF ===")
    print(last or "(the session ended without a final message)")
    if code != 0 and stderr.strip():
        print("=== STDERR (tail) ===")
        print("\n".join(stderr.strip().splitlines()[-20:]))
    return code


if __name__ == "__main__":
    sys.exit(main())
