---
id: 23
type: bug
title: "Po starcie Netflixa albo wybudzeniu ekran kilka razy gaśnie i wraca z szumem"
parent: none
covers: []
after: []
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Po starcie Netflixa albo wybudzeniu ekran kilka razy gaśnie i wraca z szumem

## Description

Po uruchomieniu Netflixa przez kafelek albo po wybudzeniu UM760 ekran kilka razy gaśnie i wraca z szumem, a po chwili sam wraca do normy. To ten sam objaw co w bugu 18 z epic-windows-audio, zamkniętym zmianą aktywacji okna (`6a6504e`), której plan nie zawiera dowodu przyczyny. Eliza traci obraz w chwili, gdy zaczyna oglądać, i nie ma jak temu zaradzić pilotem.

## Reproduction

1. Na **UM760** (host `MEDIASTATION`, płyta `F7BSI`, model `EliteMini Series`) uruchom świeży publish bez dev harnessu, ekran DELL P2415Q 3840x2160 po DisplayPort, profil `profiles\netflix` zalogowany.
2. Wciśnij OK na kafelku Netflix i patrz na ekran przez około minutę.
3. Uśpij komputer, gdy Netflix działa, i obudź go po kilkunastu sekundach.
4. Actual: w odbiorze epic-browserhost (2026-10-30, build `b041c5d`) ekran gasł i wracał z szumem kilka razy po pierwszym starcie (S4, `ActivateWindow completed` 13:22:13Z) i po wybudzeniu (notatka z S19, `systemResumed` 13:41:05Z). To około 2 razy na 6 otwarć i wybudzeń.
5. Expected: obraz pozostaje stabilny przy każdym starcie i wybudzeniu.

## Cause Hypothesis

Przyczyna jest nieznana. Log MediaStation nie ma w tych chwilach `warn` ani `error`, a Windows nie zapisał zdarzeń wyświetlania. Niesprawdzona hipoteza to ponowne uzgadnianie HDCP przy PlayReady SL3000. Bug 18 zakładał konflikt aktywacji okna z paskiem zadań; ta zmiana nie usunęła objawu, ale nie jest wykluczone, że go ogranicza. Netflix w zwykłym Edge bez MediaStation, na tym samym profilu, przez około 2 minuty nie dał objawu, lecz przy tej częstości jedna próba niczego nie rozstrzyga.

## Acceptance Criteria

1. **Przyczyna jest rozstrzygnięta**
   **Given** seria co najmniej 15 startów i wybudzeń przez MediaStation oraz co najmniej 15 startów Netflixa w Edge bez MediaStation, na UM760
   **When** dla każdej próby zapisany jest wynik obserwacji obrazu i odpowiadający fragment logu
   **Then** plan wskazuje ścieżkę, która wywołuje objaw, albo zawiera dowód `Source conflict:`, że przyczyna leży poza MediaStation

2. **Objaw nie występuje po poprawce**
   **Given** przyczyna leży w MediaStation i poprawka jest zbudowana
   **When** użytkownik wykonuje co najmniej 15 startów i wybudzeń
   **Then** ekran ani razu nie gaśnie i nie pokazuje szumu

3. **Poprawka ma test**
   **Given** poprawka zmienia kod MediaStation
   **When** działają testy .NET
   **Then** test przypina zmienione zachowanie i oblewa na kodzie przed poprawką

4. **Przyczyna poza MediaStation**
   **Given** dowód z kryterium 1 wskazuje przyczynę poza MediaStation
   **When** ticket się kończy
   **Then** Notes zawierają dowód i obejście dla użytkownika, a kryteria 2 i 3 nie dotyczą

## References

- ticket — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/tickets/bug-uruchomienie-netflixa-przez-kafelek-wywoluje-cykliczne-zaniki-obrazu.md` i jego plan (bug 18, `6a6504e`)
- acceptance — `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost-um760-acceptance.md`, S4, notatka z S19, obserwacje po S46
- architecture — `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md`, AD-1 (`--kiosk`), AD-10 (wybudzenie)

## Notes

- Decision: bug 18 zostaje zamknięty, bo należy do zaakceptowanej epiki; nawrót objawu śledzi ten ticket (2026-10-30).
- Decision: użytkownik zaakceptował epic-browserhost, wiedząc o tym objawie (2026-10-30).
- Assumption: serie po 15 prób przy częstości około 1/3 dają szansę poniżej 1%, że objaw istnieje, a nie wystąpi ani razu; plan może zmienić tę liczbę, gdy częstość okaże się inna.
- Weryfikacja: automat nie widzi obrazu. Agent przygotowuje serię na bieżącej maszynie, a obserwacje zbiera PO z użytkownikiem przed `done`. Bez UM760 lub ekranu kryteria 1 i 2 oznacza się jako nie dotyczy z powodem.
