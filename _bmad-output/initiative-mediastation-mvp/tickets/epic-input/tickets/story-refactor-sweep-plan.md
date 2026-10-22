---
title: 'Refactor sweep'
type: 'refactor'
ticket: '10'
created: '2026-10-22'
status: done
baseline_revision: '6dae28a5653189bc2b9aaa197ec288c2abb39b37'
route: 'full'
route_source: 'auto'
risk: 'low'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 1
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      S10 nie odróżnia okien najwyższego poziomu z właścicielem (`GW_OWNER`) od okien bez właściciela: `EnumWindows` w `Get-BrowserWindowIds` i `Find-WindowProcess` przepuszcza widoczne okna z właścicielem (tooltip/popup) oraz klasę `Chrome_WidgetWin_0`, co mogłoby dać fałszywy `browser window` albo `target not foreground`.
    evidence: |-
      Review pass 2: zmiana zastąpiła `Process.MainWindowHandle` (pierwsze widoczne okno najwyższego poziomu bez właściciela) enumeracją bez informacji o właścicielu, a filtr klasy jest prefiksem `Chrome_WidgetWin_*`. Osiągalności nie wykazano: na ALDER jedyne widoczne okno przeglądarki to `Chrome_WidgetWin_1` bez właściciela, a `Find-WindowProcess` dla MediaStation zwrócił ten sam uchwyt co `MainWindowHandle` (także w stanie zawieszonym). Rozstrzyga przebieg S10 na UM760 z Edge i Moonlightem (porównanie zbioru `pid:handle` i uchwytu celu z `MainWindowHandle`, brak fałszywych `browser window`); wpis w ledgerze jako DW-15. Naprawa: pole `Owner` (`GetWindow(hwnd, GW_OWNER)`) w `Get-TopLevelWindows` i pomijanie okien z właścicielem w obu miejscach.
    location: >-
      tools/check-home-s10.ps1 Get-BrowserWindowIds / Find-WindowProcess
    severity: medium (unverified)
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Plany stories 1–9 epiki zostawiły dziesięć findings z dyspozycją `defer` (ledger: DW-5…DW-14). Cztery dotyczą kodu epiki: DW-5 (kolejność zwalniania adapterów w composition root bez testu), DW-13 (S10 liczy nowe okno przeglądarki po jednym `MainWindowHandle` na proces), DW-14 (foreground sprawdzany po PID celu, nie po oknie) oraz DW-8 (sygnatura z `*` w środku przechodzi walidację i nigdy nie dopasowuje ścieżki Raw Input, więc urządzenie po cichu wpada w fallback). Dziś S10 może zaliczyć nieprzełknięty Home (DW-13) albo odrzucić poprawny press Edge (DW-14), a teardown inputu nie ma żadnego testu.

**Approach:** Domknąć cztery findings w kodzie epiki bez zmiany zachowania produktu: kolejność zwalniania wychodzi do testowalnego typu App linkowanego do testów Core, skrypt S10 opiera zbiór okien przeglądarki i decyzję o foreground na enumeracji okien najwyższego poziomu (`EnumWindows`) i dokładnym uchwycie okna celu, a walidacja odrzuca `*` w środku sygnatury (dokładnie `*` zostaje bez zmian). Pozostałe defer zostają: DW-6 i DW-11 to nowe zachowanie (watchdog hooka, DPI), DW-10 wymaga sprzętu, DW-7/DW-9/DW-12 to dokumentacja, nie kod.

## Boundaries & Constraints

**Always:** te same linie logu (`msg`, poziom, `data`), te same Actions, decyzje routingu, kody wyjścia i kolejność zwalniania (hub → pompa Actions → pompa wstrzyknięć → power → timer → pętla), a `stop` pozostaje ostatnim wpisem; `tools/samples/config` bez zmian; schematy bez zmian (reguły semantyczne żyją tylko w runtime); `dotnet build -c Release` 0 ostrzeżeń; testy w stylu `Snake_case_opis`; komentarze i komunikaty walidacji po polsku jak w otoczeniu.

**Never:** DW-6 (watchdog hooka / reset `InputPolicy`), DW-10 (realny `SendInput`), DW-11 (DPI awareness), DW-7/DW-9/DW-12 (pass dokumentacyjny architektury i notatki odbioru epiki); projekt `App.Tests`; zmiany w `InputPolicy`, `InputHub`, `InputCalibration`, `LogActionSink`, `ActionQueue`, `web/**`, `MessageTypes.All`; nowe pola configu; zmiana punktacji `Test-Press`, fixture i przebiegów S10.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Teardown porządku | siedem adapterów, w tym `null` w slotach opcjonalnych | każdy obecny zwolniony w kolejności hub → pompa → wstrzyknięcia → power → timer → pętla | — |
| Teardown z wyjątkiem | drugi adapter rzuca w `Dispose` | pozostałe i tak zwolnione, potem rzucany pierwszy wyjątek (ta sama instancja) | wyjątek leci dalej z `app.Exit` |
| Sygnatura z `*` w środku | `"signature": "VID_1915*"`, `"*VID*"`, `"**"` | jeden `configError` na `$.devices[i].signature`, `Config` = `null` | ekran błędu configu jak każdy inny błąd |
| Sygnatura `*` | `"signature": "*"`, `"source": "keyboard"` | config poprawny jak dziś (sample) | — |
| Nowe okno w tym samym procesie | `before` = 1 okno `msedge`, `after` = 2 okna tego samego PID | zbiór `pid:handle` zmienia się, press = `browser window` | — |
| Foreground okna celu | foreground = uchwyt celu | `pass` | — |
| Foreground innego okna | foreground = inne okno, ten sam PID | `fail (target not foreground)` | — |
| Zawieszony shell | foreground należy do `dwm` (ghost) | `pass` tylko dla targetu `shell-hung` | — |

</frozen-after-approval>

## Code Map

- `src/MediaStation.App/CompositionRoot.cs:242-290` — handler `app.Exit` z siatką `try/finally`: `inputHub`, `actionPump`, `keyInjection`, `notifications`, `reporter`, `timers`, `loop`; komentarz 243-244 opisuje kolejność; `inputHub`/`actionPump`/`notifications`/`reporter` są przypisywane **po** rejestracji handlera (237-240, 297-316), więc sekwencja musi powstawać w momencie wyjścia. `AttachStopLogging` (359-362) jest rejestrowany później, stąd `stop` na końcu.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj:26-45` — wzorzec linkowania plików App bez WPF/Win32 (`UiHang.cs`, `ClipCursorGate.cs`, `ActionPump.cs`); brak projektu testów App (granica epiki).
- `src/MediaStation.Core/Input/InputTarget.cs:46-62` — `internal static DeviceSignature.Matches`: `*` pasuje do każdej niepustej ścieżki, poza tym `Contains`; jedna definicja sygnatur dla `InputPolicy` i `InputCalibration`.
- `src/MediaStation.Core/Config/ConfigValidation.cs:283-315` — `CheckInput`: łańcuch `if/else if` (pusta → unikalna → `*` wymaga `keyboard`) i `JsonErrorPath.Property(devicePath, "signature")`; każdy błąd to jeden `ConfigError(file: "input.json", path, reason)`.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs:186,211-232,569-630,1202-1213` — `Sample_config_files_are_valid`, `InvalidSamples`, testy sygnatur (`Signature_must_not_be_empty`, `Wildcard_*`), helper `InputJson(signature: …)`/`SingleError`, `SampleDir`.
- `tools/check-home-s10.ps1:193-204` — `Get-BrowserWindows` (`Process.MainWindowHandle` na proces → `pid:handle`); `:367-431` — `Get-MainWindowHandle`, `Get-WindowProcessId`, `Find-WindowProcess`; `:472-511` — `Measure-Press`: `Set-FrontWindow` → 700 ms → porównanie PID celu i foregroundu + wyjątek `dwm` dla `shell-hung`; `:513-537` — zbiór okien przed/po i `Test-Press`; `:236-316` — `Invoke-SelfTest` na fixture; `:562-576` — `-SelfTest` kończy przed `Add-Type` (blok P/Invoke `keybd_event`, `FindWindow`, `IsIconic`, `ShowWindow`, `SetForegroundWindow`, `GetForegroundWindow`, `GetWindowThreadProcessId`); dokumentacja `:1-36`.
- `tools/fixtures/check-home-s10.fixture.jsonl` — nietknięty; oczekiwania self-testu 0/1/2/1 (`:241-248`) i przypadek `browser window` (`:293-306`).
- `tests/MediaStation.Core.Tests/RecordingLog.cs`, `InputPolicyTests.cs` — wzorce fake'ów/logu; `InputCalibration` filtruje tylko `remote`, więc `*` nigdy nie jest w `_remoteSignatures`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.App/TeardownSequence.cs` **(nowy)** — `internal sealed class TeardownSequence : IDisposable`, ctor `(IDisposable? inputHub, IDisposable? actionPump, IDisposable keyInjection, IDisposable? notifications, IDisposable? reporter, IDisposable timers, IDisposable loop)`; `Dispose` zwalnia po kolei, łapie pierwszy wyjątek przez `ExceptionDispatchInfo`, zwalnia resztę i rzuca go na końcu — DW-5.
- [x] `src/MediaStation.App/CompositionRoot.cs` — `app.Exit += (_, _) => new TeardownSequence(inputHub, actionPump, keyInjection, notifications, reporter, timers, loop).Dispose();` zamiast siatki `try/finally`; komentarz o kolejności zostaje — DW-5.
- [x] `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` — `<Compile Include="..\..\src\MediaStation.App\TeardownSequence.cs" Link="App\TeardownSequence.cs" />` — DW-5.
- [x] `tests/MediaStation.Core.Tests/TeardownSequenceTests.cs` **(nowy)** — kolejność siedmiu fake'ów, wyjątek w środku (reszta zwolniona, ten sam wyjątek), tolerancja `null` — DW-5.
- [x] `src/MediaStation.Core/Input/InputTarget.cs` — `DeviceSignature.HasEmbeddedWildcard(string signature)` (bool: zawiera `*`, ale nie jest dokładnie `InputDevice.WildcardSignature` ani pusty), obok `Matches` — DW-8.
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` — w łańcuchu `CheckInput` po regule `*` → `keyboard`: `else if (DeviceSignature.HasEmbeddedWildcard(device.Signature))` → `errors.Add(signaturePath, $"sygnatura {Quote(device.Signature)} może zawierać „*” tylko jako całą sygnaturę");` — DW-8.
- [x] `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — wiersze theory `VID_1915*`, `*VID*`, `**` (jeden błąd, ścieżka `$.devices[0].signature`, `Config` null) + test `HasEmbeddedWildcard` (`*` = false, `VID_1915&PID_1025` = false, `VID_1915*` = true) — DW-8.
- [x] `tools/check-home-s10.ps1:193-204,367-431,472-511` — `Get-TopLevelWindows` przez `EnumWindows` (Add-Type: delegat + `EnumWindows`, `GetWindowTextW`, `GetClassNameW`; `GetWindowThreadProcessId` już jest) → `{Handle, ProcessId, ProcessName, Class, Title}`; `Get-BrowserWindows` = każde widoczne okno najwyższego poziomu procesu `msedge`/`chrome` **klasy `Chrome_WidgetWin_*`** jako `pid:handle` (filtr klasy odsiewa tooltipy i popupy tego samego procesu, które pojawiają się i znikają same); `Find-WindowProcess` po tej samej enumeracji (nazwa/PID/tytuł); foreground zaliczony przy `foreground -eq Handle`, plus dwa nazwane wyjątki: ghost `dwm` dla `shell-hung` i desktop (`Progman`/`WorkerW` **należący do procesu `explorer`**) — DW-13, DW-14. Dokumentacja `:1-36` mówi o oknie, nie o procesie.
- [x] `tools/check-home-s10.ps1:236-316,562-576` — czyste funkcje decyzji (`Get-BrowserWindowIds -Windows`, `Get-NewWindowIds -Before -After`, `Test-TargetForeground -Target -ForegroundHandle -TargetHandle -ForegroundProcessName -ForegroundClass`) i ich przypadki w `-SelfTest` (dwa okna jednego PID dają dwa id; okno klasy tooltipa nie daje id; inne okno tego samego PID = `fail`; ten sam uchwyt = `pass`; ghost `dwm` tylko dla `shell-hung`; desktop `Progman`/`WorkerW` explorera = `pass`, ta sama klasa innego procesu = `fail`), bez `Add-Type` — DW-13, DW-14.

**Acceptance Criteria:**
- Given opublikowany build Release self-contained z `tools/samples/config` (sample ma `"signature": "*"`), when `MediaStation.exe` wystartuje i zostanie zamknięty `CloseMainWindow`, then log ma `start` → `snapshotPublished` → `stop {exitCode: 0}`, zero `configError`/`warn`/`error`, a kod procesu to 0 (`Sample_config_files_are_valid` zielony — reguła DW-8 nie rusza `*`).
- Given repo na baseline, when `dotnet test MediaStation.slnx -c Release`, then 0 failed i nowe testy (kolejność teardownu, `*` w środku sygnatury, `HasEmbeddedWildcard`) przechodzą; when `dotnet build MediaStation.slnx -c Release`, then 0 ostrzeżeń.
- Given `input.json` z `"signature": "VID_1915*"`, when config jest walidowany, then dokładnie jeden `configError` na `$.devices[0].signature` i `LoadedConfig` = `null`; given `"signature": "*"`, then config poprawny.
- Given wyjście aplikacji, when `app.Exit` się wykonuje, then każdy adapter jest zwolniony w kolejności hub → pompa → wstrzyknięcia → power → timer → pętla (pin w `TeardownSequenceTests`), a `stop` nadal jest ostatnim wpisem logu.
- Given `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest`, when self-test przechodzi na fixture, then okna 0/1/2/1 dają `fail/pass/fail/pass`, przypadek `browser window` dalej kończy się porażką, nowe przypadki decyzji przechodzą, kod wyjścia 0.

## Implementation Notes

Wszystkie cztery findingi domknięte w kodzie epiki; zachowanie produktu bez zmian.

- DW-5: `TeardownSequence` w App (bez WPF/Win32) linkowany do testów Core tym samym wzorcem co `UiHang.cs`; handler `app.Exit` konstruuje sekwencję w chwili wyjścia, bo `inputHub`, `actionPump`, `notifications` i `reporter` są `null` przy rejestracji. Kolejność i semantyka wyjątku (pierwszy wyjątek po zwolnieniu reszty) pinowane w `TeardownSequenceTests`; `stop` nadal ostatni, bo `AttachStopLogging` rejestruje się później.
- DW-8: `DeviceSignature.HasEmbeddedWildcard` + jedna gałąź `else if` w `CheckInput`; dokładnie `*` (i puste) bez zmian, więc `Sample_config_files_are_valid` dalej zielony, a sample `tools/samples/config` nietknięty.
- DW-13/DW-14: `Get-TopLevelWindows` (`EnumWindows` + `IsWindowVisible` + `GetWindowTextW`/`GetClassNameW`, nazwy procesów cache'owane po PID) zastępuje `MainWindowHandle`/`MainWindowTitle`; `Get-BrowserWindows` liczy każde widoczne okno najwyższego poziomu, a foreground jest porównywany z uchwytem celu, nie z PID. Decyzje (`Get-BrowserWindowIds`, `Get-NewWindowIds`, `Test-TargetForeground`) są czyste i działają przed `Add-Type`, więc `-SelfTest` pokrywa je bez sprzętu; punktacja `Test-Press`, fixture i oba przebiegi bez zmian.

### Verification evidence (ALDER, Windows 11 Home, 2026-10-22)

- `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test MediaStation.slnx -c Release` — 862 total, 0 failed, 0 skipped (baseline 848 + 14 nowych: 4 `TeardownSequenceTests`, 3 wiersze `Wildcard_inside_signature_is_rejected`, 7 wierszy `HasEmbeddedWildcard_only_flags_a_wildcard_inside_a_signature`).
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` — `Self-test passed.`, exit 0; okna fixture 0/1/2/1 dają fail/pass/fail/pass, `browser window` dalej porażka, nowe przypadki decyzji: `4242:273,4242:546` (dwa okna jednego PID), nowe id `4242:546`, foreground: dokładny uchwyt = pass, inne okno tego samego PID = fail, ghost `dwm` tylko dla `shell-hung`.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + start `MediaStation.exe` z `tools/samples/config` + `CloseMainWindow` — log `start → inputRouting → powerSignalsRegistered → snapshotPublished → stop {exitCode: 0}`, zero `configError`/`warn`/`error`, `stop` ostatni, kod procesu 0; config użytkownika (`%LOCALAPPDATA%\MediaStation\config`, 3 pliki) odtworzony bajt w bajt (SHA-256 równe), katalog publish usunięty.
- Ten sam build z `input.json` podmienionym na `"signature": "VID_1915*"` — dokładnie jeden `configError` (`file: input.json`, `path: $.devices[0].signature`, powód o `*` tylko jako cała sygnatura), log `start → configError → stop {exitCode: 0}`, kod procesu 0; config użytkownika odtworzony bajt w bajt.
- `Get-TopLevelWindows` sprawdzone na żywym pulpicie ALDER (harness na funkcjach wyciętych AST z prawdziwego skryptu): 21 widocznych okien najwyższego poziomu, `chrome` rozpoznany jako `pid:handle` z klasą `Chrome_WidgetWin_1` i tytułem karty.
- S10 na sprzęcie (16/16 pilotem na donglu) oraz DW-10/DW-11 pozostają odbiorem epiku na UM760 — nieobjęte tym ticketem.

### Follow-up: poprawki po review (2026-10-22)

Review diffu wykrył dwie regresje wprowadzone przez pierwszą wersję S10; obie naprawione, reszta bez zmian.

- **DW-13 — filtr klasy okna.** `Get-BrowserWindowIds` po filtrze nazwy procesu sprawdza jeszcze klasę okna prefiksem `Chrome_WidgetWin_*`. `EnumWindows` zwraca też tooltipy i popupy należące do `msedge`/`chrome`; pojawiają się i znikają między `before` a `after`, więc poprawny press mógłby dostać fałszywe `browser window`. Zbiór pozostaje jeden `pid:handle` na okno (nigdy na proces), więc drugie okno w istniejącym procesie to nadal nowe id. Komentarz w funkcji mówi, po co jest filtr.
- **DW-14 — desktop po `MinimizeAll`.** `Test-TargetForeground` przyjmuje teraz `-ForegroundClass`, a `Measure-Press` czyta ją przez nowe `Get-WindowClass` (`GetClassNameW`) i dokłada do noty przebiegu. Dla `Target -eq 'desktop'` foreground o klasie `Progman` albo `WorkerW` jest zaliczany: po `MinimizeAll` `GetForegroundWindow()` zwraca często `WorkerW`, a `FindWindow` daje `Progman`, i oba są oknem desktopu tego samego explorera — sam dokładny uchwyt dawałby fałszywe `target not foreground` w odbiorze epiku. Żaden inny target nie dostaje tego wyjątku, a ghost `dwm` dalej działa tylko dla `shell-hung`.
- **`-SelfTest`.** Nowe przypadki: `msedge` z klasą `Chrome_Tooltip_Window` nie daje id, `chrome` z `Chrome_WidgetWin_0` daje (`4343:1365`), `desktop` z foregroundem `WorkerW` i `Progman` przechodzi, inna klasa (`CabinetWClass`) nie, a `WorkerW` nie przechodzi dla `edge` (żadnego poluzowania poza desktopem). Nagłówek skryptu opisuje nowy warunek zaliczenia (okno `Chrome_WidgetWin_*`, desktop `Progman`/`WorkerW`).
- **Re-weryfikacja:** `dotnet build MediaStation.slnx -c Release` — 0 warning, 0 error; `dotnet test MediaStation.slnx -c Release` — 862/862; `-SelfTest` — `Self-test passed.`, exit 0, wszystkie 11 przypadków decyzji zielone; harness na żywym pulpicie ALDER: filtr klasy zostawia okno `chrome` (`Chrome_WidgetWin_1`), `Get-WindowClass` zwraca `Shell_TrayWnd` dla bieżącego foregroundu, a `Test-TargetForeground` daje `False` i dla `desktop`, i dla `edge` (pasek zadań nie jest oknem desktopu — brak poluzowania).

### Loopback po review pass 1 (2026-10-22)

Review zakwalifikował zmiany jako `bad_plan` (Tasks/Design Notes nie nazywały dwóch wyjątków powłoki, a notatka o semantyce wyjątków teardownu nie odpowiadała dawnej siatce). Kod cofnięty do `baseline_revision`, plan zmieniony poza blokiem Intent (Tasks 8–9, Design Notes, Plan Change Log), a implementacja jest derywowana ponownie z poprawionego planu. Dowody z tego przebiegu powyżej dotyczą wersji sprzed loopbacku; nowe dowody dopisze przebieg po derywacji. W nowej wersji dochodzi bramka procesu `explorer` w wyjątku desktopu, więc każdy przebieg weryfikacji trzeba powtórzyć.

### Verification evidence po loopbacku (ALDER, Windows 11 Home, 2026-10-22)

- `dotnet build MediaStation.slnx -c Release` — Build succeeded, 0 Warning(s), 0 Error(s).
- `dotnet test MediaStation.slnx -c Release` — 862 total, 0 failed, 0 skipped (baseline 848 + 14 nowych: 4 `TeardownSequenceTests`, 3 wiersze `Wildcard_inside_signature_is_rejected`, 7 wierszy `HasEmbeddedWildcard_only_flags_a_wildcard_inside_a_signature`).
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` — `Self-test passed.`, exit 0; okna fixture 0/1/2/1 dają fail/pass/fail/pass, `browser window` dalej porażka, 12 przypadków decyzji zielonych: dwa okna jednego PID dają `4242:273,4242:546`, `Chrome_Tooltip_Window` nie daje id, `Chrome_WidgetWin_0` daje `4343:1365`, inne okno tego samego PID nie zalicza foregroundu, ghost `dwm` tylko dla `shell-hung`, desktop `Progman`/`WorkerW` procesu `explorer` zalicza, `CabinetWClass` i `WorkerW` obcego procesu nie zaliczają, `WorkerW` explorera nie zalicza `edge`.
- Harness na żywym pulpicie ALDER (funkcje wycięte AST z prawdziwego skryptu, ten sam blok `Add-Type`): 21 widocznych okien najwyższego poziomu; `chrome` rozpoznany jako `6440:199816` z klasą `Chrome_WidgetWin_1` i tytułem karty; `Find-WindowProcess` dla `explorer` zwraca realny uchwyt; bieżący foreground `Shell_TrayWnd` nie zalicza ani `desktop`, ani `edge`, więc nie ma poluzowania poza oknem desktopu.
- Publish `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + start `MediaStation.exe` z `tools/samples/config` + `CloseMainWindow` — log `start → inputRouting → powerSignalsRegistered → snapshotPublished → stop {exitCode: 0}`, zero `configError`/`warn`/`error`, `stop` ostatni, kod procesu 0; config użytkownika (`%LOCALAPPDATA%\MediaStation\config`, 3 pliki + pusty katalog `invalid`) odtworzony bajt w bajt (SHA-256 równe, katalogi równe), katalog publish usunięty.
- Ten sam build z `input.json` podmienionym na `"signature": "VID_1915*"` — dokładnie jeden `configError` (`file: input.json`, `path: $.devices[0].signature`, powód o `*` tylko jako cała sygnatura), log `start → configError → stop {exitCode: 0}`, kod procesu 0; config użytkownika odtworzony bajt w bajt.
- S10 na sprzęcie (16/16 pilotem na donglu) oraz DW-10/DW-11 pozostają odbiorem epiku na UM760 — nieobjęte tym ticketem.

### Residual risk

- `desktop` w S10: reguła `Progman`/`WorkerW` pokrywa oba warianty okna desktopu po `MinimizeAll`; jeśli fokus wyląduje na innym oknie shella (np. `Shell_TrayWnd` — pasek zadań), press dalej da `target not foreground`, bo to nie jest okno desktopu. Do potwierdzenia w przebiegu S10 na UM760.
- Filtr `Chrome_WidgetWin_*` opiera się na klasie okna Chromium: gdyby przyszła wersja Edge/Chrome zmieniła nazwę klasy, S10 przestałby widzieć nowe okna przeglądarki (press przeszedłby mimo otwartego okna). Wykrywalne w self-teście tylko pośrednio — fixture ma stałe klasy; do obserwacji przy odbiorze epiku.

## Plan Change Log

### 2026-10-22 — Loopback z review pass 1 (bad_plan)
- **Trigger:** review wskazał, że implementacja dodaje dwa wyjątki od reguły „foreground = dokładny uchwyt celu” (desktop `Progman`/`WorkerW`, filtr klasy `Chrome_WidgetWin_*`), których plan w Tasks/Design Notes nie nazywał, a notatka o teardownie opisywała semantykę wyjątków jako identyczną z dawną siatką `try/finally`.
- **Amended:** Tasks 8–9 mówią teraz o filtrze klasy i o desktopie ograniczonym do procesu `explorer`; Design Notes nazywają oba wyjątki powłoki, poprawiają semantykę wyjątków teardownu (pierwszy, nie ostatni) i zastępują zdanie „punktacja bez zmian” zdaniem o zmienionym zbiorze okien.
- **Known-bad state avoided:** implementacja z wyjątkiem desktopu bez bramki procesu (okno `WorkerW` dowolnej aplikacji tapet zaliczałoby press) oraz nieopisane w planie zawężenie zbioru okien do `Chrome_WidgetWin_*`.
- **KEEP:** cała reszta pierwszej implementacji jest dobra i musi przetrwać ponowną derywację — `TeardownSequence` + link w testach + `TeardownSequenceTests`, `DeviceSignature.HasEmbeddedWildcard` + gałąź `else if` w `CheckInput` + wiersze theory, `Get-TopLevelWindows`/`Get-ProcessName`/`Get-WindowClass`, czyste `Get-BrowserWindowIds`/`Get-NewWindowIds`/`Test-TargetForeground` z sekcją self-testu, zachowana punktacja `Test-Press`, fixture i przebiegi.

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 4 findings — high 0, medium 2, low 2, false 0, maybe-false 0
- findings:
  - `[low]` `[patch]` `deferred-work.md` nie odnotowuje domknięcia DW-5/DW-8 (nadal `open`, bez `evidence`) — potwierdzone: ledger `:32-38,56-62` bez zmian, a sąsiedni sweep epiki prowadzi `DW-2 resolved` z `evidence`. Akcja: DW-5 i DW-8 dostają `status: resolved` + `evidence`, DW-13/DW-14 zostają `open` z notą, co zmienił ten ticket i co rozstrzyga UM760.
  - `[medium]` `[patch]` Wyjątek desktopu w `Test-TargetForeground` patrzy tylko na klasę, więc `Progman`/`WorkerW` dowolnego procesu (np. aplikacji tapet) zalicza press — potwierdzone: gałąź czyta `$ForegroundClass`, a `$ForegroundProcessName` pomija; self-test nie ma przypadku z obcym procesem. Akcja: bramka `explorer` + przypadek negatywny w self-teście.
  - `[medium]` `[bad_plan]` Implementacja zawiera dwa wyjątki powłoki i filtr klasy, których Tasks/Design Notes nie nazywały, a macierz czyta się jako „zawsze dokładny uchwyt” — potwierdzone porównaniem diffu z planem. Amended: Tasks 8–9 + Design Notes + Plan Change Log; ponowna derywacja kodu.
  - `[low]` `[bad_plan]` Design Notes twierdzą, że semantyka wyjątków teardownu jest „ta sama” co dawna siatka `try/finally`, a przy dwóch awariach stara siatka rzucała ostatni wyjątek, nowa pierwszy — potwierdzone testem `Dispose_keeps_releasing_and_rethrows_the_first_exception` (na starej siatce nie przechodzi). Amended: notatka opisuje różnicę i jej zakres; kod zgodny z macierzą Intent.

### 2026-10-22 — Review pass 2
- verdicts: 3 findings — high 0, medium 0, low 1, false 0, maybe-false 2
- findings:
  - `[maybe-false]` `[defer]` Filtr `Chrome_WidgetWin_*` przepuszcza także `Chrome_WidgetWin_0`, choć komentarz i plan obiecują tylko prawdziwe okna przeglądarki (self-test pinuje `_0` jako id) — potwierdzone, że prefiks obejmuje `_0`, ale nie wykazano widocznego okna `_0` w oknie pomiaru (na ALDER jedyne widoczne okno przeglądarki to `Chrome_WidgetWin_1` bez właściciela); gdyby wystąpiło, press dostałby fałszywy `browser window`. Rozstrzyga S10 na UM760, wpis DW-15 w ledgerze.
  - `[maybe-false]` `[defer]` `Find-WindowProcess` nie odtwarza reguły `MainWindowHandle` „pierwsze widoczne okno bez właściciela”, więc widoczne okno z właścicielem (tooltip/popup) może stać się uchwytem celu dla `moonlight`/`shell-hung` — na ALDER zero widocznych okien z `GW_OWNER`, a uchwyt MediaStation zgadza się z `MainWindowHandle` w stanie normalnym i zawieszonym; to samo rozstrzygnięcie i ta sama naprawa (pole `Owner`), więc jeden wpis DW-15.
  - `[low]` `[patch]` `deferred-work.md`: rozwiązany DW-5 nadal wskazuje `CompositionRoot.cs:151` (wnętrze `StartCalibration`) — Akcja: `location` wskazuje `TeardownSequence.cs` i handler `app.Exit` (`CompositionRoot.cs:246-253`).
## Design Notes

- `TeardownSequence` siedzi w App (obok `CompositionRoot.cs`), bo kolejność zna tylko composition root, ale jest wolny od WPF/Win32, więc idzie do testów Core linkiem — ten sam wzorzec co `UiHang.cs` i `ClipCursorGate.cs`, bez projektu `App.Tests` (granica epiki). Handler musi budować sekwencję w momencie wyjścia: `inputHub`, `actionPump`, `notifications` i `reporter` są `null` w chwili rejestracji `app.Exit`.
- Kolejność i wyjątki: żaden adapter nie zostaje niezamknięty, a na zewnątrz leci **pierwszy** wyjątek (macierz Intent), nie ostatni. Dzisiejsza siatka `try/finally` zachowuje się inaczej, gdy padnie więcej niż jeden adapter — wyjątek z wewnętrznego `finally` zastępuje wcześniejszy, więc na zewnątrz leci ostatni; ta różnica jest świadoma i obejmuje wyłącznie ścieżkę wyjścia z wieloma awariami, a pinuje ją `Dispose_keeps_releasing_and_rethrows_the_first_exception`.
- DW-8 nie zmienia zachowania poprawnego configu: ścieżka Raw Input nigdy nie zawiera `*`, więc wpis `VID_1915*` i tak nie dopasowywał niczego i urządzenie wpadało w `*` albo `keyboard`. Walidacja zamienia martwy wpis w błąd configu, a dokładnie `*` zostaje wildcardem. Schematu nie ruszamy — decyzja projektu: reguły semantyczne tylko w runtime.
- S10: `EnumWindows` + filtr procesu daje **nadzbiór** dzisiejszego zbioru okien (każde okno, nie jedno na proces), więc DW-13 (drugie okno w istniejącym procesie Edge) przestaje być niewidzialne; filtr klasy `Chrome_WidgetWin_*` zawęża ten nadzbiór do prawdziwych okien przeglądarki (tooltipy i popupy tej samej nazwy procesu odpadają). `Find-WindowProcess` po tej samej enumeracji zwraca prawdziwy uchwyt najwyższego poziomu, więc foreground można porównać dokładnie (`foreground -eq Handle`) zamiast po PID — DW-14 (wiele procesów `msedge` / inny profil) znika, a kontrola staje się ostrzejsza: inne okno tego samego procesu nie zalicza pressu. Wyjątki zostają dwa i oba są nazwane: desktop (`Progman`/`WorkerW` **procesu `explorer`** — nazwa klasy nie jest zarezerwowana, więc bez bramki procesu okno klasy `WorkerW` innej aplikacji, np. tapety, zaliczyłoby press) oraz ghost `dwm` dla `shell-hung`. Desktop jest trzecim przypadkiem powłoki obok ghosta; wiersz macierzy „foreground innego okna” dotyczy okien aplikacji i zostaje bez wyjątku.
- Nietknięte zostają `Test-Press`, fixture i oba przebiegi; zmienia się wyłącznie zbiór okien wchodzących do punktacji (każde okno `Chrome_WidgetWin_*`, nie jedno na proces). 16/16 pilotem na donglu oraz DW-10/DW-11 są sprzętowe — odbiór epiku na UM760.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` — oczekiwane: 0 ostrzeżeń, 0 błędów.
- `dotnet test MediaStation.slnx -c Release` — oczekiwane: wszystkie zielone (baseline 848) z nowymi `TeardownSequenceTests` i wierszami `ConfigValidationTests`.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` — oczekiwane: `Self-test passed.`, kod 0, okna fixture 0/1/2/1 i nowe przypadki decyzji.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` — oczekiwane: `start` → `snapshotPublished` → `stop {exitCode: 0}`, zero `configError`/`warn`/`error`, kod procesu 0; config użytkownika (`%LOCALAPPDATA%\MediaStation\config`) odtworzony bajt w bajt (SHA-256), katalog publish usunięty.

## Auto Run Result

**Zmiana:** Domknięte trzy code-side findings z dyspozycją `defer` w epice (DW-5, DW-13, DW-14) oraz reguła walidacji DW-8 — bez zmiany zachowania produktu. Poza ticketem zostają: DW-6 (watchdog hooka), DW-10 (`SendInput`), DW-11 (DPI) i dokumentacyjne DW-7/DW-9/DW-12.

**Pliki:**
- `src/MediaStation.App/TeardownSequence.cs` (nowy) — zwalnianie siedmiu adapterów w kolejności hub → pompa Actions → wstrzyknięcia → power → timery → pętla, z pierwszym wyjątkiem rzucanym po zwolnieniu reszty (DW-5).
- `src/MediaStation.App/CompositionRoot.cs` — `app.Exit` buduje `TeardownSequence` w chwili wyjścia zamiast siatki `try/finally` (DW-5).
- `tests/MediaStation.Core.Tests/TeardownSequenceTests.cs` (nowy) i `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj` — pin kolejności, pierwszy wyjątek i `null` w slotach; link pliku App do testów Core (DW-5).
- `src/MediaStation.Core/Input/InputTarget.cs` — `DeviceSignature.HasEmbeddedWildcard` (DW-8).
- `src/MediaStation.Core/Config/ConfigValidation.cs` — `configError` na `$.devices[i].signature` dla `*` w środku sygnatury; dokładnie `*` bez zmian (DW-8).
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — theory `VID_1915*`/`*VID*`/`**` i 7 wierszy `HasEmbeddedWildcard` (DW-8).
- `tools/check-home-s10.ps1` — S10 liczy okna, nie procesy: `EnumWindows` w `Get-TopLevelWindows`, zbiór `pid:handle` po oknach klasy `Chrome_WidgetWin_*`, foreground równy dokładnemu uchwytowi celu z dwoma nazwanymi wyjątkami powłoki (ghost `dwm` dla `shell-hung`, `Progman`/`WorkerW` procesu `explorer` dla `desktop`), czyste decyzje i 12 przypadków `-SelfTest` (DW-13, DW-14).
- `_bmad-output/initiative-mediastation-mvp/deferred-work.md` — DW-5 i DW-8 `resolved` z `evidence`, DW-13/DW-14 `open` z notą, nowy DW-15 (unverified).

**Review:** pass 1 — 4 findings (medium 2, low 2): dwa `bad_plan` (Tasks/Design Notes nie nazywały filtra klasy ani wyjątku desktopu; notatka o semantyce wyjątków teardownu nie odpowiadała dawnej siatce) → amend planu poza blokiem Intent, Plan Change Log i ponowna derywacja; dwa `patch` (ledger oraz bramka procesu `explorer` dla desktopu) weszły w tę derywację. Pass 2 — 3 findings (low 1, maybe-false 2): `patch` (błędna lokalizacja DW-5 w ledgerze) naprawiony; dwa `maybe-false` (brak reguły `GW_OWNER` w enumeracji: prefiks `Chrome_WidgetWin_*` i uchwyt celu) odłożone do `deferred` i do ledgera jako DW-15 (medium unverified), rozstrzyga je S10 na UM760. Findings odrzucone: brak.

**Następny przebieg review:** niezalecany — w tym przebiegu jeden patch `low`, żadnego `high`.

**Weryfikacja (ALDER, Windows 11 Home, 2026-10-22):**
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów.
- `dotnet test MediaStation.slnx -c Release` — 862/862, 0 failed, 0 skipped (baseline 848).
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` — `Self-test passed.`, exit 0; okna fixture 0/1/2/1 dają fail/pass/fail/pass, 12 przypadków decyzji zielonych.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + start z `tools/samples/config` i `CloseMainWindow` — `start → inputRouting → powerSignalsRegistered → snapshotPublished → stop {exitCode: 0}`, zero `configError`/`warn`/`error`, kod procesu 0; config użytkownika odtworzony bajt w bajt (SHA-256), katalog publish usunięty.
- Ten sam build z `"signature": "VID_1915*"` — dokładnie jeden `configError` (`input.json`, `$.devices[0].signature`), log `start → configError → stop {exitCode: 0}`, kod procesu 0.

**Ryzyka resztkowe:** DW-15 (widoczne okno z właścicielem albo `Chrome_WidgetWin_0` mogłoby dać fałszywy FAIL w S10 — unverified); `desktop` w S10 zależy od tego, czy po `MinimizeAll` foreground to `Progman`/`WorkerW` explorera; nazwa klasy Chromium może się zmienić w przyszłej wersji Edge/Chrome. Przebieg 16/16 pilotem na donglu, DW-10 i DW-11 są sprzętowe — odbiór epiku na UM760.
