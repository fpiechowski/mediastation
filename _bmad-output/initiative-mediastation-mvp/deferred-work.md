### DW-1: The adapter shim's Win32/WPF plumbing (HWND hook, `RegisterPowerSettingNotification`, `SystemEvents` subscribe, `POWERBROADCAST_SETTING` copy) has no automated test.
origin: spec-deferred a8812465b072
location: src/MediaStation.App/Platform/System/PowerNotifications.cs — `PowerNotifications.OnWindowMessage` / `PowerNotifications.OnPowerModeChanged`
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
severity: low
reason: The registration-echo window and the three-source mapping moved out of the HWND-bound shim into `PowerSignalRouter` (linked into `MediaStation.Core.Tests`, see `PowerSignalRouterTests`), so what stays untested is only the plumbing that needs a real window. Settle it with an HWND-level harness, or with an echo observation during the UM760 epic acceptance.
status: done 2026-10-28
resolution: resolved by sweep bundle dw-power-notifications-harness
resolution-undo: 9d3a7e02dffc9dbe540b411d0984ddd619e0987306fa110be7e4eb6b8305f771 2026-10-28 7374617475733a206f70656e
ticket: `_bmad-output/backlog/bug-power-notifications-hwnd-harness.md`

### DW-2: A throw from `HwndSource.AddHook` or the `SystemEvents.PowerModeChanged` subscribe after the hook is attached leaks that hook for the process lifetime.

status: done 2026-10-22
origin: spec-deferred e179f05da2da
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
archived: 2026-10-26

### DW-3: A late `ProcessExited` from the process killed by `RestartApp` can hit the freshly restarted app and show `Failed` although the new process is alive.
origin: spec-deferred 05ea8e7513dd
location: src/MediaStation.Core/Orchestrator/Orchestrator.cs — `OnProcessExited`
source_spec: `epic-orchestrator-lifecycle/story-failed-wejscie-wyjscie-przez-appmenu-i-awaria-na-pierwszym-p-plan.md`
severity: medium
reason: `ProcessExited` carries only `appId`. After `CommandCompleted{StopApp}` the stop correlation is consumed, the Orchestrator moves to `Starting` and sends `StartApp`, and a second exit fact from the host (exit watcher next to the command confirmation) is now treated as a failure of the new start. Settled by host story 2.9 guaranteeing that `ProcessExited` refers to the current process (pid in the Event, or dedupe by `rootPid`), plus a loop test with two facts for one stop.
owner: epic-browserhost (pierwszy prawdziwy host raportujący `ProcessExited`), decyzja refinementu epiki 4, 2026-10-26
status: open

### DW-4: Spóźniony AppWindowAttached po timeoucie Starting jest ignorowany, a Hwnd zdjęte w Failed, więc okno, które pojawi się po terminie, może zostać na wierzchu nad shellem bez kompensującego
origin: spec-deferred 794dc1866536
location: src/MediaStation.Core/Orchestrator/Orchestrator.cs — `OnAppWindowAttached` / `EnterFailed`
source_spec: `epic-orchestrator-lifecycle/story-transitionid-i-home-w-trakcie-starting-plan.md`
severity: medium
reason: Guard `app.State != AppLifecycleState.Starting` w `OnAppWindowAttached` i `app.Hwnd = null` w `EnterFailed` pochodzą ze story 5 i nie zmieniły się w tej story; zmiana domyka kompensację tylko dla attachu w Starting. Skutek potwierdzony sondą: Home w Starting → TimerElapsed → AppWindowAttached daje brak komend i brak snapshotu, a kolejny Home nic nie robi. Rozwiązanie wymaga decyzji, czy Failed zachowuje Hwnd i minimalizuje spóźnione okno, czy zostawia to hostowi — to osobna story.
owner: epic-browserhost (pierwszy prawdziwy host z `AppWindowAttached`), decyzja refinementu epiki 4, 2026-10-26
status: open
decision: 2026-10-28 Decide with epic-browserhost
decision: 2026-10-27 Decide with epic-browserhost

### DW-5: Kolejność zwalniania adapterów w composition root (hub → pompa → power → timer → pętla) nie ma testu.

status: done 2026-10-22
origin: spec-deferred e995e2719403
source_spec: `tickets/epic-input/tickets/story-tracer-bullet-pilot-na-donglu-daje-actions-z-watku-input-plan.md`
severity: low
archived: 2026-10-26

### DW-6: `InputPolicy` nie resetuje stanu przy (re)rejestracji hooka, bo ten adapter rejestruje hook tylko raz.
origin: spec-deferred 0e8c677d9198
location: src/MediaStation.Core/Input/InputPolicy.cs:111
source_spec: `tickets/epic-input/tickets/story-tracer-bullet-pilot-na-donglu-daje-actions-z-watku-input-plan.md`
severity: low
reason: Story 1 odroczyła reset `_held`/`_pendingLong` do story, która rejestruje hook; InputHub nie ma ścieżki re-rejestracji ani watchdoga, więc reset nie ma wołającego. Skutek: utracony key-up (np. blokada sesji w trakcie trzymania klawisza) zamraża VK do końca procesu. Domknięcie wymaga watchdoga hooka.
evidence: Retro A1 (2026-10-23, 19c5038): key-down Home, Back, OK i Keyboard (klawisze bez siatki `repeat`) starszy niż `MaxRepeatHoldMs` zaczyna nowe naciśnięcie, więc Home nie zamarza po utraconym key-up; test `Swallowed_home_with_missing_key_up_is_stale_after_max_hold_and_acts_again`. Zostaje otwarte tylko dla połkniętej strzałki (siatka `repeat`) i dla resetu `_pendingLong` przy re-rejestracji hooka.
owner: epic-osk, decyzja refinementu epiki 4, 2026-10-26
status: open
ticket: `_bmad-output/backlog/spike-epic-osk-deferred-risks.md`

### DW-7: AD-8 („Jedno naciśnięcie daje jedną Action”, „InputHub jest właścicielem timingu”) i checklista odbioru epiku („dokładnie jeden wpis `action`” dla strzałek) nie opisują, że trzymana strzałka daje `press` + N×`repeat`

status: done 2026-10-22
origin: spec-deferred d26d50d7da39
source_spec: `tickets/epic-input/tickets/story-dowolna-klawiatura-i-repeat-strzaek-plan.md`
severity: low
archived: 2026-10-26

### DW-8: Sygnatura zawierająca `*`, ale niebędąca dokładnie `*` (np. `VID_1915*`), przechodzi walidację i nigdy nie dopasowuje ścieżki Raw Input, więc urządzenie wpada w fallback `*` albo `keyboard`.

status: done 2026-10-22
origin: spec-deferred 3a100f145c60
source_spec: `tickets/epic-input/tickets/story-dowolna-klawiatura-i-repeat-strzaek-plan.md`
severity: low
archived: 2026-10-26

### DW-9: Checklista odbioru epiku („Jedno naciśnięcie daje jedną Action”, „każde naciśnięcie … daje w logu dokładnie jeden wpis `action`”) nie mówi, że D-pad i OK w `app` + `moonlight`/`native` są celowo przepuszczane bez Actions

status: done 2026-10-22
origin: spec-deferred f2c6f5214410
source_spec: `tickets/epic-input/tickets/story-routing-z-lokalnej-kopii-setinputrouting-i-actions-do-orches-plan.md`
severity: low
archived: 2026-10-26

### DW-10: Realne wywołanie SendInput w KeyInjector nigdy nie zostaje wykonane przez testy ani przez przebiegi aplikacji, więc przyjęcie dwóch rekordów INPUT przez system pozostaje niepotwierdzone automatem.
origin: spec-deferred 487e5d5979fa
location: src/MediaStation.App/Platform/Input/KeyInjector.cs:22-38
source_spec: `tickets/epic-input/tickets/story-wstrzykniecie-klawisza-ze-znacznikiem-plan.md`
severity: low
reason: KeyInjector jest tylko przekazywany jako delegat w CompositionRoot.cs, testy karmią KeyInjectionPump własnym delegatem, a logi z 2026-10-20..22 nie mają żadnego wpisu keyInjection. Układ struktur pochodzi z CsWin32 (wygenerowane INPUT/KEYBDINPUT), więc ryzyko jest niskie. Rozstrzyga odbiór epiku na UM760: prawdziwy SendKey daje wpis keyInjection, a wstrzyknięty klawisz nie daje Action i wraca przez hook; alternatywnie jednorazowe wywołanie helpera poza testami.
owner: epic-osk (pierwszy `SendInput` w TextSink foreground), decyzja refinementu epiki 4, 2026-10-26
status: open
ticket: `_bmad-output/backlog/spike-epic-osk-deferred-risks.md`

### DW-11: Przestrzeń współrzędnych clipu (`GetWindowRect`, piksele fizyczne) i zdarzeń hooka (`MSLLHOOKSTRUCT.pt`) może się rozejść, gdy aplikacja nie jest per-monitor DPI aware, a okno OSK stoi na monitorze o
origin: spec-deferred ae465c0c9b5a
location: src/MediaStation.App/Platform/Input/InputHub.cs:570-601
source_spec: `tickets/epic-input/tickets/story-pointer-hook-myszy-i-clipcursor-w-osk-plan.md`
severity: medium
reason: Plan (Implementation Notes) twierdzi „WPF w .NET jest per-monitor DPI aware”, ale `src/MediaStation.App/MediaStation.App.csproj` nie deklaruje żadnego manifestu DPI, a domyślny WPF raportuje `SYSTEM_AWARE`. ALDER pracuje przy 200% i ma dwa monitory (1920×1080 oraz 1080×1920), więc na monitorze o skali innej niż systemowa prostokąt clipu i punkt hooka mogą być w różnych przestrzeniach. Rozstrzyga pomiar `GetProcessDpiAwareness` na opublikowanym exe oraz przebieg z OSK na monitorze o innej skali (porównanie prostokąta z `GetWindowRect` z punktem z hooka) — do odbioru epiku, razem z epic-osk, które pierwsze wyśle `clipHwnd`.
owner: epic-osk, decyzja refinementu epiki 4, 2026-10-26
status: open
ticket: `_bmad-output/backlog/spike-epic-osk-deferred-risks.md`

### DW-12: AD-8 nadal opisuje `SetInputRouting{ mode, appType, pointer, epoch }`, choć kontrakt ma teraz opcjonalne `clipHwnd`; dokumentacja architektury jest nieaktualna do czasu epic-osk.

status: done 2026-10-22
origin: spec-deferred 70f2922e3fed
source_spec: `tickets/epic-input/tickets/story-pointer-hook-myszy-i-clipcursor-w-osk-plan.md`
severity: low
archived: 2026-10-26

### DW-13: Wykrywanie nowego okna przeglądarki po `pid:handle` jednego `MainWindowHandle` na proces może przeoczyć drugie okno w istniejącym procesie Edge.
origin: spec-deferred 68662efb5eca
location: tools/check-home-s10.ps1 Get-BrowserWindows
source_spec: `tickets/epic-input/tickets/story-home-zawsze-zawieszony-ui-i-skrypt-s10-plan.md`
severity: low
reason: Niesprawdzone bez prawdziwego Edge i pilota; rozstrzygnie to przebieg na UM760 (porównanie z `EnumWindows` po klasie `Chrome_WidgetWin_1`). Nota 3.10: ticket zamienił `MainWindowHandle` na `EnumWindows` i liczy każde widoczne okno najwyższego poziomu klasy `Chrome_WidgetWin_*` jako osobne `pid:handle`, więc drugie okno w istniejącym procesie jest widoczne; rozstrzygnięcie na UM760 to nadal porównanie z żywym `msedge` po klasie i brak fałszywych `browser window` z tooltipów.
status: done 2026-10-27
resolution: already resolved: tools/check-home-s10.ps1 enumerates windows via EnumWindows by Chrome_WidgetWin_* class and skips owned windows (Test-HasOwner, lines 235-264); UM760 acceptance 3 (S13, HEAD 08eaade) passed Edge 16/16 and 4/4, see DW-29 resolution (07a5cde).

### DW-14: Kontrola `target not foreground` po PID może odrzucić poprawny press Edge przy wielu procesach `msedge` lub innym profilu.
origin: spec-deferred 94e635089e98
location: tools/check-home-s10.ps1 Measure-Press
source_spec: `tickets/epic-input/tickets/story-home-zawsze-zawieszony-ui-i-skrypt-s10-plan.md`
severity: low
reason: Niesprawdzone na ALDER; potwierdzić na UM760 przy odbiorze epiku. Nota 3.10: ticket porównuje foreground z dokładnym uchwytem okna celu (nie z PID), a reguła żyje w czystym `Test-TargetForeground` pokrytym `-SelfTest`; dwa nazwane wyjątki powłoki to ghost `dwm` tylko dla `shell-hung` i okno desktopu `Progman`/`WorkerW` należące do procesu `explorer` tylko dla `desktop`. UM760 ma potwierdzić, że press na Edge przy wielu procesach/profilach przechodzi, a inne okno tego samego procesu nie.
status: done 2026-10-27
resolution: already resolved: tools/check-home-s10.ps1 compares foreground with the exact target window handle in Test-TargetForeground (-SelfTest covered); UM760 acceptance 3 (S13, HEAD 08eaade) passed Edge 16/16 and 4/4, see DW-29 resolution (07a5cde).

### DW-15: `Get-BrowserWindows` i `Find-WindowProcess` nie odróżniają okien najwyższego poziomu z właścicielem (`GW_OWNER`) od okien bez właściciela.
origin: review 3.10, 2026-10-22
location: tools/check-home-s10.ps1 — `Get-BrowserWindowIds` / `Find-WindowProcess`
source_spec: `tickets/epic-input/tickets/story-refactor-sweep-plan.md`
severity: medium (unverified)
reason: Diff 3.10 zastąpił `Process.MainWindowHandle` (pierwsze widoczne okno najwyższego poziomu **bez właściciela**) enumeracją `EnumWindows`, która nie niesie informacji o właścicielu, a filtr klasy `Chrome_WidgetWin_*` przepuszcza także `Chrome_WidgetWin_0`, używane przez Chromium dla okien niebędących oknami przeglądarki. Gdyby w oknie pomiaru pojawiło się widoczne okno z właścicielem (tooltip/popup) albo widoczne `Chrome_WidgetWin_0`, S10 zaliczyłoby poprawny press jako `browser window` lub `target not foreground` (fałszywy FAIL) albo wybrało zły uchwyt celu. Osiągalności nie wykazano: na ALDER jedyne widoczne okno przeglądarki to `Chrome_WidgetWin_1` bez właściciela, a `Find-WindowProcess` dla MediaStation zwrócił ten sam uchwyt co `MainWindowHandle` w stanie normalnym i zawieszonym. Rozstrzyga przebieg S10 na UM760 z Edge i Moonlightem: porównanie zbioru `pid:handle` i uchwytu celu z `MainWindowHandle` oraz brak fałszywych `browser window`. Naprawa: pole `Owner` (`GetWindow(hwnd, GW_OWNER)`) w `Get-TopLevelWindows` i pomijanie okien z właścicielem w obu miejscach.
status: done 2026-10-27
resolution: already resolved: tools/check-home-s10.ps1:235-243 Test-HasOwner reads the GW_OWNER field and is applied at lines 264, 299 and 385 (window enumeration, Select-EdgeWindow, Find-WindowProcess), so owned windows are skipped.

### DW-16: S10 nie odróżnia okien najwyższego poziomu z właścicielem (`GW_OWNER`) od okien bez właściciela: `EnumWindows` w `Get-BrowserWindowIds` i `Find-WindowProcess` przepuszcza widoczne okna z właścicielem

status: done 2026-10-23
origin: spec-deferred 176036279b85
source_spec: `tickets/epic-input/tickets/story-refactor-sweep-plan.md`
archived: 2026-10-26

### DW-17: `Keyboard` jest emitowane jako Action w `app` + `web` także bez `textFocus`; epic-osk musi zbramkować je w Orchestratorze, zanim podłączy OSK
origin: retro epiki 3, 2026-10-22 (F7a/A2)
location: src/MediaStation.Core/Input/InputPolicy.cs (emituje `Keyboard` w każdym wierszu AD-8); src/MediaStation.Core/Orchestrator/Orchestrator.cs:220-225 (ignoruje Actions inne niż `Home`)
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A2
severity: medium
reason: AD-8/AD-9 każą otwierać OSK z `Keyboard` w `app` + `web` tylko przy `textFocus{active:true}`, a InputHub sam nie bramkuje i dziś Orchestrator ignoruje każdą Action poza `Home`, więc zachowanie odpowiada tabeli tylko przypadkiem. Pierwsza story epic-osk, która podłączy `Keyboard` do OSK, musi dodać bramkę `textFocus` w Orchestratorze i test: `Keyboard` w `app` + `web` bez `textFocus` nie otwiera OSK, a z `textFocus` otwiera. Bez tego przycisk Klawiatura otworzy OSK w przeglądarce bez pola tekstowego.
owner: epic-osk, decyzja refinementu epiki 4, 2026-10-26
status: open
ticket: `_bmad-output/backlog/spike-epic-osk-deferred-risks.md`

### DW-18: Orchestrator nie zachowuje `rootPid` z `AppWindowAttached`, którego `epic-windows-audio` potrzebuje do AD-15.

status: done 2026-10-26
origin: retro epiki 2, 2026-10-22 (A3), przeniesione z prozy w `epic-orchestrator-lifecycle.md`
source_spec: `tickets/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle-retrospective.md`, A3
severity: medium
archived: 2026-10-26

### DW-19: `OrchestratorTests` (1 553 linii) i switch dispatchu (12 gałęzi) nie zostały rozbite przed kolejnymi epikami.
origin: retro epiki 2 (A4), potwierdzone w retro epiki 3 (follow-through)
location: tests/MediaStation.Core.Tests/OrchestratorTests.cs; src/MediaStation.Core/Orchestrator/Orchestrator.cs:71-84
source_spec: `tickets/epic-input/epic-input-retrospective.md`, follow-through A4
severity: low
reason: `epic-input` dodała sink i command, nie gałąź, więc rozbicie nie stało się pilne. Wyzwalacz: pierwsza epika, która dodaje gałąź dispatchu (`epic-home-screen`, `epic-osk`) najpierw wydziela grupy handlerów per funkcja i dzieli plik testów.
owner: epic-home-screen, story 4.2 (refactor otwierający), 2026-10-26
status: done 2026-10-26
resolution: story 4.2 (`00c2c1d`): handlery Orchestratora w partialach (`Orchestrator.cs` 196 linii), testy podzielone (m.in. `OrchestratorFailureTests.cs`); retro epiki 4, F2/A2.

### DW-20: Plany ticketów nie zapisują HEAD zamknięcia, a squashe pętli są nazywane `Merge`.
origin: retro epiki 2 (A6), retro epiki 3 (A5)
location: bmad-loop (narzędzie), plany w `tickets/*/tickets/`
source_spec: `tickets/epic-input/epic-input-retrospective.md`, F17
severity: low
reason: Atrybucja per ticket zadziałała, bo żadna epika się nie przeplatała; następna przeplatana będzie wymagała ręcznej oceny zakresu commitów. Naprawa po stronie pętli: zapisać close HEAD w planie przy zamknięciu, nie etykietować jednorodzicowych squashy jako `Merge`.
owner: bmad-loop tooling
status: open
ticket: `_bmad-output/backlog/story-bmad-loop-close-head-timeout-reconciliation.md`

### DW-21: Pętla nie uzgadnia planu w stanie `built` z sesją dev zakończoną timeoutem.
origin: retro epiki 2 (A7)
location: bmad-loop (narzędzie)
source_spec: `tickets/epic-input/epic-input-retrospective.md`, follow-through A7
severity: low
reason: Nie wystąpiło w epice 3 (brak timeoutu sesji dev), więc ani niezrealizowane, ani obalone. Zostaje jako znany brak do pierwszego timeoutu.
owner: bmad-loop tooling
status: open
ticket: `_bmad-output/backlog/story-bmad-loop-close-head-timeout-reconciliation.md`

### DW-22: Clip kursora jest liczony tylko przy zmianie `epoch`; zdegenerowany `GetWindowRect` jest przyjmowany jako clip.
origin: retro epiki 3, 2026-10-23 (F9/A6/Q5)
location: src/MediaStation.App/Platform/Input/ClipCursorGate.cs:54-57; InputHub.SyncClip
source_spec: `tickets/epic-input/epic-input-retrospective.md`, F9, A6
severity: medium
reason: Dla stałego, nieaktywowanego okna OSK epoch-only wystarcza na start. Epic-osk musi odrzucić zdegenerowany (zerowy, zminimalizowany) `GetWindowRect`, bo inaczej każde kliknięcie w trybie `osk` zostanie połknięte, oraz przemierzyć clip przy przesunięciu lub zmianie rozmiaru okna i przy zmianie aktywacji. Wspólnie z DW-11 (przestrzeń DPI).
owner: epic-osk
status: open
ticket: `_bmad-output/backlog/spike-epic-osk-deferred-risks.md`

### DW-23: Wzorzec „źródła App linkowane do `MediaStation.Core.Tests`" (19 plików) jest opisany tylko komentarzem w csproj.
origin: retro epiki 3, 2026-10-23 (F5/A7)
location: tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A7
severity: low
reason: Świadoma konwencja (accepted as-is), ale niezapisana w architekturze, więc jest odkrywana ponownie. Przy następnym dotknięciu architektury dopisać ją jako konwencję projektu.
owner: author, następny pass architektury
status: done 2026-10-26
resolution: konwencja dopisana w architekturze (commit 11c7d36): Consistency Conventions, wiersz „Testy kodu App” (linkowanie źródeł App bez WPF, WebView2 i Win32 do `MediaStation.Core.Tests`, wydzielanie testowanej logiki z typów WPF/WebView2/Win32), oraz AD-13 → Testy.

### DW-24: Dedupe wciśniętych usages w kalibracji czyści wszystkie usages przy dowolnym key-up pilota.
origin: retro epiki 3, 2026-10-23 (F10/A8)
location: src/MediaStation.Core/Input/InputCalibration.cs
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A8
severity: low
reason: Przy równoległym trzymaniu dwóch przycisków pilota puszczenie jednego pozwala zalogować drugi ponownie. Klucz dedupe powinien być VK→usage, nie globalny reset.
owner: backlog: story „`--calibrate-input` zapisuje wybrany przycisk Klawiatura do input.json”, 2026-10-26
status: done 2026-10-28
resolution: resolved by sweep bundle dw-calibrate-input-hardening
resolution-undo: bb3da08324fed9006132303b8ad20dfbecd360dfd9469d8aeaf7458207d66c5b 2026-10-28 7374617475733a206f70656e
ticket: `_bmad-output/backlog/story-calibrate-input-dedupe-usages.md`

### DW-25: `InputHub` (815 linii) i `InputPolicy` (678 linii) przerosły rolę „cienkiego adaptera".
origin: retro epiki 3, 2026-10-23 (F2/F3/A11)
location: src/MediaStation.App/Platform/Input/InputHub.cs; src/MediaStation.Core/Input/InputPolicy.cs
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A11
severity: low
reason: Klasy są spójne, ale każda story dokładała 150-520 linii. Przed rozszerzeniem przez epic-osk lub epic-browserhost wydzielić z `InputHub` polling XInput i pointer/clip, a z `InputPolicy` mapowanie pada; wspólny helper pompy w tle przy trzeciej pompie.
owner: epic-osk / epic-browserhost (pierwsza z nich)
status: open
ticket: `_bmad-output/backlog/spike-epic-osk-deferred-risks.md`

### DW-26: Refactor sweep (3.10) nie miał listy zakresu i zużył 20,5 M tokenów (budżet 2 M).
origin: retro epiki 3, 2026-10-23 (F16/A10)
location: bmad-loop (planowanie sweepów), PO
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A10
severity: low
reason: Zakres „odroczone znaleziska ze stories 1-9" rozrósł się do przepisania narzędzia, które potem wymagało własnego review, pominiętego. Sweep ma mieć listę ID z ledgera, dzielić się przy przekroczeniu budżetu sesji i nie przepisywać narzędzi bez wpisu w ledgerze. Story 4.10 zastosowała to: lista ID ustalona na starcie była pusta (stories 4.1–4.9 i 4.11 odroczyły 0 znalezisk), kodu nie zmieniono. Wpis zostaje `open` do zamknięcia przez orkiestrator/PO po jego weryfikacji 4.10.
owner: PO / bmad-loop tooling; zastosowane w zakresie story 4.10 (Refactor sweep z zamkniętą listą ID), 2026-10-26
status: done 2026-10-26
resolution: zasada zastosowana i zweryfikowana w 4.10 (`2d7cfaa`): lista ID ustalona na starcie, pusta, bez zmian w kodzie i bez przepisywania narzędzi; retro epiki 4, follow-through A10. Zamknięte przez PO.

### DW-27: `--calibrate-input` filtruje po sygnaturach z `input.json`, więc nowy pilot (inny VID/PID) nie daje żadnego logu.
origin: retro epiki 3, 2026-10-23 (Q4)
location: src/MediaStation.Core/Input/InputCalibration.cs; `spike/` (sygnatury G20S Pro)
source_spec: `tickets/epic-input/epic-input-retrospective.md`, Q4
severity: low
reason: Decyzja 2026-10-23: na teraz wystarcza ręczne odkrycie sygnatury (G20S Pro znany ze spike S10). Ulepszenie: kalibracja loguje nieznane urządzenia HID, gdy nic nie pasuje. Dotyczy kroku odbioru „wybór przycisku Klawiatura".
owner: author, kiedy zmieni się pilot
status: done 2026-10-28
resolution: resolved by sweep bundle dw-calibrate-input-hardening
resolution-undo: bb3da08324fed9006132303b8ad20dfbecd360dfd9469d8aeaf7458207d66c5b 2026-10-28 7374617475733a206f70656e
ticket: `_bmad-output/backlog/story-calibrate-input-accept-new-remote-signature.md`

### DW-28: Generator schematów (`MediaStation.SchemaExport`) i zwykły `dotnet publish` nie działają na maszynach z Application Control.
origin: plan AD-8 foreground gate, 2026-10-23
location: `tools/MediaStation.SchemaExport`; `web/contracts/schema`, `web/contracts/generated`; build `src/MediaStation.App`
source_spec: `_bmad-output/plan-ad8-foreground-gate.md`
severity: medium
reason: Application Control blokuje świeży niepodpisany `MediaStation.SchemaExport.dll` (0x800711C7, CodeIntegrity 3077/3033), więc `dotnet build` nie odświeża `web/contracts` i publish wymaga `-p:SkipWebBuild=true`, ręcznego `npm run build` w `web/` i ręcznego skopiowania `web/shell/dist` do `publish/shell`. Schemat `set-managed-windows.schema.json` i `generated/set-managed-windows.ts` zostały napisane ręcznie w formacie istniejących plików (pliki nie są w gicie, więc nic nie rozjeżdża się w repo; test `CoreMessageContractTests` sprawdza kształt schematu z kodu). Dotyczy też UM760, jeśli ma tę samą politykę. Rozwiązanie: podpisać narzędzie albo dodać je do reguł Application Control; alternatywnie generować schematy z testów.
owner: epic-home-screen, story 4.1, 2026-10-26
status: done 2026-10-26
resolution: story 4.1 (`d7780e4`): eksport schematów w przebiegu testów; odbiór epiki 4 na UM760 (P0.2, P0.3): `dotnet publish` bez ręcznych kroków i czysty `git status` po buildzie.

### DW-29: `check-home-s10.ps1` nie znajduje okna Edge jako celu (`no target window`), mimo że okno Edge jest otwarte.
origin: plan `tickets/epic-input/epic-input-um760-acceptance-2.md`, S12 (rerun 1), 2026-10-25
location: `tools/check-home-s10.ps1` (wybór celu `edge`)
source_spec: `tickets/epic-input/epic-input-um760-acceptance-2.md`, S12; DW-13, DW-14, DW-15
severity: medium
reason: Na UM760 (HEAD 2bb3c95) powtórka S12 z otwartymi przez użytkownika oknami dała moonlight 4/4 i desktop 4/4, ale edge 4 × `no target window`; shell-hung dał 2 × `browser window` i 2 × `target not foreground`. Przyczyna nie zbadana (acceptance nie diagnozuje); powiązane z DW-13/14/15 (wykrywanie okien Edge po `pid:handle`, `GW_OWNER`). Bez tego nie ma wyniku 16/16 (Done when 3).
owner: bmad-build (S12 odbioru epiki 3)
status: done 2026-10-26
resolution: bug 3.13 (`07a5cde`) zmienił wybór celu Edge i kolejność celów; odbiór epiki 3 (`epic-input-um760-acceptance-3.md`, S13, HEAD 08eaade, UM760): dwa uruchomienia 16/16 i 4/4.

### DW-30: `rawInput` w `--calibrate-input` loguje tylko Home i Back; OK, strzałki nie dają wpisów, a przycisk asystenta głosowego G20S Pro ma tylko usage 0x00CF bez VK.
origin: plan `tickets/epic-input/epic-input-um760-acceptance-2.md`, S9 i S10, 2026-10-25
location: `src/MediaStation.Core/Input/InputCalibration.cs`; `src/MediaStation.App/Platform/Input/InputHub.cs`; AD-8
source_spec: `tickets/epic-input/epic-input.md`, Done when 5; `tickets/epic-input/tickets/story-home-przy-shellu-na-wierzchu-bez-raw-input-klawiatury.md`, wymaganie 4
severity: medium
reason: Trzy przebiegi S9 na UM760 (HEAD 2bb3c95): wpisy tylko Home (vk 0xAC, usage 0x0223) i Back (vk 0xA6, usage 0x0224), oba injected true, `llBeforeRawMs` null; brak OK i strzałek; asystent głosowy: usage 0x00CF, `hasVk` false. Kryterium „≥7 wpisów z hasVk” niespełnione. S10: żaden przycisk z VK nie pasuje na Klawiaturę, więc Source conflict do `bmad-architecture` (wyzwalacz HID zamiast VK, zmiana AD-8). Dodatkowo obserwacja użytkownika: Home w trybie kalibracji (nie połyka klawiszy) otworzył Edge.
owner: bmad-architecture (Source conflict), bmad-build (wpisy kalibracji)
status: done 2026-10-26
resolution: bug 3.14 (`c0dabb3`, kryterium zawężone w `120f28f`), AD-8 z wyzwalaczem HID `0x00CF` (`e9fab30`), stories 3.15–3.17 (`5498b5f`, `f4b931b`, `08eaade`); odbiór epiki 3 na UM760 zaakceptowany (`epic-input-um760-acceptance-3.md`).

### DW-31: Zawieszony shell zasłonięty oknem-duchem `dwm` nie dostaje Home od hooka LL (brak `keyEnter`/`action` w logu aplikacji), więc Home otwiera przeglądarkę.
origin: spec-deferred 36ef84bc8a59
location: src/MediaStation.App/Platform/Input/InputHub.cs (hook LL)
source_spec: `tickets/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md`
severity: medium
reason: Logi przebiegów 1 i 2 z 2026-10-25 (brak wpisów w oknach shell-hung #1 i #2, na wierzchu `Ghost` procesu `dwm`) oraz sonda na UM760: z duchem na wierzchu wstrzyknięte Enter nie dało `keyEnter`, bez ducha dało. Wstrzyknięcie mogła zablokować UIPI, więc dla fizycznego pilota rozstrzygnie dopiero przebieg użytkownika; ewentualna poprawka to zmiana uprawnień hooka (AD-8).
status: open
decision: 2026-10-28 Verify with physical remote first
decision: 2026-10-28 Verify with physical remote first
ticket: `_bmad-output/backlog/bug-home-shell-hung-dwm-ghost-hook.md`

### DW-32: Domyślny cel Edge to pierwsze okno `msedge` z tytułem, więc może to być obce okno użytkownika (np. DevTools, PWA).
origin: spec-deferred 34503e8bb1f7
location: tools/check-home-s10.ps1 Select-EdgeWindow
source_spec: `tickets/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md`
severity: low
reason: `Select-EdgeWindow` nie odróżnia okien tego samego procesu; zawężenie daje `-EdgeTitle`. Skutek: Home wysyłany nad niewłaściwym oknem Edge, wynik nadal mierzy log aplikacji.
status: done 2026-10-28
resolution: resolved by sweep bundle dw-s10-select-edge-window
resolution-undo: 54d7f6d1f822fd8c918dddaa39634bece12a278f969945de2c34b7d705e2ef76 2026-10-28 7374617475733a206f70656e
ticket: `_bmad-output/backlog/bug-s10-select-edge-window.md`

### DW-33: Stany kafelka `Starting`, `Stopping`, `Foreground` i `Background`, kropka „w tle”, powrót fokusu na ostatni kafelek i długie OK na `Background` nie zostały sprawdzone na żywym shellu.
origin: odbiór epiki 4 na UM760, 2026-10-26 (`tickets/epic-home-screen/epic-home-screen-um760-acceptance.md`, Poza zakresem i werdykt)
location: web/shell (kafelek, zakładki, model fokusu, AppMenu); Orchestrator (snapshot z `focusRequest`)
source_spec: `tickets/epic-home-screen/epic-home-screen.md`, R2, R3, R4, Done when 2, 3, 4
severity: medium
reason: Bez hostów jedynym stanem osiągalnym na UM760 był `Failed`; reszta jest pokryta tylko testami na snapshotach testowych (decyzja tracer bulleta epiki 4). Pierwsze prawdziwe okno aplikacji daje dev harness epic-windows-audio (story 5.1). Odbiór tej epiki na UM760 dokłada scenariusze: kafelek w `Starting` i `Foreground` po OK, w `Background` z kropką na zakładce po Home, fokus na ostatnim kafelku po powrocie, AppMenu z długiego OK na `Background`, a w logu `RestartApp` odróżniony od `CloseApp` (po restarcie nowy start aplikacji).
owner: epic-windows-audio (odbiór całej epiki przez `fp-acceptance`), decyzja PO przy zamknięciu epiki 4, 2026-10-26
status: open

### DW-34: `tools/inject-power.ps1` liczy wszystkie `snapshotPublished` procesu, więc na aplikacji używanej przed skryptem kończy się fałszywym FAIL.
origin: odbiór epiki 4 na UM760, 2026-10-26, S18 (pierwszy przebieg)
location: tools/inject-power.ps1 (sprawdzenie `snapshotPublished: expected 3`, `seq` od 1)
source_spec: `tickets/epic-home-screen/epic-home-screen-um760-acceptance.md`, S18
severity: low
reason: Przy aplikacji działającej od S5 skrypt zobaczył 9 snapshotów (rev 1–7 sprzed wybudzeń) i `seq 1 instead of 2`, choć oba wybudzenia dały poprawne `focusRequest{default}` z rosnącym `seq`; powtórka na świeżo uruchomionej aplikacji przeszła. Naprawa: liczyć snapshoty i `seq` względem ostatniego snapshotu przed pierwszym `powerSignalInjected`, albo odmówić startu z jasnym komunikatem, gdy aplikacja ma historię. Do tego czasu plany odbioru uruchamiają S18 na świeżej aplikacji.
owner: epic-windows-audio, story 7 (Refactor sweep, zamknięta lista), 2026-10-26
status: done 2026-10-27
resolution: already resolved: tools/inject-power.ps1:205-215 Get-WakeSnapshotProblems treats snapshots before the first powerSignalInjected marker as history and takes the last as baseline, with seq asserted as baseline+1 and +2.

### DW-35: Granica most/okno shella (4.3/4.11) i przyrost `tokens.css` (6 commitów) nie przeszły review na poziomie całej epiki.
origin: retro epiki 4, 2026-10-26 (F5/A3)
location: src/MediaStation.App/Shell/ (`ShellBridge`, `ShellOutbox`, `ShellWindow`); web/shell/src/tokens.css
source_spec: `tickets/epic-home-screen/epic-home-screen-retrospective.md`, F5, A3
severity: low
reason: Review szło per ticket; retro nie uruchomiło review diffu `fe0a400..2d7cfaa`. Odbiór na UM760 nie pokazał błędów hosta shella (0× `navigationBlocked`, `newWindowBlocked`, `webViewProcessFailed`), więc ryzyko dotyczy struktury, nie zachowania. Jedno `bmad-review` tych plików przed pierwszą story, która zmienia most albo `tokens.css`.
owner: pierwsza story, która zmienia `ShellBridge`, `ShellOutbox`, `ShellWindow` albo `tokens.css` (plan epiki 5 ich nie zmienia, 2026-10-26)
status: open

### DW-36: `MissingHostPort` odpowiada `CommandFailed{ errorCode: unexpected }`, więc każdy OK na kafelku bez hosta daje `warn commandFailed`, nieodróżnialny od prawdziwej awarii.
origin: odbiór epiki 4 na UM760, 2026-10-26, S5 (pierwszy przebieg, zmiana planu z `--allow "msg=commandFailed"`)
location: src/MediaStation.App/Hosts/MissingHostPort.cs:45,59
source_spec: `tickets/epic-home-screen/epic-home-screen-um760-acceptance.md`, S5
severity: low
reason: Brak hosta jest stanem przewidzianym (decyzja tracer bulleta), a `unexpected` znaczy awarię, więc plany odbioru muszą dopuszczać `commandFailed` i tracą czułość na prawdziwe błędy. Rozwiązanie: dedykowany `ErrorCode` (np. `HostMissing`) w AD-4 albo zniknięcie `MissingHostPort` dla typów z prawdziwym hostem; rozstrzyga epic-browserhost przy pierwszym prawdziwym hoście.
owner: epic-browserhost (pierwszy prawdziwy host)
status: open

### DW-37: Sessions muted by a MediaStation instance that crashed or was killed stay muted after restart, because the engine ledger is empty and Unmute only restores what it muted.
origin: spec-deferred 0b621765860a
location: src/MediaStation.App/Platform/Audio/AudioRouterEngine.cs
source_spec: `tickets/epic-windows-audio/tickets/story-audiorouter-wyciszenie-drzewa-procesow-od-rootpid-i-nowe-ses-plan.md`
severity: medium
reason: ISimpleAudioVolume mute persists for the life of the browser session; after supervised restart and adoption, UnmuteProcessTree finds no ledger entries. Settle by deciding in story 3 whether an explicit Unmute forces unmute of in-tree sessions outside the ledger.
status: done 2026-10-27
resolution: story 5.3 (`b955fc4`): adopcja wysyła `MuteProcessTree`, a silnik przy wyciszaniu sesji spoza rejestru zapisuje ją w rejestrze (`AudioRouterEngine.Mute`), więc następne `UnmuteProcessTree` ją odcisza; sprawdzone w kodzie przez PO.

### DW-38: Notatki w plikach ticketów (epic-browserhost, epic-osk, epic-windows-audio) nadal opisują tryb okna i źródło wersji jako otwarte pytania.
origin: spec-deferred 2373b289ca6d
location: tickets/epic-browserhost/epic-browserhost.md:43-44
source_spec: `tickets/epic-browserhost/tickets/spike-tryb-okna-przegladarki-i-zrodo-wersji-poza-cdp-plan.md`
severity: low
reason: Edge-case-hunter wskazał epic-browserhost.md (Notes: dwa Open question), epic-osk.md i epic-windows-audio.md ("czeka na story 1"). Workflow zabrania zapisu w plikach ticketów, więc to zadanie dla Sofii po oznaczeniu 6.1 jako done.
status: done 2026-10-27
resolution: Update z rozstrzygnięciem AD-1/AD-13 dopisany w Notes epic-browserhost, epic-osk i epic-windows-audio.

### DW-39: Gdy BrowserHost padnie po starcie przeglądarki, a przed zalogowaniem BrowserVersionChanged, następny start widzi from==to i zmiana wersji ginie.
origin: spec-deferred 7a8458976315
location: architecture-mediastation.md AD-13 (Aktualizacja przeglądarki)
source_spec: `tickets/epic-browserhost/tickets/spike-tryb-okna-przegladarki-i-zrodo-wersji-poza-cdp-plan.md`
severity: low
reason: Last Version zapisuje przeglądarka przy starcie, a zdarzenie loguje BrowserHost. Do rozstrzygnięcia w story BrowserHost (np. log from/to przed launchem albo własny zapis ostatnio zalogowanej wersji).
status: open

### DW-40: Kiosk nie był sprawdzony przy kilku monitorach, po zmianie rozdzielczości lub wyłączeniu TV, ani z nakładką OSK nad oknem.
origin: spec-deferred 645cbb60e6a4
location: architecture-mediastation.md AD-1
source_spec: `tickets/epic-browserhost/tickets/spike-tryb-okna-przegladarki-i-zrodo-wersji-poza-cdp-plan.md`
reason: Pomiar miał jeden monitor 1920x1080. Nakładka nad pełnoekranowym Edge jest pierwszą story epiki OSK (Deferred w spine), a zachowanie okna po wygaszeniu ekranu należy do WindowManager.
status: open

### DW-41: Home pressed while the shell is already the mode (app window raised by Alt+Tab or script) is logged as an action but sends no window commands, so the app stays on top.
origin: spec-deferred 5fd06e4660a2
location: src/MediaStation.Core/Orchestrator (Home in shell mode)
source_spec: `tickets/epic-windows-audio/tickets/story-edge-i-chrome-w-trybie-okna-z-6-1-i-moonlight-borderless-prz-plan.md`
severity: medium
reason: Observed on UM760 while building 5.5: with the app restored outside MediaStation, presses 2-4 of -Restore failed 3 of 4 until the script resumed the app through the product (OK on the tile). Epic note says "powrót daje Home".
status: done 2026-10-27
resolution: resolved by sweep bundle dw-decision-dw-41
resolution-undo: c53354fb5b41cdd93673be0e91c73922c9e0aaaf5e98332af8b6a188489cb9b7 2026-10-27 7374617475733a206f70656e
decision: 2026-10-27 Home in shell mode re-asserts the shell — In OnHome, when no app is Foreground or Starting, emit ShowShell and, for Background apps with a known Hwnd, MinimizeWindow, without changing app state; add Orchestrator tests for Home in shell mode with a Background app and for no app at all, and check the epic-windows-audio -Restore scenario in the check script.

### DW-42: Home on a Foreground app with no known Hwnd still returns no commands (no ShowShell).
origin: spec-deferred c6eca4ee9cc4
location: src/MediaStation.Core/Orchestrator/Orchestrator.Home.cs:58
source_spec: `plan-dw-decision-dw-41-home-in-shell-mode.md`
severity: low
reason: Orchestrator.Home.cs Foreground branch returns OrchestratorResult.None when Hwnd is null; pre-existing and outside DW-41's intent (no Foreground app).
status: open
ticket: `_bmad-output/backlog/bug-foreground-without-hwnd-shows-shell.md`

### DW-43: Moonlight po Home i powrocie zawiesza się na „Uruchamianie input stream establishment…”, bez transmisji (log pokazuje poprawne komendy okienne).
origin: odbiór epiki 5 na UM760, 2026-10-27, S6 (user: fail), `tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`
location: dev harness Moonlight (`Hosts/DevHarness/`) i przywrócenie okna Moonlight (`Platform/Window`), przyczyna niezbadana
source_spec: `tickets/epic-windows-audio/epic-windows-audio.md`, Done when 2, R2
severity: high
reason: Za pierwszym OK strumień startuje; po Home (okno zminimalizowane) i ponownym OK wraca UI Moonlighta z zamrożonym komunikatem, także po ręcznym restarcie Moonlighta. Użytkownik uznał to za fail i nie zaakceptował epiki. Nie zdiagnozowano w odbiorze (nieznane, czy winny jest kod epiki, czy zachowanie Moonlight-qt po minimalizacji).
owner: bmad-build (bug epiki 5), potem ponowny odbiór S6 i S18–S21
ticket: `tickets/epic-windows-audio/tickets/tickets.toml` entry 8 (bug 5.8), 2026-10-27
status: open

### DW-44: Po Alt+Tab ekran główny jest na wierzchu, ale tryb wejścia zostaje „aplikacja”, więc pilot i klawiatura go nie sterują do czasu Home.
origin: odbiór epiki 5 na UM760, 2026-10-27, przerwa przed S18
location: src/MediaStation.Core/Orchestrator (routing wejścia a foreground), AD-8
source_spec: `tickets/epic-windows-audio/epic-windows-audio.md`, Notes (polityka `ForegroundChanged`, 2026-10-26)
severity: medium
reason: Zgodne z AD-8 (zmiana okna poza MediaStation nie zmienia trybu), ale użytkownik chce, żeby pilot działał także po Alt+Tab. To zmiana wymagań, nie błąd wykonania.
owner: bmad-architecture (zmiana AD-8)
ticket: entry 9 w epic-windows-audio (5.9), czeka na decyzję bmad-architecture, 2026-10-27
status: done 2026-10-28
resolution: already resolved: story 5.9 (dd14a69): Orchestrator.Foreground.cs OnForegroundChanged resolves the window owner and re-evaluates input mode after a settle timer, so Alt+Tab no longer leaves routing on app mode.

### DW-45: AudioRouter widzi tylko wyciszenia, które sam zainicjował; ręczne zmiany w mikserze Windows i Alt+Tab rozjeżdżają jego stan.
origin: odbiór epiki 5 na UM760, 2026-10-27, S19 (1. podejście) i przerwa przed S18
location: src/MediaStation.App/Platform/Audio/AudioRouterEngine.cs (ledger własnych wyciszeń)
source_spec: `tickets/epic-windows-audio/epic-windows-audio.md`, R5, R6, Done when 4
severity: medium
reason: Po ręcznych zmianach w mikserze użytkownik miał ciszę mimo odciszonej reguły (Moonlight) i słyszał Edge razem ze streamem (po ręcznym odciszeniu Edge). W czystym stanie dźwięk działał (S19 powtórka, S20, S21, S23, S24). Do rozważenia: uzgadnianie ze stanem sesji audio przy każdym przejściu.
owner: bmad-architecture
ticket: entry 10 w epic-windows-audio (5.10), czeka na decyzję bmad-architecture, 2026-10-27
status: done 2026-10-28
resolution: already resolved: story 5.10 (199144e): AudioRouterEngine/CoreAudioSessionBackend reconcile the audio rule as target state over the Windows mixer instead of an own-mutes-only ledger.

### DW-46: Kropka „działa w tle” przy zakładce Video jest w prawym górnym rogu pigułki; użytkownik woli ją wycentrowaną w pionie.
origin: odbiór epiki 5 na UM760, 2026-10-27, S3
location: web/shell (zakładka, `tab-running-dot`, `DESIGN.md`)
source_spec: `tickets/epic-home-screen/epic-home-screen.md`, R2
severity: low
reason: Kosmetyka ekranu głównego z epiki 4, zauważona przy odbiorze epiki 5; nie blokuje.
ticket: `_bmad-output/backlog/bug-kropka-w-tle-przy-zakladce-nie-jest-wycentrowana-w-pionie.md` (backlog 5), 2026-10-27
status: open

### DW-47: Production Moonlight host (epic-moonlight-native) must pick the SDL stream window (`SDL_app`), not the first visible window of the process, or it reproduces bug 5.8.
origin: spec-deferred dccc73e9da61
location: src/MediaStation.App/Hosts/DevHarness/HarnessMoonlight.cs:StreamWindowClass
source_spec: `tickets/epic-windows-audio/tickets/bug-moonlight-po-home-i-powrocie-stoi-na-uruchamianie-input-stre-plan.md`
severity: medium
reason: Moonlight-qt shows a Qt connecting window (`Qt6120QWindowIcon`) for ~4-6 s before the `SDL_app` stream window; the dev harness fix is in `Hosts/DevHarness` only. Blind hunter finding; the future host does not exist yet.
status: open
ticket: `_bmad-output/backlog/story-moonlight-production-picks-sdl-window.md`

### DW-48: With only a `game` category tile in apps.json the injected OK did not start the tile (restore pass 0/4, no StartApp); cause not diagnosed.
origin: spec-deferred dd29815a18e8
location: tools/check-home-s10.ps1:New-RestoreAppsJson
source_spec: `tickets/epic-windows-audio/tickets/bug-moonlight-po-home-i-powrocie-stoi-na-uruchamianie-input-stre-plan.md`
reason: Seen on UM760 2026-10-27 with the generated one-tile apps.json (category `game`): `action ok` logged, no start for 40 s; the same tile in `video` starts at once. Could be shell default focus with a lone `game` row; check with a `game`-only config and OK from a fresh start. Unverified.
status: open
ticket: `_bmad-output/backlog/bug-game-only-tile-ok-start.md`

### DW-49: A window command whose result is lost keeps evaluation blocked until the next transitionId.
origin: spec-deferred 6d67a13a710e
location: src/MediaStation.Core/Orchestrator/Orchestrator.Foreground.cs (IsHardBlocked)
source_spec: `tickets/epic-windows-audio/tickets/story-pilot-steruje-tym-co-jest-na-wierzchu-takze-po-alt-tab-plan.md`
reason: Window-command results are tracked in a pending set cleared only by CommandCompleted/CommandFailed or a transitionId change; executors always answer (AD-4), so unverified. Settled by a test or run where the executor drops a result.
status: open
ticket: `_bmad-output/backlog/bug-window-command-result-loss-unblocks-evaluation.md`


### DW-50: PowerSignalRouter reports the GUID_CONSOLE_DISPLAY_STATE registration echo as a winning systemResumed at startup, because the echo arrives after the registration window closes.
origin: spec-deferred c0f617c750f2
location: src/MediaStation.App/Platform/System/PowerSignalRouter.cs
source_spec: `plan-dw-power-notifications-harness.md`
severity: medium
reason: Observed by the new harness; Registration_echo_is_delivered_through_the_hook_after_start pins the behavior and Registration_echo_is_not_reported_as_a_wake (Skipped) holds the desired one. Pre-existing router/shim timing, not caused by the harness.
status: open
