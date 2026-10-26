---
title: 'Kropka „w tle” na zakładce'
type: 'feature'
ticket: '6'
created: '2026-10-26'
status: done
baseline_revision: 'd850ca87bed0996a237f6b87b3e50170ce66bd06'
route: 'oneshot'
route_source: 'auto'
risk: 'low'
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

**Problem:** Zakładka nie sygnalizuje, że jakaś aplikacja jej kategorii działa w tle (R2, AD-16).

**Approach:** Czysta funkcja `tabsWithBackground(apps)` w `focus.ts` (kategorie z aplikacją w stanie `background`, z `category` ze snapshotu); `Placeholder` rysuje w takiej zakładce `tab-running-dot` (12px, `--accent`, halo `--accent-halo`). Stany `foreground`, `failed` i inne nie zapalają kropki; powrót do `notRunning` ją gasi, bo kropka jest wyliczana ze snapshotu.

</intent-contract>

## Implementation Notes

Oneshot: zmiana ok. 40 linii w `web/shell/src` (`focus.ts`, `Placeholder.tsx`, `tokens.css`, testy); brak zmian w Core, moście i kontraktach.
- Maszyna: **UM760** (COMPUTERNAME `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`).
- `focus.ts`: `tabsWithBackground(apps)`; `Placeholder.tsx`: `<span class="tab-running-dot">` w zakładce; `tokens.css`: `--accent-halo` i `.tab-running-dot` (poza przepływem, po review); `Placeholder.test.tsx`: testy na snapshotach testowych.

## Review Triage Log

### 2026-10-26 — Review pass
- verdicts: 6 findings — high 0, medium 1, low 4, false 0, maybe-false 1
- findings:
  - `[medium]` `[patch]` Kropka w flexie `.tab` dodawała 24px szerokości i przesuwała kolejne zakładki — poprawione: `position: absolute` w rogu zakładki, bez `gap`; zero skoku układu.
  - `[low]` `[reject]` Niebieska kropka na białej pigułce fokusu ma niski kontrast — DESIGN.md nie definiuje wariantu kropki w fokusie; halo i kolor są wg tokenów; odbiór wizualny na UM760 w akceptacji epiki.
  - `[low]` `[reject]` Regex helpera testowego zależy od dokładnego markupu — test „powrót do NotRunning” sprawdza dodatni przypadek, więc dryf markupu wywali test, nie przejdzie po cichu.
  - `[low]` `[reject]` Test „powrót do NotRunning” to dwa niezależne rendery — kropka jest czystą pochodną snapshotu (AD-16), brak stanu do przejścia.
  - `[low]` `[reject]` Brak testów z fokusem w strefie `tabs` i wieloma aplikacjami w kategorii — `tabsWithBackground` nie zależy od fokusu; dodatkowa gałąź nie jest potrzebna.
  - `[maybe-false]` `[reject]` Brak alternatywy tekstowej dla kropki — `aria-hidden` jak sąsiednie dekoracje; TV bez czytnika ekranu, a opcjonalnie tylko `low` gdyby prawdziwe.

## Auto Run Result

Machine: ran on UM760 (host MEDIASTATION, board F7BSI, model EliteMini Series).

**Summary:** Zakładka pokazuje `tab-running-dot` (12px, accent, halo), gdy co najmniej jedna aplikacja jej `category` jest w `Background`; `foreground`, `failed` i pozostałe stany jej nie zapalają, a powrót do `notRunning` gasi (kropka wyliczana ze snapshotu).

**Files changed:** `web/shell/src/focus.ts`, `Placeholder.tsx`, `tokens.css`, `Placeholder.test.tsx`; plan file.

**Review:** quick lens, 6 findings: 1 patched (layout shift), 0 deferred, 5 rejected (reasons in the triage log).

**Verification (UM760):** `npm test` 56 passed; `npm run typecheck` ok; `npm run build:shell` ok. `dotnet test` nie uruchamiano (brak zmian w Core, moście i kontraktach). Przebiegu aplikacji nie wykonano; wygląd kropki (w tym na pigułce fokusu) zostaje do akceptacji epiki na UM760.

**Residual risks:** kontrast niebieskiej kropki na białej pigułce fokusu nieoceniony wizualnie.

Follow-up review recommended: false (one medium patched).
