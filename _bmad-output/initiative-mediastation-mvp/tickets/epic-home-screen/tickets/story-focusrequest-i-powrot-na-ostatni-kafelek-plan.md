---
title: 'focusRequest i powrót na ostatni kafelek'
type: 'feature'
ticket: '7'
created: '2026-10-26'
status: done
baseline_revision: '9dfcfcde77fec94d37c7c9e5ba5b5639ad752c33'
route: 'oneshot'
route_source: 'auto'
risk: 'medium'
review: 'quick'
review_source: 'pinned'
lenses_ran: [quick]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred: []
---

<intent-contract>

## Intent

**Problem:** Shell ma stosować każde nowe `focusRequest.seq` dokładnie raz (`default` → pierwszy kafelek Video, `<appId>` → kafelek i zakładka aplikacji), zostawiać fokus na ostatnim kafelku po powrocie z aplikacji i nie przenosić go przy zmianie stanu kafelka (R3, AD-16).

**Approach:** Logika `applyFocusRequest` kluczowana `appId` i `seq` istnieje w `web/shell/src/focus.ts` (story 3); ta story domyka kontrakt testami na każdy warunek `verify` i nie zmienia kodu produkcyjnego, jeśli testy nie pokażą luki.

</intent-contract>

## Implementation Notes

Oneshot: zmiana tylko w testach (ok. 80 linii); `focus.ts` i `App.tsx` już realizują wymagania (`appliedSeq`, `focusTile`, pamięć po `appId`), testy potwierdziły to bez zmian w kodzie.
- Maszyna: **UM760** (COMPUTERNAME `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`).
- `web/shell/src/focus.test.ts`: blok „focusRequest i powrót na ostatni kafelek”: ten sam `seq` z nowym `rev` nie rusza fokusu, nowy `seq` z `appId` (także ze strefy zakładek) działa raz, fokus po `appId` przeżywa przestawienie kafelków, Home/Back i powrót ze stanu `foreground` zostawiają fokus na ostatnim kafelku, zmiana stanu kafelka pod fokusem go nie przenosi, awaria aplikacji z innej zakładki przenosi fokus i zakładkę.
- `web/shell/src/Placeholder.test.tsx`: po pierwszym snapshocie z `default` dokładnie jeden kafelek ma `tile--focused` i jest to pierwszy kafelek Video.

## Verification

**Commands:**
- `npm test` (w `web/`) -- expected: 63 testów przechodzi
- `npm run typecheck` (w `web/`) -- expected: bez błędów

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 7 findings — high 0, medium 0, low 5, false 1, maybe-false 0 (1 evidence-only)
- findings:
  - `[low]` `patch` Test Home/Back nie może się nie powieść — przepisany: OK daje Intent `launchApp`, snapshot `foreground` z `seq`, Home/Back bez zmiany stanu, powrót z tym samym `seq` zostawia fokus na `b`, a strzałka idzie od `b`.
  - `[low]` `patch` Brak testu `appId` z tym samym `seq` i nowym `rev` na przestawionej liście — dodano asercję `toBe(state)`.
  - `[low]` `patch` Test zmiany stanu nie sprawdzał nawigacji — dodano Left/Right na zmienionej liście.
  - `[low]` `patch` Wiązanie z referencją (`toBe`) — zostawione tam, gdzie reduktor zwraca ten sam obiekt z założenia; behawioralne asercje dodane obok.
  - `[low]` `reject` Test Placeholder duplikuje `default` z focus.test i regex jest słaby — to jedyny test renderu (jeden kafelek z `tile--focused`, kategoria Game przed Video); poprawka dodałaby komplikację bez korzyści.
  - `[low]` `patch` Brak zapisanych wyników weryfikacji w planie — uzupełnione w Auto Run Result.
  - `[false]` `reject` `App.tsx` konsumuje `seq` w snapshocie `configError` — Core ustawia `focusRequest` tylko przy starcie/restarcie (`Orchestrator.Startup.cs:41,63`) i awarii (`Failures.cs:99`), a snapshot z błędem konfiguracji nie ma `apps`; po poprawie konfiguracji i tak przychodzi nowy `seq`.

## Auto Run Result

**Summary:** `focus.ts`/`App.tsx` (story 3) już stosują `focusRequest` raz na `seq`, kluczują fokus po `appId` i zostawiają go na ostatnim kafelku; ta story domyka to testami, bez zmian w kodzie produkcyjnym.

**Files changed:**
- `web/shell/src/focus.test.ts` — testy `focusRequest` i powrotu na ostatni kafelek (ten sam `seq` z nowym `rev`, `appId` po przestawieniu, Home/Back, zmiana stanu pod fokusem, awaria z innej zakładki).
- `web/shell/src/Placeholder.test.tsx` — po starcie fokus ma jeden kafelek: pierwszy Video.

**Review:** 4 patches applied (testy), 0 deferred, 2 rejected (słaby test renderu — zostaje; `configError` — false). `followup_review_recommended: false`.

**Verification (UM760, 2026-10-26):** `npm test` w `web/` — 5 plików, 63 testy przeszły; `npm run typecheck` — bez błędów. Start aplikacji z fokusem na pierwszym kafelku Video pokrywa test renderu `Placeholder` na snapshocie z `focusRequest default` (jeden `tile--focused`, pierwszy Video); pełny przebieg na TV należy do odbioru epiki na UM760.

**Residual risks:** brak testu na poziomie `App.tsx` (glue mostu); logika jest w czystych funkcjach.
