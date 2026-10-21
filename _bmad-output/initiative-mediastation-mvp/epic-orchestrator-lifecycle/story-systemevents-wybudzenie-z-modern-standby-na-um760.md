---
id: 2
type: story
title: "SystemEvents: wybudzenie z Modern Standby na UM760"
parent: epic-orchestrator-lifecycle
covers: [CAP-2]
after: [1]
hitl: true
risk: high
---

# SystemEvents: wybudzenie z Modern Standby na UM760

## Description

Dodaje produkcyjny adapter power w `Platform/System`, który na UM760 loguje każdy kandydujący sygnał wybudzenia jako `powerSignal {source}` (`PBT_APMRESUMEAUTOMATIC`, `GUID_CONSOLE_DISPLAY_STATE`, `PowerModeChanged`), wybiera jeden według reguły z Notes epiki (deduplikacja w oknie 5 s działa w adapterze w App, nie w Core) i wysyła do kolejki Orchestratora Event `SystemResumed` z wpisem logu `systemResumed`, na który Orchestrator przechodzi na ekran główny i publikuje snapshot z nowym `seq` `focusRequest{default}`; pełną politykę wybudzenia (Background dla aplikacji na pierwszym planie, `RenewSession`, zastępowanie przełączenia) dokłada story 8.

## Acceptance Criteria

Verify: Na UM760 uśpienie do Modern Standby i wybudzenie pilotem zostawia w logu `powerSignal` każdego kandydata, jeden `systemResumed` i `snapshotPublished` z `focusRequest{default}` o `seq` większym niż przed uśpieniem, a test Core sprawdza ten sam wynik dla `SystemResumed`.

## References

- parent — _bmad-output/initiative-mediastation-mvp/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle.md
- architecture-mediastation/architecture-mediastation.md, AD-10
- research-browser-control-for-streaming-services/digests/spike-um760.md, claim S7/S8 (CDP zrywa się po Modern Standby)

## Notes

- Kontrola zewnętrzna (risk high): autor zatwierdza plan z wybranym sygnałem przy `plan_checkpoint` i potwierdza check na UM760 przy `done_checkpoint`.
- Reguła wyboru sygnału i deduplikacji jest w Notes epiki (Decision z 2026-10-21); autor może ją zmienić przy `plan_checkpoint` na podstawie pomiaru.
- HITL (autor, UM760): zainstalować build tej story jak w story 1 i uruchomić `MediaStation.exe`; zanotować czas; Start → Zasilanie → Uśpij; po co najmniej 2 min wybudzić pilotem przez dongle USB 2.4G (jeśli pilot nie budzi, przyciskiem zasilania, i zanotować to); w logu dnia od zanotowanego czasu sprawdzić `powerSignal` każdego kandydata, jeden `systemResumed` i `snapshotPublished` z `focusRequest{default}` o `seq` większym niż przed uśpieniem.
- HITL: wynik z datą, HEAD, nazwą maszyny i listą zaobserwowanych sygnałów trafia do `Verification` planu; maszyna dev nie spełnia checku bez decyzji autora.
