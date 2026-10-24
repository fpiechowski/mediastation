---
title: 'Spike 3.11: dlaczego hook WH_KEYBOARD_LL milczy, gdy shell jest na wierzchu'
type: 'chore'
ticket: '11'
created: '2026-10-24'
status: 'built'
baseline_revision: '9a166fed0c08b19082d056e716a5c17f543d66e1'
route: 'full'
route_source: 'auto'
risk: 'high'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
context:
  - '{project-root}/AGENTS.md'
  - '{project-root}/_bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/spike-dlaczego-hook-wh-keyboard-ll-nie-dostaje-klawiszy-gdy-shell.md'
---

<frozen-after-approval reason="human-owned intent — do not modify unless human renegotiates">

## Intent

**Problem:** Na UM760 przy shellu (WebView2) na wierzchu hook `WH_KEYBOARD_LL` nie dostaje klawiszy, a Home z pilota (`0x0223` → `VK_BROWSER_HOME`) otwiera Edge. Przyczyna jest niepotwierdzona; blokuje odbiór `epic-input` (Done when 3) i epiki zależne od działającego Home.

**Approach:** Zbudować sterowane zmiennymi środowiskowymi warianty InputHub (bez Raw Input, Raw Input na osobnym wątku) i minimalne okno bez WebView2 z tym samym hookiem, a skrypt ma je uruchomić i zapisać wiersz wyniku na eksperyment. Przeprowadzić je na ALDER (wejście syntetyczne), przygotować przebieg na UM760 i opisać skutki dla AD-8.

## Boundaries & Constraints

**Always:** kod eksperymentów tylko na gałęzi spike'u, `master` bez zmian do decyzji dla AD-8; warianty wyłączone domyślnie (brak zmiennej = dzisiejsze zachowanie); log tylko z Actions i śladem `MS_INPUT_TRACE`, bez surowych kodów poza śladem diagnostycznym; wynik każdego eksperymentu to wiersz: co na wierzchu, czy hook dostał „down", liczba wpisów `action`, czy otworzył się Edge.

**Never:** nie zmieniać statusu odbioru epiki i nie zamykać jej; nie mergować do `master`; nie blokować ticketu na sprzęcie UM760 (AGENTS.md) — kroki wymagające pilota i UM760 oznaczone „nie dotyczy na ALDER" z powodem i przekazane w handoffie; nie przełączać okien użytkownika w trakcie testu z fizycznym wejściem.

## I/O & Edge-Case Matrix

| Scenariusz | Stan | Oczekiwane | Błąd |
|------------|------|-----------|------|
| Wariant nieustawiony | brak zmiennych | InputHub jak dziś | — |
| Wariant bez Raw Input | `MS_HUB_NO_RAWINPUT=1` | brak `RegisterRawInputDevices`, wpis `warn` o wariancie | brak wpisu = wariant nieaktywny |
| Raw Input na osobnym wątku | `MS_HUB_RAW_THREAD=1` | `WM_INPUT` na wątku bez hooków | porażka rejestracji → `inputHubError` |
| Puste okno | `tools/hook-probe` | log „down" przy własnym oknie na wierzchu | brak hooka → kod wyjścia ≠ 0 |

</frozen-after-approval>

## Code Map

- `src/MediaStation.App/Platform/Input/InputHub.cs` -- hooki, `RegisterRawInput` (l. ~253), `PumpMessages`, `WindowProc` dla `WM_INPUT`; tu pojawiają się przełączniki wariantów 2 i 3.
- `src/MediaStation.App/Platform/Input/TracingInputTarget.cs` -- `keyEnter` loguje się PRZED polityką, więc brak `keyEnter` przy shellu na wierzchu znaczy: callback hooka nie został wywołany. Bramka (`InputPolicy.IsForeign`, `Core/Input/InputPolicy.cs:565`) działa dopiero w `_inner`, więc eksperyment 1 (`MS_NO_GATE`) ma niską wartość diagnostyczną; zostaje jako tani sanity check.
- `src/MediaStation.App/CompositionRoot.cs:~280` -- składa `InputPolicy`, sondę foreground i `TracingInputTarget`; miejsce na odczyt zmiennych wariantów.
- `src/MediaStation.Core/Input/ManagedWindows.cs` -- sonda bramki z logiem `foregroundGate`; nie zmieniać.
- `tools/check-home-s10.ps1` -- wzorzec skryptu: odczyt logu, `-SelfTest`, beep, wynik do `-Out`; model dla skryptu spike'u.
- `spike/win/HomeLogger.cs` -- referencja: hook bez `RegisterRawInputDevices` na wątku UI; przypadek „działa/nie działa" z S10.
- Nie ruszać: `Core/Input/*` poza ewentualnym `MS_NO_GATE`, kontraktów i `input.json`.

## Tasks & Acceptance

**Execution:**
- [x] gałąź `spike-3-11` z `master` -- utworzona przed zmianą kodu -- kod eksperymentów poza `master`
- [x] `src/MediaStation.App/Platform/Input/InputHub.cs` -- warianty `MS_HUB_NO_RAWINPUT` i `MS_HUB_RAW_THREAD` z jednym wpisem `warn inputHubVariant` -- eksperymenty 2 i 3
- [x] `src/MediaStation.App/CompositionRoot.cs` -- `MS_NO_GATE` (sonda zawsze „managed") -- eksperyment 1
- [x] `tools/hook-probe/` -- minimalna aplikacja Win32 bez WebView2: okno na wierzchu, ten sam hook LL, log „down" -- eksperyment 4
- [x] `tools/spike-3-11.ps1` -- uruchamia warianty, wstrzykuje klawisze `SendInput` przy shellu/oknie na wierzchu, czyta log i dopisuje wiersz do tabeli wyników; instrukcja z góry i „start" -- powtarzalny pomiar
- [x] plik ticketu 3.11, sekcja „Wynik" -- tabela i odnośniki do dowodów ALDER, ograniczenia wniosku i skutki dla AD-8
- [x] przekazanie fizycznych prób Home na UM760 do odbioru całej epiki; per-ticket best-effort wykonany na ALDER. Jawna akceptacja w AC 4 pozostaje decyzją użytkownika, nie blokadą sprzętową.

**Acceptance Criteria:**
- Given shell MediaStation na wierzchu na ALDER, when skrypt wstrzykuje Home, Back i OK w każdym wariancie, then tabela ma wiersz z „down" hooka tak/nie na wariant.
- Given warianty nieustawione, when uruchamiamy testy `MediaStation.Core.Tests` i build App, then przechodzą bez zmian.
- Given wynik eksperymentów, when czyta go `bmad-architecture`, then sekcja „Wynik" podaje przyczynę albo wyczerpaną listę 1–5 i skutek dla AD-8.

## Implementation Notes

## Plan Change Log

- 2026-10-24: po trzech powtórzonych przebiegach `raw-hid-only` na ALDER zapisano weryfikację i doprecyzowano, że fizyczny pilot/UM760 należy do odbioru całej epiki wg `AGENTS.md`.

## Review Triage Log

Pass 1 (quick): 0 high, 3 medium, 2 low, 1 false.

| # | verdict | route | evidence |
|---|---------|-------|----------|
| 1 | medium | patch | Brak zapisanej weryfikacji w planie/tickecie; uruchomiono build (0/0), testy 892/892, `-SelfTest` OK i zapisano w „Wynik". |
| 2 | medium | patch | Kolumna Edge na ALDER bez wartości dowodowej (`VK_HOME` nie uruchamia Edge); dopisano ograniczenie w „Wniosek". |
| 3 | medium | patch | Zbyt mocne sformułowanie przyczyny i „należy wycofać"; zmiękczono do hipotezy z ALDER. |
| 4 | low | patch | Wiersze 4a–4c, 5 dodane po zatwierdzeniu; odnotowano w „Wynik". Domyślne `-Presses` 1 vs 3 w przebiegu finalnym: wynik podaje faktyczne liczby. |
| 5 | false | rejected | Wyścig `MS_HUB_RAW_THREAD` dotyczy `action`, ale „down" to `keyEnter` logowany przed polityką; wariant opisany jako eksperymentalny, nie idzie do master. |
| 6 | low | rejected | Alt przez `keybd_event` w skrypcie z odliczaniem startowym; `hook-probe` nie jest w `MediaStation.slnx`; konflikt dwóch zmiennych to ścieżka diagnostyczna. Poprawka wymaga dodatkowej złożoności. |

## Design Notes

Wstrzyknięcie `SendInput` na ALDER ma flagę `LLKHF_INJECTED`; hook dostaje je zwykle także przy własnym oknie, więc ALDER może nie odtworzyć błędu. Brak odtworzenia jest wynikiem (przyczyna zależy od fizycznego Raw Input/HID albo UM760), a rozstrzygnięcie zostaje w przebiegu UM760.

## Verification

**Commands:**
- `dotnet build src/MediaStation.App -c Release -v q` -- PASS, 0 ostrzeżeń, 0 błędów.
- `dotnet test tests/MediaStation.Core.Tests -v q` -- PASS, 892/892, 0 pominiętych.
- `dotnet build tools/hook-probe/hook-probe.csproj -c Release -v q` -- PASS, 0 ostrzeżeń, 0 błędów.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/spike-3-11.ps1 -SelfTest` -- PASS.
- Test aplikacyjny ALDER: `-Variants raw-hid-only,raw-hid-only,raw-hid-only -Presses 1 -StartDelaySeconds 3 -SettleSeconds 1` -- trzy uruchomienia pod rząd; w każdym shell na wierzchu, hook down dla Home/Back/OK, po jednej odpowiadającej Action, `inputHubVariant` dokładnie raz i brak `inputHubError`. Dla Home: 1 `action {kind: home}` w każdym z trzech przebiegów.
- Asercje JSONL dla powtórzeń: każdy z PID 19388, 3304 i 2164 ma jeden wpis wariantu `rawHidOnly`, jedno `keyEnter` down `VK_HOME`, jedną `action home` i zero `inputHubError`; tabela skryptu zawiera także po jednej Action Back i OK w każdym przebiegu.
- Sonda `hook-probe --raw-hid`: 9/9 syntetycznych down (Home/Back/OK), potwierdzone w `hook-probe-20261024-194709.jsonl`.

- **Granica sprzętowa:** fizyczny pilot G20S Pro i zachowanie okna przeglądarki są N/A dla per-ticket testu na ALDER: wejście było syntetyczne i nie modeluje `VK_BROWSER_HOME`/HID Consumer. Najmocniejszy per-ticket dowód to trzy uruchomienia ALDER powyżej. UM760 jest częścią późniejszego odbioru całej epiki, nie warunkiem ukończenia tego ticketu.
- **Decyzja użytkownika:** AC 4 wymaga jawnej akceptacji wyniku i pozostaje do odnotowania w `Notes` ticketu jako `Decision:` z datą.
