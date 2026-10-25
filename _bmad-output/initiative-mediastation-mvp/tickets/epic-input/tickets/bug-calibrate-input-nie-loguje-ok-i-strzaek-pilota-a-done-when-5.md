---
id: 14
type: bug
title: "--calibrate-input nie loguje OK i strzałek pilota, a Done when 5 wymaga co najmniej 7 przycisków z VK"
parent: epic-input
covers: [CAP-3]
after: [12]
refined: true
hitl: true
risk: medium
severity: P2
---

# --calibrate-input nie loguje OK i strzałek pilota, a Done when 5 wymaga co najmniej 7 przycisków z VK

## Description

`MediaStation.exe --calibrate-input` ma zalogować `rawInput` z `hasVk` dla przycisków pilota (Home, Back, OK, strzałki), a nie zwykłej klawiatury. Na UM760 loguje tylko Home i Back; OK i strzałek nie ma w logu. Trzeba ustalić, czy to błąd kalibracji po story 3.12, czy te przyciski nie przychodzą kanałem, który Raw Input widzi, i wtedy poprawić kryterium (za zgodą użytkownika).

## Reproduction

Środowisko: UM760, HEAD 2bb3c95, pilot G20S Pro na donglu (VID_1915&PID_1025).

1. `MediaStation.exe --calibrate-input`, w ciągu 60 s nacisnąć Home, Back, OK, cztery strzałki, przycisk asystenta głosowego, potem zwykłą klawiaturę.
2. Trzy podejścia (2026-10-25: 12:58, 13:03, 13:08Z) dały zawsze `rawInput` tylko dla Home (vk 0xAC, usage 0x0223) i Back (vk 0xA6, usage 0x0224), `injected true`, `llBeforeRawMs null`; w trzecim dodatkowo asystent głosowy (usage 0x00CF, `hasVk false`, vk null). OK i strzałek brak w każdym.
3. Oczekiwane (epic-input, Odbiór na UM760, punkt 2): `rawInput` z `hasVk` dla przycisków pilota z VK, bez zwykłej klawiatury. Faktyczne: 2 wpisy z VK zamiast ≥7.

## Cause Hypothesis

Kalibracja po story 3.12 używa wąskiej ścieżki diagnostycznej (HID Consumer, bez klawiatury), więc przyciski pilota, które dla Windows są zwykłymi zdarzeniami klawiatury (OK, strzałki), nie trafiają do `rawInput`. To zgadza się z kryterium „bez zwykłej klawiatury”, ale wtedy te przyciski są nie do odróżnienia od klawiatury po stronie kalibracji. Możliwe też, że to defekt filtra sygnatury.

## Acceptance Criteria

1. **Przyczyna ustalona z dowodem**
   **Given** reprodukcja na UM760
   **When** kalibracja jest uruchomiona z naciśnięciem OK i strzałek
   **Then** plan podaje, czy ich brak w `rawInput` jest błędem, czy właściwością kanału, z logiem jako dowodem
2. **Zachowanie zgodne z wymaganiem**
   **Given** wynik z kryterium 1
   **When** `--calibrate-input` jest uruchomiony na UM760 z naciśnięciem każdego przycisku pilota
   **Then** `rawInput` jest zalogowane dla każdego przycisku, który ma VK, a zwykła klawiatura nadal nie jest logowana; albo Done when 5 i punkt 2 listy odbioru są poprawione za zgodą użytkownika, jeśli OK i strzałki nie mogą mieć wpisów
3. **Tests cover the condition found and fixed**
   **Given** the test suite
   **When** it runs
   **Then** a test that fails on the defect and passes on the fix covers the reproduction, and one covers each related case the fix touched
4. **Or: no change is needed, with proof**
   **Given** the reproduction
   **When** it is run on the current code
   **Then** the expected behavior already holds, or the report was mistaken, with the evidence recorded in Notes — this supersedes 1–3

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Done when 5, Odbiór na UM760 (2)
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance-2.md, S9 i S10
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8
- story — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/story-home-przy-shellu-na-wierzchu-bez-raw-input-klawiatury.md (3.12)

## Notes

- Boundaries: nie wybieramy tu przycisku Klawiatura ani nie zmieniamy wyzwalacza z VK na HID usage; to decyzja architektury (Source conflict w Notes epiki, 2026-10-25).
- Open question: czy brak OK i strzałek w kalibracji jest błędem, czy zamierzoną właściwością ścieżki bez Raw Input klawiatury (3.12).
- Decision: nie wiadomo, czy to błąd; bug diagnozuje (2026-10-25).
