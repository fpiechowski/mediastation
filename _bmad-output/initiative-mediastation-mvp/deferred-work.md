### DW-1: The adapter shim's Win32/WPF plumbing (HWND hook, `RegisterPowerSettingNotification`, `SystemEvents` subscribe, `POWERBROADCAST_SETTING` copy) has no automated test.
origin: spec-deferred a8812465b072
location: src/MediaStation.App/Platform/System/PowerNotifications.cs — `PowerNotifications.OnWindowMessage` / `PowerNotifications.OnPowerModeChanged`
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
severity: low
reason: The registration-echo window and the three-source mapping moved out of the HWND-bound shim into `PowerSignalRouter` (linked into `MediaStation.Core.Tests`, see `PowerSignalRouterTests`), so what stays untested is only the plumbing that needs a real window. Settle it with an HWND-level harness, or with an echo observation during the UM760 epic acceptance.
status: open

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

### DW-11: Przestrzeń współrzędnych clipu (`GetWindowRect`, piksele fizyczne) i zdarzeń hooka (`MSLLHOOKSTRUCT.pt`) może się rozejść, gdy aplikacja nie jest per-monitor DPI aware, a okno OSK stoi na monitorze o
origin: spec-deferred ae465c0c9b5a
location: src/MediaStation.App/Platform/Input/InputHub.cs:570-601
source_spec: `tickets/epic-input/tickets/story-pointer-hook-myszy-i-clipcursor-w-osk-plan.md`
severity: medium
reason: Plan (Implementation Notes) twierdzi „WPF w .NET jest per-monitor DPI aware”, ale `src/MediaStation.App/MediaStation.App.csproj` nie deklaruje żadnego manifestu DPI, a domyślny WPF raportuje `SYSTEM_AWARE`. ALDER pracuje przy 200% i ma dwa monitory (1920×1080 oraz 1080×1920), więc na monitorze o skali innej niż systemowa prostokąt clipu i punkt hooka mogą być w różnych przestrzeniach. Rozstrzyga pomiar `GetProcessDpiAwareness` na opublikowanym exe oraz przebieg z OSK na monitorze o innej skali (porównanie prostokąta z `GetWindowRect` z punktem z hooka) — do odbioru epiku, razem z epic-osk, które pierwsze wyśle `clipHwnd`.
owner: epic-osk, decyzja refinementu epiki 4, 2026-10-26
status: open

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
status: open

### DW-14: Kontrola `target not foreground` po PID może odrzucić poprawny press Edge przy wielu procesach `msedge` lub innym profilu.
origin: spec-deferred 94e635089e98
location: tools/check-home-s10.ps1 Measure-Press
source_spec: `tickets/epic-input/tickets/story-home-zawsze-zawieszony-ui-i-skrypt-s10-plan.md`
severity: low
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
status: open

### DW-20: Plany ticketów nie zapisują HEAD zamknięcia, a squashe pętli są nazywane `Merge`.
origin: retro epiki 2 (A6), retro epiki 3 (A5)
location: bmad-loop (narzędzie), plany w `tickets/*/tickets/`
source_spec: `tickets/epic-input/epic-input-retrospective.md`, F17
severity: low
reason: Atrybucja per ticket zadziałała, bo żadna epika się nie przeplatała; następna przeplatana będzie wymagała ręcznej oceny zakresu commitów. Naprawa po stronie pętli: zapisać close HEAD w planie przy zamknięciu, nie etykietować jednorodzicowych squashy jako `Merge`.
owner: bmad-loop tooling
status: open

### DW-21: Pętla nie uzgadnia planu w stanie `built` z sesją dev zakończoną timeoutem.
origin: retro epiki 2 (A7)
location: bmad-loop (narzędzie)
source_spec: `tickets/epic-input/epic-input-retrospective.md`, follow-through A7
severity: low
reason: Nie wystąpiło w epice 3 (brak timeoutu sesji dev), więc ani niezrealizowane, ani obalone. Zostaje jako znany brak do pierwszego timeoutu.
owner: bmad-loop tooling
status: open

### DW-22: Clip kursora jest liczony tylko przy zmianie `epoch`; zdegenerowany `GetWindowRect` jest przyjmowany jako clip.
origin: retro epiki 3, 2026-10-23 (F9/A6/Q5)
location: src/MediaStation.App/Platform/Input/ClipCursorGate.cs:54-57; InputHub.SyncClip
source_spec: `tickets/epic-input/epic-input-retrospective.md`, F9, A6
severity: medium
reason: Dla stałego, nieaktywowanego okna OSK epoch-only wystarcza na start. Epic-osk musi odrzucić zdegenerowany (zerowy, zminimalizowany) `GetWindowRect`, bo inaczej każde kliknięcie w trybie `osk` zostanie połknięte, oraz przemierzyć clip przy przesunięciu lub zmianie rozmiaru okna i przy zmianie aktywacji. Wspólnie z DW-11 (przestrzeń DPI).
owner: epic-osk
status: open

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
status: open

### DW-25: `InputHub` (815 linii) i `InputPolicy` (678 linii) przerosły rolę „cienkiego adaptera".
origin: retro epiki 3, 2026-10-23 (F2/F3/A11)
location: src/MediaStation.App/Platform/Input/InputHub.cs; src/MediaStation.Core/Input/InputPolicy.cs
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A11
severity: low
reason: Klasy są spójne, ale każda story dokładała 150-520 linii. Przed rozszerzeniem przez epic-osk lub epic-browserhost wydzielić z `InputHub` polling XInput i pointer/clip, a z `InputPolicy` mapowanie pada; wspólny helper pompy w tle przy trzeciej pompie.
owner: epic-osk / epic-browserhost (pierwsza z nich)
status: open

### DW-26: Refactor sweep (3.10) nie miał listy zakresu i zużył 20,5 M tokenów (budżet 2 M).
origin: retro epiki 3, 2026-10-23 (F16/A10)
location: bmad-loop (planowanie sweepów), PO
source_spec: `tickets/epic-input/epic-input-retrospective.md`, A10
severity: low
reason: Zakres „odroczone znaleziska ze stories 1-9" rozrósł się do przepisania narzędzia, które potem wymagało własnego review, pominiętego. Sweep ma mieć listę ID z ledgera, dzielić się przy przekroczeniu budżetu sesji i nie przepisywać narzędzi bez wpisu w ledgerze.
owner: PO / bmad-loop tooling; zastosowane w zakresie story 4.10 (Refactor sweep z zamkniętą listą ID), 2026-10-26
status: open

### DW-27: `--calibrate-input` filtruje po sygnaturach z `input.json`, więc nowy pilot (inny VID/PID) nie daje żadnego logu.
origin: retro epiki 3, 2026-10-23 (Q4)
location: src/MediaStation.Core/Input/InputCalibration.cs; `spike/` (sygnatury G20S Pro)
source_spec: `tickets/epic-input/epic-input-retrospective.md`, Q4
severity: low
reason: Decyzja 2026-10-23: na teraz wystarcza ręczne odkrycie sygnatury (G20S Pro znany ze spike S10). Ulepszenie: kalibracja loguje nieznane urządzenia HID, gdy nic nie pasuje. Dotyczy kroku odbioru „wybór przycisku Klawiatura".
owner: author, kiedy zmieni się pilot
status: open

### DW-28: Generator schematów (`MediaStation.SchemaExport`) i zwykły `dotnet publish` nie działają na maszynach z Application Control.
origin: plan AD-8 foreground gate, 2026-10-23
location: `tools/MediaStation.SchemaExport`; `web/contracts/schema`, `web/contracts/generated`; build `src/MediaStation.App`
source_spec: `_bmad-output/plan-ad8-foreground-gate.md`
severity: medium
reason: Application Control blokuje świeży niepodpisany `MediaStation.SchemaExport.dll` (0x800711C7, CodeIntegrity 3077/3033), więc `dotnet build` nie odświeża `web/contracts` i publish wymaga `-p:SkipWebBuild=true`, ręcznego `npm run build` w `web/` i ręcznego skopiowania `web/shell/dist` do `publish/shell`. Schemat `set-managed-windows.schema.json` i `generated/set-managed-windows.ts` zostały napisane ręcznie w formacie istniejących plików (pliki nie są w gicie, więc nic nie rozjeżdża się w repo; test `CoreMessageContractTests` sprawdza kształt schematu z kodu). Dotyczy też UM760, jeśli ma tę samą politykę. Rozwiązanie: podpisać narzędzie albo dodać je do reguł Application Control; alternatywnie generować schematy z testów.
owner: epic-home-screen, story 4.1, 2026-10-26
status: open

### DW-29: `check-home-s10.ps1` nie znajduje okna Edge jako celu (`no target window`), mimo że okno Edge jest otwarte.
origin: plan `tickets/epic-input/epic-input-um760-acceptance-2.md`, S12 (rerun 1), 2026-10-25
location: `tools/check-home-s10.ps1` (wybór celu `edge`)
source_spec: `tickets/epic-input/epic-input-um760-acceptance-2.md`, S12; DW-13, DW-14, DW-15
severity: medium
reason: Na UM760 (HEAD 2bb3c95) powtórka S12 z otwartymi przez użytkownika oknami dała moonlight 4/4 i desktop 4/4, ale edge 4 × `no target window`; shell-hung dał 2 × `browser window` i 2 × `target not foreground`. Przyczyna nie zbadana (acceptance nie diagnozuje); powiązane z DW-13/14/15 (wykrywanie okien Edge po `pid:handle`, `GW_OWNER`). Bez tego nie ma wyniku 16/16 (Done when 3).
owner: bmad-build (S12 odbioru epiki 3)
status: open

### DW-30: `rawInput` w `--calibrate-input` loguje tylko Home i Back; OK, strzałki nie dają wpisów, a przycisk asystenta głosowego G20S Pro ma tylko usage 0x00CF bez VK.
origin: plan `tickets/epic-input/epic-input-um760-acceptance-2.md`, S9 i S10, 2026-10-25
location: `src/MediaStation.Core/Input/InputCalibration.cs`; `src/MediaStation.App/Platform/Input/InputHub.cs`; AD-8
source_spec: `tickets/epic-input/epic-input.md`, Done when 5; `tickets/epic-input/tickets/story-home-przy-shellu-na-wierzchu-bez-raw-input-klawiatury.md`, wymaganie 4
severity: medium
reason: Trzy przebiegi S9 na UM760 (HEAD 2bb3c95): wpisy tylko Home (vk 0xAC, usage 0x0223) i Back (vk 0xA6, usage 0x0224), oba injected true, `llBeforeRawMs` null; brak OK i strzałek; asystent głosowy: usage 0x00CF, `hasVk` false. Kryterium „≥7 wpisów z hasVk” niespełnione. S10: żaden przycisk z VK nie pasuje na Klawiaturę, więc Source conflict do `bmad-architecture` (wyzwalacz HID zamiast VK, zmiana AD-8). Dodatkowo obserwacja użytkownika: Home w trybie kalibracji (nie połyka klawiszy) otworzył Edge.
owner: bmad-architecture (Source conflict), bmad-build (wpisy kalibracji)
status: open

### DW-31: Zawieszony shell zasłonięty oknem-duchem `dwm` nie dostaje Home od hooka LL (brak `keyEnter`/`action` w logu aplikacji), więc Home otwiera przeglądarkę.
origin: spec-deferred 36ef84bc8a59
location: src/MediaStation.App/Platform/Input/InputHub.cs (hook LL)
source_spec: `tickets/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md`
severity: medium
reason: Logi przebiegów 1 i 2 z 2026-10-25 (brak wpisów w oknach shell-hung #1 i #2, na wierzchu `Ghost` procesu `dwm`) oraz sonda na UM760: z duchem na wierzchu wstrzyknięte Enter nie dało `keyEnter`, bez ducha dało. Wstrzyknięcie mogła zablokować UIPI, więc dla fizycznego pilota rozstrzygnie dopiero przebieg użytkownika; ewentualna poprawka to zmiana uprawnień hooka (AD-8).
status: open

### DW-32: Domyślny cel Edge to pierwsze okno `msedge` z tytułem, więc może to być obce okno użytkownika (np. DevTools, PWA).
origin: spec-deferred 34503e8bb1f7
location: tools/check-home-s10.ps1 Select-EdgeWindow
source_spec: `tickets/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md`
severity: low
reason: `Select-EdgeWindow` nie odróżnia okien tego samego procesu; zawężenie daje `-EdgeTitle`. Skutek: Home wysyłany nad niewłaściwym oknem Edge, wynik nadal mierzy log aplikacji.
status: open
