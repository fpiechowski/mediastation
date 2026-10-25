---
id: 13
type: bug
title: "check-home-s10: Home przy zawieszonym shellu i cel Edge nie dają 16/16 na UM760"
parent: epic-input
covers: [CAP-3]
after: [12]
refined: true
hitl: true
risk: high
severity: P1
---

# check-home-s10: Home przy zawieszonym shellu i cel Edge nie dają 16/16 na UM760

## Description

`tools/check-home-s10.ps1` nie daje 16/16 na UM760, więc Done when 3 epiki nie jest spełnione. Powinno być 16/16 (Edge, Moonlight, pulpit, zawieszony shell × 4) i 4/4 w przebiegu „shell na wierzchu”, każdy Home jako jedna `action home` i bez nowego okna przeglądarki. Diagnoza rozstrzyga, czy winny jest skrypt (cel Edge, cel zawieszony), czy produkt (Home przy zawieszonym UI).

## Reproduction

Środowisko: UM760, HEAD 2bb3c95, build 1.0.0+2bb3c95, pilot G20S Pro na donglu.

1. `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1`, po sygnale naciskać Home na pilocie.
2. Przebieg 1, pierwsze podejście (żadne okno Edge ani Moonlight nie było otwarte): 4/16; edge i moonlight `no target window` × 8; shell-hung 2 × `browser window`, 2 × `target not foreground`; pulpit 4/4. Przebieg 2 „shell na wierzchu”: 4/4. Log: `%LOCALAPPDATA%\MediaStation\logs\check-home-s10-20261025-184223.log`.
3. Powtórka z oknami otwartymi przez użytkownika: 8/16; moonlight 4/4, pulpit 4/4, edge 4 × `no target window` mimo otwartego okna, shell-hung jak wyżej; „shell na wierzchu” 4/4. Log: `check-home-s10-20261025-184645.log`.

Oczekiwane: 16/16 i 4/4, exit 0. Faktyczne: exit 1.

## Cause Hypothesis

Edge: skrypt nie rozpoznaje okna docelowego procesu Edge (wykrywanie okna, nie Home), bo Moonlight i pulpit z tym samym mechanizmem Home przechodzą. Shell-hung: „browser window” wskazuje, że przy zawieszonym UI Home mimo to otwiera przeglądarkę, czyli hook nie połknął Home albo połknął po czasie; „target not foreground” może być skutkiem tego samego lub kolejności celów po zabiciu shella. Nie wiadomo, które z trzech to produkt, a które skrypt.

## Acceptance Criteria

1. **Diagnoza z dowodem**
   **Given** reprodukcja na UM760
   **When** każda z trzech niezgodności (Edge `no target window`, shell-hung `browser window`, shell-hung `target not foreground`) zostanie zbadana
   **Then** plan wskazuje dla każdej, czy to skrypt, czy produkt, z logiem aplikacji i logiem skryptu jako dowodem
2. **16/16 i 4/4 na UM760**
   **Given** build z poprawką, okna Edge i Moonlight przygotowane przez skrypt albo opisaną w nim procedurę
   **When** uruchomiony jest `tools/check-home-s10.ps1` bez zmiany asercji po fakcie
   **Then** wynik to 16/16 i 4/4, exit 0, każdy Home daje jedną `action home {source: remote}`, żadne nowe okno przeglądarki się nie otwiera; wynik powtórzony w co najmniej dwóch uruchomieniach
3. **Tests cover the condition found and fixed**
   **Given** the test suite
   **When** it runs
   **Then** a test that fails on the defect and passes on the fix covers the reproduction (dla produktu w Core/InputHub; dla skryptu `-SelfTest`), and one covers each related case the fix touched
4. **Or: no change is needed, with proof**
   **Given** the reproduction
   **When** it is run on the current code
   **Then** the expected behavior already holds, or the report was mistaken, with the evidence recorded in Notes — this supersedes 1–3

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Done when 3
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance-2.md, S12 i uwaga S12
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8
- story — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/story-home-zawsze-zawieszony-ui-i-skrypt-s10-plan.md (3.9)
- logs — %LOCALAPPDATA%\MediaStation\logs\check-home-s10-20261025-184223.log, check-home-s10-20261025-184645.log

## Notes

- Boundaries: nie zmieniać AD-8 poza tym, co wynika z diagnozy; nie wracać shella na wierzch po Home (epic-windows-audio).
- Check beyond criteria (risk high): zawieszony shell powtórzony na tym samym buildzie, z `MEDIASTATION_DEBUG_HANG_UI`, w dwóch niezależnych uruchomieniach.
- Decision: bug diagnozuje, nie zakłada winnego; S12 użytkownik powtórzy na UM760 przy odbiorze całej epiki (2026-10-25).
