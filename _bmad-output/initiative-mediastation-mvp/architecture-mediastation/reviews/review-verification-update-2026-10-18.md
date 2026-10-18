---
title: 'Review: weryfikacja aktualizacji AD-12 i AD-13 (2026-10-18)'
target: ../architecture-mediastation.md
decision-log: ../.memlog.md
date: 2026-10-18
lenses: [verification]
scope: AD-12 (klucz serialu), AD-13 (wykrycie aktualizacji przeglądarki bez CDP, CdpUnavailable), trigger planu C w AD-1
---

# Review: weryfikacja aktualizacji AD-12 i AD-13

**Werdykt:** założenia delty trzymają się w rzeczywistości. Wersję Edge/Chrome da się odczytać bez CDP (sprawdzone na źródłach i lokalnie), a custom profile nadal działają z `--remote-debugging-port`. Są dwa caveaty, które story BrowserHost musi znać. Po pierwsze, część źródeł wersji podaje *ostatnio uruchomioną* wersję, a nie *zainstalowaną*. Po drugie, objaw „proces żyje, port nie słucha” nie jest unikalny dla blokady portu, więc `CdpUnavailable` w smoke nie powinien sam w sobie oznaczać „przejdź na plan C”.

Wszystkie źródła odczytane 2026-10-18. „Lokalnie” = odczyt na maszynie deweloperskiej autora (Edge 155.0.4283.x, Chrome 155.0.8059.85), nie na UM760.

## Podsumowanie

| # | Status | Punkt | Wniosek |
| --- | --- | --- | --- |
| V1 | confirmed | AD-13: wersja przeglądarki bez CDP | Są co najmniej 4 działające źródła; dwa mówią „zainstalowana”, dwa „ostatnio uruchomiona” |
| V2 | caveat | AD-13: wybór źródła i „poprzednia wersja” | BLBeacon i `Last Version` mogą być starsze od binarki (lokalnie: .45 vs .62); staged update `new_msedge.exe` |
| V3 | confirmed (Chrome) / unconfirmed (Edge) | AD-1: blokada portu tylko dla domyślnego profilu | Chrome 136+ ignoruje port tylko dla domyślnego user-data-dir; dla Edge brak źródła Microsoftu, działanie potwierdza spike (Edge 155) |
| V4 | confirmed (Chrome) / caveat | AD-13: blokada = „proces żyje, port nie słucha” | Tak dla Chrome; ten sam objaw dają też zajęty port i polityka enterprise |
| V5 | unverifiable (publicznie) | AD-12: `/watch/<id>` = id odcinka | Brak publicznego źródła; wspiera to spike; nic aktualnego temu nie przeczy |

## V1. Odczyt wersji bez CDP: confirmed

Źródła, które działają (lokalnie dla Edge i Chrome):

| Źródło | Znaczenie | Status |
| --- | --- | --- |
| `FileVersion`/`ProductVersion` pliku `msedge.exe` / `chrome.exe` | binarka, która wystartuje | confirmed lokalnie (Edge `155.0.4283.62`, Chrome `155.0.8059.85`). Uwaga z BigFix dotyczy `MicrosoftEdge.exe` (legacy Edge) z wersją powiązaną z OS; `msedge.exe` niesie prawdziwą wersję [S3] |
| `HKLM\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}` → `pv` (Chrome: `...\Google\Update\Clients\{8A69D345-D564-463c-AFF1-A69D9E530F96}` → `pv`) | wersja zainstalowana według updatera | confirmed lokalnie (`155.0.4283.62`). Forum Patch My PC zgłasza brak klucza w HKLM na niektórych maszynach (instalacja per-user) [S2] |
| `HKCU\Software\Microsoft\Edge\BLBeacon` → `version` (Chrome: `HKCU\Software\Google\Chrome\BLBeacon`) | wersja **ostatnio uruchomiona** przez użytkownika, wspólna dla wszystkich profili | confirmed lokalnie (`155.0.4283.45`) i w źródłach [S1][S2] |
| `<user-data-dir>\Last Version` | wersja ostatnio uruchomiona **z tym user-data-dir** | confirmed w kodzie Chromium (`DowngradeManager::UpdateLastVersion` zapisuje `version_info::GetVersionNumber()` do user-data-dir, logika nie tylko pod Windows, bez warunku na domyślny katalog) [S4]; lokalnie dla domyślnych katalogów Edge (`.45`) i Chrome. Brak lokalnego custom profilu do sprawdzenia |
| Folder wersji obok `msedge.exe` | nie wskazuje jednoznacznie wersji | caveat: lokalnie leżą obok siebie `155.0.4283.45` i `155.0.4283.62` |

## V2. Caveaty wyboru źródła: caveat

- **Zainstalowana vs ostatnio uruchomiona.** Lokalnie Edge był zamknięty podczas aktualizacji 2026-10-17 23:22, więc `msedge.exe` i EdgeUpdate `pv` mają już `.62`, a BLBeacon i `Last Version` nadal `.45`. Wersja „przed startem” według AD-13 to `FileVersion` binarki. BLBeacon nie nadaje się na `from`/`to`, bo jest per użytkownik, a nie per `appId`, i pokazuje inny moment.
- **Wskazówka dla story BrowserHost (opcja, nie wymóg):** `profiles\<appId>\Last Version` przeczytany *przed* startem to naturalna „poprzednia wersja dla tego appId”. Leży poza `data/`, zgodnie z AD-13. Chromium nadpisuje go przy starcie, więc odczyt musi poprzedzić launch.
- **Aktualizacja w trakcie działania.** Gdy przeglądarka działa, instalator zostawia `new_chrome.exe` (Edge analogicznie `new_msedge.exe`), a podmiana na `chrome.exe` następuje przy wyjściu albo starcie przez komendę rename [S5]. Gdy działa inna instancja (druga aplikacja MediaStation albo prywatny Edge Elizy), binarka może zostać stara mimo pobranej aktualizacji. `FileVersion` nadal odpowiada temu, co wystartuje, ale `BrowserVersionChanged` pojawi się dopiero po faktycznej podmianie. Obecność `new_msedge.exe` warto logować jako „aktualizacja oczekuje”. Mechanizm `new_msedge.exe` w Edge wynika z analogii z Chromium, bez źródła Microsoftu.
- `Browser.getVersion` przy podpięciu (AD-13) zamyka te luki jako potwierdzenie. Decyzja jest więc odporna na powyższe; caveaty dotyczą tylko wyboru źródła w story.

## V3. Blokada `--remote-debugging-port`: confirmed dla Chrome, unconfirmed dla Edge

- Od Chrome 136 `--remote-debugging-port` i `--remote-debugging-pipe` „will no longer be respected” dla domyślnego katalogu danych. Muszą iść z `--user-data-dir` wskazującym niestandardowy katalog. Wpis z 2025-03-17, bez wzmianki o Edge [S6].
- Kod Chromium (`remote_debugging_server.cc`, `IsRemoteDebuggingAllowed`): przy domyślnym katalogu zwraca `NotStartedReason::kDisabledByDefaultUserDataDir`. Sprawdzenie jest **zawsze włączone tylko w buildach `GOOGLE_CHROME_BRANDING`**, a w innych buildach Chromium tylko w testach. Nieznany stan katalogu (`value_or(true)`) traktuje jak domyślny. Brak polityki, która by to obchodziła [S7].
- Stan aktualny: na Chrome 151 (zgłoszenie z 2026-08-05) domyślny profil nie otwiera portu, a custom `--user-data-dir` otwiera `/json/version` [S8].
- **Edge:** nie znalazłem źródła Microsoftu ani zgłoszenia z lat 2025–2026, które potwierdza albo wyklucza tę samą blokadę w Edge. Bramka `GOOGLE_CHROME_BRANDING` oznacza, że Edge musiałby ją włączyć osobno. Założenie spine'u („custom profile działają”) potwierdza spike na UM760 (Edge/Chrome 155). Kierunek zmian Chromium jest jednak restrykcyjny, więc trigger planu C w AD-1 jest zasadny.
- Pokrewna, **niepotwierdzona** teza (strona pluginu, nie źródło pierwotne): od Chromium 136 uruchomienie z `--remote-debugging-port` wyłącza rozszerzenia [S9]. Nie dotyczy planu B, ale jeśli jest prawdziwa, plan C (rozszerzenie) musi startować **bez** portu CDP. Do sprawdzenia przy planie C, nie teraz.

## V4. Objaw blokady a `CdpUnavailable`: confirmed (Chrome) / caveat

- Przy blokadzie przeglądarka **działa dalej**: kod tylko nie startuje serwera (`base::unexpected(...)`), bez wyjścia procesu [S7]. Na porcie nic nie słucha i w user-data-dir nie powstaje `DevToolsActivePort` [S8]. Selenium na Windows raportuje komunikat „DevTools remote debugging requires a non-default data directory. Specify this using --user-data-dir.” [S10]. Kanał tego komunikatu (stderr czy log) nie jest potwierdzony; aplikacja GUI na Windows zwykle nie ma podpiętego stderr. Wniosek: objaw „proces żyje, port nie przyjmuje połączeń” zgadza się z tym, co wykrywa `CdpUnavailable`.
- **Caveat: objaw nie jest unikalny.** Ten sam wynik daje polityka enterprise wyłączająca remote debugging (`kDisabledByPolicy` w tym samym kodzie [S7]) i port zajęty przez inny proces. W tym drugim przypadku połączenie może się nawet udać, tyle że do obcego endpointu. Smoke, który traktuje `CdpUnavailable` jako trigger planu C, powinien w raporcie odróżniać przyczyny. Tanie sygnały to brak pliku `profiles\<appId>\DevToolsActivePort`, właściciel portu (np. `GetExtendedTcpTable`) i wersja z V1. To uwaga do story BrowserHost i smoke; kontraktu w spine'ie nie zmienia, bo trigger i tak uruchamia autor po przeczytaniu raportu.
- Uwaga uboczna: jeśli instancja z tym samym `--user-data-dir` już działa, nowy proces przekazuje jej linię poleceń i kończy się. To daje `ProcessExited`, nie `CdpUnavailable`, i jest zgodne z rozróżnieniem w AD-13. Adopcja (AD-10) pokrywa ten przypadek.

## V5. AD-12: `/watch/<id>` niesie id odcinka: unverifiable publicznie

- Nie znalazłem oficjalnego ani aktualnego źródła, które opisuje semantykę id w `/watch/` vs `/title/` vs `jbv`. Źródła społeczności potwierdzają tylko, że `jbv` to numeryczne id tytułu (przykład: serial) [S11]. Jedna biblioteka wkłada id serialu w `/watch/<id>` [S12]. To zgodne z obserwacją, że Netflix przyjmuje id serialu w `/watch` i przechodzi do odcinka, ale nie rozstrzyga, co jest w URL-u w trakcie odtwarzania.
- Wewnętrzne wsparcie: spike (`spike-results-um760.md`: „odcinek startuje (`/watch/80021960`)”, `/browse?jbv=ID` → `/title/ID`) i forge (`forge-filmweb-rating-prompt/.memlog.md`). Nic aktualnego temu nie przeczy.
- Decyzja AD-12 jest odporna na błąd tego założenia: `seriesId` jest best-effort, z fallbackiem `titleNorm` i dopasowaniem uzupełniającym. Weryfikacja należy do testów drivera na fixture'ach DOM (AD-13), nie do spine'u.

## Źródła (dostęp 2026-10-18)

- [S1] ConfigMgr Microsoft Edge SQL Query (BLBeacon `version`): https://www.anoopcnair.com/configmgr-microsoft-edge-sql-query-sccm-report/
- [S2] Patch My PC forum (BLBeacon, EdgeUpdate `pv`, brak klucza HKLM): https://forum.patchmypc.com/index.php?msg=16891
- [S3] BigFix forum, Microsoft Edge Version Detection: https://forum.bigfix.com/t/microsoft-edge-version-detection/41561
- [S4] Chromium `downgrade_manager.cc`: https://raw.githubusercontent.com/chromium/chromium/main/chrome/browser/downgrade/downgrade_manager.cc
- [S5] Chromium `upgrade_util_win.cc` / `.h` (`new_chrome.exe` swap): https://chromium.googlesource.com/chromium/src/+/lkgr/chrome/browser/first_run/upgrade_util_win.h
- [S6] Chrome for Developers, „Changes to remote debugging switches to improve security” (2025-03-17): https://developer.chrome.com/blog/remote-debugging-port
- [S7] Chromium `remote_debugging_server.cc`: https://raw.githubusercontent.com/chromium/chromium/main/chrome/browser/devtools/remote_debugging_server.cc
- [S8] trycua/cua issue #2916 (Chrome 151, 2026-08-05): https://github.com/trycua/cua/issues/2916
- [S9] edge-debug-port skill page (niepotwierdzone): https://www.claudepluginhub.com/skills/stgmt-dev-pomogator/edge-debug-port
- [S10] SeleniumHQ/selenium issue #16274 (2025-08-29): https://github.com/SeleniumHQ/selenium/issues/16274
- [S11] Szablon „Netflix title” (jbv jako id): https://hiwiki.iiit.ac.in/index.php/साँचा:नेटफ्लिक्स_शीर्षक/doc
- [S12] netwix: https://www.github.com/new92/netwix
- Lokalnie (2026-10-18, maszyna deweloperska): `reg query` BLBeacon Edge/Chrome, EdgeUpdate/GoogleUpdate `pv`, `FileVersion` `msedge.exe`/`chrome.exe`, listing `C:\Program Files (x86)\Microsoft\Edge\Application`, pliki `Last Version` w domyślnych user-data-dir.
