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

### DW-5: Kolejność zwalniania adapterów w composition root (hub → pompa → power → timer → pętla) nie ma testu.
origin: spec-deferred e995e2719403
location: src/MediaStation.App/TeardownSequence.cs (handler app.Exit w src/MediaStation.App/CompositionRoot.cs:246-253)
source_spec: `tickets/epic-input/tickets/story-tracer-bullet-pilot-na-donglu-daje-actions-z-watku-input-plan.md`
severity: low
reason: Brak projektu testów App i seamu w `CompositionRoot`, więc zamiana kolejności `inputHub?.Dispose()` i `actionPump?.Dispose()` nie zapali żadnego testu; skutek to Action w locie bez wpisu w logu przy wyjściu. Ten sam dług jest już zapisany w retrospektywie epiki (composition root bez testów).
status: resolved
evidence: Kolejność i semantyka wyjątków wyszły do `TeardownSequence` (App, bez WPF/Win32), linkowanego do `MediaStation.Core.Tests` jak `UiHang.cs`; handler `app.Exit` buduje sekwencję w chwili wyjścia, bo część slotów jest wtedy `null`. `TeardownSequenceTests` pinują kolejność siedmiu adapterów (hub → pompa Actions → wstrzyknięcia → power → timer → pętla), zwalnianie reszty po awarii i pierwszy wyjątek na zewnątrz; `stop` zostaje ostatnim wpisem, bo `AttachStopLogging` rejestruje się później. `dotnet test MediaStation.slnx -c Release` 862/862 plus publish/start/`CloseMainWindow` z `stop {exitCode: 0}`.

### DW-6: `InputPolicy` nie resetuje stanu przy (re)rejestracji hooka, bo ten adapter rejestruje hook tylko raz.
origin: spec-deferred 0e8c677d9198
location: src/MediaStation.Core/Input/InputPolicy.cs:111
source_spec: `tickets/epic-input/tickets/story-tracer-bullet-pilot-na-donglu-daje-actions-z-watku-input-plan.md`
severity: low
reason: Story 1 odroczyła reset `_held`/`_pendingLong` do story, która rejestruje hook; InputHub nie ma ścieżki re-rejestracji ani watchdoga, więc reset nie ma wołającego. Skutek: utracony key-up (np. blokada sesji w trakcie trzymania klawisza) zamraża VK do końca procesu. Domknięcie wymaga watchdoga hooka.
status: open

### DW-7: AD-8 („Jedno naciśnięcie daje jedną Action”, „InputHub jest właścicielem timingu”) i checklista odbioru epiku („dokładnie jeden wpis `action`” dla strzałek) nie opisują, że trzymana strzałka daje
origin: spec-deferred d26d50d7da39
location: _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md:189,191
source_spec: `tickets/epic-input/tickets/story-dowolna-klawiatura-i-repeat-strzaek-plan.md`
severity: low
reason: Zweryfikowane w review (2 przebiegi): `architecture-mediastation.md:189,191` i `epic-input.md:70` nie mają reguły repeat, a kod emituje `press` + N×`repeat` z `InputPolicy.CollectRepeats`. AD-8 już wymienia `phase: repeat` w kontrakcie Actions (`:188`), a dopisanie reguły to pass dokumentacyjny epiki — wzorzec `clipHwnd` w `epic-input.md:61`. Wymaga wpisu w notatce odbioru epiku, żeby operator nie odczytał checklisty jako porażki przy trzymanej strzałce.
status: open

### DW-8: Sygnatura zawierająca `*`, ale niebędąca dokładnie `*` (np. `VID_1915*`), przechodzi walidację i nigdy nie dopasowuje ścieżki Raw Input, więc urządzenie wpada w fallback `*` albo `keyboard`.
origin: spec-deferred 3a100f145c60
location: src/MediaStation.Core/Input/InputTarget.cs:42
source_spec: `tickets/epic-input/tickets/story-dowolna-klawiatura-i-repeat-strzaek-plan.md`
severity: low
reason: `DeviceSignature.Matches` specjalnie traktuje wyłącznie `InputDevice.WildcardSignature` i poza tym używa `Contains`, a ścieżka Raw Input nigdy nie zawiera `*`; walidacja wymaga tylko niepustej sygnatury. Zachowanie samo w sobie jest sprzed tej story (nowe jest tylko znaczenie `*`), a naprawa to reguła walidacji odrzucająca `*` w sygnaturze innej niż dokładnie `*` — decyzja planistyczna, nie poprawka tej zmiany.
status: resolved
evidence: `DeviceSignature.HasEmbeddedWildcard` obok `Matches` plus jedna gałąź `else if` w `ConfigValidation.CheckInput` dają jeden `configError` na `$.devices[i].signature` dla `*` w środku sygnatury (`VID_1915*`, `*VID*`, `**`), a dokładnie `*` zostaje wildcardem wymagającym `source: keyboard`. Schematu nie ruszano (reguły semantyczne tylko w runtime), sample `tools/samples/config` bez zmian i dalej zielony w `Sample_config_files_are_valid`; `dotnet test MediaStation.slnx -c Release` 862/862, a publish z podmienionym `input.json` dał log `start → configError → stop {exitCode: 0}` na tej samej ścieżce.

### DW-9: Checklista odbioru epiku („Jedno naciśnięcie daje jedną Action”, „każde naciśnięcie … daje w logu dokładnie jeden wpis `action`”) nie mówi, że D-pad i OK w `app` + `moonlight`/`native` są celowo
origin: spec-deferred f2c6f5214410
location: _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md:23,70
source_spec: `tickets/epic-input/tickets/story-routing-z-lokalnej-kopii-setinputrouting-i-actions-do-orches-plan.md`
severity: low
reason: Zweryfikowane: `architecture-mediastation.md:202` (wiersz tabeli AD-8) każe przepuszczać D-pad i OK w `app` + `moonlight`/`native`, a `epic-input.md:23` (Done when 1) i `:70` (checklista po story 2) nadal obiecują jedną Action i dokładnie jeden wpis `action` na naciśnięcie. Nowy test `App_with_moonlight_or_native_passes_dpad_and_ok_without_actions_or_grid` pinuje zero Actions. Ten sam typ luki (checklista nie opisuje `repeat`) jest już zapisany jako DW-7 z uzasadnieniem „żeby operator nie odczytał checklisty jako porażki”; poprawka edytuje dokumentację architektury i notatkę odbioru epiki, więc należy do passa dokumentacyjnego epiki, nie do tej story.
status: open

### DW-10: Realne wywołanie SendInput w KeyInjector nigdy nie zostaje wykonane przez testy ani przez przebiegi aplikacji, więc przyjęcie dwóch rekordów INPUT przez system pozostaje niepotwierdzone automatem.
origin: spec-deferred 487e5d5979fa
location: src/MediaStation.App/Platform/Input/KeyInjector.cs:22-38
source_spec: `tickets/epic-input/tickets/story-wstrzykniecie-klawisza-ze-znacznikiem-plan.md`
severity: low
reason: KeyInjector jest tylko przekazywany jako delegat w CompositionRoot.cs, testy karmią KeyInjectionPump własnym delegatem, a logi z 2026-10-20..22 nie mają żadnego wpisu keyInjection. Układ struktur pochodzi z CsWin32 (wygenerowane INPUT/KEYBDINPUT), więc ryzyko jest niskie. Rozstrzyga odbiór epiku na UM760: prawdziwy SendKey daje wpis keyInjection, a wstrzyknięty klawisz nie daje Action i wraca przez hook; alternatywnie jednorazowe wywołanie helpera poza testami.
status: open

### DW-11: Przestrzeń współrzędnych clipu (`GetWindowRect`, piksele fizyczne) i zdarzeń hooka (`MSLLHOOKSTRUCT.pt`) może się rozejść, gdy aplikacja nie jest per-monitor DPI aware, a okno OSK stoi na monitorze o
origin: spec-deferred ae465c0c9b5a
location: src/MediaStation.App/Platform/Input/InputHub.cs:570-601
source_spec: `tickets/epic-input/tickets/story-pointer-hook-myszy-i-clipcursor-w-osk-plan.md`
reason: Plan (Implementation Notes) twierdzi „WPF w .NET jest per-monitor DPI aware”, ale `src/MediaStation.App/MediaStation.App.csproj` nie deklaruje żadnego manifestu DPI, a domyślny WPF raportuje `SYSTEM_AWARE`. ALDER pracuje przy 200% i ma dwa monitory (1920×1080 oraz 1080×1920), więc na monitorze o skali innej niż systemowa prostokąt clipu i punkt hooka mogą być w różnych przestrzeniach. Rozstrzyga pomiar `GetProcessDpiAwareness` na opublikowanym exe oraz przebieg z OSK na monitorze o innej skali (porównanie prostokąta z `GetWindowRect` z punktem z hooka) — do odbioru epiku, razem z epic-osk, które pierwsze wyśle `clipHwnd`.
status: open

### DW-12: AD-8 nadal opisuje `SetInputRouting{ mode, appType, pointer, epoch }`, choć kontrakt ma teraz opcjonalne `clipHwnd`; dokumentacja architektury jest nieaktualna do czasu epic-osk.
origin: spec-deferred 70f2922e3fed
location: _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md:194
source_spec: `tickets/epic-input/tickets/story-pointer-hook-myszy-i-clipcursor-w-osk-plan.md`
severity: low
reason: `architecture-mediastation.md:194` nie wymienia `clipHwnd`, a `set-input-routing.schema.json` i testy kontraktu pinują klucz `clipHwnd` w każdej kopercie. Epika już to przewidziała: `epic-input.md:61` każe dopisać `clipHwnd` do AD-8 (`bmad-architecture`) przed epic-osk, więc luka jest śledzona, ale nie ma wykonawcy w tym przebiegu.
status: open

### DW-13: Wykrywanie nowego okna przeglądarki po `pid:handle` jednego `MainWindowHandle` na proces może przeoczyć drugie okno w istniejącym procesie Edge.
origin: spec-deferred 68662efb5eca
location: tools/check-home-s10.ps1 Get-BrowserWindows
source_spec: `tickets/epic-input/tickets/story-home-zawsze-zawieszony-ui-i-skrypt-s10-plan.md`
reason: Niesprawdzone bez prawdziwego Edge i pilota; rozstrzygnie to przebieg na UM760 (porównanie z `EnumWindows` po klasie `Chrome_WidgetWin_1`). Nota 3.10: ticket zamienił `MainWindowHandle` na `EnumWindows` i liczy każde widoczne okno najwyższego poziomu klasy `Chrome_WidgetWin_*` jako osobne `pid:handle`, więc drugie okno w istniejącym procesie jest widoczne; rozstrzygnięcie na UM760 to nadal porównanie z żywym `msedge` po klasie i brak fałszywych `browser window` z tooltipów.
status: open

### DW-14: Kontrola `target not foreground` po PID może odrzucić poprawny press Edge przy wielu procesach `msedge` lub innym profilu.
origin: spec-deferred 94e635089e98
location: tools/check-home-s10.ps1 Measure-Press
source_spec: `tickets/epic-input/tickets/story-home-zawsze-zawieszony-ui-i-skrypt-s10-plan.md`
reason: Niesprawdzone na ALDER; potwierdzić na UM760 przy odbiorze epiku. Nota 3.10: ticket porównuje foreground z dokładnym uchwytem okna celu (nie z PID), a reguła żyje w czystym `Test-TargetForeground` pokrytym `-SelfTest`; dwa nazwane wyjątki powłoki to ghost `dwm` tylko dla `shell-hung` i okno desktopu `Progman`/`WorkerW` należące do procesu `explorer` tylko dla `desktop`. UM760 ma potwierdzić, że press na Edge przy wielu procesach/profilach przechodzi, a inne okno tego samego procesu nie.
status: open

### DW-15: `Get-BrowserWindows` i `Find-WindowProcess` nie odróżniają okien najwyższego poziomu z właścicielem (`GW_OWNER`) od okien bez właściciela.
origin: review 3.10, 2026-10-22
location: tools/check-home-s10.ps1 — `Get-BrowserWindowIds` / `Find-WindowProcess`
source_spec: `tickets/epic-input/tickets/story-refactor-sweep-plan.md`
severity: medium (unverified)
reason: Diff 3.10 zastąpił `Process.MainWindowHandle` (pierwsze widoczne okno najwyższego poziomu **bez właściciela**) enumeracją `EnumWindows`, która nie niesie informacji o właścicielu, a filtr klasy `Chrome_WidgetWin_*` przepuszcza także `Chrome_WidgetWin_0`, używane przez Chromium dla okien niebędących oknami przeglądarki. Gdyby w oknie pomiaru pojawiło się widoczne okno z właścicielem (tooltip/popup) albo widoczne `Chrome_WidgetWin_0`, S10 zaliczyłoby poprawny press jako `browser window` lub `target not foreground` (fałszywy FAIL) albo wybrało zły uchwyt celu. Osiągalności nie wykazano: na ALDER jedyne widoczne okno przeglądarki to `Chrome_WidgetWin_1` bez właściciela, a `Find-WindowProcess` dla MediaStation zwrócił ten sam uchwyt co `MainWindowHandle` w stanie normalnym i zawieszonym. Rozstrzyga przebieg S10 na UM760 z Edge i Moonlightem: porównanie zbioru `pid:handle` i uchwytu celu z `MainWindowHandle` oraz brak fałszywych `browser window`. Naprawa: pole `Owner` (`GetWindow(hwnd, GW_OWNER)`) w `Get-TopLevelWindows` i pomijanie okien z właścicielem w obu miejscach.
status: open

### DW-16: S10 nie odróżnia okien najwyższego poziomu z właścicielem (`GW_OWNER`) od okien bez właściciela: `EnumWindows` w `Get-BrowserWindowIds` i `Find-WindowProcess` przepuszcza widoczne okna z właścicielem
origin: spec-deferred 176036279b85
location: tools/check-home-s10.ps1 Get-BrowserWindowIds / Find-WindowProcess
source_spec: `tickets/epic-input/tickets/story-refactor-sweep-plan.md`
reason: Review pass 2: zmiana zastąpiła `Process.MainWindowHandle` (pierwsze widoczne okno najwyższego poziomu bez właściciela) enumeracją bez informacji o właścicielu, a filtr klasy jest prefiksem `Chrome_WidgetWin_*`. Osiągalności nie wykazano: na ALDER jedyne widoczne okno przeglądarki to `Chrome_WidgetWin_1` bez właściciela, a `Find-WindowProcess` dla MediaStation zwrócił ten sam uchwyt co `MainWindowHandle` (także w stanie zawieszonym). Rozstrzyga przebieg S10 na UM760 z Edge i Moonlightem (porównanie zbioru `pid:handle` i uchwytu celu z `MainWindowHandle`, brak fałszywych `browser window`); wpis w ledgerze jako DW-15. Naprawa: pole `Owner` (`GetWindow(hwnd, GW_OWNER)`) w `Get-TopLevelWindows` i pomijanie okien z właścicielem w obu miejscach.
status: open
