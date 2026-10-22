---
title: 'Home zawsze: zawieszony UI i skrypt S10'
type: 'feature'
ticket: '9'
created: '2026-10-22'
status: done
baseline_revision: 'a760de0cb0768bdc4ef5561069b7bff6a761e47a'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred:
  - summary: >-
      Wykrywanie nowego okna przeglądarki po `pid:handle` jednego `MainWindowHandle` na proces może przeoczyć drugie okno w istniejącym procesie Edge.
    evidence: |-
      Niesprawdzone bez prawdziwego Edge i pilota; rozstrzygnie to przebieg na UM760 (porównanie z `EnumWindows` po klasie `Chrome_WidgetWin_1`).
    location: >-
      tools/check-home-s10.ps1 Get-BrowserWindows
    severity: medium (unverified)
  - summary: >-
      Kontrola `target not foreground` po PID może odrzucić poprawny press Edge przy wielu procesach `msedge` lub innym profilu.
    evidence: |-
      Niesprawdzone na ALDER; potwierdzić na UM760 przy odbiorze epiku.
    location: >-
      tools/check-home-s10.ps1 Measure-Press
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Done when 3 epiki wymaga, żeby Home docierał jako Action także przy zawieszonym wątku UI shella, a odbiór epiku ma powtórzyć S10 (4 cele × 4 naciśnięcia) pilotem na donglu. Dziś nie ma czym zawiesić UI na żądanie, a `spike/helpers/s10-run.ps1` napędza logger spike'u, nie MediaStation, i nie liczy wyniku z logu aplikacji.

**Approach:** Zmienna `MEDIASTATION_DEBUG_HANG_UI=<s>` (każdy build) jest parsowana w Core, a App po starcie zostawia `warn debugHangUi {seconds}` i blokuje wątek UI na podany czas na dispatcherze (po pierwszym renderze). Nowy `tools/check-home-s10.ps1` wysuwa po kolei Edge, Moonlight, pulpit i zawieszony shell, przed każdym naciśnięciem daje sygnał dźwiękowy, czyta log aplikacji i ocenia każde naciśnięcie (dokładnie jedna `action {kind: home}`, żadne nowe okno przeglądarki), licząc X/16, plus osobny przebieg „shell na wierzchu, niezawieszony”; `-SelfTest` liczy wynik z zapisanego fixture logu bez sprzętu.

## Boundaries & Constraints

**Always:** parse czysty w Core (bez I/O, bez wyjątków); zmienna czytana raz przy starcie i tylko w trybie głównym (`--calibrate-input` jej nie widzi, bo nie ma UI); wpis dokładnie jeden, `warn`, `src: app`, `msg: debugHangUi`, `data.seconds` = sekundy; blokada wątku UI po pierwszym renderze (okno istnieje i jest widoczne), więc InputHub z własnego wątku dalej loguje `action`; skrypt czyta wyłącznie log aplikacji (`%LOCALAPPDATA%\MediaStation\logs\mediastation-*.jsonl`) przez `FileShare.ReadWrite` i liczy tylko wpisy bieżącego procesu (granica `start {pid}`); sukces naciśnięcia = dokładnie jedna `action` z `data.kind == home` w oknie pomiaru i zero nowych okien `msedge`/`chrome`; ta sama funkcja licząca obsługuje przebieg żywy i `-SelfTest`; skrypt nie wymaga niczego poza naciśnięciem Home przez człowieka.

**Never:** `#if DEBUG` ani inny warunek kompilacji; zmiany w `InputPolicy`, `InputHub`, kontraktach, `MessageTypes.All`, polityce routingu; zawieszanie procesu (`Suspend-Process`, `Kill`, debugger) zamiast blokady wątku UI (samo zakończenie zawieszonej instancji po przebiegu jest dozwolone); nowe klucze logu poza `debugHangUi`; zrzuty ekranu i interakcja ze strony skryptu poza sygnałem dźwiękowym; edycja `spike/`; nowe pakiety; zapisywanie logów przebiegu do repo.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| start główny z `MEDIASTATION_DEBUG_HANG_UI=600` | env ustawiona | po pierwszym renderze `warn debugHangUi {seconds: 600}`, wątek UI zajęty 600 s, proces `Responding = False`, hub dalej loguje `action home` | brak błędu |
| start główny bez zmiennej, z `0`, `abc`, `-5`, `NaN`, `1e12` (overflow) | env pusta/zła | brak wpisu `debugHangUi`, brak blokady, start normalny | brak błędu; zła wartość nie rzuca |
| `--calibrate-input` z ustawioną zmienną | env `600` | tryb kończy się po 60 s, brak `debugHangUi` i blokady | brak błędu |
| pomiar naciśnięcia | 0 / 2 / 1 nowych `action home` w oknie | 0 i 2 → porażka (brakująca, podwójna), 1 → sukces | powód w wyniku |
| pomiar naciśnięcia z nowym oknem przeglądarki | 1 `action home` + nowe `pid:handle` msedge/chrome | porażka („browser window”) mimo poprawnej liczby Action | powód w wyniku |
| brak okna celu | Edge/Moonlight/MediaStation nie działa | naciśnięcia celu liczone jako porażka z powodem, przebieg idzie dalej | brak wyjątku, wynik < X/16 |
| zawieszenie wygasło przed presses shella | `-HangSeconds` za mały | pozostałe naciśnięcia shella → porażka `hang expired`, wskazówka „increase -HangSeconds” | brak wyjątku |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Diagnostics/DebugHangUi.cs` — nowy (nowy folder): `const string EnvVarName = "MEDIASTATION_DEBUG_HANG_UI"` i `static TimeSpan? Parse(string? value)`; `null`/białe znaki → `null`; `double.TryParse` z `NumberStyles.Float`, `CultureInfo.InvariantCulture`; tylko skończone (`double.IsFinite`), `> 0` i mieszczące się w `TimeSpan` (`< TimeSpan.MaxValue.TotalSeconds`) → `TimeSpan.FromSeconds`; reszta → `null`. Zero wyjątków, zero I/O.
- `src/MediaStation.App/Diagnostics/UiHang.cs` — nowy, bez WPF/Win32 (linkowany do testów jak `ClipCursorGate`): `const string Src = "app"`; `static void Schedule(TimeSpan hang, ILog log, Action<Action> post)` — dla `hang <= 0` nic; inaczej jedno `post` z callbackiem, który pisze `log.Warn(Src, "debugHangUi", new Dictionary<string, object?> { ["seconds"] = hang.TotalSeconds })` i `Thread.Sleep(hang)`.
- `src/MediaStation.App/CompositionRoot.cs:57-90` — w `Start`, po `StartRun(app, log, time)` i `AttachStopLogging` (linie 88-89): `DebugHangUi.Parse(Environment.GetEnvironmentVariable(DebugHangUi.EnvVarName))` → jeśli `TimeSpan`, `UiHang.Schedule(hang, log, action => app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, action))`; `using System.Windows.Threading;`. `ApplicationIdle` jest niższy priorytet niż `Render`, więc pierwszy render już się odbył; `StartCalibration` (121-164) pozostaje nietknięty.
- `tests/MediaStation.Core.Tests/DebugHangUiTests.cs` — nowy: macierz parse (`null`, `""`, `"  "`, `"0"`, `"abc"`, `"-5"`, `"NaN"`, `"Infinity"`, `"1e12"`, `"600"` → `TimeSpan.FromSeconds(600)`, `"0.5"`, `"600s"` → `null`), oraz `UiHang.Schedule` z `RecordingLog` (`RecordingLog.cs:12-38`) i przechwyconym `post`: wpis `warn`/`debugHangUi`/`seconds`, blokada `>= 0.1 s` dla `200 ms`, brak wpisu i braku `post` dla `TimeSpan.Zero`.
- `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj:26-44` — dopisać `<Compile Include="..\..\src\MediaStation.App\Diagnostics\UiHang.cs" Link="App\UiHang.cs" />`.
- `tools/check-home-s10.ps1` — nowy, samodzielny (wzorce: `tools/inject-power.ps1:69-197,346-382` — czytanie logu z `FileShare.ReadWrite`, granica `start {pid}`, lista `problems`, kody wyjścia; `spike/helpers/s10-run.ps1:8-52` — `Front` z Alt, `TargetWindow`, beep, `-Out`; `spike/win/_common.ps1` nie jest dot-source'owany, bo spike jest jednorazowy). Parametry: `-Targets @('edge','moonlight','desktop','shell-hung')`, `-Presses 4`, `-MediaStation` (domyślnie `src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe`), `-LogsDir` (`%LOCALAPPDATA%\MediaStation\logs`), `-Out` (domyślnie `<LogsDir>\check-home-s10-<yyyyMMdd-HHmmss>.log`), `-HangSeconds 600`, `-PressTimeoutSeconds 45`, `-SettleSeconds 2`, `-SkipShellTopPass`, `-SelfTest`, `-Fixture` (domyślnie `tools\fixtures\check-home-s10.fixture.jsonl`). Funkcje: `Read-LogEntries`, `Get-RunEntries -ProcessId`, `Get-HomeActions -Entries` (`msg -eq 'action'` i `data.kind -eq 'home'`), `Get-PressCount -Homes -From -To` (UTC, `From` włącznie, `To` wyłącznie), `Get-BrowserWindows` (zbiór `"pid:handle"` z `msedge`/`chrome` z `MainWindowHandle -ne 0`), `Test-Press` (Count + `NewBrowserWindow` → `Ok`/`Reason`), `Add-Type` P/Invoke `keybd_event`, `FindWindow`, `IsIconic`, `ShowWindow`, `SetForegroundWindow`, `GetForegroundWindow`, `TargetWindow` (edge: `msedge` z tytułem `Netflix`; moonlight: proces `Moonlight`; desktop: `MinimizeAll` + `Progman`; shell-hung: proces MediaStation). Przebiegi: 1) start MediaStation z `MEDIASTATION_DEBUG_HANG_UI=$HangSeconds`, czekanie na okno i na wpis `debugHangUi` (dowód zawieszenia; wygaśnięcie = `ts` wpisu + `seconds`), `Targets` po kolei (`From` przed beepem, oczekiwanie na przyrost `action home` do `-PressTimeoutSeconds`, `To` po `-SettleSeconds`), potem `Stop-Process -Force` (zawieszone okno nie przetwarza `WM_CLOSE`); 2) „shell na wierzchu, niezawieszony” = start bez zmiennej, czekanie na `snapshotPublished`, `Presses` naciśnięć, zamknięcie `CloseMainWindow` i `stop {exitCode: 0}`; wynik: `X/<Targets*Presses>` (domyślnie X/16), `Y/<Presses>`; kod 0 tylko przy pełnym X i Y.
- `tools/fixtures/check-home-s10.fixture.jsonl` — nowy: realistyczne linie `start`, `action` (`home`, `ok`), `inputHubError`, `stop` z ustalonymi `ts`, tak by cztery okna pomiaru dawały brakującą, podwójną, poprawną i „poza oknem” `action home`; `-SelfTest` buduje okna z tego pliku i sprawdza oczekiwane `0/1/2/1` i werdykty `fail/pass/fail/pass` oraz porażkę przy `NewBrowserWindow`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Diagnostics/DebugHangUi.cs` — parser zmiennej w Core — czysta decyzja bez I/O (AD-3, AD-14).
- [x] `src/MediaStation.App/Diagnostics/UiHang.cs` — wpis `debugHangUi` + blokada wątku UI na `post` — wykonanie po pierwszym renderze, testowalne bez WPF.
- [x] `src/MediaStation.App/CompositionRoot.cs` — odczyt env i `UiHang.Schedule` w trybie głównym — wpięcie po hubie, więc Home działa w trakcie blokady.
- [x] `tests/MediaStation.Core.Tests/{DebugHangUiTests.cs,MediaStation.Core.Tests.csproj}` — macierz parse, wpis i czas blokady, link `UiHang` — `verify`.
- [x] `tools/check-home-s10.ps1` i `tools/fixtures/check-home-s10.fixture.jsonl` — przebieg S10 z zawieszonym shellem, osobny „shell na wierzchu”, pomiar z logu i `-SelfTest` — narzędzie odbioru epiku.

**Acceptance Criteria:**
- Given opublikowany build Release, when start z `MEDIASTATION_DEBUG_HANG_UI=5`, then log ma `warn debugHangUi {seconds: 5}` po wpisie `start` i przed `stop`, w trakcie blokady `(Get-Process MediaStation).Responding` jest `False`, a po ~5 s proces odpowiada i zamyka się kodem 0 wpisem `stop`.
- Given ten sam build bez zmiennej, when start i stop, then nie ma wpisu `debugHangUi`, kod wyjścia to 0, a przebieg startu jest jak dziś (bez blokady).
- Given `powershell -NoProfile -File tools/check-home-s10.ps1 -SelfTest`, when uruchomienie bez sprzętu, then wypisuje 4 werdykty (brakująca → porażka, podwójna → porażka, poprawna → sukces, poza oknem → sukces) i kod 0.
- Given zły fixture albo usunięty plik fixture, when `-SelfTest`, then kod 1 i czytelny powód (self-test nie może udawać sukcesu).
- Given brak działającego Edge/Moonlight, when przebieg bez `-SkipShellTopPass`, then naciśnięcia celu są porażką z powodem, skrypt kończy się kodem 1 i nie rzuca wyjątku poza `problems`.

## Implementation Notes

- `UiHang.Schedule` przyjmuje `System.Action<System.Action>`, bo alias `Action` w App i w testach wskazuje kontrakt `MediaStation.Core.Contracts.Action`; typ w pełni kwalifikowany jest jedynym poprawnym wariantem.
- `MEDIASTATION_DEBUG_HANG_UI` czytana jest raz, po `StartRun` i `AttachStopLogging`, więc `--calibrate-input` (wczesny powrót) nigdy jej nie widzi.
- Evidence (ALDER, Windows 11 Home, Release publish win-x64 self-contained, 2026-10-22):
  - `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów.
  - `dotnet test MediaStation.slnx -c Release` — 845/845 przed poprawkami review; klasa `DebugHangUiTests` po poprawkach: 20/20 (nazwa zmiennej, 16 wartości parse, wpis i blokada 200 ms, dwa niedodatnie czasy).
  - `tools/check-home-s10.ps1 -SelfTest` — werdykty `missing` 0/fail, `correct` 1/pass, `double` 2/fail, `outside` 1/pass oraz `browser window` 1/fail; kod 0. Fixture z usuniętym `action home` w oknie `correct` → kod 1 (`count 0 instead of 1`); brak pliku fixture → kod 1 (`fixture not found`).
  - Start z `MEDIASTATION_DEBUG_HANG_UI=5`: `start` o 16:16:31.432Z, `warn debugHangUi {seconds: 5}` o 16:16:31.973Z (dokładnie jeden wpis, między `start` i `stop`), `stop {exitCode: 0}` o 16:16:37.034Z, exit 0, po blokadzie `Responding = True`; config użytkownika bez zmian (SHA256 przed/po).
  - Start bez zmiennej: `snapshotPublished`, zero wpisów `debugHangUi`, exit 0, `stop {exitCode: 0}`.
  - `--calibrate-input` z `MEDIASTATION_DEBUG_HANG_UI=600`: 60,2 s, zero wpisów `debugHangUi`, exit 0, `stop {exitCode: 0}`.
  - `Responding = False` w trakcie blokady: przebieg z `HangSeconds=20` dał `False` w sondzie po 5,02 s (i w drugiej po 6 s), po blokadzie `True`, exit 0. Przy `HangSeconds=5` sonda 5 s kończy się razem z blokadą i daje `True` — szczegóły w Plan Change Log.
  - Brak okna celu (`-Targets moonlight -Presses 1 -SkipShellTopPass`): porażka `no target window` w `problems`, wynik 0/1, exit 1, bez wyjątku. Przebieg „shell na wierzchu” bez pilota: start bez zmiennej, `snapshotPublished`, brak `action home` → `no action`, zamknięcie `CloseMainWindow` i `stop {exitCode: 0}`.
- Hardware-only (N/A na ALDER, brak pilota): pełny przebieg 16/16 pilotem na donglu i przebieg „shell na wierzchu” z pilotem — UM760, odbiór epiku (Done when 3).

## Plan Change Log

- AC dla `MEDIASTATION_DEBUG_HANG_UI=5` oczekuje `Responding = False` „w trakcie blokady”, ale `Process.Responding` sonduje `WM_NULL` z timeoutem 5 s: blokada 5 s kończy się równo z timeoutem sondy i w praktyce daje `True`. Dowód `False` zebrano na tej samej ścieżce kodu z `HangSeconds=20` (sonda 5,02 s → `False`), a przebieg 5 s zachowuje wpis `seconds: 5`, odzyskanie responsywności i `stop {exitCode: 0}`. Wiersz macierzy I/O dla 600 s (`Responding = False`) pozostaje pokryty przez przebieg 20 s.
- Poprawki review: parser odrzuca sekundy powyżej `int.MaxValue / 1000` (limit `Thread.Sleep`), więc `MEDIASTATION_DEBUG_HANG_UI=3000000` nie zabija wątku UI; w przebiegu 1 zawieszenie musi objąć okno pomiaru każdego celu (nie tylko `shell-hung`), inaczej naciśnięcie kończy się `hang expired`; naciśnięcie jest porażką `target not foreground`, gdy okno pierwszego planu należy do innego procesu niż okno celu; fixture używa realnej operacji `rawInput`.

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 5 findings — high 0, medium 3, low 2, false 0, maybe-false 0
- findings:
  - `[medium]` `[patch]` `Parse` przyjmował wartości do `TimeSpan.MaxValue`, a `Thread.Sleep(TimeSpan)` w `UiHang` odrzuca ponad `int.MaxValue` ms — sprawdzone na opublikowanym exe: `MEDIASTATION_DEBUG_HANG_UI=3000000` zostawił `debugHangUi {seconds: 3000000}` (`UiHang.cs:40`), a potem `dispatcherUnhandledException` (`ArgumentOutOfRangeException`, exit `0xE0434352`), wbrew dokumentacji klasy i wierszowi macierzy „zła wartość nie rzuca”. Poprawka: `Parse` odrzuca `seconds > int.MaxValue / 1000` (24,86 dnia), dokumentacja mówi o limicie `Thread.Sleep`, a testy dostają granicę `2147483.648` (odrzucona) i `2147483.646`/`2147483.647` (przyjęte); po poprawce `3000000` nie zostawia wpisu, nie ma `dispatcherUnhandledException`, exit 0.
  - `[low]` `[reject]` AC1 żąda `Responding = False` przy `MEDIASTATION_DEBUG_HANG_UI=5`, ale `Process.Responding` sonduje `WM_NULL` z timeoutem 5 s, więc blokada 5 s kończy się razem z sondą i zwraca `True` (odtworzone: sonda +1,26 s blokowała 4,48 s → `True`; przy 20 s → `False` po 5,01 s). Odrzucone, bo jedyną naprawą jest edycja AC/`Verification` tego planu, czego workflow zabrania; sam obserwowalny efekt jest dowiedziony na tej samej ścieżce kodu blokadą 20 s i zapisany w Plan Change Log.
  - `[medium]` `[patch]` Pass 1 drukował „shell hung: X/16”, ale wygaśnięcie zawieszenia sprawdzał tylko cel `shell-hung`, więc po wygaśnięciu blokady w środku przebiegu naciśnięcia na Edge, Moonlight i pulpit były mierzone na responsywnym shellu i nadal liczyły się jako sukcesy (16 × 47,7 s > domyślne 600 s). Poprawka: każdy press w pass 1 dostaje `-RequiresHung $true`, a test po pomiarze porównuje koniec okna z terminem wygaśnięcia i loguje wymóg; sprawdzone `-Targets desktop -HangSeconds 2` → `FAIL count=0 (hang expired (increase -HangSeconds))`, exit 1.
  - `[medium]` `[patch]` Naciśnięcie było oceniane tylko po liczbie `action home` i różnicy okien przeglądarki, choć `SetForegroundWindow` jest tylko sugestią — press dostarczony przy innym oknie na wierzchu liczył się jako sukces, mimo że cel nigdy nie stanął z przodu. Poprawka: `GetWindowThreadProcessId` porównuje proces okna pierwszego planu z procesem okna celu i przed sygnałem dźwiękowym kończy press porażką `target not foreground` (dla `desktop` process Progmana), a notatka pokazuje oba PID-y; sprawdzone na `shell-hung`, `shell-top` i `desktop` (PID-y zgodne, pressy oceniane dalej).
  - `[low]` `[patch]` Fixture używał `"operation":"registerRawInput"`, którego aplikacja nigdy nie pisze — realna wartość to `rawInput` (`InputHubOperations.RawInput`), więc fixture nie odpowiadał logowi czytanemu przez skrypt. Poprawka: `rawInput`; self-test nadal daje `0/1/2/1` → `fail/pass/fail/pass` i kod 0 (tej linii nie asertuje).

### 2026-10-22 — Review pass (follow-up)
- verdicts: 5 findings — high 0, medium 3, low 1, false 0, maybe-false 1
- findings:
  - `[medium]` `[patch]` `Measure-Press` (`tools/check-home-s10.ps1`) po kroku `desktop` nie stawia zawieszonego shella na wierzchu — odtworzone `-Targets desktop,shell-hung -HangSeconds 120`: `shell-hung #1: foreground pid=2372 (target pid=13056)` → `FAIL (target not foreground)`, a pid 2372 to `dwm` (ghost window zawieszonego okna po ~5 s); to samo zablokowałoby 4 z 16 naciśnięć przebiegu rozstrzygającego. Dodatkowo synchroniczne `ShowWindow` na oknie zawieszonego wątku blokuje do końca zawieszenia (zmierzone 52,5 s przy zawieszeniu 60 s), ale w skrypcie jest wołane tylko dla `IsIconic`, a `MinimizeAll` zawieszonego okna nie minimalizuje, więc ta ścieżka nie jest osiągana. Poprawka: dla celu `shell-hung` proces pierwszego planu `dwm` (ghost) liczy się jako zawieszony shell na wierzchu; po poprawce press dochodzi do sygnału dźwiękowego (`no action` bez pilota), self-test bez zmian, kod 0.
  - `[medium]` `[defer]` `Get-BrowserWindows` kluczuje po `pid:handle` jednego `MainWindowHandle` na proces `msedge`/`chrome`, więc drugie okno otwarte w istniejącym procesie Edge może nie zmienić zbioru i nieprzełknięty Home policzy się jako sukces — niesprawdzone bez prawdziwego Edge i pilota na UM760; do rozstrzygnięcia przy odbiorze epiku (porównanie `EnumWindows` z klasą `Chrome_WidgetWin_1`); metoda wynika z Design Notes planu.
  - `[low]` `[reject]` AC1 / wiersz macierzy 600 s: `Responding = False` przy blokadzie 5 s — `carried` z poprzedniego przebiegu (ta sama lokalizacja i ten sam zarzut; naprawą byłaby edycja AC tego planu, udokumentowana w Plan Change Log i dowiedziona blokadą 20 s).
  - `[medium]` `[defer]` `Measure-Press` porażka `target not foreground` przy porównaniu PID okna pierwszego planu z PID okna celu może odrzucić poprawny press dla Edge z wieloma procesami `msedge` lub innym profilem — niesprawdzone (brak Edge z wieloma procesami na ALDER); to samo ryzyko opisano w „Unverified risk” poprzedniego przebiegu; do potwierdzenia na UM760.
  - `[low]` `[reject]` `Read-LogEntries` pomija nieparsowalne linie także w fixture `-SelfTest` — uszkodzenie linii `action home` zmienia licznik i self-test to wykrywa; naprawa dodałaby gałąź i parametr dla nikłego zysku, uszkodzony fixture jest mało prawdopodobny.

## Design Notes

Blokada UI idzie przez `Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle)`, a nie `Thread.Sleep` w `OnStartup`: priorytet `Render` jest wyższy, więc shell zdąży się namalować i okno istnieje, zanim wątek UI przestanie odbierać komunikaty — dokładnie stan „zawieszony shell” z S10. `UiHang` nie zna WPF (bierze `Action<Action>`), więc testy App linkują go jak `ClipCursorGate` i dowodzą wpisu oraz czasu blokady bez okna. Skrypt liczy naciśnięcie po oknie czasu (UTC) na wpisach `action`, bo linie logu pisze kilka wątków, a `ts` jest jedynym porządkiem wspólnym; nowe okno przeglądarki wykrywa różnica zbioru `pid:handle`, bo niepołknięty Home otwiera w spike'u nowe okno (a nie kartę w istniejącym).

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` — expected: 0 ostrzeżeń, 0 błędów.
- `dotnet test MediaStation.slnx -c Release` — expected: wszystkie zielone (baseline 828 plus macierz `DebugHangUi`/`UiHang`).
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` — expected: kod 0, werdykty jak w AC.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + start z `MEDIASTATION_DEBUG_HANG_UI=5` — expected: `warn debugHangUi {seconds: 5}`, `Responding = False` w trakcie, potem `stop {exitCode: 0}`; drugi start bez zmiennej bez wpisu; config użytkownika odtworzony bajt w bajt.
- `git status --porcelain` — expected: tylko zamierzone plany/pliki źródłowe, żadnych logów.

**Manual checks (hardware-only, not blocking):**
- Pełny przebieg 16/16 z pilotem na donglu i przebieg „shell na wierzchu” — UM760, odbiór epiku (Done when 3); na ALDER brak pilota, więc N/A z powodem.

## Auto Run Result

**Status:** built (follow-up review pass).

**Summary:** `MEDIASTATION_DEBUG_HANG_UI=<s>` jest parsowana w Core (`DebugHangUi.Parse`, z limitem `Thread.Sleep`) i wykonywana raz w trybie głównym przez `UiHang.Schedule` na dispatcherze z priorytetem `ApplicationIdle`: `warn debugHangUi {seconds}` i blokada wątku UI, a InputHub dalej loguje `action`. `tools/check-home-s10.ps1` wysuwa Edge, Moonlight, pulpit i zawieszony shell, przed każdym naciśnięciem daje sygnał dźwiękowy, ocenia okno `[From, To)` w logu (dokładnie jedna `action home`, brak nowego okna przeglądarki, cel na wierzchu, zawieszenie nie wygasło), liczy `shell hung: X/16` plus przebieg „shell na wierzchu”; `-SelfTest` liczy fixture bez sprzętu. Ten przebieg poprawił jedną usterkę skryptu: po kroku `desktop` zawieszony shell był zastępowany ghost window `dwm`, więc kontrola foregroundu odrzucała każde naciśnięcie `shell-hung`.

**Files changed:**
- `src/MediaStation.Core/Diagnostics/DebugHangUi.cs` — nazwa zmiennej i czysty parse sekund.
- `src/MediaStation.App/Diagnostics/UiHang.cs` — wpis `debugHangUi` i blokada wątku UI.
- `src/MediaStation.App/CompositionRoot.cs` — odczyt zmiennej i `UiHang.Schedule`.
- `tests/MediaStation.Core.Tests/DebugHangUiTests.cs`, `MediaStation.Core.Tests.csproj` — macierz parse/blokady i link `UiHang.cs`.
- `tools/check-home-s10.ps1` — przebieg S10, pomiar z logu, `-SelfTest`; w tym przebiegu: ghost `dwm` liczy się jako zawieszony shell na wierzchu.
- `tools/fixtures/check-home-s10.fixture.jsonl` — fixture logu do `-SelfTest`.

**Review findings:** 5 findings (high 0, medium 3, low 1, maybe-false 1). Patches applied: 1 (`medium` — foreground zawieszonego shella po `desktop`). Deferred: 2 (`medium`, niezweryfikowane: wykrywanie okna przeglądarki po `MainWindowHandle`, kontrola foregroundu po PID przy wielu procesach `msedge`). Rejected: AC1 `Responding = False` przy 5 s (`low`, carried — naprawa to edycja AC planu), `Read-LogEntries` pomija uszkodzone linie fixture (`low`, nikły zysk, uszkodzenie i tak zmienia licznik).

**Follow-up review recommended:** false — ten przebieg poprawił jeden `medium`, żadnego `high`.

**Verification:** `dotnet build MediaStation.slnx -c Release` — 0 błędów; `tools/check-home-s10.ps1 -SelfTest` — kod 0, werdykty `fail/pass/fail/pass` i `failed as expected`; `-Targets desktop,shell-hung -HangSeconds 120` przed poprawką: `target not foreground` (pid `dwm`), po poprawce: press dochodzi do sygnału dźwiękowego i kończy się `no action` (brak pilota na ALDER). Pełny `dotnet test` z poprzedniego przebiegu (848/848) bez zmian kodu produkcyjnego.

**Residual risks:** pełny przebieg 16/16 pilotem na donglu i przebieg „shell na wierzchu” są sprzętowe — N/A na ALDER (brak pilota), do odbioru epiku na UM760; dwa deferred wyżej do potwierdzenia na UM760; synchroniczne `ShowWindow` na zawieszonym oknie blokuje do końca zawieszenia, ale ścieżka jest nieosiągalna, bo `MinimizeAll` nie minimalizuje zawieszonego okna.
