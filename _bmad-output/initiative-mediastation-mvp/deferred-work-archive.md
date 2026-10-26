### DW-2: A throw from `HwndSource.AddHook` or the `SystemEvents.PowerModeChanged` subscribe after the hook is attached leaks that hook for the process lifetime.
origin: spec-deferred e179f05da2da
location: src/MediaStation.App/Platform/System/PowerNotifications.cs — `PowerNotifications` constructor / `PowerSourcesAttachment.Attach`
source_spec: `epic-orchestrator-lifecycle/story-systemevents-wybudzenie-z-modern-standby-na-um760-plan.md`
reason: Reachable only if `AddHook` or the event subscribe throws during startup, after part of the sources is already attached; the partially constructed instance never reaches `Dispose`.
status: done 2026-10-22
archived: 2026-10-26
resolution: zamknięte w story 2.9 (evidence powyżej)
evidence: `PowerSourcesAttachment.Attach` releases every source attached before the failing one, in reverse order, and rethrows, so the HWND hook, the display-state registration and the `SystemEvents` subscription cannot outlive a failed construction; `PowerNotifications.Dispose` uses the same idempotent release and the registration line still reports the startup state with `powerModeChanged: true`. Covered by `PowerSourcesAttachmentTests` (releasing on a throwing attach, reverse order, idempotence) plus the full `dotnet test MediaStation.slnx -c Release` and the publish/start/inject-power smoke; forcing a throw inside the Win32 plumbing itself still needs an HWND-level harness, which the epic forbids per ticket.

### DW-5: Kolejność zwalniania adapterów w composition root (hub → pompa → power → timer → pętla) nie ma testu.
origin: spec-deferred e995e2719403
location: src/MediaStation.App/TeardownSequence.cs (handler app.Exit w src/MediaStation.App/CompositionRoot.cs:246-253)
source_spec: `tickets/epic-input/tickets/story-tracer-bullet-pilot-na-donglu-daje-actions-z-watku-input-plan.md`
severity: low
reason: Brak projektu testów App i seamu w `CompositionRoot`, więc zamiana kolejności `inputHub?.Dispose()` i `actionPump?.Dispose()` nie zapali żadnego testu; skutek to Action w locie bez wpisu w logu przy wyjściu. Ten sam dług jest już zapisany w retrospektywie epiki (composition root bez testów).
status: done 2026-10-22
archived: 2026-10-26
resolution: zamknięte w epice 3 (evidence powyżej)
evidence: Kolejność i semantyka wyjątków wyszły do `TeardownSequence` (App, bez WPF/Win32), linkowanego do `MediaStation.Core.Tests` jak `UiHang.cs`; handler `app.Exit` buduje sekwencję w chwili wyjścia, bo część slotów jest wtedy `null`. `TeardownSequenceTests` pinują kolejność siedmiu adapterów (hub → pompa Actions → wstrzyknięcia → power → timer → pętla), zwalnianie reszty po awarii i pierwszy wyjątek na zewnątrz; `stop` zostaje ostatnim wpisem, bo `AttachStopLogging` rejestruje się później. `dotnet test MediaStation.slnx -c Release` 862/862 plus publish/start/`CloseMainWindow` z `stop {exitCode: 0}`.

### DW-7: AD-8 („Jedno naciśnięcie daje jedną Action”, „InputHub jest właścicielem timingu”) i checklista odbioru epiku („dokładnie jeden wpis `action`” dla strzałek) nie opisują, że trzymana strzałka daje `press` + N×`repeat`
origin: spec-deferred d26d50d7da39
location: _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md:189,191
source_spec: `tickets/epic-input/tickets/story-dowolna-klawiatura-i-repeat-strzaek-plan.md`
severity: low
reason: Zweryfikowane w review (2 przebiegi): `architecture-mediastation.md:189,191` i `epic-input.md:70` nie mają reguły repeat, a kod emituje `press` + N×`repeat` z `InputPolicy.CollectRepeats`. AD-8 już wymienia `phase: repeat` w kontrakcie Actions (`:188`), a dopisanie reguły to pass dokumentacyjny epiki — wzorzec `clipHwnd` w `epic-input.md:61`. Wymaga wpisu w notatce odbioru epiku, żeby operator nie odczytał checklisty jako porażki przy trzymanej strzałce.
status: done 2026-10-22
archived: 2026-10-26
resolution: AD-8 i checklista odbioru dopisane (evidence powyżej)
evidence: AD-8 ma regułę `repeat`: trzymana strzałka daje `press` na key-down, a potem `repeat` po `timing.repeatDelayMs` i co `timing.repeatIntervalMs` aż do key-up, wyłącznie dla strzałek, a autorepeat systemu nie daje kolejnej Action. Checklista odbioru epiki (`epic-input.md`, Done when 1 i pozycja 1 odbioru po story 2) mówi o `press` + `repeat` przy trzymanej strzałce, więc operator nie odczyta tego jako porażki. Zmiana wyłącznie dokumentacyjna, bez zmian kodu (2026-10-22).

### DW-8: Sygnatura zawierająca `*`, ale niebędąca dokładnie `*` (np. `VID_1915*`), przechodzi walidację i nigdy nie dopasowuje ścieżki Raw Input, więc urządzenie wpada w fallback `*` albo `keyboard`.
origin: spec-deferred 3a100f145c60
location: src/MediaStation.Core/Input/InputTarget.cs:42
source_spec: `tickets/epic-input/tickets/story-dowolna-klawiatura-i-repeat-strzaek-plan.md`
severity: low
reason: `DeviceSignature.Matches` specjalnie traktuje wyłącznie `InputDevice.WildcardSignature` i poza tym używa `Contains`, a ścieżka Raw Input nigdy nie zawiera `*`; walidacja wymaga tylko niepustej sygnatury. Zachowanie samo w sobie jest sprzed tej story (nowe jest tylko znaczenie `*`), a naprawa to reguła walidacji odrzucająca `*` w sygnaturze innej niż dokładnie `*` — decyzja planistyczna, nie poprawka tej zmiany.
status: done 2026-10-22
archived: 2026-10-26
resolution: walidacja odrzuca `*` w środku sygnatury (evidence powyżej)
evidence: `DeviceSignature.HasEmbeddedWildcard` obok `Matches` plus jedna gałąź `else if` w `ConfigValidation.CheckInput` dają jeden `configError` na `$.devices[i].signature` dla `*` w środku sygnatury (`VID_1915*`, `*VID*`, `**`), a dokładnie `*` zostaje wildcardem wymagającym `source: keyboard`. Schematu nie ruszano (reguły semantyczne tylko w runtime), sample `tools/samples/config` bez zmian i dalej zielony w `Sample_config_files_are_valid`; `dotnet test MediaStation.slnx -c Release` 862/862, a publish z podmienionym `input.json` dał log `start → configError → stop {exitCode: 0}` na tej samej ścieżce.

### DW-9: Checklista odbioru epiku („Jedno naciśnięcie daje jedną Action”, „każde naciśnięcie … daje w logu dokładnie jeden wpis `action`”) nie mówi, że D-pad i OK w `app` + `moonlight`/`native` są celowo przepuszczane bez Actions
origin: spec-deferred f2c6f5214410
location: _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md:23,70
source_spec: `tickets/epic-input/tickets/story-routing-z-lokalnej-kopii-setinputrouting-i-actions-do-orches-plan.md`
severity: low
reason: Zweryfikowane: `architecture-mediastation.md:202` (wiersz tabeli AD-8) każe przepuszczać D-pad i OK w `app` + `moonlight`/`native`, a `epic-input.md:23` (Done when 1) i `:70` (checklista po story 2) nadal obiecują jedną Action i dokładnie jeden wpis `action` na naciśnięcie. Nowy test `App_with_moonlight_or_native_passes_dpad_and_ok_without_actions_or_grid` pinuje zero Actions. Ten sam typ luki (checklista nie opisuje `repeat`) jest już zapisany jako DW-7 z uzasadnieniem „żeby operator nie odczytał checklisty jako porażki”; poprawka edytuje dokumentację architektury i notatkę odbioru epiki, więc należy do passa dokumentacyjnego epiki, nie do tej story.
status: done 2026-10-22
archived: 2026-10-26
resolution: AD-8 i checklista odbioru poprawione (evidence powyżej)
evidence: Wiersz tabeli AD-8 dla `app` + `moonlight`/`native` mówi teraz „przechodzą do aplikacji (bez Actions)”, a checklista `epic-input.md` (Done when 1 i pozycja 1 odbioru po story 2) jawnie opisuje celowe przepuszczenie D-pada i OK bez wpisu `action`. Zmiana wyłącznie dokumentacyjna, bez zmian kodu (2026-10-22).

### DW-12: AD-8 nadal opisuje `SetInputRouting{ mode, appType, pointer, epoch }`, choć kontrakt ma teraz opcjonalne `clipHwnd`; dokumentacja architektury jest nieaktualna do czasu epic-osk.
origin: spec-deferred 70f2922e3fed
location: _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md:194
source_spec: `tickets/epic-input/tickets/story-pointer-hook-myszy-i-clipcursor-w-osk-plan.md`
severity: low
reason: `architecture-mediastation.md:194` nie wymienia `clipHwnd`, a `set-input-routing.schema.json` i testy kontraktu pinują klucz `clipHwnd` w każdej kopercie. Epika już to przewidziała: `epic-input.md:61` każe dopisać `clipHwnd` do AD-8 (`bmad-architecture`) przed epic-osk, więc luka jest śledzona, ale nie ma wykonawcy w tym przebiegu.
status: done 2026-10-22
archived: 2026-10-26
resolution: AD-8 opisuje `clipHwnd` (evidence powyżej)
evidence: AD-8 opisuje `SetInputRouting{ mode, appType, pointer, epoch, clipHwnd? }` z opcjonalnym `clipHwnd` (`null` = brak ograniczenia), `ClipCursor` do prostokąta okna w trybie `osk`, połykanie kliknięć poza nim i zwalnianie przy zmianie trybu. Frontmatter architektury ma `updated: 2026-10-22`. Zmiana wyłącznie dokumentacyjna, bez zmian kodu (2026-10-22).

### DW-16: S10 nie odróżnia okien najwyższego poziomu z właścicielem (`GW_OWNER`) od okien bez właściciela: `EnumWindows` w `Get-BrowserWindowIds` i `Find-WindowProcess` przepuszcza widoczne okna z właścicielem
origin: spec-deferred 176036279b85
location: tools/check-home-s10.ps1 Get-BrowserWindowIds / Find-WindowProcess
source_spec: `tickets/epic-input/tickets/story-refactor-sweep-plan.md`
reason: Review pass 2: zmiana zastąpiła `Process.MainWindowHandle` (pierwsze widoczne okno najwyższego poziomu bez właściciela) enumeracją bez informacji o właścicielu, a filtr klasy jest prefiksem `Chrome_WidgetWin_*`. Osiągalności nie wykazano: na ALDER jedyne widoczne okno przeglądarki to `Chrome_WidgetWin_1` bez właściciela, a `Find-WindowProcess` dla MediaStation zwrócił ten sam uchwyt co `MainWindowHandle` (także w stanie zawieszonym). Rozstrzyga przebieg S10 na UM760 z Edge i Moonlightem (porównanie zbioru `pid:handle` i uchwytu celu z `MainWindowHandle`, brak fałszywych `browser window`); wpis w ledgerze jako DW-15. Naprawa: pole `Owner` (`GetWindow(hwnd, GW_OWNER)`) w `Get-TopLevelWindows` i pomijanie okien z właścicielem w obu miejscach.

evidence: Duplikat DW-15 (ten sam problem `GW_OWNER`, wpis z harvestu spec-deferral story 3.10); kanoniczny jest DW-15. Scalone 2026-10-23 (retro epiki 3, A4).
status: done 2026-10-23
archived: 2026-10-26
resolution: duplikat DW-15, scalony w retro epiki 3 (A4)

### DW-18: Orchestrator nie zachowuje `rootPid` z `AppWindowAttached`, którego `epic-windows-audio` potrzebuje do AD-15.
origin: retro epiki 2, 2026-10-22 (A3), przeniesione z prozy w `epic-orchestrator-lifecycle.md`
location: src/MediaStation.Core/Orchestrator/Orchestrator.cs
source_spec: `tickets/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle-retrospective.md`, A3
severity: medium
reason: Retro epiki 3 (follow-through A3) stwierdziło, że punkt istniał tylko jako proza, bez właściciela. Pierwsza story `epic-windows-audio`, która mutuje audio lub okna po PID, musi wziąć `rootPid` z `AppWindowAttached` i zachować go w stanie sesji.
owner: epic-windows-audio
status: done 2026-10-26
archived: 2026-10-26
resolution: Orchestrator zapisuje `app.RootPid` z `AppWindowAttached` (`Orchestrator.cs:315,353`), więc `epic-windows-audio` ma go w stanie sesji

