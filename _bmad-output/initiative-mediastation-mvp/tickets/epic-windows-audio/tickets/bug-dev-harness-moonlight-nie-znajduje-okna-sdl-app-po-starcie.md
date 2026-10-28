---
id: 13
type: bug
title: "Dev harness Moonlight nie znajduje okna SDL_app po starcie"
parent: epic-windows-audio
covers: [R2, R8]
after: [12]
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Dev harness Moonlight nie znajduje okna SDL_app po starcie

## Description

Po uruchomieniu kafelka Moonlight przez MediaStation klient pozostaje na ustanawianiu połączenia, a dev harness kończy start błędem `windowTimeout` dla oczekiwanej klasy `SDL_app`. MediaStation nie publikuje `appWindowAttached`, więc nie może wykonać przełączenia okna ani pełnego scenariusza Home. Start ręczny z tym samym hostem i aplikacją jest punktem porównania.

## Reproduction

1. Na UM760 uruchomić świeży publish z `MEDIASTATION_DEV_HARNESS=1` i `MEDIASTATION_HARNESS_MOONLIGHT`.
2. Upewnić się, że host Sunshine jest osiągalny i skonfigurowany wpis `moonlight-pc`.
3. Nacisnąć OK na kafelku PC.
4. Actual: Moonlight pozostaje na „Ustanawianie połączenia z komputerem”, loguje `appLaunchStage ... windowTimeout expectedWindowClass=SDL_app`, `devHarnessStartFailed reason=windowTimeout` i brak `appWindowAttached`.
5. Expected: handshake kończy się, pojawia się okno strumienia, log zawiera `appWindowAttached` z `hwnd` i `rootPid`.

## Cause Hypothesis

Błąd leży w kontrakcie uruchomienia lub obserwacji procesu: parametry, środowisko albo katalog roboczy mogą różnić się od startu ręcznego, albo harness czeka na klasę/okno zanim Moonlight je utworzy lub wybiera niewłaściwe okno procesu. Należy rozdzielić etap handshake od etapu wyboru okna na podstawie logu porównawczego.

## Acceptance Criteria

1. **Start przez MediaStation kończy się działającym strumieniem**
   **Given** osiągalny host Sunshine i konfiguracja `moonlight-pc`
   **When** użytkownik uruchamia kafelek przez MediaStation
   **Then** handshake kończy się, pojawia się okno strumienia, a log zawiera `appWindowAttached`; timeout nie występuje.
2. **Diagnostyka rozróżnia etap awarii**
   **Given** start ręczny i start przez MediaStation
   **When** jeden z nich nie dochodzi do strumienia
   **Then** log porównawczy zawiera pełną komendę, argumenty, katalog roboczy, istotne środowisko, PID, klasy i uchwyty okien oraz etap handshake/window attach.
3. **Regresja jest pokryta testem**
   **Given** test suite
   **When** test uruchamia scenariusz z opóźnionym lub brakującym oknem
   **Then** test odtwarza `windowTimeout`, a po poprawce przechodzi dla właściwego okna `SDL_app`; test obejmuje też ponowny start po zamknięciu.
4. **Powrót po Home pozostaje sprawny**
   **Given** działający strumień
   **When** wykonuje się OK → Home → OK
   **Then** minimalizacja i przywrócenie okna działają bez regresji ticketu 5.8.
5. **Source conflict kończy ticket bez zmiany kodu tylko z dowodem**
   **Given** identyczne parametry startu i potwierdzony problem poza MediaStation
   **When** porównanie ręczne i przez MediaStation daje ten sam błąd
   **Then** Notes zawiera `Source conflict:` z logami obu uruchomień i decyzją architektoniczną.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md, R2/R8
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md, S6 (2026-10-28)
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-43, DW-47, DW-48

## Notes

- Decision: `hitl = false`; UM760 verification is acceptance evidence, while reproduction and regression tests are agent-owned (2026-10-28).
- High-risk extra check: compare a clean manual launch and a MediaStation launch before accepting the fix.
