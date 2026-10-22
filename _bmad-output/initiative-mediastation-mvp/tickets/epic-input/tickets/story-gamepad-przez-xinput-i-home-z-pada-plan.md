---
title: 'Gamepad przez XInput i Home z pada'
type: 'feature'
ticket: '6'
created: '2026-10-22'
status: done
baseline_revision: '88a81eafffda51b6ac71f082f0e71baceb9a95a9'
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
deferred: []
---

<intent-contract>

## Intent

**Problem:** Pad jest martwy: `gamepadHome` jest sparsowane, ale nikt go nie czyta, w App nie ma ani jednego wywołania XInput, a `ActionSource.Gamepad` nigdy nie powstaje — więc Home z pada i `Keyboard` z pada (Y) nie istnieją, choć `input.json` już wymaga sekcji `gamepadHome`.

**Approach:** `InputHub` odpytuje XInput co 10 ms na wątku Input i oddaje migawki stanu polityce. `InputPolicy` mapuje przyciski z nowej sekcji `gamepad` w `input.json` (domyślnie D-pad, A → `ok`, B → `back`, Y → `keyboard`), stosuje te same reguły co klawiatura (jedna Action na naciśnięcie, `repeat` D-pada, `long` dla OK w `shell`/`osk`), emituje jedną Action Home na kombinację `gamepadHome` i routuje Actions pada według AD-8.

## Boundaries & Constraints

**Always:** XInput user index 0; polling wyłącznie na wątku Input; decyzja pada tylko produkuje Actions (XInput nie da się połknąć — `Swallow` jest zawsze `false`); jedna Action na naciśnięcie (powtarzana migawka bez zmiany stanu nic nie daje); `repeat` tylko D-pad, z progami `timing`; `long` tylko OK (A) w `shell` i `osk`; Home z kombinacji połknięty i dostarczony w każdym wierszu; decyzja z krawędzi naciśnięcia rządzi trzymaniem do zwolnienia; kombinacja daje dokładnie jedną Action Home, dopóki nie zostanie przerwana; przyciski kombinacji nie mają własnego mapowania (walidacja); wiersze: `shell`, `osk`, `app` + `web` — wszystkie zmapowane przyciski → Actions, `app` + `moonlight` — żadnej Action poza Home, `app` + `native` — D-pad i A bez Actions, pozostałe zmapowane przyciski → Actions; przyciski niezmapowane i pad niepodłączony nie dają Actions i nie zaśmiecają logu; P/Invoke tylko w `App/Platform` przez CsWin32; czas przez `TimeProvider` (AD-14).

**Never:** mysz/żyroskop i `ClipCursor` (story 8), `SendKey` i znacznik `dwExtraInfo` (story 7), zachowanie OSK, gałęzie Orchestratora dla `action`/`back`, zmiany `MessageTypes`, `ContractJson`, schematów poza `input-config`, `web/**`, `timeBeginPeriod` ani inne globalne zmiany rozdzielczości zegara, wibracje, drugi pad, surowe kody pada w `--calibrate-input`, wpis logu na każdy poll.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| `shell`, A krótko | migawka A, potem pusta przed progiem | jedno `{ok, press, gamepad}` przy zwolnieniu | brak błędu |
| `shell`, A długo | migawka A + `OnTimer` po progu | samo `{ok, long, gamepad}`, zero `press` | brak błędu |
| `shell`, trzymany D-pad | migawki z D-pad + `OnTimer` | `press`, potem `repeat` po 400 ms i co 120 ms, koniec na zwolnieniu | brak błędu |
| `app` + `moonlight` | dowolny zmapowany przycisk (także D-pad i A) | zero Actions, `Swallow=false` | brak błędu |
| `app` + `native` | D-pad albo A | zero Actions | brak błędu |
| `app` + `native` | B, Y | `{back, press, gamepad}`, `{keyboard, press, gamepad}` | brak błędu |
| każdy wiersz | Back + Start trzymane | dokładnie jedna `{home, press, gamepad}`, zero Actions składowych | brak błędu |
| kombinacja przerwana i znowu pełna | zwolnienie Start, ponowne naciśnięcie | jedna kolejna Action Home | brak błędu |
| pad niepodłączony | XInput `ERROR_DEVICE_NOT_CONNECTED` | pusta migawka, zero Actions | brak wpisu na poll |
| zmiana trybu w trakcie trzymania | `shell` → `app` + `moonlight` z trzymanym D-padem | `repeat` i zwolnienie idą za decyzją naciśnięcia | brak błędu |
| przycisk niezmapowany | np. X | brak Action i brak wpisu w trzymanych | brak błędu |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Config/InputConfig.cs` — `InputConfig` (10-14) dostaje `Gamepad` jako **init property** (opcjonalne w schemacie, `required` zostaje `["devices","gamepadHome"]`); nowy `InputGamepad` obok `InputTiming` (17-27): 14 właściwości `ActionKind?` w kolejności `GamepadButton` (45-61), domyślnie `dpadUp/…/dpadRight` → `Up/Down/Left/Right`, `a` → `Ok`, `b` → `Back`, `y` → `Keyboard`, reszta `null`; helper `Mappings()` zwraca pary niepuste. Słownik `Dictionary<GamepadButton, ActionKind>` **nie działa** (patrz Design Notes).
- `src/MediaStation.Core/Config/ConfigValidation.cs` — `CheckInput` (283-385): po pętli deviców odrzuć `appMenu` w mapowaniu pada (path `$.gamepad.<button>`, komunikat wzorem 340-344) i każde mapowanie przycisku z `GamepadHome` (path `$.gamepad.<button>`); `JsonErrorPath.Property` + `JsonNamingPolicy.CamelCase` jak w 377.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `GamepadSnapshot` obok `KeyStroke` (22); mapowanie z `InputGamepad` w ctorze (93-112); nowa metoda `OnGamepad` (krawędzie, Home, routing, długie/repeat); `HeldKey`/`PendingLong` (447-467) przenoszą się na klucz rozróżniający klawiaturę od pada, żeby `NextDeadline` (116-131), `OnTimer` (246-263), `NewHeldKey` (295-311) i `CollectRepeats` (322-377) zostały jedynym źródłem siatki `repeat` i `long`.
- `src/MediaStation.Core/Input/InputTarget.cs` — `IInputTarget` (12-28) dostaje `OnGamepad`; `InputCalibration` (Core, 21) implementuje je jako `PassDecision` (41).
- `src/MediaStation.Core/Input/InputHubError.cs` — nowa stała `InputHubOperations.Gamepad` (wzór 16-28), używana tylko przy przejściu w błąd XInput inny niż brak pada.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — `XInputGetState` (typy `XINPUT_STATE`/`XINPUT_GAMEPAD` generuje CsWin32; metadata 71.0.30-preview ma `xinput1_4.dll`).
- `src/MediaStation.App/Platform/Input/InputHub.cs` — drugi `TimerId` (33) na 10 ms uzbrajany w `Run` po `ArmTimer()` (149), gałąź `WM_TIMER` w `WindowProc` (277-289) → poll XInput, budowa migawki z `wButtons`, `OnGamepad`, `Enqueue` (426-432), `ArmTimer()` (439-452), `Report`; `KillTimer` w `ReleaseNativeResources` (516-532); czas `Environment.TickCount64` (wolne w App, w Core zakazane przez `BannedSymbols.txt`).
- `tools/samples/config/input.json` — jawna sekcja `gamepad` równa domyślnym (odkrywalność).
- `tools/samples/config/invalid/` + `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — `InputJson` (39-40), theory nieznanych pól (221-231), `InvalidSamples` (199-217), `Sample_config_files_are_valid` (176-197), pin schematu (1015-1047: kolejność `["devices","gamepadHome","gamepad","timing"]`, `required` bez zmian).
- `tests/MediaStation.Core.Tests/GamepadPolicyTests.cs` — nowy; helpery wzorem `InputPolicyTests` (`Create` 87-98, `Press`/`Repeat` 106-110, `FakeTimeProvider`), `RecordingLog` do wpisów `action`.
- Reuse: `InputRoutingState`, `InputDecision`, `ActionQueue`, `ActionPump`, `LogActionSink`, `InputTimerDelay`. Nie ruszać: `RawInput*`, `InputCalibration` poza no-opem, `CompositionRoot` (bez zmian — hub polluje zawsze), `MessageTypes`, `web/**`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Config/InputConfig.cs` — `InputGamepad` z domyślnymi mapowaniami i `InputConfig.Gamepad` — źródło mapowania pada (`schemat`, sample).
- [x] `src/MediaStation.Core/Config/ConfigValidation.cs` — reguły `appMenu` i kolizji z `gamepadHome` — „walidacja” z ticketu.
- [x] `src/MediaStation.Core/Input/InputTarget.cs` + `InputPolicy.cs` + `InputCalibration.cs` — `OnGamepad` i klucz trzymania z rozróżnieniem źródła — polityka pada na współdzielonej siatce `repeat`/`long`.
- [x] `src/MediaStation.Core/Input/InputHubError.cs` — stała `gamepad` — jednorazowy raport błędu XInput.
- [x] `src/MediaStation.App/Platform/Input/InputHub.cs` + `NativeMethods.txt` — polling 10 ms na wątku Input, migawka i `OnGamepad` — „polling XInput co 10 ms”.
- [x] `tools/samples/config/input.json`, `tools/samples/config/invalid/input-gamepad-home-mapped.json` — sample i invalid sample.
- [x] `tests/MediaStation.Core.Tests/{GamepadPolicyTests,ConfigValidationTests}.cs` — wiersze macierzy, kombinacja Home, walidacja, pin schematu — `verify`.

**Acceptance Criteria:**
- Given routing `shell`, when migawka z A, potem pusta migawka przed 600 ms, then dokładnie jedno `{kind: ok, phase: press, source: gamepad}`; a gdy minie próg, then jedno `{ok, long, gamepad}` i zero `press`.
- Given routing `app` + `moonlight`, when dowolny zmapowany przycisk pada jest naciśnięty i trzymany, then zero Actions poza kombinacją Home (pusta lista przy każdym pollu).
- Given routing `app` + `native`, when B, potem Y, then `{back, press, gamepad}` i `{keyboard, press, gamepad}`; a przy D-padzie i A then zero Actions.
- Given przytrzymane Back + Start, when pierwsza migawka ma oba przyciski, then jedna `{home, press, gamepad}` i zero Actions dla Back i Start; trzymanie dalej nie daje nic, a po zwolnieniu i ponownym naciśnięciu then jedna kolejna Home.
- Given `input.json`, w którym `gamepad.back` ma mapowanie przy `gamepadHome` `["back","start"]`, when walidacja, then jeden `configError` na `$.gamepad.back`; a sam sample przechodzi walidację.
- Given sample config i maszyna bez pada, when `MediaStation.exe` działa 10 s, then log nie ma `inputHubError` ani wpisów `action` z pada, a proces kończy się kodem 0.

## Implementation Notes

- **`InputGamepad` jako rekord 14 pól, nie słownik:** potwierdzone — `ContractJson.ReadOptions` rzuca `NotSupportedException` na kluczach enumowych, bo `StrictEnumConverter` nie implementuje `ReadAsPropertyName`. Rekord daje schemat z nazwanymi przyciskami i odrzucenie nieznanego pola; jawne `null` wyłącza domyślne mapowanie (test `Explicit_null_disables_a_default_gamepad_mapping`).
- **Jedna siatka `repeat`/`long`:** `HeldId` (`ForKeyboard(vk)` / `ForGamepad(button)`) zastępuje klucz VK w `_held` i `PendingLong`; `NextDeadline`, `OnTimer`, `NewHeldKey` i `CollectRepeats` pozostały jedynym źródłem timingu. Dla pada `NewHeldKey` dostaje odpowiednik połknięcia: `GamepadProducesActions` (czy wiersz AD-8 daje Actions).
- **Wiersze AD-8 dla pada są kind-based, nie button-based:** `app` + `moonlight` daje wyłącznie `home`, `app` + `native` przepuszcza `up/down/left/right/ok`, każdy inny wiersz daje Actions. Dzięki temu niestandardowe mapowanie (np. `A → back`) zachowuje się jak odpowiednik klawisza, a nie jak nazwa fizycznego przycisku.
- **Kombinacja Home:** `_homeEmitted` daje jedno `home` na pełne złożenie; brak choćby jednego składnika zeruje flagę i uzbraja kolejne złożenie. Przyciski kombinacji są pomijane w mapowaniu także w polityce, więc niepoprawny config nie zdubluje Actions składowych.
- **Hub:** drugi `SetTimer` (10 ms) na wątku Input; `OnGamepad` jest wołany tylko przy zmianie bitmaski `wButtons` (powtarzany poll nic nie daje). Brak pada/błąd XInput wysyła **niedostępną** migawkę (`Available=false`) raz na przejście w niedostępność: polityka zrzuca trzymania pada bez Actions i przyjmuje pierwszą dostępną migawkę po przerwie jako nowy stan początkowy (bez krawędzi press i bez Home). Raport `inputHubError {operation: gamepad}` powstaje raz na przejście w błąd XInput inny niż `ERROR_DEVICE_NOT_CONNECTED`, a `ERROR_DEVICE_NOT_CONNECTED` czyści stan błędu, żeby fault po unplug/replug znów był nowym przejściem; wyjątek celu idzie jako `hookCallback`, żeby `gamepad` zostało zarezerwowane dla XInput.
- **Czas:** `Environment.TickCount64` w App (ta sama jednostka co `time` hooka); Core nadal wyłącznie `TimeProvider`.
- **Poza planem, wymuszone przez STJ:** sekcja `"gamepad": null` jest odrzucana semantycznie (`$.gamepad`, `pole nie może być null`), bo STJ przepuszcza `null` do init-właściwości rekordu mimo `RespectNullableAnnotations`.

## Plan Change Log

- Dodano `WIN32_ERROR` do `NativeMethods.txt`: CsWin32 odrzuca pojedyncze stałe `ERROR_DEVICE_NOT_CONNECTED`/`ERROR_SUCCESS` regułą `PInvoke004` i wymaga nazwy enuma. Nadal zero ręcznych `DllImport`.
- Dodano walidację `"gamepad": null` (patrz Implementation Notes) — bez niej STJ przyjąłby null, a polityka po cichu wróciłaby do domyślnych mapowań.
- Hub porównuje bitmaskę przycisków zamiast wołać `OnGamepad` co 10 ms; pusta migawka przy braku pada jest wysyłana tylko po przejściu z „podłączony”, więc nie ma wpisu ani pracy na poll.
- Dodano test `Gamepad_snapshot_is_passed_through_without_an_entry`, żeby no-op kalibracji dla pada był pokryty.
- **Poprawka po przeglądzie:** `GamepadSnapshot` ma `Available`; niedostępna migawka zrzuca trzymania pada bez Actions, a pierwsza dostępna po przerwie jest baseline'em (bez press i bez Home) — reconnect z trzymanym A, D-padem albo pełną kombinacją nie emituje fałszywego `press`/`home`. Hub wysyła ją raz na przejście i czyści `_gamepadFaulted` także na `ERROR_DEVICE_NOT_CONNECTED`, więc fault po unplug/replug znów jest raportowany.
- **Poprawka po przeglądzie:** komunikat kolizji z `gamepadHome` mówi, że remedy to jawne `null` (`...; ustaw dla niego null`), a testy i invalid sample pinują nowy tekst.

## Review Triage Log

### 2026-10-22 — Review pass
- verdicts: 4 findings — high 0, medium 1, low 3, false 0, maybe-false 0
- findings:
  - `[medium]` `[patch]` Błąd odczytu XInput jest zamieniany na „wszystko zwolnione”, a polityka traktuje pustą migawkę jak fizyczne zwolnienie: przy trzymanym A poniżej progu pad rozłącza się i leci `{ok, press, gamepad}`, a po powrocie pada to samo trzymanie daje drugą Action (a trzymana kombinacja Back + Start drugie `home`) — potwierdzone: `InputHub.ReleaseGamepad` (`InputHub.cs:544-560`) woła `DeliverGamepad` z pustą listą, `InputPolicy.ReleaseGamepadButtons` (`InputPolicy.cs:288-318`) domyka `_pendingLong` jako `press`, `EmitGamepadHome` (`:266-281`) zeruje `_homeEmitted`, a `_gamepadPolled = false` sprawia, że następny udany odczyt (`InputHub.cs:476-484`) jest nową krawędzią. Wiersz macierzy `pad niepodłączony → zero Actions` nie jest więc spełniony dla trzymanego przycisku, a „jedna Action na naciśnięcie” pęka po chwilowej awarii odczytu. Naprawa: migawka niesie informację „stan niedostępny”; polityka na niej gasi trzymania pada **bez** Actions i przyjmuje pierwszą kolejną dostępną migawkę jako nową bazę (zero krawędzi i zero Home), a hub wysyła ją raz na przejście w niedostępność; testy na trzymane A, trzymaną kombinację i ponowne podłączenie.
  - `[low]` `[patch]` `_gamepadFaulted` nie jest zerowany przy `ERROR_DEVICE_NOT_CONNECTED`, więc po jednym raporcie błędu odpięcie i ponowne podłączenie pada zostawia flagę i kolejna awaria odczytu nie daje wpisu — potwierdzone: `TryReadGamepad` (`InputHub.cs:491-509`) zeruje flagę tylko w gałęzi `ERROR_SUCCESS`. Naprawa: brak urządzenia jest brakiem stanu błędu, więc zeruje flagę i następna awaria jest nowym przejściem.
  - `[low]` `[reject]` Implementation Notes (`plan:92`) mówią, że brak pada wysyła pustą migawkę, a Plan Change Log (`plan:100`) i kod (`InputHub.ReleaseGamepad` wczesny `return` przy `!_gamepadPolled`) mówią „tylko po przejściu z podłączony”; maszyna bez pada nigdy nie woła `OnGamepad`. Naprawa tego spostrzeżenia to edycja tekstu tego planu, czego triage nie robi; samo zachowanie (brak wpisu i brak Actions bez pada) jest poprawne i pokryte testem polityki oraz przebiegiem `MediaStation.exe` bez `inputHubError`.
  - `[low]` `[patch]` Reguła kolizji z `gamepadHome` odrzuca mapowanie domyślne (`a`, `b`, `y`, D-pad) i wskazuje `$.gamepad.a`, pole którego nie ma w pliku użytkownika, bez podpowiedzi, że lekiem jest jawne `null` — potwierdzone: `ConfigValidation.CheckGamepad` (`ConfigValidation.cs:389-418`) + domyślne wartości `InputGamepad` (`InputConfig.cs:37-80`), pinowane testem `Gamepad_home_with_a_default_mapped_button_is_rejected`. Naprawa: komunikat podaje remedy (`null` w `$.gamepad.<przycisk>`), a testy i invalid sample pinują nowy tekst.

## Design Notes

Pad nie ma odpowiednika połknięcia: XInput jest odpytywany, więc aplikacja i tak widzi przyciski. Dlatego `OnGamepad` zwraca `Swallow=false` zawsze, a wiersze AD-8 realizują się wyłącznie jako zestaw Actions (`app` + `moonlight` = pusty zestaw poza Home).

Wspólna siatka `repeat`/`long` zamiast drugiej kopii: `InputPolicy` dostaje klucz trzymania rozróżniający klawiaturę od pada (np. `HeldId(source, id)`), więc `NextDeadline`, `OnTimer`, `NewHeldKey` i `CollectRepeats` zostają jednym źródłem. Dla pada odpowiednikiem „połknięte” jest „ten wiersz daje Actions”: przepuszczony przycisk nie uzbraja siatki ani terminu `long`, ale jego zwolnienie idzie za decyzją naciśnięcia, tak jak key-up.

Home to kombinacja, nie przycisk: polityka pamięta, czy kombinacja jest pełna i czy już dała Action, więc pełna kombinacja daje jedno Home, a przerwanie i ponowne złożenie — kolejne. `ConfigValidation` pilnuje, że przyciski kombinacji nie mają własnego mapowania, więc Home nie dubluje Actions składowych.

`gamepad` w `input.json` jest rekordem z 14 opcjonalnymi polami, nie słownikiem `GamepadButton → ActionKind`: sprawdzone w tym przebiegu, `ContractJson.ReadOptions` (StrictEnumConverter bez `ReadAsPropertyName`) rzuca `NotSupportedException` na kluczach enumowych, a rekord daje schemat z nazwanymi przyciskami i odrzucenie nieznanego pola. Jawny `null` wyłącza domyślne mapowanie.

Polling przez drugi `SetTimer` (10 ms) zamiast przepisywania pętli komunikatów: `GetMessage` jest blokujący i obsługuje `WM_QUIT` z `PostThreadMessage`, więc zmiana nośnika waitu ryzykuje zamknięcie; `WM_TIMER` bez `timeBeginPeriod` ma ziarno zegara systemowego (~15,6 ms), co jest poniżej progu 60 Hz i nie zmienia zachowania polityki.

## Verification

**Commands:**
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów (baseline 711 testów).
- `dotnet test MediaStation.slnx -c Release` — wszystkie zielone, 0 skipped; nowe przypadki dla każdego wiersza macierzy I/O.
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `input-config.schema.json` ma `gamepad` z 14 właściwościami, `required` bez zmian; brak zmian w `web/**`.
- `cd web && npm run build` — contracts + typecheck + Vite zielone (generowany `input-config.ts` się kompiluje).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` 10 s — kod 0, log `start` → … → `stop`, zero `inputHubError`/`warn`/`error`; config użytkownika odtworzony po przebiegu.
- Sprzętowo N/A: fizyczny pad na ALDER (brak gamepada) — zachowanie pokrywają testy polityki; pad na UM760 w odbiorze epiku.

**Current-machine evidence (2026-10-22, ALDER / B760 Pro RS/D4 / Windows 11 Home, baseline `88a81ea` + working tree):**
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów; `npm run contracts`, `tsc` i Vite w tym samym buildzie zielone.
- `dotnet test MediaStation.slnx -c Release` — **757/757** passed, 0 failed, 0 skipped (baseline 711). Nowe: `GamepadPolicyTests` (każdy wiersz macierzy: `shell`/`osk`/`app` + `web`/`moonlight`/`native`, kombinacja Home, repeat D-pada, `long` A, brak pada, zmiana trybu w trakcie trzymania, przycisk niezmapowany), `ConfigValidationTests` (kolizja z `gamepadHome`, `appMenu`, domyślne i jawne `null`, pin schematu) oraz no-op `InputCalibration.OnGamepad`.
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `input-config.schema.json` ma `gamepad` z 14 właściwościami, `required` nadal `devices, gamepadHome`; kolejność `devices, gamepadHome, gamepad, timing`.
- `cd web && npm run build` — contracts + typecheck + Vite zielone; generowany `input-config.ts` się kompiluje.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` 10 s — proces żył po 10 s, `CloseMainWindow` zadziałał, kod **0**. Log: `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished {rev: 1}` → `stop {exitCode: 0}`; zero `inputHubError`, zero `action` (także z pada), zero `warn`/`error`. Config użytkownika odtworzony bajt w bajt: SHA-256 `apps.json`, `input.json` i `settings.json` zgodne z kopią sprzed przebiegu.
- **Sprzętowo N/A:** na ALDER nie ma fizycznego pada, więc `XInputGetState` zwraca `ERROR_DEVICE_NOT_CONNECTED` i log pozostaje czysty; pełny przebieg z padem (D-pad, A, B, Y, Home z Back + Start) należy do odbioru epiku na UM760. Na tej maszynie nie da się wstrzyknąć stanu XInput, a zachowanie polityki pokrywają testy na `FakeTimeProvider`.
- **Po poprawkach przeglądu (tylko testy zmienionych plików):** `GamepadPolicyTests` + `ConfigValidationTests` — 217/217 passed, 0 failed, 0 skipped; pełny `dotnet test` i przebieg aplikacji po stronie operatora.

**Po poprawkach po przeglądzie (2026-10-22, ALDER, ten sam baseline `88a81ea` + working tree):**
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów (kontrakty, `tsc` i Vite w tym samym buildzie zielone).
- `dotnet test MediaStation.slnx -c Release` — **760/760** passed, 0 failed, 0 skipped (baseline 711). Poprawka dodała `Unavailable_snapshot_drops_a_held_a_without_press`, `Reconnect_with_a_held_home_combo_does_not_emit_second_home`, `Reconnect_with_a_held_dpad_does_not_emit_press_or_arm_repeat` i zmieniła `Unavailable_snapshot_drops_a_held_button_without_action`.
- `dotnet run --project tools/MediaStation.SchemaExport -c Release` — bez zmian: `gamepad` z 14 właściwościami, `required` `devices, gamepadHome`; `web/**` czyste.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` 10 s — proces żył po 10 s, `CloseMainWindow` zadziałał, kod **0**; log: `start` → `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}` → `powerSignalsRegistered` → `snapshotPublished {rev: 1}` → `stop {exitCode: 0}`, zero `inputHubError`, zero `action`, zero `warn`/`error`. Config użytkownika odtworzony bajt w bajt (SHA-256 `apps.json`, `input.json`, `settings.json` zgodne), katalog `config/invalid` na miejscu, katalog publish usunięty.

## Auto Run Result

**Status:** built.

**Summary:** `InputHub` odpytuje XInput (indeks 0) co 10 ms na wątku Input i oddaje polityce migawki stanu pada, a `InputPolicy` mapuje przyciski z nowej sekcji `gamepad` w `input.json` (domyślnie D-pad → strzałki, A → `ok`, B → `back`, Y → `keyboard`). Pad korzysta z tej samej siatki `repeat`/`long` co klawiatura (`HeldId` rozróżnia VK od przycisku pada), więc trzymany D-pad daje `press` + `repeat` po progach `timing`, A w `shell`/`osk` daje `press` na zwolnieniu przed progiem albo samo `long`, a kombinacja `gamepadHome` (domyślnie Back + Start) daje dokładnie jedno `action {kind: home, source: gamepad}` na pełne złożenie. Wiersze AD-8 realizują się jako zestaw Actions: `shell`, `osk` i `app` + `web` dają Actions, `app` + `moonlight` nic poza Home, a `app` + `native` tylko B, Y i Home. Migawka oznaczona jako niedostępna (brak pada albo błąd XInput) zrzuca trzymania pada bez Actions, a pierwsza dostępna migawka po przerwie jest nowym stanem początkowym — reconnect z trzymanym przyciskiem nie dubluje `press` ani `home`. Walidacja odrzuca `appMenu` i mapowanie przycisku z `gamepadHome` (komunikat podaje remedy: jawne `null`), a `tools/samples/config/input.json` pokazuje pełną sekcję `gamepad`.

**Files changed:**
- `src/MediaStation.Core/Config/InputConfig.cs` — rekord `InputGamepad` (14 opcjonalnych mapowań z domyślnymi, `Mappings()`) i `InputConfig.Gamepad` jako opcjonalna właściwość init.
- `src/MediaStation.Core/Config/ConfigValidation.cs` — `CheckGamepad`: `appMenu` i kolizja z `gamepadHome` odrzucone pod `$.gamepad.<przycisk>`, z remedy w komunikacie.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `GamepadSnapshot`, `OnGamepad` (krawędzie, Home, wiersze AD-8), `HeldId` i wspólna siatka `repeat`/`long`, stan niedostępny + baseline po przerwie.
- `src/MediaStation.Core/Input/InputTarget.cs` — `OnGamepad` w `IInputTarget`.
- `src/MediaStation.Core/Input/InputCalibration.cs` — no-op `OnGamepad` dla `--calibrate-input`.
- `src/MediaStation.Core/Input/InputHubError.cs` — stała `InputHubOperations.Gamepad`.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — drugi timer 10 ms, poll XInput, migawka przy zmianie bitmaski, niedostępność raz na przejście, one-shot raport błędu, `KillTimer` w teardownie.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — `XInputGetState` i `WIN32_ERROR` (CsWin32).
- `tools/samples/config/input.json` — jawna sekcja `gamepad` równa domyślnym.
- `tools/samples/config/invalid/input-gamepad-home-mapped.json` — invalid sample kolizji z `gamepadHome`.
- `tests/MediaStation.Core.Tests/GamepadPolicyTests.cs` — nowy: macierz I/O, kombinacja Home, repeat/long, routing, stan niedostępny, reconnect, niezmapowany przycisk, wpis `action` z `source: gamepad`.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — domyślne mapowanie, jawne `null`, kolizje z `gamepadHome`, `appMenu`, nieznane pole, invalid sample, pin schematu `input-config`.
- `tests/MediaStation.Core.Tests/InputCalibrationTests.cs` — no-op pada w kalibracji.

**Review findings:** 4 findings (high 0, medium 1, low 3). Patches applied (3: 1 `medium`, 2 `low`): migawka niedostępna zamiast pustej (brak fałszywego `press` przy rozłączeniu i drugiej Action/Home po reconnect), `_gamepadFaulted` zerowany także na `ERROR_DEVICE_NOT_CONNECTED`, komunikat kolizji z `gamepadHome` podaje remedy (`null`). Deferred: 0. Rejected: 1 `low` — rozjazd tekstu Implementation Notes z Plan Change Log co do wysyłki pustej migawki; naprawa wymagałaby edycji tego planu, a zachowanie (brak wpisu i brak Actions bez pada) jest poprawne i pokryte testem oraz przebiegiem aplikacji.

**Follow-up review recommended:** false — pierwszy przebieg; patchowane wpisy to jeden `medium` i dwa `low`, bez `high` i bez dwóch `medium`.

**Verification:** `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów. `dotnet test MediaStation.slnx -c Release` — 760/760 passed, 0 failed, 0 skipped (baseline 711); każdy wiersz macierzy I/O ma przechodzący test (m.in. `Shell_a_before_threshold_gives_one_ok_press_on_release`, `A_past_threshold_gives_only_long_and_no_press`, `Held_dpad_repeats_after_delay_and_interval_until_release`, `Moonlight_row_gives_no_actions_for_any_mapped_button`, `Native_row_*`, `Home_combo_gives_exactly_one_action_per_full_composition`, `Unavailable_snapshot_*`, `Reconnect_with_a_held_*`, `Mode_change_during_hold_*`, `Unmapped_button_gives_no_action_and_no_held_entry`). `dotnet run --project tools/MediaStation.SchemaExport -c Release` — `gamepad` z 14 właściwościami, `required` bez zmian, `web/**` czyste. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` 10 s — kod 0, log `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, zero `inputHubError`/`action`/`warn`/`error`; config użytkownika odtworzony bajt w bajt (SHA-256 trzech plików zgodne), katalog publish usunięty.

**Residual risks / checks for UM760 (whole-epic acceptance):** brak fizycznego pada na ALDER, więc `XInputGetState` zwraca `ERROR_DEVICE_NOT_CONNECTED` i sprzętowe zachowanie pada potwierdza dopiero odbiór epiku: D-pad w `shell` daje `press` + `repeat`, A w `shell`/`osk` daje `long` po 600 ms, B w `app` + `native` daje `back`, Y daje `keyboard` (OSK), a Back + Start daje dokładnie jedno `action {kind: home}` nad Moonlightem i pulpitem. `WM_TIMER` bez `timeBeginPeriod` ma ziarno zegara systemowego (~15,6 ms), więc bardzo krótkie muśnięcia przycisku mogą wypaść między pollami; pad w Moonlightcie jest widziany przez samą aplikację, bo XInput nie da się połknąć (zgodnie z AD-8).
