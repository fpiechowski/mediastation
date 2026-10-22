---
title: 'Polityka wejścia w Core: jedna Action i reguła long'
type: 'feature'
ticket: '1'
created: '2026-10-22'
status: done
baseline_revision: 'a51da3248077879e34801abb463219158821f237'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: ['oversized']
deferred: []
---

<intent-contract>

## Intent

**Problem:** `Action` jest dziś tylko kontraktem — nic w Core nie zamienia zdarzeń wejścia na Actions: brak decyzji o połknięciu, wyboru `kind` po VK, wyliczenia `source`, reguły „jedno naciśnięcie = jedna Action", reguły `long` dla OK i kolejki, a `input.json` nie ma progu long-press. Bez tego story 2 decydowałaby w adapterze Win32, a polityka byłaby nieprzetestowalna bez Windows.

**Approach:** Czysta `InputPolicy` w `MediaStation.Core.Input` zamienia klawisz z hooka LL, raport HID Consumer i `WM_INPUT` klawiatury na `InputDecision` (połknij + Actions). Połknięcie i `kind` idą z VK ze scalonego mapowania `input.json`, `source` z korelacji w oknie 50 ms, próg long-press przez szew `NextDeadline`/`OnTimer(now)` na wstrzykniętym `TimeProvider`. Config dostaje `timing.longPressMs` (600), a `ActionQueue` nieograniczoną kolejkę z nieblokującym zapisem.

## Boundaries & Constraints

**Always:** Core bez Win32, IO i wątków (AD-3); czas tylko przez wstrzyknięty `TimeProvider` (AD-14); decyzja o połknięciu i `kind` po samym VK ze scalonego mapowania wszystkich urządzeń, bo hook LL działa przed `WM_INPUT`, a połknięty klawisz może nie dojść do Raw Input; walidacja odrzuca ten sam VK z różnym `kind` w dwóch urządzeniach; `tsMono` z pola `time` zdarzenia hooka; nowa `timing` w schemacie `input-config.schema.json`; styl jak w `Settings`, `TimerScheduler` i `Contracts`.

**Never:** `Platform/Input`, hooki, Raw Input, XInput, `--calibrate-input` (3), `repeat` i `repeatDelayMs`/`repeatIntervalMs` (4), routing z `SetInputRouting` poza stałym `shell` (5), `SendKey`/`dwExtraInfo` (7), pointer/`ClipCursor` (8), logi `action`/`inputHubError` (2), zmiany w `Orchestrator`, `CommandRouter`, `CompositionRoot`, `web/**`.

## I/O & Edge-Case Matrix

| Scenario | Input / State | Expected Output / Behavior |
|----------|--------------|---------------------------|
| Zmapowany klawisz | key-down `0x0D`→`ok`, tryb `shell` | połknięcie + `Action(ok, press, source, tsMono)` |
| Autorepeat | drugi key-down bez key-up | połknięcie, zero Actions |
| Nieznany VK | key-down i key-up spoza mapowania | brak połknięcia i brak Actions |
| OK w `shell` przed progiem | key-up po 300 ms przy progu 600 | jedno `Action(ok, press)` dopiero na key-up |
| OK w `shell` po progu | `OnTimer` po 600 ms, potem key-up | jedno `Action(ok, long)`; key-up bez Action |
| OK w `app` | key-down `ok` | `Action(ok, press)` na key-down, `NextDeadline` `null` |
| Spóźniony timer | `OnTimer` po key-up albo po `long` | zero Actions, `NextDeadline` `null` |
| Home z pilota | wstrzyknięty `0xAC` po raporcie Consumer 30 ms wcześniej | `Action(home, press, remote, …)`; bez raportu i `WM_INPUT` → `keyboard` |
| Urządzenie z `WM_INPUT` | zwykły key-down, `WM_INPUT` dongla 20 ms wcześniej | `source: remote`; ścieżka dopasowana bez względu na wielkość liter |
| Kolejka bez czytelnika | 10 000 zapisów bez `TryDequeue` | żaden nie blokuje, nic nie ginie, kolejność FIFO |
| Walidacja progu | `timing.longPressMs` 0 lub −1; brak `timing`; `timing: {}` | błąd `$.timing.longPressMs`; brak sekcji i `{}` → 600 |
| Konflikt VK | dwa urządzenia: `0x0D`→`ok` i `0x0D`→`home` | błąd przy kluczu późniejszego urządzenia; ten sam `kind` w obu jest poprawny |

</intent-contract>

## Code Map

- `Core/Input/InputPolicy.cs` **(nowy)** — zdarzenia platformy bez Win32 (`enum KeyTransition`, `record KeyStroke(int VirtualKey, KeyTransition Transition, bool Injected, long TimeMs)`, `record HidConsumerReport(string DevicePath, long TimeMs)`, `record RawKeyboardInput(string DevicePath, long TimeMs)`; `TimeMs` = pole `time` zdarzenia, `DevicePath` = ścieżka Raw Input) oraz `sealed class InputPolicy` z `InputDecision(bool Swallow, IReadOnlyList<ActionMessage> Actions)`; ctor `(InputConfig config, TimeProvider time, InputMode mode = InputMode.Shell)`; `public const int CorrelationWindowMs = 50`; `OnKey(KeyStroke)`, `void OnHidConsumer(HidConsumerReport)`, `void OnRawKeyboard(RawKeyboardInput)`, `InputDecision OnTimer(DateTimeOffset now)`, `DateTimeOffset? NextDeadline`; stan: scalone `Dictionary<int, ActionKind>` (`TryAdd`), lista `(Signature, Source)`, `HashSet<int>` trzymanych VK, jeden `PendingLong` (kind, source, `down.TimeMs`, deadline), ostatni dopasowany consumer i ostatni dopasowany `WM_INPUT`; alias `ActionMessage = MediaStation.Core.Contracts.Action` (wzór `Orchestrator.cs`).
- `Core/Input/ActionQueue.cs` **(nowy)** — `ConcurrentQueue<ActionMessage>`: ctor `(Action? wake = null)`, `Enqueue` (nieograniczony, nigdy nie czeka i nie gubi), `TryDequeue`; `wake` wołany po zapisie na wątku wołającego, musi nie blokować (App da sygnał dla wątku ze story 2).
- `Core/Config/InputConfig.cs` — `InputConfig` dostaje `public InputTiming Timing { get; init; } = new();`; `InputTiming` to `sealed record` z `public int LongPressMs { get; init; } = 600` (wzór `Settings.Update`, bez `[JsonRequired]`).
- `Core/Config/ConfigValidation.cs` — `CheckInput`: `CheckPositive` na ścieżce `$.timing.longPressMs`; `Dictionary<int, (ActionKind, string)>` po urządzeniach: inny `kind` dla tego samego VK → błąd na kluczu późniejszego urządzenia (powód w stylu `kod VK „0x0D” ma już kind „ok” w $.devices[0].keys['0x0D']`); `required` schematu bez zmian.
- `tools/samples/config/input.json` — urządzenie dongla: `0x28`→`down`, `0x25`→`left`, `0x27`→`right`, `0xA6`→`back` obok `0x26`/`0x0D`/`0xAC`, plus `"timing": { "longPressMs": 600 }`.
- `tests/.../InputPolicyTests.cs` **(nowy)** — `FakeTimeProvider(T0)`, `InputConfig` budowany wprost (sygnatura `VID_1915&PID_1025`, `ActionSource.Remote`, mapa `0x25`–`0x28`/`0x0D`/`0xA6`/`0xAC`): każdy wiersz macierzy, `NextDeadline`, `OnTimer`.
- `tests/.../ActionQueueTests.cs` **(nowy)** — 10 000 zapisów bez czytelnika, FIFO, pusty `TryDequeue`, `wake` raz na zapis, `null` odrzucony.
- `tests/.../ConfigValidationTests.cs` — nowe przypadki `timing` i konfliktu VK (`0x0d` vs `0x0D` też konflikt); rozszerz `Input_schema_maps_keys_to_action_kinds` o opcjonalne `timing.longPressMs` typu `integer`.
- Reuse: `FakeTimeProvider`, `JsonErrorPath`, `GamepadButton`, `InputMode`, `ActionKind`, `ActionPhase`, `ActionSource`. Nie ruszać `MessageTypes.All`, `ContractJson` ani `web/**`.

## Tasks & Acceptance

**Execution:**
- [x] `Core/Input/InputPolicy.cs` — zdarzenia, `InputDecision`, mapowanie po VK, korelacja `source`, jedna Action na naciśnięcie, `press`/`long`, szew timera — AD-8 i `verify` ticketu.
- [x] `Core/Input/ActionQueue.cs` — nieograniczona kolejka z nieblokującym zapisem — Home nigdy nie ginie.
- [x] `Core/Config/InputConfig.cs`, `Core/Config/ConfigValidation.cs` — `timing.longPressMs` i konflikt VK — schemat i walidacja.
- [x] `tools/samples/config/input.json` — Back `0xA6`, cztery strzałki dongla, przykład `timing`.
- [x] `tests/.../{InputPolicyTests,ActionQueueTests,ConfigValidationTests}.cs` — każdy wiersz macierzy i `verify` ticketu — dowód na `FakeTimeProvider`.

**Acceptance Criteria:**
- Given polityka na `FakeTimeProvider` i config z donglem, when przechodzi pełną sekwencję klawiszy z macierzy, then każdy wiersz macierzy daje zadeklarowane połknięcie i Actions, a `NextDeadline` znika po key-up i po `OnTimer`.
- Given `ActionQueue` bez czytelnika, when 10 000 zapisów, then żaden nie blokuje i wszystkie dają się odebrać w kolejności.
- Given `input.json` z błędnym `timing.longPressMs` albo z tym samym VK o różnym `kind` w dwóch urządzeniach, when walidacja, then powstaje jeden błąd na właściwej ścieżce, a brak sekcji `timing` daje próg 600 ms.
- Given build Release, when `SchemaExport` pisze schematy, then `input-config.schema.json` ma `timing` poza `required` z `longPressMs` typu `integer`, a `tools/samples/config/input.json` przechodzi walidację.

## Implementation Notes

- `InputPolicy` trzyma `PendingLong` z dodatkowym `VirtualKey` (poza polami z Code Map), bo key-up musi trafić we własny key-down (AD-8). Jeden termin oznacza, że drugi klawisz OK trzymany równolegle dostaje `press` od razu; to jedyny przypadek poza macierzą i nie zmienia zachowania jednego naciśnięcia.
- Scalanie mapowania: `TryAdd` po VK w kolejności urządzeń; klucze spoza formatu `0xNN` są pomijane (walidacja i tak ich nie przepuści), więc polityka nie może się wywrócić na configu zbudowanym wprost.
- Korelacja to dwa ostatnie dopasowane raporty (HID Consumer i `WM_INPUT`), a nie historia: okno jest jednostronne (`raport.TimeMs <= key.TimeMs`, różnica ≤ 50 ms), więc wynik jest deterministyczny na `FakeTimeProvider`. `source` long-pressa jest zamrożony na key-downie.
- `ActionQueue` to `ConcurrentQueue` z opcjonalnym `wake` wołanym po `Enqueue` na wątku wołającego; `null` odrzuca `ArgumentNullException` przed zapisem i przed `wake`.
- `CheckPositive` przyjmuje teraz pełną ścieżkę JSON zamiast nazwy pola (wywołania w `CheckSettings` składają `$.<pole>`), bo `JsonErrorPath.Property("$", "timing.longPressMs")` dałoby `$['timing.longPressMs']`. Konflikt VK liczy się dopiero po walidacji formatu, zakresu i duplikatu w urządzeniu, więc jeden błędny klucz daje jeden błąd; komunikat używa camelCase `kind` i ścieżki pierwszego wystąpienia.
- Przykładowy `input.json` ma 7 kluczy (cztery strzałki, OK, Back, Home) i `timing.longPressMs: 600`; `Sample_config_files_are_valid` pilnuje obu.
- Świadome odroczenia do story 2 (poza zakresem tej story, bo nie ma jeszcze ani jednego wołającego): `_held` czyści wyłącznie key-up, więc utracony key-up (restart hooka, przełączenie sesji) zamroziłby VK do końca procesu — InputHub ze story 2, który rejestruje hook, musi przy (re)rejestracji zawołać reset stanu polityki. Pole `time` hooka jest 32-bitowe i zawija się co ~49,7 dnia; korelacja wtedy zawodzi „bezpiecznie" (spada do `keyboard`), a `long` dostaje przeskok `TsMono` — normalizacja należy do adaptera w story 2. `source` klawisza z dongla bez raportu w oknie to `keyboard` (tak mówi ticket), więc ocena epiku „Home i Back z `source: remote`" opiera się na raporcie HID Consumer, nie na `WM_INPUT`.
- Wiersz macierzy „Zmapowany klawisz" podaje `0x0D`→`ok` jako przykład, a wiersze OK w `shell` rozstrzygają ten klawisz jako `press` na key-up; przykładem wiersza ogólnego jest `0x26`, a test `Mapped_arrow_gives_one_press_on_key_down_and_is_swallowed` pokrywa zachowanie wiersza.

## Plan Change Log

## Review Triage Log

### 2026-10-22 — Review pass (thorough)
- verdicts: 43 findings — high 0, medium 3, low 33, false 7, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: `_held` nie ma Reset — utracony key-up zamraża VK. Wymaga restartu hooka, którego ta zmiana nie ma (InputHub to story 2); reset należy do (re)rejestracji hooka i jest odnotowany w Implementation Notes — dziś nieosiągalne w kodzie bez wołającego.
  - `[low]` `[reject]` Blind: macierz nie opisuje trzech przypadków key-up (mapped up bez down, `OnTimer` bez terminu, drugi VK `ok`). `OnTimer` bez terminu jest pokryty (`Late_timer_after_key_up_gives_no_action`), up bez down wynika z reguły „połknięcie po VK" i dopina je story 5, a drugi VK `ok` to Implementation Notes.
  - `[low]` `[patch]` Blind: `wake` bez kontraktu wyjątków — wyjątek z callbacku trafiłby do hooka LL. Poprawione w XML doc `ActionQueue`: `wake` po zapisie, musi nie blokować i nie rzucać (bez zmiany zachowania).
  - `[low]` `[reject]` Blind: brak analizy nieograniczonego wzrostu kolejki. Kolejka bez ograniczenia rozmiaru to wymóg ticketu; obserwacja zaległości (`action` w logu) należy do story 2, która ją opróżnia.
  - `[low]` `[reject]` Blind: tożsamość urządzenia zwinięta do VK. Wynika wprost z decyzji ticketu (połknięcie i `kind` po VK); dwa urządzenia trzymające ten sam VK naraz to przypadek graniczny, który walidacja dopuszcza tylko dla tego samego `kind`.
  - `[false]` `[reject]` Blind: korelacja nie odróżnia usage 0x0223. Story 2 rejestruje Raw Input tylko dla HID Consumer 0x0C, więc strumień raportów jest z definicji Consumer — usage nie zmieniłoby wyniku.
  - `[false]` `[reject]` Blind: wiersz `app` miesza podtryby i przeczy AD-8. Ticket jawnie: „tryb jest parametrem polityki; routing dokłada story 5" i long tylko dla `shell`; tabela AD-8 należy do story 5.
  - `[low]` `[reject]` Blind: `OnTimer(now)` duplikuje zegar i używa zegara ściennego. Szew `OnTimer(now)` jest wymagany przez ticket; AD-14 i `BannedSymbols.txt` wskazują `TimeProvider` jako jedyny zegar, a `FakeTimeProvider` steruje oboma w testach.
  - `[low]` `[reject]` Blind: zawijanie 32-bitowego `time` hooka (~49,7 dnia). Korelacja zawodzi wtedy bezpiecznie (spada do `keyboard`), a `tsMono` jest informacyjny; normalizacja należy do adaptera w story 2 (notatka w Implementation Notes).
  - `[low]` `[reject]` Blind: brak górnej granicy `longPressMs`. Config jest ręcznie edytowany i walidowany jak progi `settings` (> 0); górna granica nie jest w intencie, a skutek (OK tylko `long`) użytkownik widzi od razu.
  - `[low]` `[reject]` Blind: `MatchDevice` działa na podłańcuchu z pierwszeństwem kolejności configu. Wymaga dwóch sygnatur, z których jedna zawiera drugą; dziś sample ma jedno urządzenie, a kolejność jest deterministyczna.
  - `[false]` `[reject]` Blind: polityka luźniejsza niż walidacja (0x00, silent `TryAdd`). `0x00` i `-1` nie przechodzą `Length == 4 && StartsWith("0x")`, a pierwszeństwo `TryAdd` jest nieosiągalne dla configu po walidacji, która odrzuca konflikt VK.
  - `[low]` `[patch]` Blind: metadane i dowody przeczą treści planu. Poprawione: „okno 50 ms jednostronne" zamiast „w obie strony", publish opisany jako dowód ładowania configu; `deferred` dotyczy findings z przeglądu (nie podziału zakresu), a `oversized` jest informacyjne.
  - `[false]` `[reject]` Blind: sample nie ćwiczy scalania urządzeń. Ticket wylicza zawartość sample (Back `0xA6` i cztery strzałki dongla) i jest ona dodana; drugie urządzenie i fixture bez `timing` należą do story 4/6.
  - `[low]` `[reject]` Edge: utracony key-up — to samo co finding Blind o `_held`; reset przy rejestracji hooka w story 2.
  - `[low]` `[reject]` Edge: key-up zmapowanego VK bez key-down jest połknięty. Zgodne z regułą „połknięcie po VK"; key-up idzie za decyzją key-down i dopina to story 5.
  - `[false]` `[reject]` Edge: raport Consumer po klawiszu powinien używać `Math.Abs`. Ticket: raport „najwyżej 50 ms wcześniej"; `Consumer_report_after_the_key_is_ignored` pinuje ignorowanie raportu po klawiszu.
  - `[low]` `[reject]` Edge: nakładające się sygnatury — to samo co finding Blind o `MatchDevice`; kontrprzykład nieosiągalny z dzisiejszym samplem.
  - `[low]` `[reject]` Edge: zegar cofa się przy trzymanym OK. Ten sam powód co przy szwie `OnTimer(now)`; `TimeProvider` to jedyny dopuszczony zegar (AD-14), a cofnięcie czasu systemowego to awaria środowiska, nie wejścia.
  - `[low]` `[reject]` Edge: zawinięcie `time` hooka — to samo co finding Blind o 32-bitowym czasie; notatka dla adaptera story 2.
  - `[low]` `[patch]` Edge: `wake` rzuca wyjątek — to samo co finding Blind o kontrakcie `wake`; poprawiony XML doc.
  - `[low]` `[reject]` Edge: ogromny `longPressMs` — to samo co finding Blind o górnej granicy; config ręczny, walidacja > 0.
  - `[low]` `[reject]` Edge (claim): wiersz macierzy „Zmapowany klawisz" mówi `press` na key-down dla `0x0D`, a kod czeka na key-up. Poprawka dotyczy treści macierzy w `<intent-contract>` (poza triage'em); wiersze OK rozstrzygają ten klawisz, przykładem wiersza ogólnego jest `0x26`, a rozbieżność jest odnotowana w Implementation Notes.
  - `[medium]` `[patch]` Verification gap: skonfigurowany próg nigdy nie jest ćwiczone przez `InputPolicy` (twarde 600 przechodzi). Dodany `Configured_long_press_threshold_drives_deadline_long_timestamp_and_press_window` (450 ms: termin, `TsMono` long, `press` przed progiem).
  - `[low]` `[patch]` Verification gap: kolejność `Enqueue` → `wake` nie jest pinowana (przeniesienie `wake` wyżej przechodzi). Dodany `Wake_runs_after_the_action_is_queued`: callback sam odbiera Action.
  - `[medium]` `[patch]` Verification gap: `press` na key-up nie jest pinowany do zegara hooka (wariant sprawdzający zegar providera przechodzi). Dodany `Key_up_press_uses_hook_time_not_the_provider_clock` (zegar za terminem, brak `OnTimer`, hook przed progiem → jedno `press`).
  - `[low]` `[reject]` Intent: polityka i kolejka nie są złożone w jednym teście. Złożenie (wkładanie Actions do kolejki) to story 2; w tej zmianie nie ma wołającego, a ticket przypisuje to InputHubowi.
  - `[low]` `[reject]` Intent: long-press w `osk` nieobsłużony, choć AD-8 i Done when 2 epiki mówią „shell i osk". Ticket dla tej story mówi `shell` i „routing dokłada story 5"; `osk` to jedna gałąź w story 5.
  - `[low]` `[reject]` Intent: okno `WM_INPUT` mogłoby być dwustronne. Wybrano okno jednostronne (udokumentowane, deterministyczne); oczekiwanie epiku (`remote` dla Home/Back) opiera się na odnodze Consumer, a `llBeforeRawMs` ze story 3 rozstrzygnie kolejność hook/Raw Input.
  - `[false]` `[reject]` Intent: raport 0x0223 nie jest przenoszony. Story 2 podaje wyłącznie raporty HID Consumer 0x0C, więc usage nie zmienia decyzji o `source`.
  - `[low]` `[reject]` Intent: `TsMono` long jest syntetyzowany (`down + próg`). Alternatywa (czas providera w `TsMono`) mieszałaby domeny czasu; wartość zostaje w zegarze hooka i służy tylko diagnostyce.
  - `[medium]` `[patch]` Intent: klasyfikacja przy spóźnionym timerze czyta stan `pending`, nie różnicę czasów hooka. To zachowanie jest teraz pinowane testem `Key_up_press_uses_hook_time_not_the_provider_clock` (krótkie naciśnięcie daje `press`, nie ginie).
  - `[low]` `[reject]` Intent: „zapis nigdy nie czeka" delegowane do `wake`. Kontrakt `wake` jest teraz udokumentowany (nie blokuje, nie rzuca), a App podaje sygnał; pomiar zaległości kolejki należy do story 2.
  - `[low]` `[patch]` Intent: scalone mapowanie urządzeń nie było ćwiczone w polityce. Dodany `Mapping_is_merged_across_devices_and_source_comes_from_the_correlated_device`.
  - `[low]` `[reject]` Intent: dwa trzymane OK naraz dają `press` od razu i przeczą wierszowi macierzy. Wymaga dwóch różnych VK mapujących `ok` w dwóch urządzeniach; zachowanie jest w Implementation Notes i chroni przed utratą klawisza.
  - `[low]` `[reject]` Intent: autorepeat klawisza z regułą `long` nie ma testu. Ta sama gałąź `_held` jest pokryta testem autorepeatu strzałki, a OK przechodzi identyczne `TryAdd`.
  - `[low]` `[reject]` Intent: zamrożone klawisze (brak resetu) — to samo co finding Blind o `_held`.
  - `[low]` `[patch]` Intent: niewstrzyknięty klawisz ze świeżym raportem Consumer nie był asertowany. Dodany `Non_injected_key_ignores_a_fresh_consumer_report`.
  - `[low]` `[reject]` Intent: korelacja Back `0xA6` nie ma testu. Korelacja jest niezależna od VK (dowodzi jej Home), a sample dodaje `0xA6` zgodnie z ticketem.
  - `[low]` `[reject]` Intent: `continue` w walidacji zmienia liczbę błędów dla klucza z kilkoma wadami. Config nadal jest odrzucany, „jeden błędny klucz = jeden błąd" jest w Implementation Notes, a testy `SingleError` przechodzą.
  - `[low]` `[reject]` Intent: zmiana sygnatury `CheckPositive` sięga walidacji `settings.json`. Zachowanie jest identyczne (stara treść to `Property("$", name)`), a istniejące testy `settings.json` pokrywają ścieżki.
  - `[false]` `[reject]` Intent: schemat jest artefaktem generowanym. `web/contracts/schema/` jest w `.gitignore`, a `timing` jest asertowany w eksporcie (`Input_schema_maps_keys_to_action_kinds`).
  - `[low]` `[reject]` Intent: ocena epiku oczekuje `source: remote` także dla Back, a fallback ticketu to `keyboard`. Ticket rozstrzyga fallback wprost; ocena epiku z pilotem jest po stronie użytkownika na UM760.

### 2026-10-22 — Review pass (follow-up)
- verdicts: 32 findings — high 0, medium 2, low 27, false 3, maybe-false 0
- findings:
  - `[low]` `[reject]` Blind: osierocony `long` / utracony key-up zamraża `_held` — carried (reset przy rejestracji hooka to story 2).
  - `[low]` `[reject]` Blind: drugi równoległy OK daje `press` na key-down i nie ma testu — carried (Implementation Notes; dwa VK `ok` poza samplem).
  - `[low]` `[reject]` Blind: `continue` w walidacji ukrywa kolejne wady klucza — carried (config i tak odrzucony, „jeden błędny klucz = jeden błąd").
  - `[low]` `[reject]` Blind: brak górnej granicy `longPressMs` — carried (config ręczny, walidacja > 0 jak `settings`).
  - `[false]` `[reject]` Blind: polityka luźniejsza niż walidacja (zły VK, `TryAdd`, 0x00) — carried (`Length == 4 && StartsWith("0x")`, konflikt VK odrzucony przez walidację).
  - `[low]` `[reject]` Blind: granice okna 50 ms, zegary hooka/Raw Input, nieparowanie raportu 1:1 między urządzeniami — okno jednostronne i inkluzywne jest pinowane testami 49 ms/raport po klawiszu; raport dongla odpowiada jednemu wstrzykniętemu VK, a normalizacja zegara to adapter story 2.
  - `[low]` `[reject]` Blind: `_held` po VK, key-up bez key-down, nakładające się kandydaty long — carried (reguła „połknięcie po VK"; jeden `PendingLong` w Implementation Notes).
  - `[low]` `[patch]` Blind: `wake` bez egzekwowanego kontraktu wyjątków — carried (XML doc poprawiony w poprzednim przebiegu, bez ponownej poprawki).
  - `[false]` `[reject]` Blind: schemat/sample/dryf kopii w testach i brak `InputMode` w configu — carried (schemat generowany i asertowany w eksporcie; tryb jest parametrem polityki, routing to story 5).
  - `[medium]` `[patch]` Edge: key-up po progu przy spóźnionym `OnTimer` daje `press` — carried (zachowanie pinowane `Key_up_press_uses_hook_time_not_the_provider_clock`; App woła `OnTimer` z `NextDeadline` przed kolejnym zdarzeniem).
  - `[low]` `[reject]` Edge: brak `Reset` przy utraconym key-up — carried.
  - `[low]` `[reject]` Edge: key-up zmapowanego VK bez key-down połknięty — carried.
  - `[low]` `[reject]` Edge: drugi OK w trakcie pending — carried.
  - `[low]` `[reject]` Edge: jeden raport Consumer konsumowany przez kilka key-down w oknie — raport dongla poprzedza jeden wstrzyknięty VK; dwa wstrzyknięte VK w 50 ms z jednego raportu nie występują, a zerowanie raportu to nowa gałąź stanu.
  - `[low]` `[reject]` Edge: zawinięcie 32-bitowego `time` — carried (adapter story 2, korelacja zawodzi bezpiecznie do `keyboard`).
  - `[low]` `[reject]` Edge: `Timing`/`Keys` null w konstruktorze polityki — config po deserializacji ma domyślne wartości i przechodzi walidację; null z kodu to błąd wołającego.
  - `[low]` `[reject]` Edge: `int.TryParse` HexNumber akceptuje białe znaki, np. `0x 1` — walidator używa tej samej reguły długości 4, config po walidacji nie ma takich kluczy.
  - `[low]` `[reject]` Edge: gałąź inna niż Shell/App (osk) bez long — carried (osk to story 5).
  - `[low]` `[patch]` Edge: wyjątek z `wake` trafia do hooka — carried (XML doc).
  - `[low]` `[reject]` Edge: walidacja zbiera tylko pierwszy błąd klucza — carried.
  - `[low]` `[reject]` Edge: brak górnej granicy `longPressMs` — carried.
  - `[medium]` `[patch]` Edge (claim): test „press z zegara hooka" bez przypadku key-up ≥ próg — carried (przypadek spóźnionego timera pinowany w poprzednim przebiegu).
  - `[low]` `[reject]` Edge (claim): `TsMono` long jest syntetyczny (`down + próg`) — carried.
  - `[low]` `[reject]` Verification gap: drugi OK-VK przy `_pendingLong` niepinowany — carried (wymaga dwóch VK `ok`, Implementation Notes; gałąź nie jest osiągalna z samplem).
  - `[low]` `[reject]` Verification gap: autorepeat OK w `shell` przed i po `long` niepinowany — carried (ta sama gałąź `_held` co pinowany autorepeat strzałki).
  - `[low]` `[reject]` Verification gap (other): `continue` zmienia liczbę błędów klucza z wieloma wadami — carried.
  - `[low]` `[reject]` Verification gap (other): konflikt VK pomijany po `continue` przy `appMenu` — zamierzone, klucz i tak odrzucony.
  - `[false]` `[reject]` Intent: raport 0x0223 niemodelowany — carried (Consumer 0x0C z definicji).
  - `[low]` `[reject]` Intent: okno `WM_INPUT` jednostronne — carried.
  - `[low]` `[reject]` Intent: drugi OK `press` od razu, poza intencją — carried.
  - `[low]` `[reject]` Intent: `long` `TsMono` = `down + próg`, dwa zegary — carried.
  - `[low]` `[reject]` Intent: nic nie jest podpięte do runtime (brak hooka, konsumenta kolejki, napędu `OnTimer`) — zgodne z „bez Win32" i story 2/5; opisane w Residual risks.

## Design Notes

Korelacja `source`, w kolejności: (1) klawisz z `Injected == true` → ostatni raport HID Consumer, którego ścieżka zawiera sygnaturę z configu, o czasie `<= key.TimeMs` i nie starszy niż 50 ms, daje `source` tego urządzenia (dla dongla `remote`); (2) inaczej ostatni `WM_INPUT` klawiatury dopasowany do sygnatury w tym samym oknie; (3) inaczej `keyboard`. Polityka zapamiętuje tylko urządzenia z configu, bo zdarzenie spoza configu i tak dałoby `keyboard`; okno jest jednostronne, więc wynik jest deterministyczny na `FakeTimeProvider`.

Dwa zegary: `Action.TsMono` zawsze z `time` zdarzenia hooka, a `long` dostaje `down.TimeMs + longPressMs` (ta sama jednostka), podczas gdy termin progu liczy `TimeProvider` (`NextDeadline = GetUtcNow() + longPressMs`), bo tym zegarem gospodaruje App. `OnTimer(now)` porównuje `now` z terminem i zwraca zawsze `Swallow: false`, bo timer nie jest zdarzeniem wejścia. `OnHidConsumer`/`OnRawKeyboard` tylko buforują korelację; `OnKey` jest jedyną decyzją o połknięciu, więc story 2 woła ją z callbacku hooka bez czekania.

Long-press dotyczy wyłącznie `Ok` w `InputMode.Shell`; reszta dostaje `press` na key-down, także `home`, `back` i `keyboard`. Kolejka jest osobnym typem, bo story 2 wkłada do niej Actions z wątku Input i opróżnia ją własnym wątkiem; opcjonalny `wake` budzi ten wątek bez sięgania do wnętrza kolejki.

## Verification

**Commands:**
- `dotnet test MediaStation.slnx -c Release` — zielone, w tym `InputPolicyTests`, `ActionQueueTests`, rozszerzone `ConfigValidationTests`.
- `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń; `input-config.schema.json` z opcjonalnym `timing.longPressMs`.
- `dotnet build src/MediaStation.App -c Debug` — 0 ostrzeżeń.
- `cd web && npm run build` — `contracts`, `typecheck` i Vite zielone na nowym schemacie.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z ważnym configiem — proces żyje, log ma `snapshotPublished` i `stop`, zero `warn`/`error`.

**Current-machine evidence (2026-10-22, baseline `a51da32` + working tree, ALDER / B760 Pro RS/D4 / Windows 11 Home):**
- `dotnet test MediaStation.slnx -c Release` — 560/560 passed, 0 failed (baseline 526; +21 `InputPolicyTests`, +5 `ActionQueueTests`, +7 przypadków `ConfigValidationTests` + jeden wiersz `UnknownFields`). Każdy wiersz macierzy ma osobny test: połknięcie i `kind` po VK, autorepeat bez drugiej Action, nieznany VK bez połknięcia i bez Action, OK przed progiem (`press` na key-up), OK po progu (samo `long`), OK w `app` (`press` na key-down, brak terminu), spóźniony timer po key-up i po `long`, Home z pilota po raporcie Consumer 30 ms wcześniej (`remote`) i bez raportu (`keyboard`), `WM_INPUT` 20 ms wcześniej i dopasowanie ścieżki bez wielkości liter, okno 50 ms jednostronne (raport tylko przed klawiszem), `NextDeadline` po key-up i po `OnTimer`. Poprawka przeglądu dopina: próg `longPressMs` 450 z configu (termin i `TsMono` long), kolejność `Enqueue` → `wake`, `press` z zegara hooka mimo miniętego terminu providera, scalone mapowanie dwóch urządzeń z `source` skorelowanego urządzenia i ignorowanie świeżego raportu Consumer przez klawisz niewstrzyknięty.
- `dotnet build MediaStation.slnx -c Release` — success, 0 warnings, 0 errors. `web/contracts/schema/input-config.schema.json` ma `timing` poza `required` (required nadal `devices`, `gamepadHome`), `InputTiming.longPressMs` typu `integer`, `additionalProperties: false`; `tools/samples/config/input.json` przechodzi `Sample_config_files_are_valid`.
- `dotnet build src/MediaStation.App -c Debug` — success, 0 warnings (`npm run contracts` + `npm run typecheck` zielone na nowym schemacie).
- `cd web && npm run build` — `contracts`, `typecheck` i Vite build zielone.
- `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` + uruchomienie z `tools/samples/config/*` (w tym nowy `input.json` z `timing`): proces żył po 10 s (pid 44452), log `mediastation-2026-10-22.jsonl` ma `start`, `inputRouting {cmdId: 1, mode: "shell", pointer: "blocked", epoch: 1}`, `powerSignalsRegistered`, `snapshotPublished {rev: 1, focusRequest: {default, 1}}` i `stop {exitCode: 0}`, zero `warn`/`error`. Config użytkownika przywrócony, katalog publish usunięty. To dowód, że config z `timing` nadal się ładuje (polityka nie jest podpięta do App — to story 2), a nie test zachowania klawiszy; po poprawce przeglądu zmieniły się tylko testy, więc przebieg pozostaje aktualny.
- Polityka nie jest jeszcze podpięta do App (to story 2), więc zachowanie klawiszy na tej maszynie dowodzą testy `InputPolicyTests` i `ActionQueueTests`; pełny przebieg z pilotem i `Home` nad aplikacjami zostaje w odbiorze epiku na UM760.

## Auto Run Result

**Status:** built.

**Summary:** Core zyskał czystą politykę wejścia `MediaStation.Core.Input.InputPolicy`: decyzję o połknięciu i `ActionKind` po samym VK ze scalonego mapowania urządzeń, `source` z korelacji w oknie 50 ms (wstrzyknięty VK po raporcie HID Consumer → urządzenie z `WM_INPUT` → `keyboard`), jedno naciśnięcie = jedna Action (autorepeat bez drugiej), `press` na key-down, a dla OK w `shell` `press` na key-up przed progiem albo samo `long` po progu (szew `NextDeadline`/`OnTimer(now)` na wstrzykniętym `TimeProvider`). Nowa `timing.longPressMs` (600) weszła do `input.json`, walidacji i schematu, walidacja odrzuca też ten sam VK z różnym `kind` w dwóch urządzeniach, a `ActionQueue` daje nieograniczoną kolejkę z nieblokującym zapisem.

**Files changed:**
- `src/MediaStation.Core/Input/InputPolicy.cs` — zdarzenia wejścia, `InputDecision`, mapowanie po VK, korelacja `source`, jedna Action na naciśnięcie, `press`/`long` i szew timera.
- `src/MediaStation.Core/Input/ActionQueue.cs` — nieograniczona kolejka Actions z opcjonalnym `wake` (kontrakt: po zapisie, bez blokowania i bez wyjątków).
- `src/MediaStation.Core/Config/InputConfig.cs` — `InputConfig.Timing` i `InputTiming.LongPressMs` (600).
- `src/MediaStation.Core/Config/ConfigValidation.cs` — próg `$.timing.longPressMs` i konflikt VK między urządzeniami; `CheckPositive` przyjmuje pełną ścieżkę.
- `tools/samples/config/input.json` — Back `0xA6`, cztery strzałki dongla i `timing.longPressMs: 600`.
- `tests/MediaStation.Core.Tests/{InputPolicyTests,ActionQueueTests,ConfigValidationTests}.cs` — 21 + 5 testów polityki i kolejki oraz 7 przypadków walidacji i wpis `timing` w schemacie.

**Review findings (thorough, follow-up pass: 32 findings — medium 2, low 27, false 3; wszystkie carried lub odrzucone, zero nowych patchy):** 43 findings — high 0, medium 3, low 33, false 7. Patched: 8 (medium 3, low 5). Deferred: 0. Rejected: 35 (7 `false`, 28 `low`) — reasons per finding in `## Review Triage Log`, m.in.: wymóg ticketu (kolejka bez ograniczenia, szew `OnTimer(now)`), zakres story 2/5 (reset stanu przy rejestracji hooka, routing, złożenie kolejki z polityką), oraz pokrycie istniejącymi testami (up bez down, usage Consumer, schemat generowany).

**Patched counts by verdict:** high 0, medium 3, low 5.

**Follow-up review recommendation:** `false` — przebieg follow-up nie dał nowych patchy; pozostałe ryzyko (kolejność hook LL vs `WM_INPUT`, zachowanie pilota) rozstrzyga dopiero story 2 i odbiór na UM760.

**Verification:** `dotnet test MediaStation.slnx -c Release` 560/560 (baseline 526); `dotnet build MediaStation.slnx -c Release` i `dotnet build src/MediaStation.App -c Debug` z 0 ostrzeżeń; `input-config.schema.json` z opcjonalnym `timing.longPressMs`; `cd web && npm run build` (contracts, typecheck, Vite) zielone; publish self-contained + uruchomienie z configiem zawierającym `timing` (pid 46308, `start` → `snapshotPublished` → `stop {exitCode: 0}`, zero `warn`/`error`) i z configiem bez `timing` (domyślne 600).

**Residual risks:** polityka nie jest podpięta do App (story 2), więc na tej maszynie nie ma jeszcze end-to-end zachowania klawiszy; `_held` czyści tylko key-up, więc InputHub musi resetować stan polityki przy (re)rejestracji hooka; 32-bitowe `time` hooka zawija się co ~49,7 dnia i normalizacja należy do adaptera; górna granica `longPressMs` nie jest walidowana (config ręczny, skutek widoczny od razu); `source` klawisza z dongla bez raportu w oknie to `keyboard`, więc oczekiwanie epiku „Home i Back z `source: remote`" opiera się na odnodze raportu HID Consumer.
