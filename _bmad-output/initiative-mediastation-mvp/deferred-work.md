### DW-1: The adapter shim's Win32/WPF plumbing (HWND hook, `RegisterPowerSettingNotification`, `SystemEvents` subscribe, `POWERBROADCAST_SETTING` copy) has no automated test.
origin: spec-deferred a8812465b072
location: src/MediaStation.App/Platform/System/PowerNotifications.cs — `PowerNotifications.OnWindowMessage` / `PowerNotifications.OnPowerModeChanged`
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
reason: The registration-echo window and the three-source mapping moved out of the HWND-bound shim into `PowerSignalRouter` (linked into `MediaStation.Core.Tests`, see `PowerSignalRouterTests`), so what stays untested is only the plumbing that needs a real window. Settle it with an HWND-level harness, or with an echo observation during the UM760 epic acceptance.
status: open

### DW-2: A throw from `HwndSource.AddHook` or the `SystemEvents.PowerModeChanged` subscribe after the hook is attached leaks that hook for the process lifetime.
origin: spec-deferred e179f05da2da
location: src/MediaStation.App/Platform/System/PowerNotifications.cs — `PowerNotifications` constructor / `PowerSourcesAttachment.Attach`
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
reason: Reachable only if `AddHook` or the event subscribe throws during startup, after part of the sources is already attached; the partially constructed instance never reaches `Dispose`.
status: resolved
evidence: `PowerSourcesAttachment.Attach` releases every source attached before the failing one, in reverse order, and rethrows, so the HWND hook, the display-state registration and the `SystemEvents` subscription cannot outlive a failed construction; `PowerNotifications.Dispose` uses the same idempotent release and the registration line still reports the startup state with `powerModeChanged: true`. Covered by `PowerSourcesAttachmentTests` (releasing on a throwing attach, reverse order, idempotence) plus the full `dotnet test MediaStation.slnx -c Release` and the publish/start/inject-power smoke; forcing a throw inside the Win32 plumbing itself still needs an HWND-level harness, which the epic forbids per ticket.

### DW-3: A late `ProcessExited` from the process killed by `RestartApp` can hit the freshly restarted app and show `Failed` although the new process is alive.
origin: spec-deferred 05ea8e7513dd
location: src/MediaStation.Core/Orchestrator/Orchestrator.cs — `OnProcessExited`
source_spec: `epic-orchestrator-lifecycle/story-failed-wejscie-wyjscie-przez-appmenu-i-awaria-na-pierwszym-p-plan.md`
severity: medium
reason: `ProcessExited` carries only `appId`. After `CommandCompleted{StopApp}` the stop correlation is consumed, the Orchestrator moves to `Starting` and sends `StartApp`, and a second exit fact from the host (exit watcher next to the command confirmation) is now treated as a failure of the new start. Settled by host story 2.9 guaranteeing that `ProcessExited` refers to the current process (pid in the Event, or dedupe by `rootPid`), plus a loop test with two facts for one stop.
status: open

### DW-4: Spóźniony AppWindowAttached po timeoucie Starting jest ignorowany, a Hwnd zdjęte w Failed, więc okno, które pojawi się po terminie, może zostać na wierzchu nad shellem bez kompensującego
origin: spec-deferred 794dc1866536
location: src/MediaStation.Core/Orchestrator/Orchestrator.cs — `OnAppWindowAttached` / `EnterFailed`
source_spec: `epic-orchestrator-lifecycle/story-transitionid-i-home-w-trakcie-starting-plan.md`
severity: medium
reason: Guard `app.State != AppLifecycleState.Starting` w `OnAppWindowAttached` i `app.Hwnd = null` w `EnterFailed` pochodzą ze story 5 i nie zmieniły się w tej story; zmiana domyka kompensację tylko dla attachu w Starting. Skutek potwierdzony sondą: Home w Starting → TimerElapsed → AppWindowAttached daje brak komend i brak snapshotu, a kolejny Home nic nie robi. Rozwiązanie wymaga decyzji, czy Failed zachowuje Hwnd i minimalizuje spóźnione okno, czy zostawia to hostowi — to osobna story.
status: open
