---
id: 19
type: bug
title: "Formalna kolejność wyciszenia i minimalizacji w S16 jest niespójna"
parent: epic-windows-audio
covers: [R7]
after: []
refined: true
hitl: false
risk: high
severity: P2
---

# Formalna kolejność wyciszenia i minimalizacji w S16 jest niespójna

## Description

S16 na UM760 nie przeszło formalnej asercji, choć użytkownik potwierdził wyciszenie Netflixa, a log zawierał `sessions=2` i zakończone minimalizowanie okna. Problem dotyczy niezgodności kontraktu kolejności zdarzeń: asercja wymagała `audioRuleApplied` przed `MinimizeWindow`, lecz przebieg zarejestrował odwrotną kolejność. Ticket ma rozstrzygnąć właściwy kontrakt i doprowadzić do zgodności implementacji lub asercji; nie obejmuje naprawy w tym wątku.

## Reproduction

1. Na UM760 (`MEDIASTATION / F7BSI / EliteMini Series`) uruchom build `HEAD 6a6504e` z przebiegiem odbioru Netflixa.
2. Otwórz Netflix przez kafelek, rozpocznij odtwarzanie i naciśnij fizyczny Home.
3. Uruchom formalny `check` dla S16 z `--ordered`.

Rzeczywisty wynik: wartości przeszły (`audioRuleApplied muted=true`, `sessions=2`; `MinimizeWindow result=completed`), ale `order=false`. W oknie czasowym `2026-10-29T13:42:46.975Z`–`13:43:23.391Z` check zgłosił kolejność `MinimizeWindow` → `audioRuleApplied`; cytowany wpis: `2026-10-29T13:43:20.883Z audioRuleApplied rootPid=20340 muted=true sessions=2`. Oczekiwany wynik: zaakceptowana i jawnie ustalona kolejność kontraktu, zgodna z implementacją oraz asercją.

## Cause Hypothesis

Kontrakt logowania może przedstawiać zakończenie komendy okiennej wcześniej niż zastosowanie reguły audio, mimo że wymaganie R7 opisuje przejście `mute → minimize`; alternatywnie asercja może wymuszać kolejność, której implementacja celowo nie gwarantuje. Przyczyną może być więc implementacja sekwencji, moment emisji logu albo zbyt silna asercja — ticket nie zakłada z góry, która z tych hipotez jest prawdziwa.

## Acceptance Criteria

1. **Kontrakt kolejności S16 jest rozstrzygnięty i zapisany w dowodzie**
   **Given** R7 oraz przebieg z `MinimizeWindow completed` i `audioRuleApplied muted=true sessions=2`
   **When** ticket jest analizowany
   **Then** kryterium wskazuje, czy kontrakt wymaga `audioRuleApplied` przed `MinimizeWindow`, czy dopuszcza kolejność logowania z 2026-10-29, wraz z uzasadnieniem dla implementacji i asercji.
2. **Implementacja albo asercja jest zgodna z rozstrzygniętym kontraktem**
   **Given** scenariusz reprodukcji S16
   **When** uruchamia się formalny check
   **Then** S16 przechodzi z `sessions >= 1`, `muted=true`, `MinimizeWindow result=completed` i właściwą kolejnością; jeśli obecne zachowanie jest kontraktem, asercja zostaje skorygowana bez osłabiania pozostałych warunków.
3. **Regresja jest zabezpieczona testem**
   **Given** test suite
   **When** testy uruchamiają przypadek z kolejnością zdarzeń S16
   **Then** test failuje przed zmianą i przechodzi po zmianie, a pokrycie obejmuje także powiązaną ścieżkę Home/audio.
4. **S16 jest ponownie zweryfikowane na UM760**
   **Given** wymagany commit z poprawką lub korektą asercji
   **When** użytkownik powtarza S16 na UM760
   **Then** formalny check przechodzi, a dowód zawiera commit, timestampy i wynik kolejności; S15, S17 i S20 nie mają regresji.

## References

- parent — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`
- acceptance plan — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`, S16, final user verdict 2026-10-29
- epic requirement — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`, R7 / AD-5
- log and check — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`, entry 2026-10-29T13:43:20.883Z and S16 formal check (`order false`)

## Notes

- Open question: czy prawidłowym kontraktem obserwowalnym dla S16 jest kolejność `audioRuleApplied` → `MinimizeWindow`, czy tylko stan końcowy i kolejność logowania może być odwrotna.
- Required commit: poprawka implementacji albo asercji, test regresji i ponowny odbiór S16 na UM760 muszą zostać dostarczone w jednym wskazanym commicie przed zamknięciem ticketu.
