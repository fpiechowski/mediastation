import json
import subprocess
import sys
import tempfile
import threading
import time
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "acceptance.py"


def run(*args: str) -> tuple[subprocess.CompletedProcess[str], dict | None]:
    """Run the script as a subprocess, the way the agent does."""
    result = subprocess.run([sys.executable, str(SCRIPT), *args], capture_output=True, text=True, encoding="utf-8", check=False)
    data = json.loads(result.stdout) if result.stdout.strip() else None
    return result, data


def entry(ts: str, msg: str, level: str = "info", **data: object) -> str:
    return json.dumps({"ts": ts, "level": level, "src": "app", "msg": msg, "data": data})


LOG = [
    entry("2026-10-24T10:00:00.000Z", "start", pid=4242, args=[]),
    entry("2026-10-24T10:00:00.500Z", "inputRouting", mode="shell", epoch=1),
    entry("2026-10-24T10:00:01.000Z", "snapshotPublished", rev=1),
    entry("2026-10-24T10:00:02.000Z", "action", kind="home", phase="press", source="remote"),
    entry("2026-10-24T10:00:03.000Z", "debugHangUi", level="warn", seconds=5),
    entry("2026-10-24T10:00:04.000Z", "action", kind="ok", phase="long", source="remote"),
    entry("2026-10-24T10:00:05.000Z", "rawInput", hasVk=True, vk=36),
    entry("2026-10-24T10:00:06.000Z", "stop", exitCode=0),
]

PLAN = """---
epic: epic-x
type: acceptance-plan
status: planned
---

# Plan

| # | Krok | Oczekiwane |
|---|------|------------|
| P0.1 | build | ok |

| # | Scenariusz | Aktor | Oczekiwane | Asercja | Rodzaj | Done when |
|---|---|---|---|---|---|---|
| S1 | start | agent | ready | `--expect msg=start:1` | pass/fail | 1 |
| S2 | Home | user-hardware | one action | `wait --for "msg=action"`; `--expect msg=action:1` | pass/fail | 2 |
| S3 | cursor | user | stays | — | obserwacja | — |
| S4 | press | user-hardware | x | `--expect "msg=a:5-2"` | pass/fail | 3 |

## Wyniki

| # | Wynik | Dowód |
|---|-------|-------|
| P0 | pass | built |
| S1 | | |
| S2 | | |
"""


class AcceptanceTest(unittest.TestCase):
    def setUp(self) -> None:
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.log = self.root / "app-2026-10-24.jsonl"
        self.log.write_text("\n".join(LOG) + "\n{\"ts\": \"torn", encoding="utf-8")
        self.logs = str(self.root / "app-*.jsonl")
        self.plan = self.root / "plan.md"
        self.plan.write_text(PLAN, encoding="utf-8")

    def check(self, *args: str) -> tuple[subprocess.CompletedProcess[str], dict]:
        result, data = run("check", "--logs", self.logs, "--since", "2026-10-24T10:00:00Z", "--until", "2026-10-24T10:00:07Z", *args)
        self.assertIsNotNone(data, result.stderr)
        return result, data  # type: ignore[return-value]

    def test_counts_pass_and_fail(self) -> None:
        result, data = self.check("--expect", "msg=action,data.kind=home,data.source=remote:1", "--expect", "msg=inputHubError:0")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(data["verdict"], "pass")
        self.assertEqual(data["expects"][0]["count"], 1)
        result, data = self.check("--expect", "msg=action:1")
        self.assertEqual(result.returncode, 1)
        self.assertEqual(data["expects"][0]["count"], 2)

    def test_count_forms_and_negation(self) -> None:
        _, data = self.check("--expect", "msg=action:>=2", "--expect", "msg=action:1-2", "--expect", "msg=action,data.phase!=press:<=1")
        self.assertEqual([item["ok"] for item in data["expects"]], [True, True, True])

    def test_window_is_half_open(self) -> None:
        result, data = run("check", "--logs", self.logs, "--since", "2026-10-24T10:00:02Z", "--until", "2026-10-24T10:00:04Z",
                           "--expect", "msg=action:1")
        self.assertEqual(result.returncode, 0, data)

    def test_forbidden_level_with_allow(self) -> None:
        result, data = self.check("--forbid-level", "warn,error")
        self.assertEqual(result.returncode, 1)
        self.assertEqual(data["forbidden_levels"]["count"], 1)
        result, data = self.check("--forbid-level", "warn,error", "--allow", "msg=debugHangUi")
        self.assertEqual(result.returncode, 0)
        self.assertEqual(data["forbidden_levels"]["allowed_count"], 1)

    def test_forbidden_key_anywhere(self) -> None:
        result, data = self.check("--forbid-key", "vk,scan,usage")
        self.assertEqual(result.returncode, 1)
        self.assertIn('"vk": 36', data["forbidden_keys"]["lines"][0])

    def test_ordered(self) -> None:
        result, _ = self.check("--ordered", "--expect", "msg=start", "--expect", "msg=inputRouting", "--expect", "msg=stop")
        self.assertEqual(result.returncode, 0)
        result, data = self.check("--ordered", "--expect", "msg=stop", "--expect", "msg=start")
        self.assertEqual(result.returncode, 1)
        self.assertFalse(data["order"]["ok"])

    def test_s16_accepts_async_completion_order_without_weakening_ordered(self) -> None:
        self.log.write_text("\n".join([
            entry("2026-10-24T10:00:02.100Z", "windowCommand", type="MinimizeWindow", result="completed"),
            entry("2026-10-24T10:00:03.100Z", "audioRuleApplied", muted=True, sessions=2),
        ]) + "\n", encoding="utf-8")
        result, data = self.check(
            "--expect", "msg=audioRuleApplied,data.muted=true,data.sessions,data.sessions!=0:>=1",
            "--expect", "msg=windowCommand,data.type=MinimizeWindow,data.result=completed:1",
        )
        self.assertEqual(result.returncode, 0, data)
        self.assertEqual([item["count"] for item in data["expects"]], [1, 1])

        result, data = self.check(
            "--ordered",
            "--expect", "msg=audioRuleApplied,data.muted=true,data.sessions,data.sessions!=0:>=1",
            "--expect", "msg=windowCommand,data.type=MinimizeWindow,data.result=completed:1",
        )
        self.assertEqual(result.returncode, 1)
        self.assertFalse(data["order"]["ok"])

    def test_bool_values_render_lowercase(self) -> None:
        result, _ = self.check("--expect", "msg=rawInput,data.hasVk=true:1")
        self.assertEqual(result.returncode, 0)

    def test_contains(self) -> None:
        result, _ = self.check("--expect", "msg=action,data.source~=rem:2", "--expect", "msg=start,data.args~=x:0")
        self.assertEqual(result.returncode, 0)

    def test_wait_times_out_with_what_it_saw(self) -> None:
        result, data = run("wait", "--logs", self.logs, "--since", "2026-10-24T10:00:00Z", "--for", "msg=nothing", "--timeout", "0.5", "--settle", "0")
        self.assertEqual(result.returncode, 1)
        self.assertFalse(data["found"])
        self.assertEqual(data["missing"], ["msg=nothing"])
        self.assertTrue(data["seen"])

    def test_wait_sees_an_entry_written_later(self) -> None:
        since = datetime.now(timezone.utc)

        def write_later() -> None:
            time.sleep(0.6)
            ts = (datetime.now(timezone.utc) + timedelta(milliseconds=1)).isoformat(timespec="milliseconds").replace("+00:00", "Z")
            with open(self.log, "a", encoding="utf-8") as handle:
                handle.write("\n" + entry(ts, "action", kind="back", source="remote") + "\n")

        thread = threading.Thread(target=write_later)
        thread.start()
        result, data = run("wait", "--logs", self.logs, "--since", since.isoformat(), "--for", "msg=action,data.kind=back",
                           "--timeout", "10", "--settle", "0")
        thread.join()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertTrue(data["found"])

    def test_next_and_fill(self) -> None:
        result, data = run("next", str(self.plan))
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(data["next"], "S1")
        self.assertEqual(data["status"], "planned")
        result, _ = run("fill", str(self.plan), "S1", "--result", "pass", "--evidence", "a | b\nc")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("| S1 | pass | a \\| b<br>c |", self.plan.read_text(encoding="utf-8"))
        _, data = run("next", str(self.plan))
        self.assertEqual(data["next"], "S2")

    def test_fill_never_overwrites(self) -> None:
        result, _ = run("fill", str(self.plan), "P0", "--result", "fail", "--evidence", "x")
        self.assertEqual(result.returncode, 2)
        self.assertEqual(result.stderr.count("\n"), 1, result.stderr)
        self.assertIn("never overwritten", result.stderr)
        self.assertIn("| P0 | pass | built |", self.plan.read_text(encoding="utf-8"))

    def test_rerun_adds_a_row(self) -> None:
        result, data = run("fill", str(self.plan), "P0", "--result", "fail", "--evidence", "again", "--rerun")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(data["row"], "P0 (rerun 1)")
        text = self.plan.read_text(encoding="utf-8")
        self.assertLess(text.index("| P0 | pass"), text.index("| P0 (rerun 1) | fail"))
        self.assertLess(text.index("| P0 (rerun 1) | fail"), text.index("| S1 | |"))

    def test_new_pass_moves_next_to_a_fresh_table(self) -> None:
        run("fill", str(self.plan), "P0", "--result", "fail", "--evidence", "again", "--rerun")
        result, data = run("new-pass", str(self.plan), "--heading", "Pass 2", "--note", "after the fix")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(data["rows"], ["P0", "S1", "S2"])
        _, data = run("next", str(self.plan))
        self.assertEqual(data["next"], "P0")
        self.assertIn("| P0 | pass | built |", self.plan.read_text(encoding="utf-8"))

    def test_meta_sets_and_adds_keys(self) -> None:
        result, _ = run("meta", str(self.plan), "--set", "status=in-progress", "--set", "verdict=accepted")
        self.assertEqual(result.returncode, 0, result.stderr)
        head = self.plan.read_text(encoding="utf-8").split("---")[1]
        self.assertIn("status: in-progress", head)
        self.assertIn("verdict: accepted", head)

    def test_empty_window_fails_unless_allowed(self) -> None:
        args = ("check", "--logs", self.logs, "--since", "2026-10-24T11:00:00Z", "--until", "2026-10-24T11:01:00Z", "--forbid-level", "warn,error")
        result, data = run(*args)
        self.assertEqual(result.returncode, 1)
        self.assertTrue(data["empty_window"])
        result, _ = run(*args, "--allow-empty")
        self.assertEqual(result.returncode, 0)

    def test_unparseable_logs_are_an_error_not_a_pass(self) -> None:
        result, _ = run("check", "--logs", self.logs, "--ts-key", "@t", "--since", "2026-10-24T10:00:00Z", "--forbid-level", "warn")
        self.assertEqual(result.returncode, 2)
        self.assertIn("none with a parseable '@t'", result.stderr)
        _, data = self.check("--expect", "msg=start")
        self.assertEqual(list(data["skipped_lines"].values()), [1])  # the torn last line

    def test_a_crash_exits_2_not_1(self) -> None:
        (self.root / "app-folder.jsonl").mkdir()
        result, data = run("check", "--logs", self.logs, "--since", "2026-10-24T10:00:00Z", "--expect", "msg=start")
        self.assertEqual(result.returncode, 2)
        self.assertIsNone(data)
        self.assertIn("unexpected", result.stderr)

    def test_fill_from_check_and_a_later_row_reads_its_window(self) -> None:
        saved = self.root / "s1.json"
        self.check("--expect", "msg=start:1", "--forbid-level", "warn,error", "--allow", "msg=debugHangUi", "--out", str(saved))
        result, _ = run("fill", str(self.plan), "S1", "--from-check", str(saved), "--result", "(2026-10-24, HEAD abc1234)")
        self.assertEqual(result.returncode, 0, result.stderr)
        text = self.plan.read_text(encoding="utf-8")
        self.assertIn("| S1 | pass (2026-10-24, HEAD abc1234) | window since 2026-10-24T10:00:00.000Z until 2026-10-24T10:00:07.000Z", text)
        self.assertIn("allowed: msg=debugHangUi, 1", text)
        result, data = run("check", "--logs", self.logs, "--plan", str(self.plan), "--since", "row:S1", "--until", "row:S1.until", "--expect", "msg=stop:1")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(data["window"]["until"], "2026-10-24T10:00:07.000Z")

    def test_fill_refuses_a_result_that_contradicts_the_check(self) -> None:
        saved = self.root / "s1.json"
        self.check("--expect", "msg=action:1", "--out", str(saved))
        result, _ = run("fill", str(self.plan), "S2", "--from-check", str(saved), "--result", "(2026-10-24) pass after all")
        self.assertEqual(result.returncode, 2)
        self.assertIn("verdict is 'fail'", result.stderr)
        result, _ = run("fill", str(self.plan), "S2", "--from-check", str(saved), "--result", "(2026-10-24) user: pass, I accept it")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("| S2 | fail (2026-10-24) user: pass, I accept it |", self.plan.read_text(encoding="utf-8"))
        self.assertFalse(saved.exists(), "a saved check fills one row")

    def test_fill_refuses_a_spec_that_is_not_the_frozen_assertion(self) -> None:
        saved = self.root / "s1.json"
        self.check("--expect", "msg=start:>=1", "--out", str(saved))
        result, _ = run("fill", str(self.plan), "S1", "--from-check", str(saved))
        self.assertEqual(result.returncode, 2)
        self.assertIn("is not in the Assertion of S1", result.stderr)

    def test_a_failed_check_leaves_no_saved_verdict(self) -> None:
        saved = self.root / "s1.json"
        self.check("--expect", "msg=start:1", "--out", str(saved))
        result, _ = run("check", "--logs", self.logs, "--since", "2026-10-24T10:00:00Z", "--expect", "msg=start:5-2", "--out", str(saved))
        self.assertEqual(result.returncode, 2)
        self.assertFalse(saved.exists())

    def test_fill_a_measurement(self) -> None:
        saved = self.root / "s1.json"
        self.check("--expect", "msg=action:1", "--out", str(saved))
        result, _ = run("fill", str(self.plan), "S2", "--from-check", str(saved), "--measured", "--result", "2 actions")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("| S2 | measured 2 actions |", self.plan.read_text(encoding="utf-8"))

    def test_fill_from_a_scripts_exit_code(self) -> None:
        log = self.root / "run.log"
        log.write_text("step 1 ok\nFAIL: shell hung 15/16\ndone\n", encoding="utf-8")
        result, _ = run("fill", str(self.plan), "S2", "--exit-code", "1", "--log", str(log), "--quote", "^(OK|FAIL)")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn(f"| S2 | fail | exit 1; run log {log}<br>`FAIL: shell hung 15/16` |", self.plan.read_text(encoding="utf-8"))
        result, _ = run("fill", str(self.plan), "S1", "--exit-code", "3")
        self.assertEqual(result.returncode, 2)
        self.assertIn("a script error, not a verdict", result.stderr)

    def test_next_reports_the_row_and_the_plans_problems(self) -> None:
        _, data = run("next", str(self.plan))
        self.assertEqual(data["row"]["Aktor"], "agent")
        self.assertEqual(data["row"]["Asercja"], "`--expect msg=start:1`")
        self.assertIn("S3: Actor 'user' is not one of agent, observation, user-hardware", data["problems"])
        self.assertIn("S3 has no row in the results table", data["problems"])
        self.assertIn("S4: a user-hardware row names no `wait --for` SPEC", data["problems"])
        self.assertIn("S4: count range is reversed in msg=a:5-2", data["problems"])
        self.assertIn("frontmatter lacks machine, base_head, created", data["problems"])
        self.assertFalse([problem for problem in data["problems"] if problem.startswith("S2:")])

    def test_inventory_lists_what_one_build_writes(self) -> None:
        self.log.write_text("\n".join([
            entry("2026-10-24T09:00:00Z", "start", version="1.0.0+old"),
            entry("2026-10-24T09:00:01Z", "legacy", x=1),
            entry("2026-10-24T10:00:00Z", "start", version="1.0.0+abc1234ff"),
            entry("2026-10-24T10:00:01Z", "action", kind="home", source="remote"),
            json.dumps({"ts": "2026-10-24T10:00:02Z", "level": "info"}),
        ]) + "\n", encoding="utf-8")
        result, data = run("inventory", "--logs", self.logs, "--version-contains", "abc1234")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(sorted(data["messages"]), ["(none)", "action", "start"])
        self.assertEqual(data["messages"]["action"]["paths"], ["data.kind", "data.source", "src"])
        result, data = run("inventory", "--logs", self.logs, "--version-contains", "zzz")
        self.assertEqual(result.returncode, 1)
        self.assertEqual(data["recent_versions"], ["1.0.0+abc1234ff", "1.0.0+old"])

    def test_missing_logs_fail_with_one_line(self) -> None:
        result, _ = run("check", "--logs", str(self.root / "none-*.jsonl"), "--since", "2026-10-24T10:00:00Z", "--expect", "msg=start")
        self.assertEqual(result.returncode, 2)
        self.assertEqual(result.stderr.count("\n"), 1, result.stderr)
        self.assertIn("no log file", result.stderr)


if __name__ == "__main__":
    unittest.main()
