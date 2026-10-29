# Adapters

How to check, launch and get the handoff back for each adapter. Read only the section of the adapter you resolved.

## t3-code

**Available** when the session has T3 Code's thread tools (`t3_thread_launch`, `t3_thread_send`; in Claude Code `mcp__t3-code__<name>`, often deferred: load them with ToolSearch `select:` first).

**Ids for the brief**: `t3_thread_configure` with no arguments gives this thread's `threadId`; `t3_environment_read` gives `environmentId`; `t3_thread_read` on the `threadId` with `limit: 1` gives `title` and `link`.

**Model and effort**: call `orchestrator_capabilities` (pass `search` when the model is not in the short list). Match the configured name to a model `label` or `id` ("Opus 5.5" is `claude-opus-5-5` under provider instance `claudeAgent`; "GPT-6.1 Sol" is `gpt-6.1-sol` under `codex`). Effort is the model option whose id contains "effort" (`effort` for Claude, `reasoningEffort` for Codex). Pass:

```json
"modelSelection": { "instanceId": "<providerInstanceId>", "model": "<model id>", "options": [{ "id": "<effort option id>", "value": "<effort>" }] }
```

Leave `modelSelection` out when the resolved model is this thread's own (see `t3_thread_configure`) and no effort was resolved, so the thread inherits both. With a different model and no effort, pass the model without an effort option. A provider with a constraint such as "Provider instance is disabled" does not offer its models.

**Launch**: `t3_thread_launch` once per delegate, with `title` (target plus task), `message` (the brief), `delegatedFrom: { environmentId, threadId, title }` of this thread, and a fresh `clientRequestId` (after an error or lost reply, retry with the same value, or a duplicate thread can start). Leave `workspaceStrategy` out so the delegate works in the project checkout and sees uncommitted files; use `{ type: "worktree" }` only when the user asks or both threads will edit code at once (a worktree gets no uncommitted changes). A delegate cannot get broader runtime modes than this thread. To reuse an existing thread instead, send the brief with `t3_thread_send` (`mode: "queue"`).

**Return channel**: the delegate sends its handoff with `t3_thread_send` to this thread (`mode: "queue"`, so it becomes a new turn here and never interrupts a running one). Use the T3 return contract in SKILL.md.

**Report**: the `link` from the launch result, exactly as returned.

## codex-cli

**Available** when `codex --version` succeeds.

**Model and effort**: the model id as Codex expects it (`gpt-6.1-sol` for "GPT-6.1 Sol"); with no model, Codex uses its own default from `~/.codex/config.toml`. Effort goes to `model_reasoning_effort`.

**Launch**: write the brief to a file under a fresh directory in the system temp folder (e.g. `<temp>/delegate/<yyyyMMdd-HHmmss>-<slug>/brief.md`), then start, **in the background** so you are told when it exits:

```
python {skill-root}/scripts/run_cli.py --adapter codex-cli --brief <dir>/brief.md --cwd <project root> --out <dir> [--model <id>] [--effort <effort>] [--sandbox <adapters.codex-cli.sandbox>] [--worktree]
```

In T3 Code as Claude, then pass the background task id to `t3_background_wait`, so this thread shows as working until the delegate exits (it does not block you). The script prints `SESSION codex-cli <id>` once Codex has created the thread, and at the end the handoff, the session id and the resume command (`codex resume <id>`). The session is a full Codex thread: the user can resume it in a terminal, and it is stored where the Codex desktop app keeps its threads. `--cwd` must be inside a git repository Codex trusts.

**Return channel**: the delegate's final message is its handoff, so the brief uses the CLI return contract in SKILL.md. The session runs headless: the delegate cannot ask the user mid-task, so it stops and reports `blocked` with its question under `Open`.

**Report**: the output directory, and that the session id and `codex resume <id>` come with the handoff (and appear in `<dir>/session.json` within seconds of the start, for the user to look up sooner). Do not hold the turn waiting for the id.

## opencode

**Available** when `opencode --version` succeeds.

**Model and effort**: models are `provider/model` (`opencode models` lists them); effort becomes the model variant, `provider/model#<effort>`, so only pass it when the model has that variant. With no model, OpenCode uses its configured default.

**Launch**: as for codex-cli, with `--adapter opencode --title "<title>"`, plus `--auto` when `adapters.opencode.auto` is true. The brief goes to OpenCode as an attached file. Resume with `opencode -s <id>`.

**Return channel and report**: as for codex-cli.

## chatgpt-app

**Available** only when the session has tools of the ChatGPT or Codex desktop app that create a thread and send a message to a thread by id. Its bundled `codex-app-tools` plugin had none as of 2026-10-29: Codex's own `spawn_agent` and `send_message` reach subagents, not threads, and do not count. When the tools exist, use them like t3-code: create the thread with the brief, and give the delegate the send tool and this thread's id as its return channel.

When they do not exist, the adapter is unavailable: say so and fall back as the resolution rules in SKILL.md describe. Threads started through codex-cli land in the same thread store the Codex app reads, which can serve when the user wants the thread in that app.
