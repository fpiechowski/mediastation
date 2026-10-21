### DW-1: The adapter shim's registration-echo window (`PowerNotifications._registering`) and its three-source wiring have no automated test; only the Win32-free classifier, reporter and gate are covered.
origin: spec-deferred a8812465b072
location: src/MediaStation.App/Platform/System/PowerNotifications.cs
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
reason: No test project compiles PowerNotifications (the plan forbids an App test project), DisplayStateNotificationTests passes `duringStartup` itself, and both iteration-2 runs saw no OS echo. Settle it with an HWND-level harness, or with an echo observation during the UM760 epic acceptance.
status: open

### DW-2: A throw from `HwndSource.AddHook` or the `SystemEvents.PowerModeChanged` subscribe after the hook is attached leaks that hook for the process lifetime.
origin: spec-deferred e179f05da2da
location: src/MediaStation.App/Platform/System/PowerNotifications.cs
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
reason: CompositionRoot now catches adapter-construction failures and still disposes the loop, but the partially constructed PowerNotifications instance never gets Dispose, so an already-added HWND hook stays attached (its reporter is disposed, so late callbacks are dropped). Reachable only if AddHook or the event subscribe throws during startup.
status: open

### DW-3: A late `ProcessExited` from the process killed by `RestartApp` can hit the freshly restarted app and show `Failed` although the new process is alive.
origin: spec-deferred 05ea8e7513dd
location: src/MediaStation.Core/Orchestrator/Orchestrator.cs:269
source_spec: `epic-orchestrator-lifecycle/story-failed-wejscie-wyjscie-przez-appmenu-i-awaria-na-pierwszym-p-plan.md`
severity: medium
reason: `ProcessExited` carries only `appId`. After `CommandCompleted{StopApp}` the stop correlation is consumed, the Orchestrator moves to `Starting` and sends `StartApp`, and a second exit fact from the host (exit watcher next to the command confirmation) is now treated as a failure of the new start. Settled by host story 2.9 guaranteeing that `ProcessExited` refers to the current process (pid in the Event, or dedupe by `rootPid`), plus a loop test with two facts for one stop.
status: open
