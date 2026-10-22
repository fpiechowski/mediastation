---
title: 'Dowolna klawiatura i repeat strzałek'
type: 'feature'
ticket: '4'
created: '2026-10-22'
status: done
baseline_revision: 'e07b9bad9465a68f3bfa3c60a8b532e6ac8e2a7d'
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
      AD-8 („Jedno naciśnięcie daje jedną Action”, „InputHub jest właścicielem timingu”) i checklista odbioru epiku („dokładnie jeden wpis `action`” dla strzałek) nie opisują, że trzymana strzałka daje `press` + `repeat` ani że repeat liczy `InputPolicy`.
    evidence: |-
      Zweryfikowane w review (2 przebiegi): `architecture-mediastation.md:189,191` i `epic-input.md:70` nie mają reguły repeat, a kod emituje `press` + N×`repeat` z `InputPolicy.CollectRepeats`. AD-8 już wymienia `phase: repeat` w kontrakcie Actions (`:188`), a dopisanie reguły to pass dokumentacyjny epiki — wzorzec `clipHwnd` w `epic-input.md:61`. Wymaga wpisu w notatce odbioru epiku, żeby operator nie odczytał checklisty jako porażki przy trzymanej strzałce.
    location: >-
      _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md:189,191
    severity: low
  - summary: >-
      Sygnatura zawierająca `*`, ale niebędąca dokładnie `*` (np. `VID_1915*`), przechodzi walidację i nigdy nie dopasowuje ścieżki Raw Input, więc urządzenie wpada w fallback `*` albo `keyboard`.
    evidence: |-
      `DeviceSignature.Matches` specjalnie traktuje wyłącznie `InputDevice.WildcardSignature` i poza tym używa `Contains`, a ścieżka Raw Input nigdy nie zawiera `*`; walidacja wymaga tylko niepustej sygnatury. Zachowanie samo w sobie jest sprzed tej story (nowe jest tylko znaczenie `*`), a naprawa to reguła walidacji odrzucająca `*` w sygnaturze innej niż dokładnie `*` — decyzja planistyczna, nie poprawka tej zmiany.
    location: >-
      src/MediaStation.Core/Input/InputTarget.cs:42
    severity: low
---

<intent-contract>

## Intent

**Problem:** zwykła klawiatura bez własnego wpisu w `input.json` nie daje żadnej Action, a trzymana strzałka nawiguje tylko jednym naciśnięciem, bo polityka połyka autorepeat systemu i nie zna `phase: repeat`. UX wymaga przewijania wierszy trzymaną strzałką (EXPERIENCE → Interaction Primitives).

**Approach:** urządzenie o sygnaturze `*` (`source: keyboard`) jako fallback dopasowania ścieżki Raw Input (konkretny wpis zawsze wygrywa) oraz repeat w `InputPolicy`: po `press` strzałki polityka planuje `repeat` istniejącym szwem `NextDeadline`/`OnTimer` po `timing.repeatDelayMs` (400) i `timing.repeatIntervalMs` (120).

## Boundaries & Constraints

**Always:** `repeat` tylko dla `Up|Down|Left|Right`; jedno naciśnięcie = jedno `press`, autorepeat systemu bez Action; key-up kończy repeat; czas tylko przez wstrzyknięty `TimeProvider` (AD-14); decyzje w Core, App tylko uzbraja timer; `source` z korelacji bez zmian; `*` nigdy nie przesłania urządzenia konkretnego.

**Never:** `repeat` dla OK, Back, Home, Keyboard; repeat pada (story 6); routing `SetInputRouting` (story 5); nowy timer lub wątek w App; zmiany `ContractJson`, `MessageTypes`, `web/**`; zapis `input.json`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior | Error Handling |
|----------|--------------|---------------------------|----------------|
| Trzymana strzałka | key-down `0x26`, timer po 400 ms, potem co 120 ms | jedno `press`, potem `repeat` co interwał, koniec na key-up | brak błędu |
| Trzymany OK/Back/Home/Keyboard | key-down `0x0D`/`0xA6`/`0xAC`/`0x71` + upływ czasu | jedna Action (`ok`: `press` lub `long`), zero `repeat` | brak błędu |
| Autorepeat systemu | drugi key-down trzymanego VK | brak Action, bez przesunięcia siatki | brak błędu |
| Spóźniony timer | `now` minął dwa terminy | jeden `repeat`, siatka na najnowszym terminie | brak błędu |
| Klawisz tylko w `*` | key-down `0x71` z klawiatury bez wpisu | Action `keyboard`, `source: keyboard`, połknięty | brak błędu |
| Pilot obok `*` | `WM_INPUT` dongla, `*` pierwszy w configu | `source: remote` (konkretny wpis wygrywa) | brak błędu |
| Zły config | dwa `*`, `*` z `source: remote`, próg repeat ≤ 0 | błąd na właściwej ścieżce, brak `Config` | `configError` |

</intent-contract>

## Code Map

- `Core/Config/InputConfig.cs:17-21` — `InputTiming` dostaje `RepeatDelayMs` 400 i `RepeatIntervalMs` 120; stała `InputDevice.WildcardSignature = "*"`.
- `Core/Input/InputTarget.cs:33-40` — `DeviceSignature.Matches`: `*` pasuje do każdej niepustej ścieżki; jedna definicja dla polityki i kalibracji (ta filtruje `remote`, więc `*` tam nie wchodzi).
- `Core/Input/InputPolicy.cs` — `_held` (61) na `Dictionary<int, HeldKey>` (record: `Kind`, `Source`, `DownTimeMs`, `NextRepeatAt`, `RepeatIndex`, `RepeatEndsAt`); `NextDeadline` (101) = min z `PendingLong` i terminów repeat; `OnKey` (107-153): key-down strzałki daje `press` + siatkę, autorepeat bez zmian, key-up kończy siatkę, long jak dziś; `OnTimer` (181-195): long jak dziś + jeden `repeat` na due klucz z przeskokiem siatki, a po `now >= RepeatEndsAt` czyści siatkę bez emisji; `MatchDevice` (225-241) dwuprzebiegowe (konkretne przed `*`), a `OnHidConsumer` (155-164) bierze tylko wpisy konkretne (wildcard nie może oznaczyć pilota jako `keyboard`). `tsMono` repeat = `DownTimeMs + RepeatDelayMs + n*RepeatIntervalMs` (jak `long`). `MaxRepeatHoldMs = 60_000` — liveness guard opisany w Design Notes.
- `Core/Config/ConfigValidation.cs:299-360` — unikalność sygnatur pokrywa „najwyżej jedno `*`”; w łańcuchu `else if` reguła `*` ⇒ `source: keyboard`; `CheckPositive` na `$.timing.repeatDelayMs` i `$.timing.repeatIntervalMs`.
- `tools/samples/config/input.json` — drugie urządzenie `*` (`source: keyboard`): `0x0D` ok, `0x1B` back, `0x24` home, `0x25`–`0x28` strzałki, `0x71` keyboard; `timing` z 400/120 (kindy współdzielone z donglem są dozwolone).
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` — helper `Config`/`Create` z progami repeat; testy siatki, braku repeat innych kindów, autorepeatu, spóźnionego timera, wildcardu (`FakeTimeProvider`).
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs:175-186,601-631,927-953` — sample (2 urządzenia, 8 kluczy `*`, timing 400/120), walidacja repeat i `*`, schemat `timing` z dwoma polami `integer`.
- Reuse: `FakeTimeProvider`, `RecordingLog`, `LogActionSink`, `InputTimerDelay`. Nie ruszać: `InputCalibration`, `InputHub`, `CompositionRoot`, `ContractJson`, `MessageTypes`, `web/**`.

## Tasks & Acceptance

**Execution:**
- [x] `Core/Config/{InputConfig,ConfigValidation}.cs` — pola `timing`, stała `*`, reguły walidacji — `verify` (walidacja).
- [x] `Core/Input/{InputTarget,InputPolicy}.cs` — dopasowanie `*`, repeat przez `NextDeadline`/`OnTimer` i liveness guard — `verify` (repeat, `source: keyboard`).
- [x] `tools/samples/config/input.json` — sample wildcardu i progów repeat — wzór dla autora.
- [x] `tests/MediaStation.Core.Tests/{InputPolicyTests,ConfigValidationTests}.cs` — wiersze macierzy, liveness guard, walidacja, schemat — `verify`.

**Acceptance Criteria:**
- Given `*` mapujące `0x71` na `keyboard` i brak wpisu dla klawiatury, when key-down `0x71`, then połknięcie i jedna Action `{keyboard, press, source: keyboard}`.
- Given `*` przed wpisem dongla i `WM_INPUT` dongla w oknie 50 ms, when key-down zmapowanego klawisza, then Action ma `source: remote`.
- Given trzymana strzałka, when minie `repeatDelayMs`, then `repeat` co `repeatIntervalMs` aż do key-up, po którym timer nie daje Action.
- Given trzymana strzałka bez key-up (blokada pulpitu, UAC, odpięta klawiatura), when trzymanie osiągnie `MaxRepeatHoldMs`, then polityka nie emituje już `repeat`, a `NextDeadline` jest `null` (klawisz zostaje połknięty do key-up, jak dziś).
- Given raport HID Consumer urządzenia spoza configu przy `*` w configu, when key-down wstrzykniętego VK pilota w oknie 50 ms, then Action ma `source: remote`, bo korelacja `source` nie używa wildcardu.
- Given trzymany OK w `shell`, Back, Home lub Keyboard, when minie `repeatDelayMs` + dwa interwały, then zero `phase: repeat`, a OK zachowuje `press`/`long`.
- Given dwa `*` albo `*` z `source: remote`, when walidacja, then jeden błąd na właściwej ścieżce i `Config` `null`.
- Given `timing` bez nowych pól, when walidacja, then 400 i 120.

## Implementation Notes

- **Przebieg 1 wycofany (review, 2026-10-22):** notatki i evidence poniżej opisują pierwszą implementację, która została wycofana do `e07b9ba` po findingu `bad_plan` (repeat bez liveness guardu); kod jest re-derived według zmienionego planu, a bieżące evidence jest w dopisanych niżej notatkach.

- **Core — timing i wildcard:** `InputTiming` dostała `RepeatDelayMs` 400 i `RepeatIntervalMs` 120 (opcjonalne, poza `required` schematu), a `InputDevice.WildcardSignature = "*"` jest jedną stałą dla polityki, walidacji i sample'a. `DeviceSignature.Matches` traktuje `*` jak dopasowanie każdej niepustej ścieżki, więc polityka i kalibracja mają dalej jedną definicję (kalibracja filtruje `source: remote`, więc wildcard do niej nie wchodzi — AD-13).
- **Core — repeat:** `_held` to `Dictionary<int, HeldKey>` (`Kind`, `Source`, `DownTimeMs`, `NextRepeatAt`, `RepeatIndex`). Key-down strzałki daje `press` i termin `GetUtcNow() + repeatDelayMs`; key-up usuwa wpis i kończy siatkę; autorepeat systemu (`_held.ContainsKey`) jest połykany bez Actions i bez przesunięcia terminu. `NextDeadline` = min z `PendingLong` i terminów repeat, więc App (`InputHub.ArmTimer`) bez zmian. `OnTimer` obsługuje long jak dotąd plus jeden `repeat` na każdy due klucz; `tsMono` = `DownTimeMs + repeatDelayMs + n*repeatIntervalMs` z `n = 0` dla pierwszego repeat (ta sama jednostka hooka co `long`). Spóźniony timer przeskakuje siatkę (`steps` z różnicy ticków na `repeatIntervalMs`), emituje jeden repeat z najnowszego minionego terminu i ustawia następny termin na siatce — bez serii po zacięciu wątku. Repeat mają wyłącznie `Up|Down|Left|Right`; progi `<= 0` nie uzbrajają siatki (walidacja odrzuca je wcześniej).
- **Core — walidacja:** unikalność sygnatur (bez wielkości liter) pokrywa „najwyżej jedno `*`”; w łańcuchu `else if` reguła `*` ⇒ `source: keyboard` (błąd na `$.devices[i].source`), bo wildcard z `remote` logowałby w kalibracji surowe kody zwykłej klawiatury; `CheckPositive` na `$.timing.repeatDelayMs` i `$.timing.repeatIntervalMs` obok `longPressMs`.
- **Sample:** `tools/samples/config/input.json` ma drugie urządzenie `*` (`source: keyboard`) z `0x0D` ok, `0x1B` back, `0x24` home, `0x25`–`0x28` strzałkami i `0x71` keyboard oraz `timing` 600/400/120. Kindy współdzielone z donglem są zgodne, więc walidacja VK przechodzi.
- **Testy:** `InputPolicyTests` — helper `Config`/`Create` z progami repeat, wiersze macierzy: siatka repeat (399/400/520 ms, koniec na key-up), progi z configu (300/80), spóźniony timer (jeden repeat, siatka na najnowszym terminie), brak repeat dla Back/Home/Keyboard i dla OK w `shell` (`long` i `press` zachowane), autorepeat bez przesunięcia siatki, `source` repeat z key-down, wildcard (`0x71` → `keyboard`, konkret wygrywa z `*` w obu kolejnościach, `DeviceSignature.Matches` wprost). `ConfigValidationTests` — sample (2 urządzenia, 8 kluczy `*`, 400/120), progi repeat > 0 i typu `integer`, domyślne 400/120 bez `timing`, `*` z `keyboard` OK, `*` z `remote`/`gamepad` i dwa `*` odrzucone, schemat `timing` z dwoma nowymi polami `integer`.
- **Weryfikacja na ALDER (Windows 11, B760, .NET SDK 10.0.402):**
  - `dotnet test MediaStation.slnx -c Release` — 654/654 zielone (baseline 624; +20 przypadków `InputPolicyTests`, +10 `ConfigValidationTests`).
  - `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów.
  - `dotnet run --project tools/MediaStation.SchemaExport` — `input-config.schema.json` z `timing` poza `required` (nadal `devices`, `gamepadHome`) i `repeatDelayMs`/`repeatIntervalMs` typu `integer`; `cd web && npm run build` (contracts, typecheck, Vite) zielone.
  - `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z configiem `tools/samples/config` (z `*` i `timing` 400/120): log `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, kod procesu 0, zero `configError` i zero `warn`/`error`; config użytkownika (`%LOCALAPPDATA%\MediaStation\config`) odtworzony i zweryfikowany po przebiegu.
- **Sprzętowo N/A:** trzymana strzałka i pilot na donglu są nieosiągalne automatem na ALDER (wstrzyknięte wejście nie dociera do hooka LL — ustalenie story 2), więc siatkę repeat, key-up i brak repeat innych kindów dowodzą testy na `FakeTimeProvider`, a fizyczny przebieg (przewijanie trzymaną strzałką pilota i klawiatury, `source: remote` obok `*`) idzie do odbioru epiku na UM760.
- **Ryzyko rezydualne:** przy `*` w configu dowolny raport HID Consumer albo `WM_INPUT` klawiatury w oknie 50 ms przed klawiszem ustawia korelację na `keyboard`, więc pilot mógłby dostać `source: keyboard`, gdy użytkownik w tej samej chwili pisze na klawiaturze; `source` służy dziś tylko logowaniu (decyzje Orchestratora go nie czytają), a kolejność wpisów w configu nie ma znaczenia dzięki dwuprzebiegowemu `MatchDevice`.
- **Niezależna weryfikacja w sesji głównej (ALDER, ten sam build Release):** `dotnet build` 0 ostrzeżeń, `dotnet test` 654/654 zielone (0 skipped), `SchemaExport` + `cd web && npm run build` zielone, a `MediaStation.exe` z `tools/samples/config` (sample z `*`) dał `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}` (kod 0, zero `configError`/`warn`/`error`). Config użytkownika (`%LOCALAPPDATA%\MediaStation\config`) odtworzony bajt w bajt (SHA-256 identyczne dla trzech plików).
- **Re-derivation po amendmencie (przebieg 2):** `HeldKey` ma dodatkowo `RepeatEndsAt` liczone od key-down jako `GetUtcNow() + MaxRepeatHoldMs`; `CollectRepeats` po przekroczeniu granicy czyści `NextRepeatAt`/`RepeatEndsAt` bez emisji, a wpis zostaje w `_held` do key-up (klawisz dalej połknięty). `MatchDevice` ma parametr `includeWildcard`: `OnRawKeyboard` go używa (fallback klawiatury), `OnHidConsumer` nie, więc raport HID Consumer spoza configu nie może oznaczyć pilota jako `keyboard`. Kolejność repeatów w jednym `OnTimer` jest deterministyczna (najwcześniejszy termin, przy remisie VK). Nowe testy: `Missing_key_up_stops_repeats_after_max_hold_and_keeps_the_key_swallowed`, `Wildcard_is_not_the_consumer_correlation_source`, `Non_positive_repeat_thresholds_arm_no_grid`.
- **Ponowna weryfikacja (ALDER, ten sam build Release):** `dotnet build` 0 ostrzeżeń, `dotnet test` 654/654 zielone (0 skipped; +19 przypadków `InputPolicyTests`, +11 `ConfigValidationTests`), `SchemaExport` z `repeatDelayMs`/`repeatIntervalMs` typu `integer`, `cd web && npm run build` zielone, `dotnet publish` + `MediaStation.exe` z `tools/samples/config`: `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, kod 0, zero `configError`/`warn`/`error`; config użytkownika odtworzony (SHA-256 trzech plików zgodne).

## Plan Change Log

### 2026-10-22 — bad_plan loopback (review pass 1)
- **Trigger:** `[medium]` finding — trzymana strzałka powtarza się bez końca, gdy key-up zginie (blokada pulpitu/UAC, odpięta klawiatura, hook usunięty po `LowLevelHooksTimeout`), bo siatka repeat sama się przeharmonogramowuje (`OnTimer` → `ArmTimer`), a `_held` czyści tylko key-up.
- **Amended:** Code Map `InputPolicy` (liveness guard `MaxRepeatHoldMs` + reguła, że `OnHidConsumer` używa tylko wpisów konkretnych), Design Notes (dwa nowe akapity: korelacja `source` bez wildcardu i ograniczenie czasu trzymania), Tasks & Acceptance (dwie nowe AC, checkboxy zresetowane).
- **Known-bad state avoided:** `repeat` co 120 ms do końca procesu po zgubionym key-upie (nieograniczony zapis do logu/kolejki, w Shellu samoczynne przewijanie) oraz `source: keyboard` dla pilota po raporcie HID Consumer spoza configu przy `*`.
- **KEEP:** `*` jako fallback dopasowania ścieżki z regułą „konkretny wpis wygrywa” i walidacją (`source: keyboard`, unikalność sygnatur = najwyżej jedno `*`); siatka repeat (`repeatDelayMs`/`repeatIntervalMs`, `tsMono = down + delay + n*interval`, jeden repeat na spóźniony timer z przeskokiem siatki, autorepeat bez przesunięcia, koniec na key-up); brak zmian w App, `InputCalibration`, kontraktach i `web/**`; sample `input.json` z `*` i `timing` 400/120; struktura testów na `FakeTimeProvider`.

## Review Triage Log

### 2026-10-22 — Review pass (quick)
- verdicts: 4 findings — high 0, medium 1, low 3, false 0, maybe-false 0
- findings:
  - `[medium]` `[bad_plan]` Trzymana strzałka repeatuje bez końca po zgubionym key-upie (brak liveness guardu) — potwierdzone: `_held` czyści tylko key-up (`InputPolicy.cs:174`), `NextDeadline`/`OnTimer` przeharmonogramowują siatkę bez końca (`:117-121,254-258`), a `InputHub.ArmTimer` po każdym `OnTimer` uzbraja timer ponownie (`InputHub.cs:418,439-452`); key-up ginie na secure desktopie (Win+L/UAC), przy odpięciu klawiatury i po usunięciu hooka (`LowLevelHooksTimeout`, AD-8). Plan nie rozstrzygał tej reguły → amended Code Map/Design Notes/Tasks & Acceptance o `MaxRepeatHoldMs` (60 s) i AC bez key-up; kod wycofany do `e07b9ba` i re-derived.
  - `[low]` `[patch]` Wildcard pozwala raportom HID Consumer spoza configu nadpisać korelację dongla i oznaczyć pilota jako `keyboard`; test `Wildcard_device_is_the_correlation_source...` przechodzi też bez wildcardu, więc nie dowodzi niczego — potwierdzone: `MatchDevice` drugi przebieg pasuje do każdej ścieżki także dla `OnHidConsumer` (`InputPolicy.cs:189-197,309-333`), a `ResolveSource` dla wstrzykniętego VK preferuje `_lastConsumer` (`:287`); szkoda ograniczona do logowanego `source` i wymaga raportu innego urządzenia ≤50 ms przed VK. Naprawa weszła do tej samej poprawki planu (korelacja `source` tylko z wpisów konkretnych) + nowy test; re-derivation w tym samym loopbacku.
  - `[low]` `[defer]` AD-8 („Jedno naciśnięcie daje jedną Action”) i checklista odbioru epiku („dokładnie jeden wpis `action`” dla strzałek) nie mówią, że trzymana strzałka daje `press` + `repeat` — finding dotyczy dokumentacji/odbioru, nie kodu: AD-8 już wymienia `phase: repeat` w kontrakcie Actions (`architecture-mediastation.md:188`), a dopisanie reguły repeat do AD-8 to pass dokumentacyjny epiki (`epic-input.md:61` ma ten sam wzorzec dla `clipHwnd`), poza zakresem plików tej story. Odnotowane w ryzykach rezydualnych `## Auto Run Result` do notatki odbioru epiku.
  - `[low]` `[reject]` Stare numery linii w Code Map i odwołaniach do testów — nieaktualne po zmianie plików; poprawka polegałaby na edycji tego planu, czego triage nie robi, a plan i tak jest re-derived po amendmencie (numery zostaną odświeżone w notatkach implementacji).

### 2026-10-22 — Review pass (quick, po re-derivation)
- verdicts: 5 findings — high 0, medium 0, low 5, false 0, maybe-false 0
- findings:
  - `[low]` `[reject]` `repeatDelayMs` ≥ `MaxRepeatHoldMs` przechodzi walidację, ale repeat nigdy nie wystartuje — potwierdzone: `ConfigValidation.cs:364-367` przyjmuje każdą wartość > 0, a `InputPolicy.NewHeldKey`/`CollectRepeats` (`:241-256,298-302,313-314`) kończy siatkę po 60 s od key-down, więc próg ≥ 60 s daje zero repeatów. Stan osiągalny tylko ręcznie wpisanym 70-sekundowym opóźnieniem; skutek (brak repeatów) jest widoczny natychmiast, a naprawa to reguła walidacji wiążąca config ze stałą guardu — dla nierealistycznej wartości, ten sam precedens co odrzucony w story 1 brak górnej granicy `longPressMs`; sample i odbiór używają 400/120.
  - `[low]` `[defer]` Sygnatura zawierająca `*`, ale niebędąca dokładnie `*` (np. `VID_1915*`), przechodzi walidację i nigdy nie dopasowuje ścieżki — potwierdzone: `DeviceSignature.Matches` (`InputTarget.cs:42-46`) specjalnie traktuje tylko `InputDevice.WildcardSignature`, poza tym używa `Contains`, a ścieżka Raw Input nigdy nie zawiera `*`. Zachowanie sprzed tej story (zmiana nadaje tylko znaczenie `*`), a naprawa to nowa reguła walidacji — zapisane w `deferred`.
  - `[low]` `[defer]` `carried` — AD-8 (`:189,191`) i checklista odbioru epiku (`epic-input.md:70`) nadal mówią „Jedno naciśnięcie daje jedną Action”/„dokładnie jeden wpis `action`”, a trzymana strzałka daje `press` + N×`repeat`, liczony w `InputPolicy`; ten sam claim i lokalizacja co wiersz z pass 1, kod bez zmian. Poprawka edytuje dokumentację architektury i notatkę odbioru epiki, więc wpis poszedł do `deferred` (poprzednio tylko proza w triage logu).
  - `[low]` `[patch]` Luki w testach (komentarz o dwóch terminach siatki, brak testu kolejności dwóch due kluczy) — potwierdzone: `InputPolicyTests.cs:360-366` przy `now = 650 ms` mija trzy terminy (400/520/640), a `tsMono` 1640 to trzeci; sort w `InputPolicy.CollectRepeats` (`:283-288`) nie był wołany z dwoma due kluczami. Naprawa: komentarz poprawiony na trzy terminy, dodany test `Two_due_keys_repeat_in_deadline_order` (Up/1400 przed Left/1450, następny termin 520); `InputPolicyTests` 42/42, pełny `dotnet test` 655/655.
  - `[low]` `[patch]` `deferred: []` puste, choć triage kierował finding do defer, a wiersz wskazywał nieistniejący `## Auto Run Result` — potwierdzone w frontmatterze i triage logu. Naprawa: `deferred` ma teraz dwa wpisy (AD-8/odbiór epiku, `*` w sygnaturze), a `## Auto Run Result` jest pisany w Finalize; frontmatter sparsowany jako YAML (lista 2 elementów).

## Design Notes

Repeat należy do polityki, bo decyzja o fazie już w niej jest (`long` od story 1), a `InputHub` uzbraja timer na `NextDeadline` po każdym zdarzeniu i po `OnTimer` (`InputHub.cs:394,418,439`) — App bez zmian. Termin liczony przez `TimeProvider`, a `tsMono` z zegara hooka (`down + delay + n*interval`) jak przy `long`; spóźniony `WM_TIMER` daje jeden `repeat` i przeskakuje siatkę, bo seria po zacięciu wątku byłaby widoczna jako skok fokusu.

`*` to reguła dopasowania ścieżki: pasuje do każdej niepustej ścieżki, przegrywa z każdym wpisem konkretnym niezależnie od kolejności, a scalanie kluczy zostaje per VK. Wymóg `source: keyboard` broni AD-13 — kalibracja filtruje `remote`, więc wildcard z `remote` logowałby surowe kody zwykłej klawiatury. „Najwyżej jedno `*`” pokrywa istniejąca unikalność sygnatur. `*` nie uczestniczy w korelacji `source` z raportu HID Consumer (`OnHidConsumer` bierze tylko wpisy konkretne): inaczej raport media-klawisza albo headsets w oknie 50 ms oznaczyłby pilota jako `keyboard`, a odbiór epiku wymaga `source: remote` dla Home i Back.

Key-up nie zawsze dociera: hook LL nie widzi wejścia na secure desktopie (blokada, UAC, Ctrl+Alt+Del), ginie przy odpięciu klawiatury, a Windows usuwa hook po `LowLevelHooksTimeout` (AD-8), czego hub nie obserwuje. Dlatego repeat jest ograniczony czasem trzymania: po `MaxRepeatHoldMs` (60 s) siatka jest czyszczona bez emisji, a wpis w `_held` zostaje do key-up (klawisz dalej połknięty, jak przed tą story). Bez tego guardu zgubiony key-up dawałby `repeat` co 120 ms do końca procesu i nieskończone przewijanie w Shellu.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` — zielone (baseline 624) z testami repeat, wildcardu i walidacji.
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń.
- `dotnet run --project tools/MediaStation.SchemaExport` — `input-config.schema.json` z `repeatDelayMs`/`repeatIntervalMs` typu `integer`, `timing` poza `required`; `cd web && npm run build` zielone.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` (sample z `*`) — `snapshotPublished` → `stop {exitCode: 0}`, zero `configError`, zero `warn`/`error`; config użytkownika odtworzony. Trzymana strzałka i pilot są nieosiągalne na ALDER (wstrzyknięte wejście nie dociera do hooka LL — ustalenie story 2), więc repeat dowodzą testy na `FakeTimeProvider`, a fizyczny sprzęt idzie do odbioru epiku na UM760.

## Auto Run Result

**Status:** built.

**Summary:** `input.json` dostał urządzenie z sygnaturą `*` (`source: keyboard`) jako fallback dopasowania ścieżki Raw Input — wpis konkretny zawsze wygrywa, wildcard nie bierze udziału w korelacji `source` z raportu HID Consumer, a walidacja wymaga dla niego `source: keyboard` i (przez istniejącą unikalność sygnatur) najwyżej jednego takiego urządzenia. `InputPolicy` generuje teraz `repeat` wyłącznie dla trzymanych strzałek: `press` na key-down, potem `repeat` po `timing.repeatDelayMs` (400) co `timing.repeatIntervalMs` (120) przez istniejący szew `NextDeadline`/`OnTimer` (App bez zmian), z pominięciem autorepeatu systemu, końcem na key-up, przeskokiem siatki przy spóźnionym timerze i liveness guardem `MaxRepeatHoldMs` (60 s) na wypadek zgubionego key-upa. OK, Back, Home i Keyboard zostają przy jednej Action.

**Files changed:**
- `src/MediaStation.Core/Config/InputConfig.cs` — `InputTiming.RepeatDelayMs` 400 i `RepeatIntervalMs` 120; stała `InputDevice.WildcardSignature = "*"`.
- `src/MediaStation.Core/Config/ConfigValidation.cs` — reguła `*` ⇒ `source: keyboard`, `CheckPositive` na `$.timing.repeatDelayMs`/`repeatIntervalMs`; „najwyżej jedno `*`” pokrywa unikalność sygnatur.
- `src/MediaStation.Core/Input/InputTarget.cs` — `DeviceSignature.Matches` traktuje `*` jak dopasowanie każdej niepustej ścieżki.
- `src/MediaStation.Core/Input/InputPolicy.cs` — `_held` na `Dictionary<int, HeldKey>` z siatką repeat i `RepeatEndsAt`; `NextDeadline`/`OnTimer` obsługują long i repeat; dwuprzebiegowe `MatchDevice` i `OnHidConsumer` bez wildcardu.
- `tools/samples/config/input.json` — drugie urządzenie `*` (Enter, Esc, Home, strzałki, F2) i `timing` 400/120.
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` — siatka repeat, brak repeat innych kindów, autorepeat, spóźniony timer, guard bez key-upa, kolejność dwóch due kluczy, wildcard i korelacja `source`.
- `tests/MediaStation.Core.Tests/ConfigValidationTests.cs` — sample, progi repeat, reguły `*`, schemat `timing` z trzema polami `integer`.

**Review findings:** pierwszy przebieg 4 findings (medium 1 → `bad_plan`: repeat bez liveness guardu; low 3), drugi przebieg 5 findings (low 5). Patches applied w finalnym przebiegu: 2 `low` (komentarz siatki + test kolejności due kluczy; wpis `deferred` i `Auto Run Result`). Deferred: 2 `low` — AD-8/checklista odbioru epiku nie opisują `press` + `repeat` (pass dokumentacyjny epiki), sygnatura zawierająca `*` poza dokładnie `*` nigdy nie dopasowuje ścieżki (zachowanie sprzed story, wymaga reguły walidacji). Rejected: 1 `low` + 1 `low` — `repeatDelayMs` ≥ `MaxRepeatHoldMs` daje zero repeatów tylko przy ręcznie wpisanym 60+ sekundowym opóźnieniu (skutek widoczny od razu, naprawa wiązałaby config ze stałą guardu; ten sam precedens co odrzucony brak górnej granicy `longPressMs` w story 1) oraz nieaktualne numery linii w Code Map (poprawka edytowałaby plan; odświeżone w notatkach implementacji).

**Follow-up review recommended:** false — pierwszy przebieg naprawił `medium` przez `bad_plan` (re-derivation), a w finalnym przebiegu patchowane były wyłącznie dwa `low` (progi: `high` albo dwa `medium`).

**Verification:** `dotnet test MediaStation.slnx -c Release` — 655/655 passed, 0 failed, 0 skipped (baseline 624). `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów. `dotnet run --project tools/MediaStation.SchemaExport` — `input-config.schema.json` z `timing` poza `required` i `repeatDelayMs`/`repeatIntervalMs` typu `integer`; `cd web && npm run build` (contracts, typecheck, Vite) zielone. `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + `MediaStation.exe` z `tools/samples/config` (z `*` i `timing` 400/120): `start` → `inputRouting` → `powerSignalsRegistered` → `snapshotPublished` → `stop {exitCode: 0}`, kod procesu 0, zero `configError`/`warn`/`error`; config użytkownika (`%LOCALAPPDATA%\MediaStation\config`) odtworzony bajt w bajt (SHA-256 trzech plików zgodne). Każdy wiersz macierzy ma przechodzący test: trzymana strzałka, brak repeat dla OK/Back/Home/Keyboard, autorepeat, spóźniony timer, klawisz tylko w `*`, pilot obok `*`, zły config; liveness guard i korelacja `source` bez wildcardu mają osobne testy.

**Residual risks / checks for UM760 (whole-epic acceptance):** fizyczne trzymanie strzałki pilota i klawiatury daje `press` + `repeat` co ~120 ms i kończy się na key-upie; pilot obok urządzenia `*` w configu daje `source: remote` dla Home i Back (korelacja z raportu HID Consumer ignoruje wildcard); po blokadzie pulpitu/UAC w trakcie trzymania klawisza repeat ustaje najpóźniej po 60 s, a klawisz zostaje połknięty do następnego key-upa. Notatka odbioru epiku powinna też wspomnieć, że trzymana strzałka daje `press` + `repeat` (checklista `epic-input.md:70` mówi o „dokładnie jednym wpisie `action`” dla naciśnięcia). Na ALDER wstrzyknięte wejście nie dociera do hooka LL, więc te punkty są nieosiągalne automatem.

