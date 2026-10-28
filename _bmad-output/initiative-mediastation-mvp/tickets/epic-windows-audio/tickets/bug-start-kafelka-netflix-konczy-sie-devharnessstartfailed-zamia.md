---
id: 14
type: bug
title: "Start kafelka Netflix kończy się DevHarnessStartFailed zamiast otwarciem Edge"
parent: epic-windows-audio
covers: [R2, R8]
after: []
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Start kafelka Netflix kończy się DevHarnessStartFailed zamiast otwarciem Edge

## Description

Po OK na kafelku Netflix MediaStation pokazuje „Nie odpowiada”, a Edge nie otwiera się. Log nie zawiera `appWindowAttached` dla `netflix`, więc nie da się sprawdzić przełączenia okna ani audio. Oczekiwane jest otwarcie profilu harnessu i gotowość okna do dalszego sterowania.

## Reproduction

1. Uruchomić świeży publish z `MEDIASTATION_DEV_HARNESS=1` i konfiguracją odbioru Netflix.
2. Nacisnąć OK na kafelku Netflix.
3. Actual: kafelek przechodzi do „Nie odpowiada”, Edge nie otwiera się, a log zawiera `devHarnessStartFailed` lub `windowTimeout` bez `appWindowAttached`.
4. Expected: Edge otwiera profil `netflix`, loguje `appWindowAttached`, a kafelek przechodzi do stanu działającego.

## Cause Hypothesis

Regresja może dotyczyć ścieżki startu web: profilu użytkownika, URL, parametrów Edge, wykrycia procesu/okna albo limitu oczekiwania. Hipotezę trzeba rozstrzygnąć logiem procesu i porównaniem z ostatnim działającym buildem.

## Acceptance Criteria

1. **Kafelek Netflix uruchamia Edge**
   **Given** świeży publish i poprawny profil `netflix`
   **When** użytkownik naciska OK
   **Then** Edge otwiera się, pojawia się `appWindowAttached` dla `netflix`, a stan nie przechodzi do „Nie odpowiada”.
2. **Błąd startu jest rozróżnialny**
   **Given** brak procesu, błąd profilu, błąd URL albo timeout okna
   **When** start nie powiedzie się
   **Then** log zawiera etap, PID, komendę, argumenty, katalog roboczy i przyczynę możliwą do odróżnienia od błędu WindowManagera.
3. **Regresja ma test**
   **Given** test suite
   **When** uruchamiany jest start z profilem i z błędnym/niegotowym oknem
   **Then** test odtwarza błąd i przechodzi po poprawce, a test poprawnego profilu potwierdza `appWindowAttached`.
4. **Start nie psuje dalszego sterowania**
   **Given** działający Edge
   **When** użytkownik wykonuje Home i ponownie OK
   **Then** okno jest minimalizowane/przywracane zgodnie z epiką, bez nowego procesu przeglądarki.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md, R2/R8
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md, S9 (2026-10-28)
- dependency — _bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost.md

## Notes

- Decision: `hitl = false`; implementation and regression tests are agent-owned, with UM760 smoke evidence before acceptance (2026-10-28).
- High-risk extra check: rerun the same launch after a clean process/profile reset and compare logs with the previous good build.
