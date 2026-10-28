---
title: 'Jedno naciśnięcie Home daje pojedynczą akcję i pojedyncze ShowShell'
type: 'bugfix'
ticket: 'bug-jedno-nacisniecie-home-daje-pojedyncza-akcje-i-pojedyncze-sh'
created: '2026-10-28'
status: 'built'
baseline_revision: 'a52796d9504befcc0497f611af606bc963653993'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      Przyczyna podwójnego Home z S10 nie jest potwierdzona; okno scalania w InputPolicy jest ochroną, nie dowiedzioną naprawą.
    evidence: |-
      Dziennik z S10 (UM760, 2026-10-28, 14:05:00 i 14:05:21) pokazuje dwa osobne naciśnięcia, każde z jedną akcją i jednym ShowShell. Jeśli podwójny przebieg ma inne źródło (pad, HID, echo), błąd zostanie; rozstrzyga ponowny odbiór S10 na UM760.
    location: >-
      src/MediaStation.Core/Input/InputPolicy.cs
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** S10 (UM760, 2026-10-28) zgłosił dwa `action home` i dwa `ShowShell` po jednym Home. Dziennik z tego przebiegu pokazuje dwa osobne naciśnięcia w odstępie 21 s, każde z jedną akcją i jednym `ShowShell`, więc podwójnego przebiegu z jednego naciśnięcia nie udało się odtworzyć; kod nie miał jednak żadnej ochrony przed powtórzonym key-down Home (drugi VK, echo, powtórzony sygnał).

**Approach:** Okno scalania Home w `InputPolicy` (adapter wejścia, nie Orchestrator; AD-8): drugi key-down Home w `HomeCoalesceMs` od poprzedniego, który dał Action, jest połykany bez Action. Echo `ForegroundChanged` po `ShowShell` jest już pokryte testami Orchestratora.

</intent-contract>

## Implementation Notes

Oneshot: zmiana jest mała (jeden warunek w `InputPolicy.OnKey` plus testy). Orchestrator celowo bez dedupe po czasie: istniejące testy i DW-41 wymagają, by kolejny Home w shellu ponownie potwierdzał shell.

- `src/MediaStation.Core/Input/InputPolicy.cs` — `HomeCoalesceMs = 250`, `_lastKeyHomeMs`; czas z hooka (`KeyStroke.TimeMs`), więc deterministyczny w testach. Pad ma własną ochronę (`_homeEmitted`).
- `tests/MediaStation.Core.Tests/InputPolicyTests.cs` — dwa nowe testy (powtórzenie/drugi key-down w oknie, brak połykania innych klawiszy), dwa istniejące testy korelacji `source` przesunięte poza okno (1020 → 1400).
- Ograniczenie: źródła z jednego naciśnięcia nie udało się potwierdzić w dzienniku; UM760 ma to potwierdzić przy odbiorze.

## Verification

**Commands:**
- `dotnet test tests/MediaStation.Core.Tests` -- expected: 1269 passed
- `dotnet test tests/MediaStation.App.Tests` -- expected: 14 passed, 1 skipped

## Review Triage Log

### 2026-10-28 — Review pass
- verdicts: 24 findings — high 0, medium 1, low 4, false 2, maybe-false 0 (17 rows carry the same claims, grouped below)
- findings:
  - `[medium]` `[defer]` Przyczyna nieustalona, fix spekulatywny (blind, edge, intent-alignment) — dziennik S10 to dwa osobne naciśnięcia; zapisane w `deferred`, rozstrzyga odbiór S10 na UM760.
  - `[medium]` `[patch]` Brak testu: drugi VK zmapowany na Home, remote→keyboard w oknie, granica `HomeCoalesceMs - 1` (blind, edge, verification-gap) — dodane trzy testy.
  - `[low]` `[reject]` Zawinięcie `TimeMs` i ujemna delta (blind, edge) — ujemna delta przepuszcza Home i odświeża znacznik, więc najgorzej wraca stare zachowanie raz na dziesiątki dni; poprawka to dodatkowa logika.
  - `[low]` `[reject]` Brak resetu `_lastKeyHomeMs` przy zmianie routingu/urządzenia (edge, verification-gap) — polityka nie ma ścieżki resetu, a okno 250 ms jest mniejsze niż czas ludzkiego drugiego naciśnięcia.
  - `[low]` `[reject]` Zdławiony Home zużywa dowód remote z `ResolveSource` (edge) — pierwszy Home zużywa go wcześniej; kolejny raport HID i tak ustawia nowy dowód.
  - `[low]` `[reject]` Brak logu scalenia (blind) — InputPolicy nie ma loggera; brak drugiej akcji w dzienniku jest sam w sobie dowodem.
  - `[false]` `[reject]` Pad i HID nie dzielą okna (edge, intent-alignment) — pad ma `_homeEmitted`, a zegary hooka i pada są różne, więc wspólny znacznik byłby błędny.
  - `[false]` `[reject]` Orchestrator bez dedupe/echa (intent-alignment) — echo `ShowShell` pokrywa `Echo_of_show_shell_yields_nothing_after_settling`, a dedupe po czasie w Orchestratorze łamie DW-41 i adaptery nie są jego miejscem (AD-8).

## Auto Run Result

**Status:** built (ticket 5.15), maszyna: UM760 (MEDIASTATION, F7BSI).

**Zmiana:** `InputPolicy` scala Home z hooka w oknie `HomeCoalesceMs = 250` ms (połknięty, bez drugiej Action i drugiego przejścia).

**Pliki:** `src/MediaStation.Core/Input/InputPolicy.cs` (okno scalania); `tests/MediaStation.Core.Tests/InputPolicyTests.cs` (5 nowych testów, 2 istniejące przesunięte poza okno).

**Review:** thorough, 4 soczewki; patch: 1 wpis (testy); defer: 1 (przyczyna niepotwierdzona); reszta odrzucona z powodami wyżej.

**Weryfikacja na UM760:** `dotnet test tests/MediaStation.Core.Tests` — 1272/1272; `dotnet test tests/MediaStation.App.Tests` — 14 passed, 1 skipped (przed dodaniem testów review; App niezmieniony).

**Ryzyka:** dziennik S10 nie potwierdza podwójnego Home z jednego naciśnięcia, więc AC1 na sprzęcie wymaga ponownego odbioru S10 przez użytkownika (cała epika na UM760). Testy Orchestratora (echo `ShowShell`) już istniały i pokrywają AC2 po stronie Orchestratora. `followup_review_recommended: false`.
