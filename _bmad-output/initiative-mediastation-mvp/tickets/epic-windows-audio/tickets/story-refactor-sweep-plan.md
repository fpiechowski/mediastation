---
title: 'Refactor sweep (epic-windows-audio)'
type: 'chore'
ticket: '7'
created: '2026-10-27'
status: done
baseline_revision: 'fcb4a3feb5cc8dc690d8a3ed6d16287ba8fae1ea'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Sprzątanie po stories 5.1–5.6 ma iść z zamkniętej listy ID (DW-26), inaczej sweep rośnie bez końca; jedyny wpis ledgera przypisany do sweepu to DW-34: `tools/inject-power.ps1` liczy `snapshotPublished` i `seq` od startu procesu, więc na używanej aplikacji daje fałszywy FAIL.

**Approach:** Zamknięta lista poniżej; DW-34 naprawione przez liczenie snapshotów i `seq` względem ostatniego snapshotu przed pierwszym `powerSignalInjected`, z `-SelfTest` dla czystej funkcji asercji. Bez zmian zachowania produktu.

</intent-contract>

## Closed scope list (ustalona na starcie, HEAD `fcb4a3f`)

Źródła: pole `deferred` w planach stories 5.1–5.6 i ich Review Triage Log (`[defer]`), ticket (DW-26, DW-34).

| ID | Pozycja | Decyzja |
|----|---------|---------|
| DW-26 | Zasada: lista ID na starcie, bez przepisywania narzędzi bez wpisu | Zastosowana w tym planie; wpis już `done` w ledgerze. |
| DW-34 | `inject-power.ps1`: snapshoty i `seq` względem chwili przed pierwszym `powerSignalInjected` | Zrobione (zadanie niżej). |
| 5.2 deferred | Sesje wyciszone przez zabity MediaStation zostają wyciszone | Odrzucone: rozstrzygnięte w 5.4 (adopcja `MuteProcessTree` zapisuje wyciszone sesje w księdze, późniejszy `UnmuteProcessTree` je odcisza; plan 5.4, Implementation Notes). |
| 5.5 deferred | Home w trybie shell jest tylko logowany i nie wysyła komend okiennych | Odrzucone z sweepu: zmiana zachowania Orchestratora (sweep ma być bez zmiany zachowania); scope wypchnięty to nowa story, nie sweep. |
| 5.1, 5.3, 5.4, 5.6 | `deferred: []`, brak `[defer]` w logach triage | Pusto. |

## Tasks & Acceptance

**Execution:**
- [x] `tools/inject-power.ps1` -- wydzielić `Get-WakeSnapshotProblems` (baseline = snapshoty przed pierwszym `powerSignalInjected`; po nim dokładnie 2 snapshoty z `seq` = seq baseline + 1, + 2), użyć jej w asercjach, dodać `-SelfTest` -- DW-34

**Acceptance Criteria:**
- Given aplikacja z historią snapshotów przed skryptem, when asercja liczy snapshoty, then PASS, gdy po pierwszym markerze są 2 snapshoty z kolejnymi `seq`; FAIL przy 1, 3 albo luce w `seq`.
- Given świeża aplikacja (baseline 1 snapshot, seq 1), then wymagane `seq` to 2 i 3 jak dotąd.

## Implementation Notes

Oneshot: jeden plik skryptu (ok. 120 linii dodanych z funkcją i `-SelfTest`; nad progiem 100 przez samotest, logika to ok. 40 linii). Maszyna zidentyfikowana poleceniem z AGENTS.md: `MEDIASTATION` / `F7BSI` / `EliteMini Series`, czyli UM760.

## Verification

**Commands:**
- `powershell -File tools/inject-power.ps1 -SelfTest` -- expected: wszystkie przypadki OK
- `dotnet test` -- expected: bez niepowodzeń
- `npm test` w `web/` -- expected: bez niepowodzeń

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 24 findings — high 0, medium 1, low 3, false 6, maybe-false 0 (14 further rows rejected as plan/process remarks, see below)
- findings:
  - `[medium]` `[patch]` baseline snapshot without `focusRequest.seq` becomes baseline 0 (blind, edge-case) — added an explicit problem and early return plus a SelfTest case 'baseline without seq'
  - `[low]` `[reject]` tasks unchecked, no evidence section, UM760 claim unverified (blind, edge-case) — fixed as housekeeping in this plan (task checked, machine recorded, evidence in Auto Run Result); no code change
  - `[low]` `[reject]` "<100 linii" claim wrong (blind, edge-case, intent) — Implementation Notes corrected; fix edits this plan only
  - `[low]` `[reject]` SelfTest asserts only the problem count, not the text (blind) — cases cover distinct failure shapes (missing/extra/gap/none); asserting message text adds brittleness
  - `[false]` `[reject]` a wake snapshot can be logged before the first marker (edge-case) — `Add-InjectionMarker` appends the marker before `PostMessage`, so no wake snapshot precedes it
  - `[false]` `[reject]` `$run` may mix process ids (blind) — `Get-RunEntries` already cuts at the last `start` of the process id
  - `[false]` `[reject]` seq may not increase across history (edge-case) — baseline is the last snapshot in log order, which is by construction the latest
  - `[false]` `[reject]` non-numeric or null `seq` after the marker hides a failure (edge-case) — the comparison `$seq -ne $expected` reports it as a FAIL
  - `[false]` `[reject]` scope source narrower than "stories 1–6" (intent-alignment) — "stories 1–6" are the epic's own entries 5.1–5.6 (`after = [1..6]`)
  - `[false]` `[reject]` 5.5 deferral could be lost, `deferred: []` contradicts the table (blind) — the item stays in plan 5.5's `deferred`; the table records the rejection from the sweep and the scope-push rule says new story
  - `[low]` `[reject]` `-SelfTest` not run by `dotnet test`/`npm test` (verification-gap) — the ticket's verify names those suites for "no behavior change" and `check-home-s10.ps1 -SelfTest` has the same shape; wiring PowerShell into the suites is new test infrastructure outside the closed list
  - `[low]` `[reject]` `exit` inside `Invoke-SelfTest` ends the host when dot-sourced (blind) — same idiom as `check-home-s10.ps1`; the script is run with `-File`
  - `[low]` `[reject]` no `.PARAMETER` help for `-SelfTest`, vague comment (blind) — `.EXAMPLE` and the description document it; cosmetic
  - `[low]` `[reject]` no run against a used live application (blind, verification-gap, intent-alignment) — needs a live app and the script closes it; logic is covered by the 9 SelfTest cases including a 9-snapshot history; recorded as residual risk
  - `[low]` `[reject]` ticket id `7` vs `5.7`, DW-26 state not linked (blind, intent-alignment) — `ticket` carries the entry id per the template; DW-26 is `done 2026-10-26` in `deferred-work.md`
  - `[low]` `[reject]` remaining intent-alignment descriptive remarks (surface of `dotnet test` vs script, added assertion that a baseline exists) — descriptive only; the baseline assertion is needed for the new seq rule

## Auto Run Result

**Summary:** Zamknięta lista sweepu ustalona na starcie: DW-26 (zasada, zastosowana), DW-34 (zrobione), dwie odroczone pozycje z planów 5.2 i 5.5 odrzucone z powodem. `tools/inject-power.ps1` liczy teraz snapshoty i `seq` względem ostatniego snapshotu przed pierwszym `powerSignalInjected`, więc używana aplikacja nie daje fałszywego FAIL. Produkt bez zmian.

**Files changed:**
- `tools/inject-power.ps1` — `Get-WakeSnapshotProblems` (baseline przed pierwszym markerem, 2 snapshoty po nim z kolejnym `seq`), `-SelfTest` z 9 przypadkami, zaktualizowane opisy
- `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/story-refactor-sweep-plan.md` — ten plan

**Review:** thorough, 4 lenses; patches applied 1 (medium: baseline bez `seq`); deferred 0; rest rejected with reasons in the triage log. Follow-up review recommended: false (jedna łatka medium).

**Verification (UM760):** `powershell -File tools/inject-power.ps1 -SelfTest` — OK, 9 przypadków (po łatce); `dotnet test` — 1191 zaliczonych, 0 niepowodzeń; `npm test` w `web/` — 10 plików, 118 testów zaliczonych. Przebiegu na żywej aplikacji nie wykonano (skrypt zamyka aplikację; wymaga ją uruchomić).

**Residual risks:** wiązanie funkcji z żywym logiem (`Get-RunEntries` → `Get-WakeSnapshotProblems`) sprawdzi dopiero przebieg skryptu na UM760 (S18 odbioru epiki). DW-34 w ledgerze: `status` zamyka orkiestrator po weryfikacji. Odrzucona z sweepu pozycja 5.5 (Home w trybie shell nie wysyła komend okiennych) wymaga nowej story. Brak elementów operatora.
