---
epic: epic-browserhost
type: acceptance-plan
machine: UM760
base_head: 73ff58f
created: 2026-10-30
status: in-progress
---

# Odbiór epiki 6 (epic-browserhost) na UM760: plan i scenariusz

Całościowy test UM760 przy granicy epiki (AGENTS.md). Maszyna zidentyfikowana 2026-10-30 poleceniem z AGENTS.md: `COMPUTERNAME` MEDIASTATION, płyta F7BSI, model EliteMini Series = **UM760**. Sesja powtarza tę identyfikację w P0.1.

## Co odbieramy

Do tej pory kafelek Netflixa uruchamiał okno przez tymczasowy dev harness. MediaStation nie widział, co dzieje się w przeglądarce, a po awarii zostawał z niczym. Po tej epice przeglądarka jest prawdziwą, zarządzaną aplikacją. Eliza naciska OK na kafelku i Netflix otwiera się na całym ekranie, z własnym profilem i bez śladu automatyzacji, który serwis mógłby wykryć.

Co Eliza dostaje:

- **Netflix startuje z pilota** we właściwej przeglądarce, na całym ekranie, a dane logowania są w osobnym profilu.
- **Chwilowe zerwanie połączenia z przeglądarką nie przeszkadza.** MediaStation odzyskuje je sam, a film gra dalej.
- **Restart MediaStation nie zamyka Netflixa.** Po ponownym uruchomieniu MediaStation odnajduje żywą przeglądarkę i przejmuje ją bez drugiego okna. Tak samo po wybudzeniu komputera.
- **Zawieszoną aplikację naprawia się z kanapy.** Gdy połączenia nie da się odzyskać, Home dalej działa, a kafelek pokazuje awarię. Jedno OK na kafelku i jedno OK na „Uruchom ponownie” przywracają Netflixa. To scenariusz J4, sygnał sukcesu tej epiki.
- **Błędy startu są rozróżnione** (zajęty port, zajęty profil, przeglądarka bez sterowania), a zmiana wersji przeglądarki zostaje odnotowana. To widać w logu, nie na ekranie.

Projekt tego potrzebuje, bo epic-netflix-dpad buduje na tej epice sterowanie Netflixem z pilota: ma już kanał do strony, który przeżywa nawigację i zerwanie, oraz bazowy PageRuntime do rozszerzenia.

Świadomie zostaje na później: sterowanie samym Netflixem z pilota (epic-netflix-dpad, więc dziś strzałki nie prowadzą po filmach), plan C z rozszerzeniem przeglądarki (tylko po triggerach z AD-1), kody wyjścia `--smoke` (epic-deploy-resilience), nawigacja podczas przerwy CDP (DW-56: rozstrzygnięte w AD-1 i AD-10, `1baa513`, kod buduje story backlogu 22 poza tą epiką) i nakładka OSK nad kioskiem (epic-osk).

Akceptacja zobowiązuje projekt do własnego uruchamiania przeglądarki i surowego CDP na loopbacku (AD-1), bez Playwrighta, bez `Runtime.enable` i bez `--remote-allow-origins`, oraz do trybu `--kiosk` i odczytu wersji spoza CDP (AD-13). Zmiana tych reguł wymaga decyzji architektonicznej.

## Pokaz (agenda odbioru)

Pokaz ma dziewięć części. Użytkownik wykonuje wszystkie fizyczne naciśnięcia i potwierdza jedno pytanie Windows o uprawnienia. Ja uruchamiam aplikację, psuję połączenia w tle, zmieniam rozdzielczość i odpowiadam za log. S1 (pełny przebieg automatyczny, ok. 3 minuty z migającym ekranem, przed pokazem) nie jest prezentowany.

1. **Netflix z pilota** (S2–S6). *Co to daje:* jeden przycisk otwiera serwis na całym ekranie, jak w telewizorze. *Zaproszenie:* „Wciśnij OK na kafelku Netflix, a gdy się otworzy, odśwież stronę klawiszem F5”. *Co powinno być widać:* Netflix na całym ekranie, bez paska adresu i ramek; po F5 strona ładuje się od nowa.
2. **Chwilowe zerwanie połączenia** (S7–S11). *Co to daje:* usterka, której Eliza nie powinna nawet zauważyć. *Zaproszenie:* „Włącz film, jeśli chcesz. Gdy Windows zapyta o uprawnienia, potwierdź. W tle zerwę połączenie MediaStation z przeglądarką; patrz na film. Potem odśwież stronę klawiszem F5 i wciśnij Home”. *Co powinno być widać:* film gra bez przerwy, po F5 strona wraca, a Home zabiera na ekran główny.
3. **Restart MediaStation nie zamyka Netflixa** (S12–S18). *Co to daje:* aktualizacja albo awaria MediaStation nie przerywa oglądania. *Zaproszenie:* „Zrestartuję MediaStation. Spójrz na kafelek Netflixa i wciśnij na nim OK, potem Home”. *Co powinno być widać:* po restarcie kafelek pokazuje „w tle”, a OK przywraca ten sam Netflix, bez drugiego okna i bez ładowania od nowa. Potem powtarzam to bez zgody aplikacji, zabijając jej proces.
4. **Powrót po uśpieniu** (S19–S21). *Co to daje:* komputer można uśpić w trakcie seansu. *Zaproszenie:* „Uśpij komputer (Start → Uśpij), obudź go pilotem po kilkunastu sekundach, wciśnij OK na Netflixie, potem F5”. *Co powinno być widać:* ten sam Netflix bez nowego okna, po F5 strona wraca.
5. **Kiosk przy zmianach ekranu** (S22–S31, DW-40). *Co to daje:* ekran, który zmienia tryb albo gaśnie, nie psuje układu. *Zaproszenie:* „Zmienię rozdzielczość w tle, patrz na Netflixa. Potem Home i OK. Potem wyłącz ekran na 10 sekund i włącz go ponownie, wciśnij Home”. *Co powinno być widać:* Netflix cały czas zakrywa cały ekran, Home za każdym razem wraca na ekran główny.
6. **Gdy połączenia nie da się odzyskać** (S32–S36). *Co to daje:* zawieszona aplikacja nie więzi Elizy. *Zaproszenie:* „Wejdź w Netflixa. Zerwę stronę na dobre; wciśnij Home od razu, a potem popatrz na kafelek przez pół minuty”. *Co powinno być widać:* Home działa natychmiast, a po ok. 30 s kafelek pokazuje awarię.
7. **Naprawa jednym OK, scenariusz J4** (S37–S40). *Co to daje:* Eliza naprawia aplikację sama, bez klawiatury. *Zaproszenie:* „Wciśnij OK na kafelku z awarią, a potem OK na «Uruchom ponownie»”. *Co powinno być widać:* menu z fokusem na „Uruchom ponownie”, a po drugim OK działający Netflix na wierzchu, bez żadnego dodatkowego naciśnięcia.
8. **Ponowna awaria i zamknięcie** (S41–S43). *Co to daje:* jeśli naprawa nie pomoże, zawsze jest wyjście. *Zaproszenie:* „Znów zerwę stronę, tym razem na wierzchu; nic nie naciskaj przez pół minuty. Gdy wrócisz na ekran główny, wciśnij OK, strzałkę w dół i OK na «Zamknij»”. *Co powinno być widać:* po ok. 30 s MediaStation sam wraca na ekran główny z awarią na kafelku, a „Zamknij” kończy aplikację i kafelek wraca do stanu spoczynku.
9. **Chrome** (S44–S45). *Co to daje:* przeglądarkę dobiera się pod serwis. *Zaproszenie:* „Przejdź na kafelek Chrome, wciśnij OK, potem Home”. *Co powinno być widać:* serwis otwiera się w Chrome na całym ekranie, Home wraca na ekran główny.

S46 (czyste zamknięcie i błędy całego przebiegu) to wiersz `agent`, nie jest prezentowany.

## Źródła

- Epika: `epic-browserhost.md`, Requirements 1–6, Done when 1–5, Notes (decyzje o `--kiosk`, o wersji poza CDP, o J4 w odbiorze całej epiki).
- Retro: `epic-browserhost-retrospective.md` (`accepted-with-open-items`, to dowód maszynowy, nie odbiór), Behavior verification oraz F6. **Na żywo nie widziano:** prawdziwego Netflixa w J4 i adopcji (smoke'i 6.5, 6.7 i 6.9 używają kafelka testowego z wstrzykniętymi klawiszami), zerwanego gniazda żywego celu i prawdziwego uśpienia (6.5 używa syntetycznego `WM_POWERBROADCAST`). To jest zakres odbioru.
- Rejestr: `deferred-work.md`. DW-55 (reset gniazda przy żywym Netflixie, właściciel: ten odbiór) → S7–S10. DW-40 (kiosk przy zmianach ekranu) → S22–S31; część o OSK należy do epic-osk. DW-56 (nawigacja podczas przerwy CDP) i DW-58 (dev harness) nie mają scenariusza. DW-56 jest rozstrzygnięty (AD-1 i AD-10, `1baa513`), ale AD-10 opisuje to zachowanie jako „do zbudowania”, a kod powstaje w story backlogu 22 poza tą epiką. Odbiór nie oczekuje więc cichego reloadu ani `Page.getFrameTree`, a ich brak w logach nie jest wadą tej epiki. DW-58 nie dotyczy, bo odbiór działa bez harnessu.
- Plany: `tickets/story-refactor-sweep-plan.md` (wiersze DW-55 i DW-40, kontrole dla użytkownika), `tickets/story-browserhost-end-to-end-suite-plan.md`.
- Skrypty: `tools/check-browserhost-e2e.ps1` (`-SelfTest` w P0.3, pełny przebieg w S1; uruchamia `tracer`, `reconnect`, `errors`, `adopt`, `restart`) oraz **nowy** `tools/check-browserhost-cdp-reset.ps1` (powstał dla tego odbioru; `-SelfTest` bez podnoszenia przeszedł na UM760 2026-10-30, wariant `-Elevate` w P0.3).
- Wzór: `epic-windows-audio/epic-windows-audio-um760-acceptance.md`.
- Wpisy logu pochodzą z żywych logów buildu `7c72f93`, najnowszego w `recent_versions` (`A inventory --version-contains 7c72f93`; `src/` i `web/` nie zmieniły się od `3cf71e4`), a SPEC-i z `data.args~=`, `data.from`, `data.to`, `data.cause`, `data.adopted`, `data.outcome`, `data.phase`, `data.trigger` i `data.timeoutSeconds` sprawdzono próbnym `A check` na tych logach. Z kodu, niezweryfikowane na żywym logu: `intentIgnored`, `cdpReconnectFailed`, `bindingRefreshFailed`, `browserCommandFailed`, `orchestratorFailed`, `inputHubError`, `webViewProcessFailed`, `windowCommandFailed`, `audioCommandFailed`, `processExited`. Występują tylko w oczekiwaniach `:0`.

**Poza zakresem odbioru (pokrywają testy, retro i S1):** `Runtime.enable` i zakazy AD-1 w kodzie (`CdpSession.ForbiddenMethods`, testy); spóźnione fakty starej sesji i nowy `transitionId` (R3; `BrowserHostTests`, `Orchestrator*Tests`); brak buforowania D-pada podczas awarii (R2; testy .NET); koperta `v`, `id` i spóźnione odpowiedzi (R5; `npm test`, 175 testów); build page bundle (R5; baner i hash w smoke'u tracer, S1); `BrowserVersionChanged`, `PortInUse`, `ProfileInUse`, `CdpUnavailable` z minimalizacją i wyciszeniem (R4, Done when 3 i 4; faza `errors` w S1 na kafelku testowym).

## Przygotowanie (P0)

Log: `%LOCALAPPDATA%\MediaStation\logs\mediastation-<data>.jsonl`. Legenda: `A` = `uv run .claude/skills/fp-acceptance/scripts/acceptance.py`, `LOGS` = `--logs "%LOCALAPPDATA%\MediaStation\logs\mediastation-*.jsonl"`, `FORBID` = `--forbid-level warn,error --allow "msg=appIconMissing"` (brak ikon daje celowy `warn`), `FLOST` = `FORBID` oraz `--allow "msg=cdpLost"` (wiersze z celową utratą połączenia), `Fn` = fragmenty z Uwag zapisane w `%TEMP%\fp-f\`, uruchamiane przez `powershell -NoProfile -ExecutionPolicy Bypass -File`.

| # | Krok | Oczekiwane |
|---|------|------------|
| P0.1 | Polecenie identyfikacji z AGENTS.md; `git rev-parse --short HEAD`; `git status --porcelain -- src web` | UM760 (MEDIASTATION, F7BSI, EliteMini Series); brak zmian śledzonych plików w `src` i `web`. Skrócony HEAD wpisany w miejsce `HEADSHA` w asercji S2 przed pierwszym uruchomieniem |
| P0.2 | `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` | exit 0; `publish\MediaStation.exe` świeży |
| P0.3 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1 -SelfTest`, a po uprzedzeniu użytkownika o pytaniu Windows `... -File tools/check-browserhost-cdp-reset.ps1 -SelfTest -Elevate` | oba exit 0; drugi pokazuje `elevated: the reset breaks the socket` |
| P0.4 | Pilot G20S Pro z dongle USB (`VID_1915&PID_1025`), klawiatura (F5), Netflix zalogowany w profilu `profiles\netflix`; ustalić, który ekran jest podłączony (2026-10-30 był to DELL P2415Q 3840x2160, nie TV) i czy jest drugi monitor; `F4 -Test` na tym ekranie | dongle widoczny; `F4 -Test` zwraca 0; ekran zapisany w dowodzie P0, bo DW-40 mówi o TV |
| P0.5 | `A say "test głosu" --voice "pl-PL"`; ustalić, czy użytkownik widzi tę rozmowę podczas pokazu (kiosk zakrywa ekran) | system mówi po polsku; kanał ustalony, w razie potrzeby `A overlay start` |
| P0.6 | Żaden proces `MediaStation` nie działa; porty 9222, 9223 i 9237 wolne; brak `apps.json.restore-bak`; `apps.json` użytkownika bez zmian (kafelki `netflix`, `chrome-test`, `moonlight-pc`; Moonlight bez harnessu nie startuje i nie jest używany); fragmenty F1–F4 zapisane w `%TEMP%\fp-f\` | czyste środowisko |

## Scenariusze

Wspólne: okno logu każdego wiersza zaczyna się od `A mark` tuż przed czynnością (`<mark>`), chyba że wiersz wskazuje inaczej. Wiersze `user-hardware` używają `A wait --since <mark> --for ... --say "<zaproszenie>" --voice "pl-PL" --beep --countdown 3 --timeout 60 LOGS` (S19 ma `--timeout 300`, S34 ma `--timeout 25`) i potem `A check --since <mark> --until <until> ... LOGS`. Skróty `--expect` zapisano jako `E "..."`. Każda asercja `check` ma `LOGS`, a `FORBID` albo `FLOST` według wiersza. `HOME` oznacza: `E "msg=action,data.kind=home:1" E "msg=windowCommand,data.type=ShowShell,data.result=completed:1" E "msg=foregroundChanged,data.owner=shell:>=1"` oraz `A foreground --pid <pid>` zgodny.

| # | Scenariusz | Actor | Oczekiwane | Assertion | Kind | Done when |
|---|------------|-------|------------|-----------|------|-----------|
| S1 | Pełny automatyczny przebieg BrowserHost na świeżym buildzie | agent | 5 faz, 11/11 wierszy macierzy, izolacja zachowana | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-e2e.ps1`, exit 0; `report.jsonl` z `summary` `pass` | pass/fail | 1, 2, 3, 4, 5 (mechanizm) |
| S2 | Świeży build startuje bez harnessu, ekran główny gotowy | agent | Wersja z HEAD, BrowserHost włączony, harness wyłączony, Netflix nie działa | `A launch --exe src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe --ready "msg=snapshotPublished" LOGS`, `A focus --pid <pid>`; `A check --since <mark> --expect "msg=start,data.version~=b041c5d:1" --expect "msg=browserHostEnabled:1" --expect "msg=devHarnessEnabled:0" --expect "msg=probeExisting,data.appId=netflix,data.outcome=notRunning:1" --expect "msg=snapshotPublished,data.rev=1:1" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | start |
| S3 | OK na kafelku uruchamia Netflixa przez BrowserHost | user-hardware | Odczyt wersji przed startem, start z `--kiosk`, okno, CDP, `hello` z `webdriver=false` | `A wait --since <mark> --for "msg=pageHello,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=browserVersion,data.appId=netflix:1" --expect "msg=appLaunch,data.appId=netflix,data.args~=--kiosk:1" --expect "msg=appWindowAttached,data.appId=netflix:1" --expect "msg=cdpConnected,data.appId=netflix:1" --expect "msg=cdpConnected,data.adopted=true:0" --expect "msg=pageHello,data.appId=netflix,data.service=netflix,data.webdriver=false:>=1" --ordered FORBID LOGS` | pass/fail | 1, 3 |
| S4 | Netflix na całym ekranie, bez paska i ramek | observation | Serwis zalogowany, cały ekran, brak paska adresu | werdykt użytkownika | observation | 1 |
| S5 | Proces przeglądarki ma flagi AD-1, a port słucha tylko na loopbacku | agent | Jeden root profilu `netflix`, `--kiosk`, port 9222 tylko na 127.0.0.1, bez zakazanych flag i bez drugiej flagi trybu okna | `F1 -AppId netflix -Port 9222`, exit 0; root zapisany w dowodzie jako `R` | pass/fail | 1 |
| S6 | Nawigacja głównej ramki: F5 przywraca most | user-hardware | Nowy `pageHello` po F5, bez odrzuconych komunikatów | `A wait --since <mark> --for "msg=pageHello,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=pageHello,data.appId=netflix,data.webdriver=false:>=1" --expect "msg=pageMessageRejected:0" FORBID LOGS` | pass/fail | 2 |
| S7 | DW-55: reset gniazda CDP przy żywym Netflixie, załadowany dokument dalej sięga hosta | agent | `SetTcpEntry DELETE_TCB` (podniesiony, jedno pytanie Windows), `cdpReconnected`, ten sam `timeOrigin`, wywołanie bindingu z załadowanego dokumentu daje `pageHello` | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-browserhost-cdp-reset.ps1 -AppId netflix -Port 9222`, exit 0, `VERDICT PASS`; `<mark>` tuż przed uruchomieniem | pass/fail | 2 (DW-55) |
| S8 | DW-55: log pokazuje odzyskanie bez utraty aplikacji | agent | Zerwanie, reconnect po tej samej stronie, `Runtime.bindingCalled` na nowej sesji; stan kafelka bez zmian | `A check --since <mark z S7> --expect "msg=cdpClosed,data.appId=netflix:1" --expect "msg=cdpReconnected,data.appId=netflix,data.trigger=cdpClosed:1" --expect "msg=pageHello,data.appId=netflix,data.driverVersion=dw55-probe,data.webdriver=false:1" --expect "msg=cdpLost:0" --expect "msg=processExited:0" --expect "msg=appLaunch:0" --expect "msg=appStateChanged,data.appId=netflix:0" --ordered FORBID LOGS` | pass/fail | 2 (DW-55) |
| S9 | Film nie zauważył zerwania | observation | Obraz i dźwięk bez przerwy (albo strona bez zmian, gdy film nie grał) | werdykt użytkownika | observation | 2 |
| S10 | DW-55: nawigacja głównej ramki po resecie, `hello` z nowego dokumentu | user-hardware | F5 daje `pageHello` od prawdziwego PageRuntime na odzyskanej sesji | `A wait --since <mark> --for "msg=pageHello,data.appId=netflix,data.driverVersion!=dw55-probe" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=pageHello,data.appId=netflix,data.driverVersion!=dw55-probe,data.webdriver=false:>=1" --expect "msg=pageMessageRejected:0" FORBID LOGS` | pass/fail | 2 (DW-55) |
| S11 | Home po resecie: aplikacja dalej zarządzana | user-hardware | Ekran główny, okno zminimalizowane, drzewo wyciszone | `A wait --since <mark> --for "msg=action,data.kind=home" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=action,data.kind=home:1" --expect "msg=windowCommand,data.type=MinimizeWindow,data.result=completed:>=1" --expect "msg=windowCommand,data.type=ShowShell,data.result=completed:1" --expect "msg=foregroundChanged,data.owner=shell:>=1" --expect "msg=audioRuleApplied,data.muted=true:>=1" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | 2 |
| S12 | Zwykły restart MediaStation adoptuje żywego Edge'a bez drugiego startu | agent | `A close`, nowy start: `probeExisting` adopted, `appAdopted`, `cdpConnected adopted`, wyciszenie, zero `appLaunch` | `A close --pid <pid>`, `A launch ... --ready "msg=snapshotPublished" LOGS`, `A focus --pid <pid>`; `A check --since <mark> --expect "msg=probeExisting,data.appId=netflix,data.outcome=adopted:1" --expect "msg=appAdopted,data.appId=netflix:1" --expect "msg=cdpConnected,data.appId=netflix,data.adopted=true:1" --expect "msg=audioRuleApplied,data.muted=true:>=1" --expect "msg=appLaunch:0" FORBID LOGS` | pass/fail | 5 |
| S13 | Ten sam proces przeglądarki po restarcie | agent | Jeden root, ten sam PID `R` co w S5 | `F1 -AppId netflix -Port 9222 -Root <R>`, exit 0 | pass/fail | 5 |
| S14 | OK na adoptowanym kafelku przywraca to samo okno | user-hardware | Aktywacja bez nowego startu, Edge na wierzchu | `A wait --since <mark> --for "msg=foregroundChanged,data.owner=app,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=windowCommand,data.type=ActivateWindow,data.result=completed:>=1" --expect "msg=foregroundChanged,data.owner=app,data.appId=netflix:>=1" --expect "msg=appLaunch:0" FORBID LOGS` | pass/fail | 5 |
| S15 | Po restarcie kafelek był „w tle”, a Netflix ten sam | observation | Kafelek „w tle” przed OK; po OK ta sama strona, bez ładowania od nowa, jedno okno | werdykt użytkownika | observation | 5 |
| S16 | Home po adopcji | user-hardware | Ekran główny | `A wait --since <mark> --for "msg=action,data.kind=home" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=action,data.kind=home:1" --expect "msg=windowCommand,data.type=ShowShell,data.result=completed:1" --expect "msg=foregroundChanged,data.owner=shell:>=1" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | 5 |
| S17 | Awaria MediaStation (zabity proces): adopcja jak w S12 | agent | `Stop-Process -Force`, nowy start, adopcja, zero `appLaunch` | `Stop-Process -Id <pid> -Force`, `A launch ... --ready "msg=snapshotPublished" LOGS`, `A focus --pid <pid>`; `A check --since <mark> --expect "msg=probeExisting,data.appId=netflix,data.outcome=adopted:1" --expect "msg=appAdopted,data.appId=netflix:1" --expect "msg=cdpConnected,data.appId=netflix,data.adopted=true:1" --expect "msg=appLaunch:0" FORBID LOGS` | pass/fail | 5 |
| S18 | Ten sam proces przeglądarki po awarii | agent | Jeden root, PID `R` | `F1 -AppId netflix -Port 9222 -Root <R>`, exit 0 | pass/fail | 5 |
| S19 | Prawdziwe uśpienie i wybudzenie pilotem odnawia sesję | user-hardware | `systemResumed`, `renewSession`, `cdpReconnected`, bez nowego startu. UM760 ma tylko S0 (Modern Standby) | `A wait --since <mark> --for "msg=cdpReconnected,data.appId=netflix" --timeout 300 ... LOGS`; `A check --since <mark> --until <until> --expect "msg=systemResumed:>=1" --expect "msg=renewSession,data.appId=netflix:>=1" --expect "msg=cdpReconnected,data.appId=netflix:>=1" --expect "msg=appLaunch:0" --expect "msg=processExited:0" FORBID LOGS` | pass/fail | R3 |
| S20 | OK po wybudzeniu przywraca Netflixa | user-hardware | Edge na wierzchu, bez nowego startu | `A wait --since <mark> --for "msg=foregroundChanged,data.owner=app,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=foregroundChanged,data.owner=app,data.appId=netflix:>=1" --expect "msg=appLaunch:0" FORBID LOGS` | pass/fail | R3 |
| S21 | F5 po wybudzeniu daje `hello` na odnowionej sesji | user-hardware | Nowy `pageHello` | `A wait --since <mark> --for "msg=pageHello,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=pageHello,data.appId=netflix,data.webdriver=false:>=1" FORBID LOGS` | pass/fail | R3, 2 |
| S22 | DW-40: zmiana rozdzielczości przy kiosku na wierzchu | agent | Po zmianie na 1920x1080 okno Edge zakrywa cały monitor i jest na pierwszym planie | `F4 -Width 1920 -Height 1080`, exit 0, 3 s przerwy; `F3 -AppId netflix`, exit 0, wymiary w dowodzie | pass/fail with measurement | DW-40 |
| S23 | DW-40: wygląd po zmianie rozdzielczości | observation | Netflix na całym ekranie, bez pasków i przesunięć | werdykt użytkownika | observation | DW-40 |
| S24 | DW-40: Home po zmianie rozdzielczości | user-hardware | Ekran główny | `A wait --since <mark> --for "msg=action,data.kind=home" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=action,data.kind=home:1" --expect "msg=windowCommand,data.type=ShowShell,data.result=completed:1" --expect "msg=foregroundChanged,data.owner=shell:>=1" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | DW-40 |
| S25 | DW-40: OK wraca do kiosku przy zmienionej rozdzielczości | user-hardware | Edge na wierzchu | `A wait --since <mark> --for "msg=foregroundChanged,data.owner=app,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=foregroundChanged,data.owner=app,data.appId=netflix:>=1" FORBID LOGS` | pass/fail | DW-40 |
| S26 | DW-40: kiosk po powrocie nadal zakrywa ekran | agent | Okno Edge zakrywa monitor | `F3 -AppId netflix`, exit 0 | pass/fail with measurement | DW-40 |
| S27 | DW-40: powrót rozdzielczości przy kiosku na wierzchu | agent | Rozdzielczość z rejestru przywrócona, okno zakrywa monitor | `F4 -Restore`, exit 0, 3 s przerwy; `F3 -AppId netflix`, exit 0 | pass/fail with measurement | DW-40 |
| S28 | DW-40: ekran wyłączony i włączony | observation | Po włączeniu Netflix na całym ekranie, bez ruszania czegokolwiek | werdykt użytkownika; wyłączenie i włączenie przyciskiem ekranu, ok. 10 s | observation | DW-40 |
| S29 | DW-40: okno po włączeniu ekranu | agent | Okno Edge zakrywa monitor | `F3 -AppId netflix`, exit 0 | pass/fail with measurement | DW-40 |
| S30 | DW-40: Home po włączeniu ekranu | user-hardware | Ekran główny | `A wait --since <mark> --for "msg=action,data.kind=home" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=action,data.kind=home:1" --expect "msg=windowCommand,data.type=ShowShell,data.result=completed:1" --expect "msg=foregroundChanged,data.owner=shell:>=1" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | DW-40 |
| S31 | DW-40: drugi monitor | observation | Jeśli dostępny: kiosk na jednym ekranie w całości, Home wraca. Jeśli nie: „nie dotyczy” z powodem (2026-10-30 podłączony jeden ekran) | werdykt użytkownika albo „nie dotyczy” | observation | DW-40 |
| S32 | OK wraca do Netflixa przed próbą utraty | user-hardware | Edge na wierzchu | `A wait --since <mark> --for "msg=foregroundChanged,data.owner=app,data.appId=netflix" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=foregroundChanged,data.owner=app,data.appId=netflix:>=1" FORBID LOGS` | pass/fail | 2 |
| S33 | Utrata celu strony przy żywym procesie | agent | Drugi cel otwarty, cel hosta zamknięty, `cdpClosed` z limitem 30 s | `F2 -Port 9222`, exit 0; `<mark>` tuż przed; `A wait --since <mark> --for "msg=cdpClosed,data.appId=netflix,data.timeoutSeconds=30" --timeout 5 LOGS`, exit 0 | pass/fail | 2 |
| S34 | Home działa podczas przerwy | user-hardware | Ekran główny przed upływem limitu | `A wait --since <mark z S33> --for "msg=action,data.kind=home" ... LOGS`; `A check --since <mark z S33> --until <until> --expect "msg=action,data.kind=home:1" --expect "msg=windowCommand,data.type=ShowShell,data.result=completed:1" --expect "msg=foregroundChanged,data.owner=shell:>=1" --expect "msg=cdpLost:0" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | 2 |
| S35 | Po limicie aplikacja przechodzi w `Failed`, proces żyje | agent | `cdpLost` po 30 s, `cdpLostHandled failed`, bez reconnectu i bez wyjścia procesu | `A wait --since <mark z S33> --for "msg=cdpLostHandled,data.appId=netflix,data.state=failed" --timeout 40 LOGS`; `A check --since <mark z S33> --until <until> --expect "msg=cdpClosed,data.appId=netflix:1" --expect "msg=cdpLost,data.appId=netflix,data.timeoutSeconds=30,data.trigger=cdpClosed:1" --expect "msg=cdpLostHandled,data.appId=netflix,data.state=failed:1" --expect "msg=appStateChanged,data.appId=netflix,data.to=failed:1" --expect "msg=cdpReconnected:0" --expect "msg=processExited:0" --ordered FLOST LOGS` | pass/fail | 2 |
| S36 | Kafelek pokazuje awarię | observation | Stan awarii na kafelku Netflix, ekran główny dalej sterowany pilotem | werdykt użytkownika | observation | 2, 5 |
| S37 | J4: jedno OK na kafelku `Failed` otwiera menu | user-hardware | Jedno OK, menu, bez zmiany stanu i bez startu | `A wait --since <mark> --for "msg=action,data.kind=ok,data.phase=press" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=action,data.kind=ok,data.phase=press:1" --expect "msg=appStateChanged,data.appId=netflix:0" --expect "msg=appLaunch:0" FORBID LOGS` | pass/fail | 5 |
| S38 | J4: wygląd menu | observation | „Uruchom ponownie” i „Zamknij”, fokus na „Uruchom ponownie”, jeden fokus | werdykt użytkownika | observation | 5 |
| S39 | J4: jedno OK na „Uruchom ponownie” przywraca aplikację jednym przebiegiem | user-hardware | Razem dwa OK od S37; `Stopping → Starting → Foreground`, nowy start, `hello`, bez ignorowanych intencji | `A wait --since <mark> --for "msg=pageHello,data.appId=netflix" ... LOGS`; `A check --since <mark z S37> --until <until> --expect "msg=action,data.kind=ok,data.phase=press:2" --expect "msg=appStateChanged,data.appId=netflix,data.from=failed,data.to=stopping,data.cause=RestartApp:1" --expect "msg=appStateChanged,data.appId=netflix,data.from=stopping,data.to=starting:1" --expect "msg=appLaunch,data.appId=netflix:1" --expect "msg=appStateChanged,data.appId=netflix,data.from=starting,data.to=foreground:1" --expect "msg=pageHello,data.appId=netflix,data.webdriver=false:>=1" --expect "msg=intentIgnored:0" --ordered FORBID LOGS` | pass/fail | 5 |
| S40 | J4: Netflix działa na wierzchu po naprawie | observation | Serwis na całym ekranie, bez żadnego dodatkowego naciśnięcia | werdykt użytkownika | observation | 5 |
| S41 | Ponowna awaria wraca do `Failed` i oddaje ekran główny | agent | `F2`, po 30 s `foreground → failed`, ekran główny na wierzchu | `F2 -Port 9222`, exit 0; `<mark>` tuż przed; `A wait --since <mark> --for "msg=cdpLostHandled,data.appId=netflix,data.state=failed" --timeout 40 LOGS`; `A check --since <mark> --until <until> --expect "msg=cdpLost,data.appId=netflix:1" --expect "msg=appStateChanged,data.appId=netflix,data.from=foreground,data.to=failed:1" --expect "msg=appLaunch:0" FLOST LOGS`; po 5 s `A foreground --pid <pid>` zgodny | pass/fail | R6 |
| S42 | „Zamknij” dostępne bez eskalacji | user-hardware | OK, w dół, OK: `failed → stopping` (`CloseApp`), `stopping → notRunning`, bez nowego startu | `A wait --since <mark> --for "msg=appStateChanged,data.appId=netflix,data.to=notRunning" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=appStateChanged,data.appId=netflix,data.from=failed,data.to=stopping,data.cause=CloseApp:1" --expect "msg=appStateChanged,data.appId=netflix,data.from=stopping,data.to=notRunning:1" --expect "msg=appLaunch:0" --ordered FORBID LOGS` | pass/fail | R6 |
| S43 | Po „Zamknij” nie zostaje proces przeglądarki | agent | Zero procesów profilu `netflix` | `F1 -AppId netflix -NoProcesses`, exit 0 | pass/fail | R6 |
| S44 | Chrome przez BrowserHost | user-hardware | Binarka Chrome, `--kiosk`, `hello` z `webdriver=false` | `A wait --since <mark> --for "msg=pageHello,data.appId=chrome-test" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=appLaunch,data.appId=chrome-test,data.commandLine~=chrome.exe,data.args~=--kiosk:1" --expect "msg=pageHello,data.appId=chrome-test,data.webdriver=false:>=1" --ordered FORBID LOGS` | pass/fail | R1 |
| S45 | Home nad Chrome | user-hardware | Ekran główny | `A wait --since <mark> --for "msg=action,data.kind=home" ... LOGS`; `A check --since <mark> --until <until> --expect "msg=action,data.kind=home:1" --expect "msg=windowCommand,data.type=ShowShell,data.result=completed:1" --expect "msg=foregroundChanged,data.owner=shell:>=1" FORBID LOGS`; `A foreground --pid <pid>` zgodny | pass/fail | R1 |
| S46 | Czyste zamknięcie i cały przebieg bez ukrytych błędów | agent | `A close --pid <pid>`, `stop` z `exitCode 0`; od S2 zero błędów poza celowymi; potem zamknięcie drzewa profilu `chrome-test` i `F1 -AppId chrome-test -NoProcesses` | `A check --since "row:S2" --plan <plan> --expect "msg=stop,data.exitCode=0:>=1" --expect "msg=pageMessageRejected:0" --expect "msg=browserCommandFailed:0" --expect "msg=cdpReconnectFailed:0" --expect "msg=bindingRefreshFailed:0" --expect "msg=commandFailed:0" --expect "msg=orchestratorFailed:0" --expect "msg=inputHubError:0" --expect "msg=webViewProcessFailed:0" --expect "msg=windowCommandFailed:0" --expect "msg=audioCommandFailed:0" FLOST LOGS` | pass/fail | całość |

### Dlaczego każdy scenariusz

- **S1:** sprawdza Done when 1–4 i mechanizm Done when 5 na świeżym buildzie tej sesji, a nie na zapisanym przebiegu z 6.9. Fazy `errors` i `restart` wymagają ustawień, których nie ma na prawdziwym profilu (zły `Last Version`, zajęty port, wyłączone CDP, podmieniony runtime). Bez tego wiersza Requirement 4 nie ma świeżego dowodu.
- **S2:** potwierdza, że odbieramy build z tego HEAD, działający przez prawdziwy host, a nie dev harness. Gdy zawiedzie, nie wiadomo, co się ogląda.
- **S3–S5:** potwierdzają Done when 1 i 3 na prawdziwym Netflixie z prawdziwego pilota. Gdy zawiodą, serwis może wykryć automatyzację albo profil się pomiesza, więc epic-netflix-dpad nie ma na czym stanąć.
- **S6:** potwierdza, że most do strony wraca po nawigacji na serwisie, a nie tylko na stronie testowej. Gdy zawiedzie, sterowanie Netflixem straci kanał po każdym przeładowaniu.
- **S7, S8, S10:** rozstrzygają DW-55, czyli warunek z rejestru: podniesiony reset gniazda przy otwartym Netflixie. S7 i S8 pokazują, że załadowany dokument woła binding na nowej sesji (ten sam `timeOrigin` wyklucza przeładowanie). S10 pokazuje drugą gałąź warunku. Żaden wcześniejszy przebieg tego nie dowodzi (retro F6). Gdy zawiodą, po chwilowej usterce strona żyje, ale jest głucha, a host tego nie widzi.
- **S9, S11:** potwierdzają, że odzyskanie jest niewidoczne dla Elizy, a aplikacja dalej podlega Home i wyciszeniu. Gdy zawiodą, reconnect ratuje protokół kosztem oglądania.
- **S12–S18:** potwierdzają Done when 5 w części adopcji, w obu wariantach restartu, na prawdziwym profilu. Gdy zawiodą, każda aktualizacja MediaStation przerywa film albo zostawia dwie przeglądarki.
- **S19–S21:** pierwszy przebieg z prawdziwym uśpieniem, bo 6.5 wysyła syntetyczny komunikat, a UM760 ma Modern Standby, który może zachowywać się inaczej. Gdy zawiodą, Netflix po nocy jest martwy dla MediaStation.
- **S22–S31:** rozstrzygają DW-40. Pomiar ze spike'a 6.1 miał jeden monitor i stałą rozdzielczość. Gdy zawiodą, układ kanapowy psuje się przy zmianie trybu telewizora, a od tego zależy WindowManager.
- **S32–S36:** potwierdzają Done when 2 w części limitu na prawdziwym Netflixie oraz obietnicę „Home działa cały czas”. Gdy zawiodą, Eliza zostaje uwięziona w zawieszonym oknie.
- **S37–S40:** to J4, czyli sygnał sukcesu epiki i druga część Done when 5: dokładnie dwa OK prawdziwym pilotem. Gdy zawiodą, naprawa wymaga klawiatury.
- **S41–S43:** potwierdzają Requirement 6 w części ponownej awarii i „Zamknij”. Gdy zawiodą, naprawa bywa ślepą uliczką albo zostawia proces na profilu, co blokuje następny start.
- **S44, S45:** pierwszy żywy przebieg Chrome przez BrowserHost, bo smoke'i używają tylko Edge. Gdy zawiodą, obietnica „przeglądarka dobrana pod serwis” działa na razie tylko dla Edge.
- **S46:** wyłapuje błędy, których pojedyncze wiersze nie widzą, i potwierdza czyste zamknięcie.

## Uwagi

- **`HEADSHA`.** `fill` porównuje SPEC-i z zamrożoną asercją, więc P0.1 wpisuje skrócony HEAD w S2 przed pierwszym uruchomieniem. W tym checkoucie pracują inne wątki, więc HEAD może się przesunąć; po publishu liczy się ten z P0.1, a `base_head` zostaje z dnia przygotowania.
- **S3, poprawka asercji (2026-10-30, po wyniku, za zgodą użytkownika).** Pierwotna asercja wymagała `data.adopted=false`, a `cdpConnected` zapisuje `adopted` tylko przy przejęciu (BrowserHost.cs). Pierwszy wiersz S3 zostaje jako `fail`; powtórka ocenia to samo okno logu poprawioną asercją.
- **DW-55, wywołanie bindingu.** PageRuntime nie ma dziś własnego komunikatu po `hello` (plan 6.8), więc skrypt woła z załadowanego dokumentu `window.__msBridge`, tę samą funkcję, której używa most. Robi to osobnym, krótkim połączeniem CDP i `Runtime.evaluate`, bez `Runtime.enable`, tylko w tej próbie, nie w produkcie. `driverVersion` = `dw55-probe` odróżnia tę kopertę od prawdziwego `hello`.
- **DW-55, wynik dla rejestru.** S7, S8 i S10 zaliczone oznaczają warunek spełniony i wpis do zamknięcia przez Sofię. Każda inna kombinacja zostawia wpis otwarty z obserwowanym wynikiem. Rejestru nie zmienia ten odbiór.
- **DW-55, odmowa.** Pytanie Windows odrzucone albo przeterminowane (2 minuty) to exit 2 skryptu, czyli brak werdyktu: pytamy użytkownika i powtarzamy. Bez `-Elevate` `SetTcpEntry` zwraca 317 i gniazdo przeżywa (sprawdzone 2026-10-30).
- **DW-40, ekran.** 2026-10-30 UM760 ma podłączony DELL P2415Q (3840x2160), a DW-40 mówi o TV. Jeśli w dniu odbioru nie ma TV, S22–S30 idą na tym ekranie, a dowód to nazywa; sprawdzenie na TV zostaje wtedy otwarte w handoffie, bo wyłączenie TV może inaczej zachować się na HDMI. Monitor wyłączony przyciskiem może zniknąć z systemu albo nie: to obserwujemy, nie zakładamy.
- **DW-40, rozdzielczość.** `F4` zmienia tryb dynamicznie, bez zapisu w rejestrze, więc restart systemu też wraca do poprzedniego. Gdyby S22 lub S27 zawiodły, `F4 -Restore` wykonuje się i tak.
- **Uśpienie.** `powercfg /a` na UM760: tylko S0 Low Power Idle (Network Connected) i hibernacja. Ta rozmowa może się na chwilę rozłączyć. Jeśli pilot nie budzi, wybudzenie przyciskiem albo klawiaturą zapisujemy w dowodzie. Brak `systemResumed` przy Modern Standby to obserwacja do zapisania, nie powód do zmiany asercji.
- **Netflix niezalogowany.** Do S3 użytkownik loguje się w oknie Edge (profil `profiles\netflix` zostaje). S4 i S9 opisują wtedy to, co było widać.
- **Fragmenty.** Zapisane w `%TEMP%\fp-f\` w P0.6. `F1`, `F3` i `F4 -Test` przetestowane na UM760 2026-10-30 bez zmiany ekranu, `F2` to logika fazy `reconnect` (`second target opened via json/new`, przebieg `browserhost-e2e-20261029-225240`).

**F1** (`f1.ps1`, proces i flagi):

```powershell
param([string]$AppId='netflix',[int]$Port=9222,[int]$Root=0,[switch]$NoProcesses)
$dir=Join-Path $env:LOCALAPPDATA "MediaStation\profiles\$AppId"
$all=@(Get-CimInstance Win32_Process -Filter "Name='msedge.exe' OR Name='chrome.exe'" | ? {$_.CommandLine -and $_.CommandLine.Replace('"','') -like "*--user-data-dir=$dir*"})
if($NoProcesses){"processes of profile ${AppId}: $($all.Count)"; if($all.Count -eq 0){exit 0}; exit 1}
$roots=@($all | ? {$_.CommandLine -notmatch ' --type='})
$bad=@()
if($roots.Count -ne 1){$bad+="roots: $($roots.Count)"}
else{
  $c=$roots[0].CommandLine; "root $($roots[0].ProcessId): $c"
  if($Root -and $roots[0].ProcessId -ne $Root){$bad+="root $($roots[0].ProcessId) is not the expected $Root"}
  if($c -notmatch '--kiosk'){$bad+='no --kiosk'}
  if($c -notmatch "--remote-debugging-port=$Port\b"){$bad+='no --remote-debugging-port'}
  foreach($f in '--remote-allow-origins','--start-fullscreen','--start-maximized','--app='){if($c -match [regex]::Escape($f)){$bad+="forbidden $f"}}
}
$l=@(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
"listeners on ${Port}: $(($l.LocalAddress) -join ',')"
if($l.Count -eq 0 -or @($l | ? {$_.LocalAddress -ne '127.0.0.1'}).Count -gt 0){$bad+='port not on loopback only'}
$bad; exit $bad.Count
```

**F2** (`f2.ps1`, utrata celu przy żywym procesie):

```powershell
param([int]$Port=9222)
$base="http://127.0.0.1:$Port"; $w=New-Object Net.WebClient; $w.Proxy=$null
$pages=@(($w.DownloadString("$base/json/list")|ConvertFrom-Json)|?{$_.type -eq 'page' -and -not ([string]$_.url).StartsWith('devtools://')})
if($pages.Count -ne 1){"expected one page target, found $($pages.Count)"; exit 1}
[void]$w.UploadString("$base/json/new?about:blank",'PUT','')
"closing target $($pages[0].id) $($pages[0].url): "+$w.DownloadString("$base/json/close/$($pages[0].id)"); exit 0
```

**F3** (`f3.ps1`, okno zakrywa monitor i jest na pierwszym planie):

```powershell
param([string]$AppId='netflix',[long]$Hwnd=0)
Add-Type -Namespace F3 -Name N -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)] public struct R {public int L,T,Rt,B;}
[StructLayout(LayoutKind.Sequential)] public struct MI {public int cb; public R rc; public R work; public uint f;}
[DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
[DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out R r);
[DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h,uint f);
[DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr m,ref MI i);
[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
[DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
'@
[void][F3.N]::SetProcessDPIAware()
if(-not $Hwnd){
  $log=Get-ChildItem "$env:LOCALAPPDATA\MediaStation\logs\mediastation-*.jsonl"|sort Name|select -Last 1
  $s=[IO.File]::Open($log.FullName,'Open','Read','ReadWrite'); $t=(New-Object IO.StreamReader($s)).ReadToEnd(); $s.Dispose()
  $e=$t -split "`n" | ?{$_ -match '"msg":"(appWindowAttached|appAdopted)"' -and $_ -match ('"appId":"'+$AppId+'"')} | select -Last 1 | ConvertFrom-Json
  $Hwnd=[long]$e.data.hwnd
}
$h=[IntPtr]$Hwnd; $r=New-Object F3.N+R; [void][F3.N]::GetWindowRect($h,[ref]$r)
$m=New-Object F3.N+MI; $m.cb=40; [void][F3.N]::GetMonitorInfo([F3.N]::MonitorFromWindow($h,2),[ref]$m)
$fg=[F3.N]::GetForegroundWindow() -eq $h
"hwnd $Hwnd window $($r.L),$($r.T),$($r.Rt),$($r.B) monitor $($m.rc.L),$($m.rc.T),$($m.rc.Rt),$($m.rc.B) foreground $fg iconic $([F3.N]::IsIconic($h))"
if($fg -and -not [F3.N]::IsIconic($h) -and $r.L -le $m.rc.L -and $r.T -le $m.rc.T -and $r.Rt -ge $m.rc.Rt -and $r.B -ge $m.rc.B){exit 0}; exit 1
```

**F4** (`f4.ps1`, rozdzielczość dynamiczna; `-Test` niczego nie zmienia):

```powershell
param([int]$Width=1920,[int]$Height=1080,[switch]$Test,[switch]$Restore)
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
namespace F4 {
[StructLayout(LayoutKind.Sequential,CharSet=CharSet.Ansi)] public struct DM {
 [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string dmDeviceName; public short specVersion,driverVersion,size,driverExtra; public int fields;
 public int posX,posY,orientation,fixedOutput; public short color,duplex,yRes,tt,collate;
 [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string formName; public short logPixels; public int bits,width,height,flags,freq;
 public int icm,icmIntent,media,dither,r1,r2,panW,panH; }
public static class N {
 [DllImport("user32.dll",CharSet=CharSet.Ansi)] public static extern bool EnumDisplaySettings(string d,int m,ref DM dm);
 [DllImport("user32.dll",CharSet=CharSet.Ansi)] public static extern int ChangeDisplaySettingsEx(string d,ref DM dm,IntPtr h,int f,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Ansi)] public static extern int ChangeDisplaySettingsEx(string d,IntPtr dm,IntPtr h,int f,IntPtr l);
}}
'@
if($Restore){$c=[F4.N]::ChangeDisplaySettingsEx([NullString]::Value,[IntPtr]::Zero,[IntPtr]::Zero,0,[IntPtr]::Zero); "restore: $c"; if($c -eq 0){exit 0}; exit 1}
$d=New-Object F4.DM; $d.size=[Runtime.InteropServices.Marshal]::SizeOf([type][F4.DM])
[void][F4.N]::EnumDisplaySettings([NullString]::Value,-1,[ref]$d); "current $($d.width)x$($d.height)@$($d.freq)"
$d.width=$Width;$d.height=$Height;$d.fields=0x180000
$c=[F4.N]::ChangeDisplaySettingsEx([NullString]::Value,[ref]$d,[IntPtr]::Zero,$(if($Test){2}else{0}),[IntPtr]::Zero); "set ${Width}x${Height} test=${Test}: $c"; if($c -eq 0){exit 0}; exit 1
```

## Kryterium odbioru

Muszą przejść: S1–S3, S5–S8, S10–S14, S16–S18, S32–S35, S37, S39, S41–S43, S46 oraz Home po zmianach ekranu (S24, S30). Razem to Requirements 1–6 i Done when 1–5, a Done when 5 w całości dają S12–S18 oraz S37–S40.

Mogą się różnić z wyjaśnieniem, a decyzję podejmuje użytkownik:

- S19–S21 (uśpienie): porażka to wada Requirement 3, ale ścieżkę synthetic resume sprawdza S1, więc użytkownik waży, czy blokuje epikę, czy idzie do `bmad-build` po zamknięciu.
- S22, S25–S27, S29 (geometria kiosku): wynik rozstrzyga DW-40. Porażka trafia do rejestru albo do `bmad-architecture`, bo zachowanie okna po wygaszeniu należy do WindowManagera. S31 może mieć „nie dotyczy” z powodem.
- S44, S45 (Chrome): Done when wymienia Edge, więc porażka trafia do rejestru.

S4, S9, S15, S23, S28, S36, S38 i S40 są obserwacjami: werdykt należy do użytkownika.

„Zaakceptowana” oznacza dla projektu, że przeglądarka jest zarządzana tak, jak obiecuje CAP-7, Eliza naprawia aplikację z pilota (J4), a epic-netflix-dpad może budować na tym hoście. Status epiki zmienia tylko Sofia (`bmad-agent-po`) po słowie „accepted” od użytkownika.

## Wyniki

Sesja: 2026-10-30, HEAD b041c5d, build 1.0.0+b041c5d, UM760 (MEDIASTATION, F7BSI, EliteMini Series), ekran DELL P2415Q 3840x2160, bez TV i bez drugiego monitora; rozmowa widoczna na telefonie użytkownika.

| # | Wynik | Dowód |
|---|-------|-------|
| P0 | pass (2026-10-30, HEAD b041c5d) | UM760 zidentyfikowany; publish exit 0, 1.0.0+b041c5d; e2e -SelfTest exit 0; cdp-reset -SelfTest -Elevate exit 0 (SetTcpEntry 0, ConnectionReset); dongle VID_1915&PID_1025 obecny; DELL P2415Q, jeden ekran, F4 -Test 0; głos pl-PL; bez MediaStation, porty 9222/9223/9237 wolne, brak restore-bak, apps.json netflix/chrome-test/moonlight-pc w config\; fragmenty F1-F4 w %TEMP%\fp-f |
| S1 | fail (2026-10-30, HEAD b041c5d) raport browserhost-e2e-20261030-131308 | exit 1; run log C:\Users\UYTKOW~1\AppData\Local\Temp\fp-s1.log<br>`R-navigation       fail     pageHello x1, expected at least 2; no reload measurement with a non-empty newPageHello`<br>`R-no-sw-hide       fail     tracer after-home is not iconic and visible (iconic=False visible=True); no iconic measurement in tracer`<br>`FAIL: 5 phase(s), 9 pass / 2 fail / 0 skipped` |
| S1 (rerun 1) | pass (2026-10-30, HEAD b041c5d) powtórka na prośbę użytkownika: podczas pierwszego przebiegu klikał; raport browserhost-e2e-20261030-131808 | exit 0; run log C:\Users\UYTKOW~1\AppData\Local\Temp\fp-s1b.log<br>`R-navigation       pass`<br>`R-no-sw-hide       pass`<br>`PASS: 5 phase(s), 11 pass / 0 fail / 0 skipped` |
| S2 | pass (2026-10-30, HEAD b041c5d) | window since 2026-10-30T13:21:01.962Z until 2026-10-30T13:21:10.564Z; `msg=start,data.version~=b041c5d:1` want 1, got 1; `msg=browserHostEnabled:1` want 1, got 1; `msg=devHarnessEnabled:0` want 0, got 0; `msg=probeExisting,data.appId=netflix,data.outcome=notRunning:1` want 1, got 1; `msg=snapshotPublished,data.rev=1:1` want 1, got 1; forbid error,warn: 0 (allowed: msg=appIconMissing, 3)<br>`{"ts":"2026-10-30T13:21:02.318Z","level":"info","src":"app","msg":"start","data":{"version":"1.0.0+b041c5d47cfb6e86e9a6e26fa23683b0560c033d","pid":22432,"args":[]}}` |
| S3 | fail (2026-10-30, HEAD b041c5d) | window since 2026-10-30T13:22:04.988Z until 2026-10-30T13:22:18.380Z; `msg=browserVersion,data.appId=netflix:1` want 1, got 1; `msg=appLaunch,data.appId=netflix,data.args~=--kiosk:1` want 1, got 1; `msg=appWindowAttached,data.appId=netflix:1` want 1, got 1; `msg=cdpConnected,data.appId=netflix,data.adopted=false:1` want 1, got 0; `msg=pageHello,data.appId=netflix,data.service=netflix,data.webdriver=false:>=1` want >=1, got 1; forbid error,warn: 0 (allowed: msg=appIconMissing, 0); order ok<br>`{"ts":"2026-10-30T13:22:13.184Z","level":"info","src":"browser","msg":"browserVersion","data":{"appId":"netflix","version":"156.0.4314.50","previous":"156.0.4314.50"}}` |
| S3 (rerun 1) | pass (2026-10-30, HEAD b041c5d) poprawiona asercja, to samo okno logu, zgoda użytkownika: ok | window since 2026-10-30T13:22:04.988Z until 2026-10-30T13:22:18.380Z; `msg=browserVersion,data.appId=netflix:1` want 1, got 1; `msg=appLaunch,data.appId=netflix,data.args~=--kiosk:1` want 1, got 1; `msg=appWindowAttached,data.appId=netflix:1` want 1, got 1; `msg=cdpConnected,data.appId=netflix:1` want 1, got 1; `msg=cdpConnected,data.adopted=true:0` want 0, got 0; `msg=pageHello,data.appId=netflix,data.service=netflix,data.webdriver=false:>=1` want >=1, got 1; forbid error,warn: 0 (allowed: msg=appIconMissing, 0); order ok<br>`{"ts":"2026-10-30T13:22:13.184Z","level":"info","src":"browser","msg":"browserVersion","data":{"appId":"netflix","version":"156.0.4314.50","previous":"156.0.4314.50"}}` |
| S4 | | |
| S5 | pass (2026-10-30, HEAD b041c5d) root R=16736, port 9222 tylko 127.0.0.1 | exit 0<br>F1: --kiosk, --remote-debugging-port=9222, bez --remote-allow-origins, --start-fullscreen, --start-maximized, --app=; listeners 127.0.0.1 |
| S6 | | |
| S7 | | |
| S8 | | |
| S9 | | |
| S10 | | |
| S11 | | |
| S12 | | |
| S13 | | |
| S14 | | |
| S15 | | |
| S16 | | |
| S17 | | |
| S18 | | |
| S19 | | |
| S20 | | |
| S21 | | |
| S22 | | |
| S23 | | |
| S24 | | |
| S25 | | |
| S26 | | |
| S27 | | |
| S28 | | |
| S29 | | |
| S30 | | |
| S31 | | |
| S32 | | |
| S33 | | |
| S34 | | |
| S35 | | |
| S36 | | |
| S37 | | |
| S38 | | |
| S39 | | |
| S40 | | |
| S41 | | |
| S42 | | |
| S43 | | |
| S44 | | |
| S45 | | |
| S46 | | |
