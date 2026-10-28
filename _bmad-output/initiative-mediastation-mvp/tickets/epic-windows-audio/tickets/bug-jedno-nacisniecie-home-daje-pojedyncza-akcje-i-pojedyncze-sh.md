---
id: 15
type: bug
title: "Jedno naciśnięcie Home daje pojedynczą akcję i pojedyncze ShowShell"
parent: epic-windows-audio
covers: [R2, R3]
after: []
assignee: ""
refined: true
hitl: false
risk: medium
severity: P2
---

# Jedno naciśnięcie Home daje pojedynczą akcję i pojedyncze ShowShell

## Description

Podczas S10 jedno naciśnięcie Home doprowadziło do dwóch wpisów `action home` i dwóch komend `ShowShell`. Oczekiwane jest jedno przejście Home na jedno naciśnięcie, nawet gdy echo własnej komendy okiennej albo powtórzony sygnał trafia do systemu.

## Reproduction

1. Uruchomić świeży publish i doprowadzić aplikację do stanu z oknem na wierzchu.
2. Wcisnąć fizyczny Home jeden raz.
3. Actual: w oknie oceny pojawiają się dwa `action home` i dwa `ShowShell`.
4. Expected: pojawia się dokładnie jeden `action home` i jedna komenda `ShowShell`, zakończona sukcesem.

## Cause Hypothesis

Podwójny przebieg może powstawać przez powtórzenie sygnału wejściowego, równoległe ścieżki obsługi Home albo echo własnego `ForegroundChanged`/komendy okiennej. Trzeba ustalić źródło na podstawie znaczników źródła, czasu, transitionId i kolejności logów.

## Acceptance Criteria

1. **Jedno Home daje jedno przejście**
   **Given** shell lub aplikacja jest aktywna
   **When** użytkownik wysyła jedno naciśnięcie Home
   **Then** log zawiera dokładnie jedną akcję `home` i jedną komendę `ShowShell` dla tego przejścia.
2. **Powtórzenie i echo nie dublują przejścia**
   **Given** wejście zawiera repeat/echo własnej komendy okiennej
   **When** Orchestrator ocenia zdarzenia
   **Then** nie wysyła drugiego `ShowShell`, nie tworzy drugiego przejścia i nie zmienia stanu po raz drugi.
3. **Regresja ma test**
   **Given** test suite z kontrolowanym źródłem i czasem zdarzeń
   **When** test wysyła pojedyncze Home oraz wariant z powtórzeniem/echo
   **Then** test sprawdza liczbę akcji, komend, transitionId i brak podwójnych efektów.
4. **Home nadal działa po innych oknach**
   **Given** Edge, Moonlight lub pulpit jest na wierzchu
   **When** użytkownik wysyła jedno Home
   **Then** shell wraca na wierzch, a opuszczone okno jest obsłużone zgodnie z R1/R2.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md, R2/R3
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md, S10 (2026-10-28)
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-41

## Notes

- Decision: `hitl = false`; agent owns reproduction and automated proof, UM760 confirms the final behavior (2026-10-28).
- Medium-risk extra check: repeat the test after Alt+Tab and after a window command reports completion.
