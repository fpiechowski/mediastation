---
id: 2
type: story
title: "SystemEvents: wybudzenie z Modern Standby na UM760"
parent: epic-orchestrator-lifecycle
covers: [CAP-2]
after: [1]
hitl: false
risk: high
---

# SystemEvents: wybudzenie z Modern Standby na UM760

## Description

Dodaje produkcyjny adapter power w `Platform/System`, który na aktualnej maszynie loguje każdy kandydujący sygnał wybudzenia jako `powerSignal {source}` (`PBT_APMRESUMEAUTOMATIC`, `GUID_CONSOLE_DISPLAY_STATE`, `PowerModeChanged`), wybiera jeden według reguły z Notes epiki (deduplikacja w oknie 5 s działa w adapterze w App, nie w Core) i wysyła do kolejki Orchestratora Event `SystemResumed` z wpisem logu `systemResumed`, na który Orchestrator przechodzi na ekran główny i publikuje snapshot z nowym `seq` `focusRequest{default}`; pełną politykę wybudzenia (Background dla aplikacji na pierwszym planie, `RenewSession`, zastępowanie przełączenia) dokłada story 8.

## Acceptance Criteria

Verify: Test Core sprawdza `SystemResumed` (`powerSignal`, `systemResumed`, `snapshotPublished` z nowym `seq`); na aktualnej maszynie uruchomić app i sprawdzić logi, a jeśli Modern Standby/wybudzenie nie jest dostępne, zanotować to i zaakceptować testy automatyczne; UM760 nie jest wymagany.

## References

- parent — _bmad-output/initiative-mediastation-mvp/epic-orchestrator-lifecycle/epic-orchestrator-lifecycle.md
- architecture-mediastation/architecture-mediastation.md, AD-10
- research-browser-control-for-streaming-services/digests/spike-um760.md, claim S7/S8 (CDP zrywa się po Modern Standby)

## Notes

- Kontrola zewnętrzna (risk high): epika jest budowana w pełni automatycznie; test wybudzenia jest best-effort na aktualnej maszynie, a jeśli Modern Standby nie jest dostępny, zanotuj to i zaakceptuj testy Core.
- Reguła wyboru sygnału i deduplikacji jest w Notes epiki (Decision z 2026-10-21); autor może ją zmienić przy odbiorze epiku na podstawie pomiaru.
- Automated best-effort (current machine): uruchomić build i `MediaStation.exe`; jeśli Modern Standby jest dostępny, uśpić i wybudzić, a w logu sprawdzić `powerSignal`, jeden `systemResumed` i `snapshotPublished` z nowym `seq`; jeśli nie, zanotować brak sprzętu i zaakceptować test Core.
- Automated acceptance: wynik z datą, HEAD i nazwą maszyny trafia do notatki odbioru epiku; UM760 nie jest wymagany.
