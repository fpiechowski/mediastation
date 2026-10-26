---
id: 4
type: story
title: "`--calibrate-input` zapisuje wybrany przycisk Klawiatura do input.json"
parent: none
covers: []
after: []
assignee: ""
refined: false
hitl: true
risk: medium
---

# `--calibrate-input` zapisuje wybrany przycisk Klawiatura do input.json

## Description

Po kalibracji przycisk pilota wybrany jako Klawiatura trafia do `input.json` bez ręcznej edycji: VK do `keys`, usage bez VK do `hid` (AD-8). Czysta funkcja zapisu istnieje i jest przetestowana (3.17), ale nie jest podpięta do trybu `--calibrate-input`, więc dziś mapowanie `0x000C:0x00CF` wpisuje się ręcznie. Osoba przy pilocie naciska wybrany przycisk, a kalibracja zapisuje wynik.

## Requirements

Verify: na UM760 z pilotem G20S Pro na donglu: kalibracja z wybranym przyciskiem asystenta zapisuje `"hid": { "0x000C:0x00CF": "keyboard" }` w `input.json`, a następny start aplikacji daje `action {kind: keyboard, source: remote}` po jego wciśnięciu. Kryteria numerowane przy refine.

## Boundaries

- Must not change: normalna praca InputHub, logi bez surowych kodów poza `--calibrate-input` (AD-13), dotychczasowy przebieg `--calibrate-input` bez nowej flagi.
- Must not add: tryb kalibracji z jednoczesnym hookiem LL i Raw Input klawiatury w jednym procesie (AD-8).

## References

- parent — none
- epic — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Notes (decyzja o zamknięciu 2026-10-26), Done when 5
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance-3.md, Uwagi i werdykt
- plan — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/story-kalibracja-przycisk-klawiatura-takze-bez-vk-zapis-do-hid-plan.md
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8, AD-13

## Notes

- Decision: zapis z kalibracji wypadł z zakresu epiki 3 przy odbiorze (zawężenie zatwierdzone przez użytkownika, 2026-10-26); to story je domyka.
- Decision: wybór przycisku to osobna flaga trybu kalibracji (np. `--calibrate-input --pick-keyboard`): kalibracja kończy się na pierwszym przycisku pilota przychodzącym jako raport HID Consumer, zapisuje go jako Klawiaturę i kończy z exit 0; bez flagi tryb działa jak dziś (logowanie przez 60 s, bez zapisu). (2026-10-26).
- Assumption: zapis idzie do `input.json` w `%LOCALAPPDATA%\MediaStation\config`, atomowo, z kopią poprzedniej wersji; wynik niezgodny ze schematem nie jest zapisywany (jak w `KeyboardButtonMapping`).
- Open question: komunikat dla osoby przy pilocie (log, okno, nic), bo tryb nie ma UI; rozstrzyga refine.
- Decision: ledger DW-24 (dedupe usages w kalibracji czyści wszystkie usages przy dowolnym key-up pilota; klucz VK→usage zamiast globalnego resetu) wchodzi do tej story, bo dotyka tego samego `InputCalibration` (refinement epiki 4, 2026-10-26).
