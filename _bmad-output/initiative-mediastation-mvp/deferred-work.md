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
