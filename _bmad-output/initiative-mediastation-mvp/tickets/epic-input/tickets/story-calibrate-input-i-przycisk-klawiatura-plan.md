---
title: '`--calibrate-input` i przycisk Klawiatura'
type: 'feature'
ticket: '3'
created: '2026-10-22'
status: done
baseline_revision: 'cc7e84356b6c1fb7b07a660f8aa0a12c10de6811'
route: 'full'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: ['quick']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** `--calibrate-input` jest tylko rozpoznawany: `StartDecision` kończy proces wpisem `modeUnsupported` z kodem 4, więc autor nie ma czym ustalić, który przycisk G20S Pro jest Klawiaturą, a AD-13 dopuszcza surowe kody wyłącznie w tym trybie. Otwarte pytanie specu („Przycisk Klawiatura na G20S Pro”) zostaje nierozstrzygnięte.

**Approach:** `StartDecision` zwraca nowy tryb `CalibrateInput` (mutex jak w każdym trybie), a App uruchamia sam InputHub ze story 2 z `InputCalibration` z Core zamiast `InputPolicy`: bez mapowania i bez połknięcia, przez 60 s albo do Esc z klawiatury. Każde naciśnięcie przycisku urządzenia z `source: remote` daje jeden wpis `rawInput` z surowymi kodami hooka, Raw Input i kolejnością obu źródeł; tryb kończy się kodem 0.

## Boundaries & Constraints

**Always:** filtr po sygnaturze urządzeń `source: remote` z `input.json` — zwykła klawiatura i litera pisana na niej nigdy nie trafiają do logu (AD-13); kalibracja nie mapuje i nie połyka klawiszy; czas tylko przez wstrzyknięty `TimeProvider` (AD-14); Win32 wyłącznie w `MediaStation.App/Platform` przez CsWin32 (AD-3); wpisy logu buduje Core; tryb główny (`run`) zachowuje zachowanie i kolejność zwalniania ze story 2.

**Never:** zapis `input.json` (autor wpisuje `"0x..": "keyboard"` ręcznie), zmiany zachowania `InputPolicy`, `Orchestrator`, `CommandRouter`, `MessageTypes.All`, `ContractJson`, `web/**`, `SendKey`, XInput, `repeat`, routing `SetInputRouting`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Przycisk pilota z VK | raport HID Consumer `0x0C` + hook VK `0x0D` wstrzyknięty + `WM_INPUT` klawiatury dongla w oknie 50 ms | jeden `rawInput {signature, vk: "0x0D", scan, usagePage: "0x000C", usage, injected: true, hasVk: true, llBeforeRawMs}` | brak błędu |
| Przycisk bez VK | raport HID Consumer bez hooka i bez `WM_INPUT` w oknie | jeden `rawInput` z `vk: null`, `hasVk: false` | brak błędu |
| Zwykła klawiatura | `WM_INPUT` z klawiatury o innej sygnaturze, hook poza oknem urządzenia `remote` | brak wpisu `rawInput` | cisza; AD-13 |
| Esc z klawiatury | hook `0x1B`, `injected: false` | koniec trybu, wpis `stop {exitCode: 0}` | kod 0 |
| 60 s bez Esc | brak zdarzeń, timer terminu | koniec trybu po 60 s, `stop {exitCode: 0}` | kod 0 |
| Żywa instancja główna | mutex zajęty | `instanceRejected {mode: "calibrateInput"}`, kod 3 | mutex jak w każdym trybie |
| Zły config | brak albo niepoprawny `input.json` | wpisy `configError`, kod 6, zero `rawInput` | koniec bez logowania kodów |

</intent-contract>

## Code Map

- `src/MediaStation.Core/Cli/StartDecision.cs:56-67` — `mode is CliMode.Run` → `Run`; dodać `public sealed record CalibrateInput : StartDecision` i zwrot dla `CliMode.CalibrateInput` przed gałęzią `modeUnsupported` (zostaje tylko dla `Smoke`). Mutex i wpis `instanceRejected` już są wspólne.
- `src/MediaStation.Core/Cli/ExitCode.cs` — dopisać `ConfigInvalid = 6` (config bez ekranu błędu) i `InputHubFailed = 7` (tryb, którego całą pracą jest wejście, nie wystartował); komentarz klasy zapowiada dopisywanie kodów przez epiki.
- `src/MediaStation.Core/Input/InputTarget.cs` **(nowy)** — `public interface IInputTarget { DateTimeOffset? NextDeadline { get; } InputDecision OnKey(KeyStroke key); void OnHidConsumer(HidConsumerReport report); void OnRawKeyboard(RawKeyboardInput input); InputDecision OnTimer(DateTimeOffset now); }`, dokładnie kształt dzisiejszego `InputPolicy`, więc hub przestaje zależeć od konkretnej polityki.
- `src/MediaStation.Core/Input/InputPolicy.cs:21-27,41` — `InputPolicy : IInputTarget` (bez zmian zachowania); `KeyStroke` dostaje `int Scan = 0`, `HidConsumerReport` → `(string DevicePath, long TimeMs, int UsagePage, int Usage)`, `RawKeyboardInput` → `(string DevicePath, long TimeMs, int VirtualKey, int Scan, bool Down)` (pola surowe czyta tylko kalibracja); dopasowanie sygnatury wydzielić do `internal static class DeviceSignature` (`Matches(path, signature)`, `StringComparison.OrdinalIgnoreCase`) i użyć w `MatchDevice`.
- `src/MediaStation.Core/Input/RawInputEntry.cs` **(nowy)** — `public sealed record RawInputEntry(string Signature, int? VirtualKey, int? Scan, int? UsagePage, int? Usage, bool? Injected, bool HasVk, int? LlBeforeRawMs)`.
- `src/MediaStation.Core/Input/InputCalibration.cs` **(nowy)** — `public sealed class InputCalibration : IInputTarget`, `public const int DurationSeconds = 60`, `public const int EscapeVirtualKey = 0x1B`, `public const int MergeWindowMs = 50`; ctor `(InputConfig config, TimeProvider time, IRawInputSink sink)`; `event Action? Completed`; filtr: tylko sygnatury urządzeń `Source == Remote`. Stan: jeden `PendingPress` (signature, usagePage/usage, vk, scan, `injected`, `hookTimeMs`, `rawTimeMs`, `flushAt`), bufor hooków z okna ±50 ms, zbiory „trzymanych” usage i VK. `OnHidConsumer` z `Usage == 0` to zwolnienie przycisku (ignorowane); nowe naciśnięcie domyka poprzednie. `OnKey` nigdy nie połyka i nie zwraca Actions; Esc (`!Injected`) ustawia `Completed`. `OnTimer` domyka `PendingPress` po `flushAt` i kończy tryb po 60 s. `NextDeadline` = min(koniec, `flushAt`).
- `src/MediaStation.Core/Input/LogRawInputSink.cs` **(nowy)** — `LogRawInputSink : IRawInputSink, IInputHubErrorSink`, `Src = "input"`; `Deliver` → `Info(Src, "rawInput", …)` z kluczami w kolejności `signature, vk, scan, usagePage, usage, injected, hasVk, llBeforeRawMs`; `vk`/`scan` jako `0xNN`, `usagePage`/`usage` jako `0xNNNN` (`v:X2`/`X4`), brakujące wartości jako `null` (stały kształt wpisu); `Report` jak w `LogActionSink`.
- `src/MediaStation.Core/Ports/Ports.cs:86-90` — dopisać `IRawInputSink { void Deliver(RawInputEntry entry); }` (kontrakt: wołany z wątku Input, nie blokuje i nie rzuca).
- `src/MediaStation.App/Platform/Input/InputHub.cs` — ctor `(IInputTarget target, ActionQueue queue, IInputHubErrorSink errors, TimeProvider time)`; w `OnRawInput` czytać ciało raportu (`RAWKEYBOARD` → vk/scan/down, HID → usage) i wołać `ArmTimer()` także po `WM_INPUT`; `ArmTimer()` raz po `InstallHook()` (termin 60 s i pierwszy `flushAt`); poza tym bez zmian (hook, okno message-only, `Dispose`).
- `src/MediaStation.App/Platform/Input/RawInputBody.cs` **(nowy, bez Win32)** — `TryReadKeyboard(ReadOnlySpan<byte> body, out int vk, out int scan, out bool down)` i `TryReadHidUsage(ReadOnlySpan<byte> body, out int usage)`: długość ciała z `read`, report ID pomijany, gdy raport ma > 2 bajty, usage jako LE16 (wzór `spike/win/HomeLogger.cs:206-217`); za krótkie ciało → `false`, hub raportuje `inputHubError {rawInput}`.
- `src/MediaStation.App/Platform/System/NativeMethods.txt` — dopisać `RAWKEYBOARD` (i nazwę enuma flag, jeśli CsWin32 jej wymaga) — jedna lista, bo `AdditionalFiles` wskazuje ten plik.
- `src/MediaStation.App/CompositionRoot.cs:70-81,84-98` — wydzielić `LoadConfig(ILog log)` (odczyt + `configError` + `Config`) używany przez `StartRun` i nowy `StartCalibration(app, log, time)`: `AttachStopLogging` przed startem trybu, `LogRawInputSink`, `InputCalibration`, `InputHub` z `new ActionQueue(static () => { })` (kalibracja nie emituje Actions), `Completed` zwalnia `ManualResetEventSlim`, oczekiwanie ograniczone `DurationSeconds + 10 s`, `hub.Dispose()`, zwrot `ExitCode.Ok`/`ConfigInvalid`/`InputHubFailed`; `app.Shutdown((int)code)` w `Start`.
- `tests/MediaStation.Core.Tests/{InputCalibrationTests,LogRawInputSinkTests,RawInputBodyTests}.cs` **(nowe)** + link `Platform/Input/RawInputBody.cs` w `MediaStation.Core.Tests.csproj` — wiersze macierzy, kształt wpisu i parsowanie ciała.
- `tests/MediaStation.Core.Tests/{StartDecisionTests,ExitCodeTests,InputPolicyTests}.cs` — kalibracja startuje (wolna i zajęta instancja), pinezka kodów 6/7, wpis `"0x5D": "keyboard"` daje `action {kind: keyboard}`, zaktualizowane konstruktory rekordów.
- Reuse: `ActionQueue`, `InputHub`, `InputClock`, `InputTimerDelay`, `RawInputReport`, `RecordingLog`, `FakeTimeProvider`. Nie ruszać: `InputPolicy` poza interfejsem, `Orchestrator`, `CommandRouter`, `ContractJson`, `web/**`.

## Tasks & Acceptance

**Execution:**
- [x] `src/MediaStation.Core/Cli/{StartDecision,ExitCode}.cs` + `StartDecisionTests`/`ExitCodeTests` — tryb `CalibrateInput` zamiast `modeUnsupported` i kody 6/7 — `verify` ticketu (decyzja startu).
- [x] `src/MediaStation.Core/Input/{InputTarget,InputCalibration,RawInputEntry,LogRawInputSink}.cs` + `Ports.cs` — czysta kalibracja, filtr `remote`, wpis `rawInput` — AD-13, AD-14 i `verify` (filtr, `hasVk`).
- [x] `src/MediaStation.App/Platform/Input/{InputHub,RawInputBody}.cs` + `NativeMethods.txt` — hub na `IInputTarget`, parsowanie ciała raportu i timer terminu kalibracji — AD-2, AD-3.
- [x] `src/MediaStation.App/CompositionRoot.cs` — uruchomienie trybu kalibracji z ograniczonym oczekiwaniem i kodem 0 — `verify` (kod wyjścia).
- [x] `tests/MediaStation.Core.Tests/{InputCalibrationTests,LogRawInputSinkTests,RawInputBodyTests}.cs` + link w csproj + `InputPolicyTests` — dowód wierszy macierzy i mapowania `keyboard`.

**Acceptance Criteria:**
- Given opublikowana aplikacja i zatrzymana instancja główna, when `MediaStation.exe --calibrate-input` działa 60 s bez wejścia, then log ma `start` z `args ["--calibrate-input"]`, brak wpisu `modeUnsupported`, a ostatni wpis to `stop {exitCode: 0}`, i proces kończy się kodem 0.
- Given raport HID Consumer i `WM_INPUT` klawiatury dongla oraz hook tej samej sygnatury w oknie 50 ms, when przycisk zostanie naciśnięty, then log ma dokładnie jeden `rawInput` z kluczami `signature, vk, scan, usagePage, usage, injected, hasVk, llBeforeRawMs` w tej kolejności, `vk`/`scan` jako `0x..`, `usagePage`/`usage` jako `0x....`, `hasVk: true`.
- Given raport HID Consumer bez VK w oknie, when wpis powstanie, then `hasVk` jest `false`, a `vk` jest `null`.
- Given `WM_INPUT` zwykłej klawiatury i hook poza oknem urządzenia `remote`, when tryb działa, then nie powstaje żaden wpis `rawInput`.
- Given `input.json` z `"0x5D": "keyboard"` dla urządzenia `remote`, when tryb główny dostanie key-down `0x5D`, then `LogActionSink` zapisze jedno `action {kind: keyboard, phase: press, source}`, a klawisz będzie połknięty.
- Given żywa instancja główna, when `--calibrate-input` wystartuje, then log ma `instanceRejected {mode: "calibrateInput"}`, kod 3 i nie powstaje żaden wpis `rawInput`.

## Implementation Notes

- Zaimplementowano zgodnie z planem: `StartDecision.CalibrateInput`, `ExitCode.ConfigInvalid`/`InputHubFailed`, `IInputTarget` + `DeviceSignature`, rozszerzone rekordy `KeyStroke`/`HidConsumerReport`/`RawKeyboardInput`, `RawInputEntry`, `InputCalibration`, `LogRawInputSink`, `IRawInputSink` (Core); `InputHub` na `IInputTarget`, `RawInputBody`, `StartCalibration` i wydzielony `LoadConfig` (App); nowe `InputCalibrationTests`, `LogRawInputSinkTests`, `RawInputBodyTests` + link w csproj (616 testów, baseline 583).
- **`NativeMethods.txt` bez zmian (świadoma decyzja):** ciało raportu czyta `RawInputBody` bez Win32 (jest linkowany do testów), więc `RAWKEYBOARD` nie jest nigdzie użyty. Martwy wpis w liście CsWin32 byłby zbędną konfiguracją — story 2 usunęła w ten sam sposób nieużywane `PostQuitMessage`/`RAWKEYBOARD`. Nowych symboli Win32 ten ticket nie dokłada; AD-3 zostaje spełnione.
- **Esc nie domyka otwartego wpisu:** `Complete()` tylko ustawia znacznik i budzi App. `Flush` w callbacku hooka oznaczałby zapis pliku w `WH_KEYBOARD_LL`, co łamie AD-2/AD-8; wpis otwarty w chwili Esc (≤50 ms od naciśnięcia) jest porzucany. Timer końca trybu domyka wpis przed `Completed`, bo `WM_TIMER` nie jest callbackiem hooka (pokryte testem `Open_entry_is_flushed_before_the_mode_ends`).
- **Filtr przed stanem:** `OnHidConsumer`/`OnRawKeyboard` najpierw dopasowują sygnaturę `source: remote`, dopiero potem ruszają znaczniki trzymania. Dzięki temu `usage == 0` albo key-up zwykłej klawiatury nie mogą wyczyścić znaczników pilota (AD-13). Hook key-up nadal zwalnia znaczniki bez znajomości urządzenia — hook nie niesie źródła.
- **Trzy zbiory trzymania:** `_heldUsages` (HID), `_heldVks` (hook) i `_heldRawVks` (`WM_INPUT`). Jeden wspólny zbiór VK gubiłby pierwsze `WM_INPUT` po hooku (albo odwrotnie), bo oba źródła raportują ten sam VK jednego naciśnięcia. Key-up i `usage == 0` zwalniają oba znaczniki VK i usage, zgodnie z Design Notes.
- **Bufor hooków:** wpisy starsze niż 400 ms (`8 × MergeWindowMs`) są usuwane bez wpisu, a rozmiar bufora jest ograniczony do 64; dopasowanie zawsze wymaga różnicy czasu ≤50 ms i — gdy wpis zna VK z `WM_INPUT` — tego samego VK. Hook dla przycisku bez VK jest przypinany jako najbliższy w oknie, bo to jedyna droga ustalenia `hasVk` (świadomy kompromis z Design Notes): wpis powstaje tylko wtedy, gdy w oknie jest raport urządzenia `remote`, a zwykła klawiatura bez takiego raportu nigdy nie trafia do logu.
- **`llBeforeRawMs`** liczone jako `rawTime − hookTime` z clampem do `int`; `null`, gdy brak hooka albo `WM_INPUT`.
- **Composition root:** `StartCalibration` rejestruje `stop` przed startem trybu, więc ostatni wpis zostaje `stop` także przy złym configu i porażce huba. Oczekiwanie to `DurationSeconds + 10 s`; przekroczenie zwraca `InputHubFailed` (martwy hub nie może zawiesić procesu). Wyjątek startu huba loguje `inputHubStartFailed` ze `src: input` (jak tryb główny). `InputCalibration.Completed` to `System.Action`, bo w Core `Action` koliduje z aliasem kontraktu.
- **Weryfikacja na ALDER (Windows 11, B760, .NET SDK 10.0.402):**
  - `dotnet test MediaStation.slnx -c Release` — 616/616 zielone (nowe: `InputCalibrationTests` 17, `LogRawInputSinkTests` 5, `RawInputBodyTests` 9; zaktualizowane `StartDecisionTests`, `ExitCodeTests`, `InputPolicyTests`, `InputClockTests`).
  - `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń.
  - `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe --calibrate-input` z `tools/samples/config`: proces 60,3 s, kod 0; log `start {args:["--calibrate-input"]}` → `stop {exitCode: 0}`, zero `warn`/`error`, zero `modeUnsupported`, zero `rawInput` (na maszynie nie ma fizycznego pilota). Config w `%LOCALAPPDATA%\MediaStation\config` odtworzony bajt w bajt (SHA-256 zgodne dla trzech plików).
  - Tryb główny na tym samym buildzie: `snapshotPublished`, `stop {exitCode: 0}`, zero `warn`/`error` (brak regresji story 2); przy żywej instancji `--calibrate-input` daje kod 3, `instanceRejected {mode: "calibrateInput"}` i `stop {exitCode: 3}`, bez `rawInput`.
  - Zły config (usunięty `input.json`): kod 6, `configError {file: "input.json", reason: "brak pliku"}`, `stop {exitCode: 6}`, zero `rawInput`; config odtworzony.
- **Sprzętowo N/A:** realne naciśnięcie przycisku pilota (HID + hook + `WM_INPUT` dongla) i fizyczny Esc nie są osiągalne automatem na ALDER — `SendInput` z innego procesu nie dociera tu do hooka LL (ustalenie story 2). Te wiersze macierzy dowodzą testy `InputCalibrationTests`; pozostają do odbioru epiku na UM760 z prawdziwym pilotem.
- **Poprawki po weryfikacji w sesji głównej (przegląd diffu):**
  - `InputCalibration.Merge` scala tylko zdarzenia tego samego przycisku: inny `usage` (HID) albo inny VK (`WM_INPUT`) w oknie 50 ms domyka poprzedni wpis i otwiera następny. Wcześniej drugi przycisk naciśnięty w oknie ginął z logu (`press.Usage ??=`), co łamało „jeden wpis na przycisk”. Test: `Two_different_buttons_in_the_merge_window_give_two_entries`.
  - `InputCalibration.Release` czyści znacznik `usage` tylko wtedy, gdy zwolniony VK należał do naciśnięcia pilota (nowy zbiór `_heldRemoteVks` obok `_heldVks` i `_heldRawVks`). Wcześniej key-up dowolnego klawisza (np. litera pisana w trakcie kalibracji) zdejmował `usage` pilota i powtórzony raport HID dawał drugi wpis tego samego naciśnięcia. Test: `Key_up_of_the_ordinary_keyboard_does_not_clear_the_held_remote_usage`.
  - Decyzja o kodzie wyjścia trybu kalibracji przeniesiona do Core: `StartDecision.CalibrationExit(ConfigResult)` daje `ConfigInvalid`/`Ok`, a `CompositionRoot.StartCalibration` tylko ją wykonuje. Dzięki temu wiersz macierzy „Zły config → kod 6” ma test (`Calibration_exit_code_is_config_invalid_when_config_has_errors`, `..._is_ok_for_a_valid_config`, `Null_config_result_throws`), zamiast opierać się wyłącznie na przebiegu na maszynie.
  - Ponowna weryfikacja po poprawkach (ALDER, ten sam build Release): `dotnet test` 621/621 zielone, `dotnet build` 0 ostrzeżeń, `dotnet publish` OK. `MediaStation.exe --calibrate-input` na configu użytkownika (`%LOCALAPPDATA%\MediaStation\config`, ta sama sygnatura `VID_1915&PID_1025`; pliki nietknięte) — 60,3 s, kod 0, log: `start {args:["--calibrate-input"]}` → `stop {exitCode: 0}`, zero `warn`/`error`, zero `modeUnsupported`, zero `rawInput`. Tryb główny na tym samym buildzie — `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, zero `warn`/`error`, kod 0 (brak regresji story 2).
  - **Pokrycie macierzy:** wszystkie 7 wierszy mają przechodzący test w `dotnet test` (wiersz „Esc” — `Physical_escape_ends_the_mode_...`; „60 s” — `Mode_ends_after_sixty_seconds_...`; „mutex” — `Busy_instance_in_calibrate_mode_exits_already_running`; „zły config” — `Calibration_exit_code_is_config_invalid_...`), a ścieżki App (kod 0 po 60 s, `instanceRejected` 3, `configError` + kod 6) potwierdza przebieg na maszynie.
- **Poprawki z przeglądu (quick):** hook dopinany do naciśnięcia pilota tylko jako `Injected` (fizyczna klawiatura nie może wnieść `vk`/`hasVk`), koniec trybu domyka otwarty wpis bezwarunkowo, a nieudane parsowanie ciała `WM_INPUT` nie gubi korelacji `source` w trybie głównym (cel dostaje nieznane wartości zamiast wpisu `inputHubError`). Ponowna weryfikacja po poprawkach: `dotnet test` **624/624** zielone, `dotnet build` 0 ostrzeżeń, `publish` + `--calibrate-input` 60,3 s z kodem 0 i czystym logiem (`start` z `args` → `stop {exitCode: 0}`), tryb główny bez regresji (`snapshotPublished` → `stop {exitCode: 0}`, kod 0).

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass (quick)
- verdicts: 5 findings — high 0, medium 1, low 4, false 0, maybe-false 0
- findings:
  - `[medium]` `[patch]` Quick: `AttachBufferedHook`/`Fits` dopinały dowolny key-down hooka do naciśnięcia pilota, więc litera z fizycznej klawiatury w oknie 50 ms trafiała do logu jako `vk`/`scan` pilota i ustawiała `hasVk: true` (AD-13, wybór Klawiatury), a następny `WM_INPUT` dongla rozbijał jedno naciśnięcie na dwa wpisy — dodano warunek `Injected == true` w `Fits` i `AttachBufferedHook` (VK pilota niesie `LLKHF_INJECTED`, AD-8) oraz test `Non_injected_key_down_is_not_attached_to_a_remote_hid_press`.
  - `[low]` `[patch]` Quick: `OnTimer` domykał wpis tylko po `flushAt`, a `Complete()` przy końcu trybu nie domykał wcale, więc naciśnięcie z ostatnich 50 ms ginęło wbrew „jeden wpis na przycisk” — koniec trybu domyka `PendingPress` bezwarunkowo (`now >= pending.FlushAt || now >= _endsAt`), test `Press_in_the_last_merge_window_is_flushed_when_the_mode_ends`.
  - `[low]` `[reject]` Quick: `Release` czyści `_heldUsages` przy zwolnieniu dowolnego VK pilota, więc przy dwóch trzymanych przyciskach pilota zwolnienie jednego zdejmuje deduplikację drugiego i powtórzony raport HID daje drugi wpis — wymaga dwóch przycisków pilota trzymanych naraz i urządzenia powtarzającego raport HID, czego autor w kalibracji raczej nie zrobi; poprawka to mapa VK→usage i księgowanie przy każdym scaleniu, czyli więcej złożoności niż bezpośrednia korekta (reguła odrzucania `low`).
  - `[low]` `[patch]` Quick: `InputHub.OnRawInput` po nieudanym parsowaniu ciała wracał przed `_target`, więc tryb główny tracił korelację `source` dla takiego urządzenia i dostawał nowy wpis `inputHubError`, choć ticket wymaga niezmienionego zachowania `run` — parsowanie nieudane podaje teraz celowi nieznane wartości (klawiatura `VirtualKey: 0`, `Scan: 0`, `Down: false`; HID `Usage: 0`) bez `Report`, a `InputCalibration.OnRawKeyboard` ignoruje key-down z `VirtualKey == 0`; test `Raw_keyboard_event_without_vk_produces_no_entry`.
  - `[low]` `[reject]` Quick: notatka o weryfikacji mówi „616 testów, `InputCalibrationTests` 17”, a drzewo ma 19 testów w tej klasie i 624 w całości — wpis jest append-only i późniejsze evidence (624/624 po poprawkach przeglądu) go koryguje; poprawka polegałaby na edycji planu, czego triage nie robi.

## Design Notes

Jednostką wpisu jest jedno naciśnięcie przycisku, nie jedno zdarzenie: przycisk pilota daje raport HID (usage), hook (vk, scan, `injected`) i `WM_INPUT` klawiatury dongla, więc kalibracja scala zdarzenia jednej sygnatury w oknie ±50 ms (ten sam próg co `InputPolicy.CorrelationWindowMs`) i domyka wpis timerem `flushAt` (50 ms) — bez zgadywania kolejności źródeł. `llBeforeRawMs = rawTime − hookTime`: dodatnie znaczy hook przed `WM_INPUT`, ujemne odwrotnie, `null` gdy jednego ze źródeł nie było; to właśnie „kolejność zdarzenia hooka i `WM_INPUT`”. Raport HID z `usage == 0` to zwolnienie przycisku (konwencja HID Consumer) i nie tworzy wpisu; powtórki przy trzymanym `usage`/VK są deduplikowane, a upuszczenie klawisza zwalnia oba znaczniki.

Filtr jest po sygnaturze urządzenia, nie po VK: autor musi zobaczyć także przyciski jeszcze nieobecne w mapowaniu, a AD-13 zabrania logowania klawiszy, których pilot nie może wysłać. Dlatego hook bez zdarzenia urządzenia `remote` w oknie jest tylko buforowany w pamięci i nigdy nie trafia do logu. Wpisy mają stały kształt (8 kluczy, brakujące jako `null`), bo autor porównuje kolumny między naciśnięciami, a `hasVk` jest wprost kryterium wyboru Klawiatury (epic: tylko przyciski z VK są kandydatami).

Kalibracja dzieli wątek i hook ze story 2 (jeden hub, jeden zestaw źródeł), więc nie duplikuje Win32; `InputPolicy` zostaje nietknięta poza interfejsem, bo zmiana w niej groziłaby Home i połknięciu. App czeka na `Completed` synchronicznie z ograniczeniem, bo w tym trybie nie ma okna ani pętli UI, a martwy hub nie może zawiesić procesu; Esc działa tylko przez hook (klawisz fizyczny), więc na ALDER dowodem jest przebieg 60 s, a Esc pokrywa test Core i odbiór na UM760.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` — zielone, w tym nowe `InputCalibrationTests`, `LogRawInputSinkTests`, `RawInputBodyTests` oraz zaktualizowane `StartDecisionTests`, `ExitCodeTests`, `InputPolicyTests` (baseline 583).
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń (kontrakty i schematy bez zmian: `rawInput` nie jest kontraktem).
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true`, potem `MediaStation.exe --calibrate-input` z `tools/samples/config` — proces żyje 60 s, log: `start` z `args`, zero `warn`/`error`, ostatni wpis `stop {exitCode: 0}`, kod procesu 0; `modeUnsupported` nie występuje.
- Ten sam build, tryb główny z `tools/samples/config` — bez regresji story 2: `snapshotPublished`, `stop {exitCode: 0}`, zero `warn`/`error`.

## Auto Run Result

**Status:** built.

**Summary:** `--calibrate-input` przestał kończyć się `modeUnsupported` (mutex bez zmian) i uruchamia sam InputHub ze story 2 z czystą `InputCalibration` z Core: bez mapowania i bez połknięcia, 60 s albo fizyczny Esc, z jednym wpisem `rawInput {signature, vk, scan, usagePage, usage, injected, hasVk, llBeforeRawMs}` na przycisk urządzenia `source: remote`, filtrem po sygnaturze (zwykła klawiatura i jej litery nigdy nie trafiają do logu) i kodem 0. Hub dostał wspólny kontrakt `IInputTarget`, czyta ciało raportu Raw Input (`RawInputBody`, bez Win32) i uzbraja timer terminu także po `WM_INPUT` i na starcie; `InputPolicy` zachowuje zachowanie, a wybór kodu wyjścia trybu (`Ok`/`ConfigInvalid`) jest decyzją w Core.

**Files changed:**
- `src/MediaStation.Core/Cli/StartDecision.cs` — nowa decyzja `CalibrateInput` i `CalibrationExit` (Ok/ConfigInvalid).
- `src/MediaStation.Core/Cli/ExitCode.cs`, `CliArgs.cs` — kody `ConfigInvalid = 6`, `InputHubFailed = 7`; dokumentacja trybu.
- `src/MediaStation.Core/Input/InputTarget.cs` — `IInputTarget` + `DeviceSignature` (jedna definicja dopasowania sygnatury).
- `src/MediaStation.Core/Input/InputPolicy.cs` — implementuje `IInputTarget`; `KeyStroke.Scan` i surowe pola `HidConsumerReport`/`RawKeyboardInput` (bez zmian zachowania).
- `src/MediaStation.Core/Input/{InputCalibration,RawInputEntry,LogRawInputSink}.cs` — scalanie naciśnięcia w oknie 50 ms, filtr `remote`, Esc/60 s, stały kształt wpisu `rawInput`.
- `src/MediaStation.Core/Ports/Ports.cs` — port `IRawInputSink`.
- `src/MediaStation.App/Platform/Input/InputHub.cs` — cel jako `IInputTarget`, parsowanie ciała raportu, timer terminu na starcie i po `WM_INPUT`.
- `src/MediaStation.App/Platform/Input/RawInputBody.cs` — odczyt `RAWKEYBOARD` i usage HID Consumer bez Win32 (linkowany do testów).
- `src/MediaStation.App/CompositionRoot.cs` — `LoadConfig` wspólny dla obu trybów i `StartCalibration` (bez okna, ograniczone oczekiwanie, kody 0/6/7).
- `tests/MediaStation.Core.Tests/{InputCalibrationTests,LogRawInputSinkTests,RawInputBodyTests}.cs` + `csproj` — wiersze macierzy, kształt wpisu, parsowanie ciała (22 + 5 + 9 testów).
- `tests/MediaStation.Core.Tests/{StartDecisionTests,ExitCodeTests,InputPolicyTests,InputClockTests}.cs` — start trybu kalibracji, pinezka kodów, mapowanie `"0x..": "keyboard"` → `action {kind: keyboard}`.

**Review findings:** 5 (medium 1, low 4, false 0, maybe-false 0). Patches applied: 3 (medium 1, low 2 — warunek `Injected` przy dopinaniu hooka, domknięcie wpisu przy końcu trybu, zachowanie korelacji `source` przy nieudanym parsowaniu ciała). Deferred: 0. Rejected: 2 `low` — czyszczenie `_heldUsages` przy zwolnieniu jednego z dwóch trzymanych przycisków pilota (nieosiągalne w codziennym użyciu kalibracji, poprawka to księgowanie VK→usage, więcej niż bezpośrednia korekta) oraz nieaktualne „616/17” w append-only notatce (koryguje je późniejsze evidence 624/624; poprawka edytowałaby plan).

**Follow-up review recommended:** false — pierwszy przebieg, patchowane: medium 1, low 2 (próg dla follow-upu to patchowane `high` albo co najmniej dwa `medium`).

**Verification:** `dotnet test MediaStation.slnx -c Release` — 624/624 passed, 0 failed (baseline 583). `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe --calibrate-input` na configu użytkownika (`%LOCALAPPDATA%\MediaStation\config`, pliki nietknięte) — 60,3 s, kod 0, log `start {args:["--calibrate-input"]}` → `stop {exitCode: 0}`, zero `warn`/`error`, zero `modeUnsupported`, zero `rawInput`. Tryb główny na tym samym buildzie — `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, kod 0, zero `warn`/`error`. Wcześniejsze przebiegi tej historii (subagent): żywa instancja → kod 3 `instanceRejected {mode: "calibrateInput"}`, brak `input.json` → kod 6 `configError {file: "input.json", reason: "brak pliku"}`, zero `rawInput`.

**Residual risks / checks for UM760 (whole-epic acceptance):** realny pilot na donglu USB 2.4G (HID Consumer + wstrzyknięty VK + `WM_INPUT` klawiatury dongla) daje dokładnie jeden `rawInput` na naciśnięcie z `hasVk: true` i sensownym `llBeforeRawMs`; fizyczny Esc kończy tryb przed 60 s; litery i cyfry pisane na zwykłej klawiaturze w trakcie kalibracji nie tworzą żadnego wpisu `rawInput`; przycisk bez VK (`hasVk: false`) nie jest kandydatem na Klawiatura, a wybrany przycisk z VK wpisany do `input.json` jako `keyboard` daje w trybie głównym `action {kind: keyboard}` i trafia też do `tools/samples/config/input.json`. Na ALDER wstrzyknięte wejście nie dociera do hooka LL (ustalenie story 2), więc te punkty są nieosiągalne automatem.
