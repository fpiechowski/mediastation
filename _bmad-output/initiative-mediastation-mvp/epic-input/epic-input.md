---
type: epic
title: "Remote, keyboard & gamepad input"
parent: initiative-mediastation-mvp
covers: [CAP-3]
after: []
assignee: ""
risk: high
---

# Remote, keyboard & gamepad input

## Description

CAP-3: pilot G20S Pro przez dongle USB 2.4G, klawiatura i gamepad sterują MediaStation jednym zestawem Actions, kierowanych według bieżącego trybu (`shell`, `app`, `osk`), a Home działa zawsze. Epika dostarcza InputHub (własny wątek z hookiem `WH_KEYBOARD_LL`, Raw Input, XInput), mapowanie z `input.json`, w tym przycisku pilota i klawisza dla Action `Keyboard`, lokalną kopię routingu z `SetInputRouting` oraz tryb `--calibrate-input`.

## Outcome

Każde wejście dociera do Core jako jedna Action, a Home nie zależy od wątku UI ani okna na wierzchu; sygnałem jest powtórzenie S10 z pilotem na dongle 16/16.

## Done when

1. Jedno naciśnięcie pilota, klawiatury albo gamepada daje jedną Action (AD-8).
2. Klawisz z przypisanym `long` (OK w `shell` i `osk`) daje `press` dopiero na key-up przed progiem albo samo `long`, od Story 1 (AD-8).
3. Na UM760 Home jest połykany i dostarczany jako Action nad Edge, Moonlightem, pulpitem i przy zawieszonym wątku UI shella; powtórzenie S10 z pilotem na dongle USB 2.4G daje 16/16 (AD-2, AD-8).
4. Logi zawierają tylko Actions; surowe kody pojawiają się wyłącznie w `--calibrate-input` (AD-13).
5. Przycisk pilota wybrany w `--calibrate-input` daje Action `Keyboard`, a jego mapowanie jest w `input.json` (AD-8).

## Boundaries

`Platform/Input` i `config/input.json`. Nie: przywrócenie shella na wierzch po Home (epic-windows-audio), decyzja o trybie (Orchestrator, epic-orchestrator-lifecycle).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-3, Open Questions (Przycisk Klawiatura na G20S Pro, hook LL)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-2, AD-8, AD-13 (Logi)
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S10
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Interaction Primitives

## Notes

- Open question: który przycisk G20S Pro jest Klawiaturą. Rozstrzyga Story 1 (`--calibrate-input`).
- Open question: czy hook LL we własnym wątku dostaje „down” Home i Back, także gdy MediaStation jest na wierzchu. Rozstrzyga ta sama Story 1 (prototyp InputHub).
- Decision: Story 1 to jedna spike/prototyp story: InputHub na własnym wątku, `--calibrate-input`, S10 z dongle i wybór przycisku Klawiatura oraz reguła `long` dla OK (2026-10-18).
- Decision: pilot działa przez dongle USB 2.4G; Bluetooth nie jest wymagany (change-osk-everywhere-github-update, 2026-10-18).
- Decision: epika idzie równolegle z epic-orchestrator-lifecycle; rekord `SetInputRouting` jest w kontraktach z epic-foundation-contracts (2026-10-18).
- Waits on epic-foundation-contracts because: rekordy `Action`/`SetInputRouting`, schemat `input.json`, logger, wejście CLI.
