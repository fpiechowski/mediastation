---
id: 18
type: bug
title: "Uruchomienie Netflixa przez kafelek wywołuje cykliczne zaniki obrazu"
parent: epic-windows-audio
covers: [R2, R3, R5]
after: [17]
assignee: ""
refined: true
hitl: true
risk: high
severity: P1
---

# Uruchomienie Netflixa przez kafelek wywołuje cykliczne zaniki obrazu

## Description

Po poprawnym uruchomieniu Netflixa przez kafelek MediaStation ekran UM760 cyklicznie gaśnie i wraca do szumu/static. Objaw powtórzył się kilka razy, nie występował przed uruchomieniem Netflixa przez MediaStation i ustąpił natychmiast po zamknięciu MediaStation. Oczekiwane jest stabilne wyświetlanie obrazu po starcie Netflixa, bez zmiany trybu wyświetlania poza zamierzoną aktywacją okna.

## Reproduction

1. Na UM760 uruchomić świeży publish z `MEDIASTATION_DEV_HARNESS=1`, profilem `%LOCALAPPDATA%\MediaStation\profiles\netflix` i preflightem zalogowanego, odtwarzanego `netflix.com`.
2. Uruchomić Netflix przez kafelek MediaStation i obserwować ekran oraz log `%LOCALAPPDATA%\MediaStation\logs\mediastation-<data>.jsonl`.
3. Powtórzyć uruchomienie po zamknięciu aplikacji lub powrocie na ekran główny, zachowując logi i zapis obserwacji obrazu.
4. Actual: ekran cyklicznie przechodzi w czerń i wraca do szumu/static. W odbiorze z 2026-10-29 log zawierał `appLaunch appId=netflix url=https://www.netflix.com/` o `2026-10-29T12:11:41Z`, `appWindowAttached hwnd=131950 rootPid=9792`, `ActivateWindow completed` oraz `audioRuleApplied muted=false sessions=0`.
5. Expected: obraz pozostaje stabilny przy uruchomieniu i wielokrotnym przełączaniu Netflixa; log potwierdza tylko zamierzone operacje okna/audio i nie ma błędu ani zdarzenia wskazującego niezamierzoną zmianę display mode.

## Cause Hypothesis

Przyczyna może leżeć w uruchomieniu Edge/WebView2 lub jego profilu, w sekwencji `ActivateWindow`/foreground, w routingu audio albo wyciszeniu, w parametrach kiosk/display mode, albo w innym działaniu MediaStation wpływającym na powierzchnię obrazu. Trzeba rozdzielić te hipotezy logiem etapów procesu, okna i audio oraz obserwacją obrazu; ticket nie narzuca konkretnej poprawki.

## Acceptance Criteria

1. **Przyczyna zaniku obrazu jest rozstrzygnięta**
   **Given** powtarzalny start Netflixa przez kafelek na UM760 z profilem `profiles\\netflix`
   **When** zespół porówna start Edge/WebView2, adopcję/aktywację okna, routing/mute audio, parametry kiosk/display mode i działania MediaStation
   **Then** log oraz zapis obserwacji obrazu wskazują, która ścieżka wywołuje black-screen/static, albo zawierają dowód `Source conflict:` że przyczyna jest poza MediaStation.
2. **Netflix nie powoduje zaniku obrazu**
   **Given** poprawnie przygotowany profil i działający `netflix.com`
   **When** użytkownik uruchamia Netflix przez kafelek i wykonuje co najmniej trzy cykle uruchomienie → Home → ponowne uruchomienie
   **Then** obraz pozostaje stabilny; nie występuje cykliczna czerń ani szum/static, a okno Edge pozostaje widoczne i sterowalne.
3. **Pozostałe ścieżki nie maskują regresji**
   **Given** testy uruchomienia, przełączania okna i AudioRoutera
   **When** wykonują przypadki Edge/WebView2, aktywacji/foreground, mute/unmute oraz kiosk/display mode
   **Then** test reprodukujący defekt przechodzi po poprawce, a testy nie potwierdzają niezamierzonej zmiany display mode ani wpływu audio na obraz.
4. **S15 pozostaje spełniony**
   **Given** build po poprawce i preflight gotowego `netflix.com`
   **When** użytkownik uruchamia Netflix na wierzchu
   **Then** log zawiera dokładnie jedno `appWindowAttached` dla `netflix`, `ActivateWindow completed` i `audioRuleApplied muted=false` z rzeczywistą sesją, bez błędów startu lub sterowania oknem.
5. **Manualny check UM760 jest wykonany**
   **Given** świeży publish na maszynie UM760 (MEDIASTATION / F7BSI / EliteMini Series)
   **When** użytkownik obserwuje ekran podczas wielokrotnych uruchomień Netflixa i po zamknięciu MediaStation
   **Then** zapisuje wynik obrazu wraz z odpowiadającymi fragmentami logu; check pozostaje jawnie manualny i nie jest zastępowany samymi testami automatycznymi.

## References

- parent — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md`, R2, R3, R5, Done when 4
- acceptance — `_bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md`, S15 i ograniczony odbiór z 2026-10-29
- browser host — `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/epic-browserhost.md`
- log — odbiór UM760 2026-10-29, wpisy `appLaunch`, `appWindowAttached`, `ActivateWindow`, `audioRuleApplied`

## Notes

- Decision: ticket rozdziela cykliczne zaniki obrazu po poprawnym starcie Netflixa od ticketów 14 i 16, które dotyczyły braku procesu/okna lub niedziałającego materiału (2026-10-29).
- Assumption: wielokrotne uruchomienie oznacza co najmniej trzy cykle z zamknięciem albo powrotem na Home pomiędzy startami; jeśli odbiór wymaga innej liczby, zapisuje ją plan odbioru.
- Open question: czy źródłem jest Edge/WebView2, przełączanie okna, audio routing/mute, kiosk/display mode czy inne działanie MediaStation; rozstrzyga to praca ticketu na podstawie logów i obserwacji.
- High-risk extra check: po przejściu kryteriów uruchomić ponownie S15 oraz sprawdzić, że zamknięcie MediaStation natychmiast przywraca stabilny obraz, bez pozostawionej zmiany trybu wyświetlania.
