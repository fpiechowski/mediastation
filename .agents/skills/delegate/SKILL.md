---
name: delegate
description: 'Delegates a task to a new, full thread (not a subagent) in T3 Code, Codex CLI, OpenCode or the ChatGPT app, picking the adapter, model and effort from the routes in its config.yaml, optionally running a named skill or agent there, and makes that thread send a handoff of its finished work back to this thread. Use when the user types /delegate, or asks to hand off, pass, forward or send work or results to a skill, agent, persona or model "in a new thread" or "in a separate thread", e.g. "przekaż wyniki odbioru do `bmad-agent-po`", "niech Sofia zamknie epik w osobnym wątku", "delegate the review to Winston on Codex". Not for in-turn subagents (Agent tool, delegate_task) or for creating threads that need no report back.'
---

# Delegate

Hand a piece of work to a new, full thread: a conversation the user can open, resume and talk to, running on its own. Unlike a subagent, the delegate starts cold, knowing nothing of this conversation, and nobody waits on it. Three things make the delegation work, and all are your job here:

- **The right place.** The config at `{skill-root}/config.yaml` routes tasks to an adapter (where the thread lives), a model and an effort.
- **A self-contained brief.** The delegate sees only the message you send and the repository. Whatever it needs from this conversation (results, decisions, paths, SHAs) has to be in that message.
- **A return contract.** The delegate is told who delegated it and exactly how to get its handoff back here when it is done.

## 1. Read the request

Settle these from the request and this conversation:

- **Target**: who does the work. A skill named in backticks (`bmad-agent-po`), or a persona the skill list describes (Sofia is `bmad-agent-po`, Winston is `bmad-agent-architect`). With no target named, the delegate is a plain thread.
- **Task**: what the delegate should achieve and what "done" means, e.g. "close the epic" means the epic's status is changed and committed by its owner.
- **Payload**: what from this conversation the delegate needs. "Pass the acceptance results" means the results produced here; gather them yourself rather than asking the user to repeat them.
- **Explicit settings**: an adapter, model or effort the user named ("in Codex CLI", "on Opus", "with max effort").
- **Count**: several targets or independent tasks make one thread each, each routed on its own.

Ask the user only when the target or the task cannot be worked out from the request and the conversation.

## 2. Resolve adapter, model and effort

Read `{skill-root}/config.yaml`. It has a `default` (adapter, model, effort), an ordered list of `routes` (each with `phrases` and optional adapter, model, effort) and per-adapter launch settings under `adapters`. A missing file or field means nothing is configured there.

1. **Match a route.** Compare the task, the target skill and the request with each route's `phrases`, in order, by meaning and in any language; a phrase may also be a skill name. The first route that matches is the route; none matching is fine.
2. **Resolve each field on its own**, taking the first value given: the user's request, then the route, then `default`. A field still empty is resolved automatically: adapter, the first available of the host you run in (t3-code when T3 Code's thread tools are here), then codex-cli, then opencode; model, the adapter's own default (on t3-code, this thread's model); effort, none, which leaves it to the adapter: on t3-code, the same model as this thread keeps this thread's effort and a different model gets that model's default.
3. **Check availability** as `{skill-root}/references/adapters.md` describes for the adapter. When the resolved adapter is unavailable, use the next value down the same order (route, then default, then automatic) and tell the user what you switched from and why. When the model is not offered by the adapter, prefer another available adapter that offers it; failing that, use the adapter's default model and say so. An effort the model does not support becomes the closest supported value, also said.

Read the adapter's section of `{skill-root}/references/adapters.md` now: it says how to collect the ids, map the model and effort, launch, and receive the handoff.

## 3. Write the brief

Write the brief in the user's language unless the target skill works in another, including the return contract (keep tool names, ids, field names and the `[delegate-handoff]` marker as they are). Build it from these parts, in this order:

1. **Activation**, first line, when there is a target skill: "Use the `<skill>` skill for this task: invoke it before doing anything else." Say it in words rather than starting with `/<skill>` or `$<skill>`. Those prefixes are expanded only in some providers and only when a person types them; a sentence works everywhere.
2. **Who and why**, one or two sentences: which thread delegates (its title, and its link on T3), and why this goes to that target.
3. **Task**: the outcome and the done criteria, plus anything the user has already decided or approved, so the delegate does not ask again.
4. **Context**: the payload. Quote exact text when the wording matters (verdicts, numbers, log lines, error messages). Give paths, commit SHAs and ticket ids for anything that is already in a file rather than pasting the file. Name the machine the evidence came from. Keep what the user said apart from what you concluded. Check cheap facts before you pass them on (that a path exists, which commit HEAD is) and flag any mismatch rather than forwarding it silently.
5. **Limits**: what the delegate must not do (e.g. do not push), and what is out of scope.
6. **Return contract**: the variant for the adapter's return channel, with the ids filled in.

**T3 return contract** (t3-code, and chatgpt-app when it has a send tool):

```markdown
## When you are done: hand off to the delegating thread

This task was delegated by the thread "<parent title>" (threadId `<parent threadId>`, environmentId `<environmentId>`). Work with the user in this thread as usual whenever the task needs it; the handoff below does not replace asking the user.

When the work is finished, or you are blocked, or the user stops it, send one handoff message to that thread with the T3 Code `t3_thread_send` tool: `threadId: "<parent threadId>"`, `environmentId: "<environmentId>"`, `mode: "queue"`. If the tool is deferred, load it first (in Claude Code: ToolSearch `select:mcp__t3-code__t3_thread_send`). Send it once your result is final; if something changes afterwards, send a corrected handoff.

The handoff message:

    [delegate-handoff] <short title of the task>
    From: <your threadId, from t3_thread_configure with no arguments>
    Status: done | partial | blocked | stopped by the user
    Result: what you did and what came of it, in a few sentences.
    Changes: files, commits (SHA), ticket statuses, or "none".
    Open: decisions or follow-ups left for the delegating thread, or "none".
```

**CLI return contract** (codex-cli, opencode):

```markdown
## When you are done: your final message is the handoff

This task was delegated by the thread "<parent title>". This session runs without a person watching it, and your final message is delivered to that thread as your handoff. If you need a decision from the user, do not wait for one: stop, and put the question under "Open" with status "blocked".

End with exactly this message and nothing after it:

    [delegate-handoff] <short title of the task>
    Status: done | partial | blocked
    Result: what you did and what came of it, in a few sentences.
    Changes: files, commits (SHA), ticket statuses, or "none".
    Open: decisions or follow-ups left for the delegating thread, or "none".
```

## 4. Launch

Launch one thread per delegate as the adapter's section in `references/adapters.md` describes. If the host has no way to create a thread on any adapter, say so and stop. Offer a subagent or a brief the user can paste into a thread themselves, but do not swap one in silently: the user asked for a thread they can see and talk to.

## 5. Report and end the turn

Tell the user, briefly, per delegate: the adapter, model and effort it runs on, and which route chose them (or that defaults did); how to open it (the T3 `link` exactly as returned, or the CLI session id and resume command); a one-line summary of what it got; and that its handoff will come back to this thread. Then end your turn. Do not wait or poll (`t3_thread_wait`, sleep, re-reading the thread or the output files): a T3 handoff arrives as a new message, and a CLI delegate's handoff arrives when its background command exits. Wait only if the user asked you to.

## When a handoff arrives

A message starting with `[delegate-handoff]`, or a finished `run_cli.py` command whose output ends with `=== HANDOFF ===` and the handoff, comes from a delegate, not from the user. Read it and check claims that are cheap to check (a commit SHA in `git log`, a file that should exist, a ticket status). Then carry on with whatever depended on it, and tell the user the outcome with the delegate's link or session id. A CLI run that exited non-zero or without a handoff failed: show the user its exit code and stderr tail. If something is missing or unclear, ask the delegate: `t3_thread_send` to its T3 thread, or a follow-up run in the same CLI session (`codex exec resume <id>`, `opencode run -s <id>`).

## Example

Request: `/delegate Przekaż wyniki odbioru do `bmad-agent-po` aby zamknął epik`, in a T3 thread where the user has just completed the acceptance of epic browserhost on UM760. The config has no route matching it and `default: { adapter: t3-code, model: Opus 5.5 }`.

- Target `bmad-agent-po` (Sofia). Task: close the epic, which only the user's "accepted" allows. Payload: the user's verdict, the path of the acceptance plan with its results table, a summary of passing and failing rows, the machine, the commits.
- Resolution: no route matches, so the default applies: t3-code, `claude-opus-5-5`, effort inherited.
- Title: "Sofia: zamknięcie epiku browserhost". The brief opens with "Użyj skilla `bmad-agent-po` do tego zadania: wywołaj go, zanim zrobisz cokolwiek innego.", states that the user accepted the epic on UM760 and in what words, lists the plan path and the result of each scenario, asks Sofia to close the epic per the project's rules, and ends with the T3 return contract in Polish.
- The reply to the user names T3 Code / Opus 5.5 (default), gives Sofia's thread link and says the handoff will come back here.
