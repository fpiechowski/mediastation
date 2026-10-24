---
id: 11
type: spike
title: "Dlaczego hook WH_KEYBOARD_LL nie dostaje klawiszy, gdy shell MediaStation jest na wierzchu na UM760?"
parent: epic-input
covers: [CAP-3]
after: []
refined: true
hitl: true
risk: high
---

# Dlaczego hook WH_KEYBOARD_LL nie dostaje klawiszy, gdy shell MediaStation jest na wierzchu na UM760?

## Description

Na UM760 przy shellu na wierzchu (WebView2) hook LL nie dostaje żadnego klawisza, a Home z G20S Pro (HID Consumer `0x0223` → wstrzyknięty `VK_BROWSER_HOME`) otwiera Edge przez powłokę (odbiór S2). Spike S10 zapisał tę słabość i uznał za przyczynę wątek UI zajęty `WM_INPUT`. InputHub ma już osobny wątek Input, a hooki i Raw Input `RIDEV_INPUTSINK` nadal działają na jednym wątku, więc przyczyna jest niepotwierdzona.

Czekają na odpowiedź: odbiór `epic-input` (Done when 3) oraz `epic-windows-audio`, `epic-netflix-dpad`, `epic-osk` i `epic-moonlight-native`, które zakładają działające Home. Odpowiedź trafia do `bmad-architecture` (AD-8), potem poprawki w `bmad-build`.

To spike, nie odbiór: nie zmienia statusu odbioru epiki ani nie zamyka jej. `hitl`: człowiek naciska Home na pilocie G20S Pro na UM760 i patrzy, czy otwiera się Edge.

## Approach

1. Wariant bez `foregroundGate` (zmienna `MS_NO_GATE` nie istnieje w kodzie, więc dodaje ją spike). Cel: czy winna jest bramka, czy hook.
2. Hook bez `RegisterRawInputDevices`.
3. Raw Input na osobnym wątku od hooków.
4. Puste okno bez WebView2 z tym samym hookiem.
5. Jeśli 1–4 nie dadzą rozwiązania: alternatywy dla połykania Home (`WM_APPCOMMAND`, `RIDEV_NOLEGACY`, filtr HID).
6. Po każdym eksperymencie jeden wiersz w tabeli wyników: co było na wierzchu, czy hook dostał „down", liczba wpisów `action`, czy otworzył się Edge. Eksperyment, który wskaże przyczynę, kończy kolejne.
7. Kod eksperymentów tylko na gałęzi spike'u; zmiany w `master` wchodzą dopiero po decyzji dla AD-8.

Zakończenie: jedna sesja na UM760. Dość dobre to ustalona przyczyna albo wyczerpana lista 1–5.

## Acceptance Criteria

1. **Każdy wykonany eksperyment ma wynik: hook dostaje „down" przy shellu na wierzchu albo nie.**
   Verify: tabela wyników w sekcji „Wynik" tego pliku, z odnośnikami do logów z UM760.
2. **Wybrany wariant daje na UM760 jedną `action {kind: home}` i zero nowych okien przeglądarki przy shellu na wierzchu, w 3 przebiegach pod rząd.**
   Verify: logi `mediastation-*.jsonl` i `check-home-s10-*.log` z trzech przebiegów, odnośniki w sekcji „Wynik".
3. **Decyzja i jej skutki dla AD-8 są zapisane tak, że `bmad-architecture` i `bmad-build` mogą z nich skorzystać.**
   Verify: sekcja „Wynik" w tym pliku oraz `Source conflict:` w Notes AD-8, jeśli decyzja zmienia architekturę.
4. **Użytkownik zaakceptował wynik.**
   Verify: linia `Decision:` w Notes z datą.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Done when 3, Notes (otwarte pytanie o „down" hooka)
- research — spike/spike-results.md, S10 (przebieg rozstrzygający i słabość przy oknie z hookiem na wierzchu)
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance.md, S2 i „Wstrzymanie odbioru (2026-10-24)"
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8

## Notes

- Assumption: ticket nie zmienia statusu odbioru epiki (2026-10-24).
- Assumption: czas to jedna sesja na UM760, bo użytkownik nie podał własnego limitu (2026-10-24).
- Open question: czy przy eksperymentach 1–4 wystarczy sprawdzać tylko Home, czy też Back i OK (dziś Back i OK docierają, gdy na wierzchu jest obce okno).
