# MediaStation spike: sterowanie przeglądarką (S1–S11, wariant C)

Jednorazowy harness do spike'u z raportu [research-browser-control-for-streaming-services](../_bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md), sekcje „Rekomendacja” i „Spike na Netfliksie”. Wyniki wpisujemy do [spike-results.md](spike-results.md). Kod jest celowo prosty i nie trafi do produktu.

## Co tu jest

| Plik | Do czego | Testy |
|---|---|---|
| `harness/ms.mjs` | CLI: `launch`, `serve`, `cmd`, `repl`, `ext-temp`, `smoke-page`, `copy-cdm` | S1–S8, C |
| `harness/commands.mjs` | komendy wspólne dla `cmd` i `repl`, każda zwraca JSON | S2–S8, C |
| `harness/drivers.mjs` | transporty: surowe CDP bez `Runtime.enable`, Playwright `connectOverCDP` i `launch`, serwer WebSocket dla rozszerzenia | S2, S3, C |
| `extension/probe.js` | skrypt wstrzykiwany do strony, wspólny dla wszystkich transportów: `navigator.webdriver`, zdarzenia `<video>`, trasy URL, `focusin`, klawisze z `isTrusted`, programowy fokus z fallbackiem, tryb adaptera | S2, S5–S8, C |
| `extension/` | wariant C: rozszerzenie MV3, w którym `probe.js` działa w świecie MAIN, a service worker łączy się z harnessem przez `ws://127.0.0.1:9330` | C |
| `smoke/index.html` | strona testowa bez DRM do smoke testu harnessu | — |
| `win/audio-mute.ps1` | sesje audio per PID, mute/unmute drzewa procesów, monitor | S9 |
| `win/home-logger.ps1` | logger pilota: LL hook + Raw Input (klawiatura, Consumer Control) + `WM_APPCOMMAND`, próba `SetForegroundWindow` | S10 |
| `win/moonlight.ps1` | `stream`/`list`/`quit` z CLI, ukrycie lub minimalizacja na N minut | S11 |
| `win/window.ps1` | ukrycie, pokazanie, minimalizacja okien dowolnego procesu | S8 (wariant hide), S11 |
| `win/screenshot.ps1` | zrzut całego ekranu do `logs/` (dowód, gdy na wierzchu nie ma strony) | S10, S11, dowody |
| `helpers/` | skrypty pomocnicze dopisane na UM760: przebiegi bez udziału człowieka w oknie rozmowy (`s10-run.ps1`, `s9-run.ps1`), sekwencje komend ze stanem przed i po (`s6-seq.mjs`, `focus-seq.mjs`, `c-seq.mjs`, `s7.mjs`, `c3.mjs`), pomiary (`player-poll.mjs`, `modewatch.ps1`, `endpoint.ps1`, `binding-test.mjs`), `keep-awake.ps1` (blokada Modern Standby na czas S8) | S6–S11 |

## Wymagania na UM760

- Node.js 20.11+.
- Edge i Chrome w wersjach stabilnych, zainstalowane w standardowych ścieżkach (inaczej `--exe`).
- Windows PowerShell 5.1 (`powershell.exe`, a nie `pwsh`). Skrypty mają `#Requires -PSEdition Desktop`, bo C# kompiluje się przez `Add-Type`.
- Moonlight-qt w `C:\Program Files\Moonlight Game Streaming\` (inaczej `-Exe`).
- Pilot G20S Pro przez **dongle USB 2.4G**, nie Bluetooth. Pro i Pro BT wysyłają różne kody, więc wyniki S10 dotyczą tylko dongla.

```powershell
cd spike
npm install
node harness/ms.mjs --help
node harness/ms.mjs cmd help     # lista komend
```

Pliki `.ps1` i `.cs` w `win/` są zapisane w **UTF-8 z BOM**. Bez BOM PowerShell 5.1 czyta je jako cp1252 i polskie znaki psują parsowanie. Edytor nie może usunąć BOM.

## Profile, porty, logi

- Profile przeglądarek: `spike/profiles/<nazwa>`. Domyślnie `edge-netflix` i `chrome-netflix` (`launch`), `edge-pw` i `chrome-pw` (`serve --mode pwlaunch`), `edge-ext` i `chrome-ext` (`launch --no-cdp`, wariant C). W każdym profilu trzeba raz zalogować się do Netfliksa.
- Porty: CDP Edge **9223**, Chrome **9224**; WebSocket rozszerzenia **9330** (wpisany na stałe w `extension/background.js`); `serve` **9340**; `smoke-page` **9350**.
- Logi: `spike/logs/`. Harness zapisuje JSON Lines (`<data>-<etykieta>.jsonl`), a skrypty `win/` zwykłe linie tekstu. Znaczniki czasu są wszędzie w UTC, więc logi da się zestawić.
- `profiles/` i `logs/` są w `.gitignore`. Profil zawiera zalogowaną sesję Netfliksa. Do repo trafiają tylko wnioski w `spike-results.md`.

> **Bezpieczeństwo:** otwarty port CDP pozwala każdemu lokalnemu procesowi przejąć zalogowany profil. Po sesji zamknij przeglądarki uruchomione przez harness.

## Dla agenta: serve, cmd, log

Agent nie prowadzi interaktywnego REPL. Każde polecenie uruchamia osobno, a stan trzyma proces `serve` działający w tle.

1. **Przeglądarka:** `node harness/ms.mjs launch --browser edge`. Polecenie kończy się od razu, przeglądarka działa dalej. Wariant C: `launch --browser edge --no-cdp`.
2. **Serve w tle** (Bash z `run_in_background`):

   ```bash
   node harness/ms.mjs serve --mode raw --browser edge --label S2a-edge
   ```

   Tryby: `raw` (surowe CDP bez `Runtime.enable`), `pw` (Playwright `connectOverCDP`, woła `Runtime.enable`), `ext` (rozszerzenie, port 9330), `pwlaunch` (S3: Playwright sam uruchamia przeglądarkę; ta żyje tyle co serve). `serve` trzyma połączenie, więc skrypt z `Page.addScriptToEvaluateOnNewDocument` obowiązuje na każdej kolejnej stronie. Gotowość: na stdout pojawia się linia `READY ctl=… events=…` i powstaje plik `logs/serve-9340.json` z polami `pid`, `mode`, `driver` i `events` (ścieżka do JSONL). W trybie `ext` READY pojawia się dopiero po połączeniu rozszerzenia.
3. **Komendy:** `node harness/ms.mjs cmd "focus down"`. Na stdout trafia jedna linia JSON: `{"ok":true,"cmd":…,"ms":…,"result":…}` albo `{"ok":false,"error":…}`. Kody wyjścia: 0 = ok, 1 = błąd komendy, 2 = serve nie działa. Pełna lista: `cmd help`.
4. **Log zdarzeń:** plik z pola `events` w `logs/serve-9340.json`. Jedna linia to jeden obiekt JSON: `kind` = `event` (zdarzenia probe: `type` = `probe-loaded`, `route`, `focus`, `text-focus`, `key`, `key-after`, `adapter`, `video`, `video-sample`, `visibility`, `page-error`), `cmd` (komenda i wynik), `s8`, `mark`, `info`, `serve`. Plik jest dopisywany synchronicznie, więc można go czytać w trakcie pracy.
5. **Czekanie na zdarzenie:** `cmd "wait route 60"` albo `cmd "wait key ArrowDown 30"` czeka na zdarzenie danego typu (opcjonalnie zawierające tekst). Limit to około 280 s. Przy dłuższych testach (S8) czytaj log.
6. **Koniec:** `cmd quit` zatrzymuje serve (przeglądarka zostaje, poza trybem `pwlaunch`).

Przykład S6 z udziałem człowieka: agent wysyła `cmd "mark S6 player Space fizyczny"`, prosi o naciśnięcie Space na pilocie, potem `cmd "wait key 30"`, a następnie `cmd "key Space"` i `cmd "jskey Space"`. Porównuje `trusted` i `prevented` w logu.

Komendy (te same w `cmd` i `repl`):

| Komenda | Działanie |
|---|---|
| `state` / `url` / `webdriver` / `video` | stan z probe: trasa (`profiles`, `browse`, `browse-modal`, `title`, `search`, `watch`), `href`, `navigator.webdriver`, fokus, `<video>`, API playera Netfliksa, błąd |
| `key ArrowDown Enter` | zaufane klawisze przez CDP `Input.dispatchKeyEvent`, potem stan. W C niezaufane, jak `jskey` |
| `key Ctrl+Alt+Shift+D` | nakładka statystyk Netfliksa (nie w C) |
| `jskey ArrowDown` | niezaufany `KeyboardEvent` przez `dispatchEvent` |
| `type tekst` / `inserttext tekst` | CDP `Input.insertText` (w C: `execCommand`) |
| `jstype exec tekst` / `jstype value tekst` | tekst bez CDP |
| `focus up\|down\|left\|right` | programowy fokus; wynik ma pole `fallback`: `none`, `dom-order`, `row-arrow` (z selektorem strzałki), `offscreen-geometric`, `none-found` |
| `activate` | `el.click()` z `isTrusted=false` |
| `adapter on\|off` | probe przechwytuje **fizyczne** strzałki i OK, robi `focus` lub `activate` i loguje zdarzenia `adapter`. Wyłączony na `/watch` i dla strzałek w lewo/prawo w polach tekstowych. Nie łącz z `--spatial` |
| `back esc\|history\|modal` | trzy metody Back; wynik: stan przed i po |
| `pause video\|api`, `play video\|api` | `<video>` albo wewnętrzne API playera Netfliksa |
| `win minimized\|normal\|maximized\|fullscreen`, `front` | stan okna; `front` wysuwa przeglądarkę na pierwszy plan |
| `s8 30 api minimized` | S8 w tle procesu serve; koniec w logu jako `kind=s8, phase=end` |
| `screenshot [nazwa]` | PNG widoku strony do `logs/<data>-<nazwa>.png`; wynik zawiera ścieżkę `file`, a log zdarzenie `kind=screenshot`. Raw i pw używają CDP `Page.captureScreenshot`, C używa `chrome.tabs.captureVisibleTab` (karta musi być widoczna). Obraz wideo z DRM zwykle wychodzi czarny, ale nakładka statystyk i interfejs Netfliksa są widoczne |
| `drm`, `nav URL`, `eval JS` (nie w C), `mark tekst`, `events N`, `wait TYP [TEKST] [S]`, `quit` | |

**Dla człowieka:** `node harness/ms.mjs repl` to interaktywny klient tego samego `serve`, przez który przechodzi ten sam mechanizm. `exit` wychodzi z REPL, `quit` zatrzymuje serve. Konsola `serve` (bez `--quiet`) pokazuje zdarzenia na żywo.

## Skrypty `win/` w tle

Skrypty działające długo przyjmują `-DurationSec` i kończą się też po pojawieniu się pliku stop. Domyślnie jest to `logs\<skrypt>.stop`, czyli `home-logger.stop`, `audio-mute.stop` albo `moonlight.stop`; inną ścieżkę ustawia `-StopFile`. Log zapisują do pliku: domyślnie `logs\<data>-<etykieta>.log`, a `-LogFile` ustala stałą ścieżkę. Na początku wypisują `LOG=`, `STOP=` i `PID=`.

```powershell
# start w tle (agent: Bash run_in_background albo Start-Process)
Start-Process powershell.exe -ArgumentList '-ExecutionPolicy','Bypass','-File','win\home-logger.ps1','-DurationSec','300','-LogFile','logs\S10-home.log'
# ... prośba do człowieka o naciśnięcie Home, potem czytanie logs\S10-home.log ...
New-Item -ItemType File logs\home-logger.stop      # wcześniejsze zatrzymanie
```

| Skrypt | Długie tryby | Uwagi |
|---|---|---|
| `home-logger.ps1` | zawsze (okno loggera) | `-HomeVk`, `-HomeAppCommand`, `-HomeHidUsage` wybierają, co liczy się jako Home; log zawsze zawiera wszystkie źródła. `-Foreground plain\|attach\|alt`, `-Swallow` |
| `audio-mute.ps1` | `-Action monitor`, `mute`/`unmute -Watch` | `-IntervalSec`; `list`, `mute` i `unmute` bez `-Watch` kończą się od razu |
| `moonlight.ps1` | `-Action hide-test` | niczego nie pyta; po przywróceniu loguje „SPRAWDŹ”, a wynik ocenia człowiek |
| `window.ps1` | — | krótkie akcje |
| `screenshot.ps1` | — | `-Name S10-edge-home -DelaySec 3`; wypisuje `FILE=`. Zrzut całego ekranu; treści DRM i HDR mogą wyjść czarne lub wyblakłe |

- **audio-mute:** korzeniem jest proces, którego linia poleceń zawiera ścieżkę profilu i nie ma `--type=`. Wyciszane są sesje wszystkich potomków. Z `-NoTree` tylko korzeń, jako kontrola negatywna (sesję audio ma proces potomny). Metoda `via=mute` to `ISimpleAudioVolume::SetMute`. `via=channel` to fallback `IChannelAudioVolume` (wszystkie kanały 0 lub 1,0), użyty, gdy sesja nie udostępnia `ISimpleAudioVolume`.
- **home-logger:** każda linia ma źródło (`LL`, `RAW-KBD`, `RAW-HID`, `WM_APPCOMMAND`, `HSHELL_APPCOMMAND`) i proces na pierwszym planie (`fg=nazwa:PID "tytuł"`). Przy pierwszym zdarzeniu z urządzenia log zawiera jego nazwę, VID/PID oraz usage page i usage. AC Home to zwykle consumer usage 0x0223, często bez kodu VK.

## Wariant C: rozszerzenie zamiast CDP

### Ręczne „Load unpacked” (do testów C1–C3)

Chrome 137+ ignoruje `--load-extension` w buildach branded. Instalacja przez CDP (`ext-temp`) działa tylko na czas sesji i ustawia `webdriver=true`. Dlatego do pomiarów C rozszerzenie instaluje się ręcznie, raz na profil:

1. `node harness/ms.mjs launch --browser edge --no-cdp` uruchamia profil `edge-ext` bez portu debugowania (Chrome: `--browser chrome`, profil `chrome-ext`).
2. Otwórz `edge://extensions` (w Chrome `chrome://extensions`).
3. Włącz **tryb dewelopera** (przełącznik po lewej w Edge, w prawym górnym rogu w Chrome).
4. Kliknij **„Załaduj rozpakowane”** („Load unpacked”) i wskaż katalog `spike\extension`.
5. Sprawdź, czy pojawiło się „MediaStation spike probe (variant C)” bez błędów.
6. Zaloguj się do Netfliksa w tym profilu.

Po każdej zmianie plików w `extension/` kliknij przy rozszerzeniu **„Załaduj ponownie”** i odśwież kartę Netflixa. Bez tego Chrome uruchamia stary, zapamiętany service worker (zaobserwowane w smoke teście). Na wszelki wypadek możesz też podnieść `version` w `manifest.json`.

### Sesja C

```bash
node harness/ms.mjs launch --browser edge --no-cdp            # jeśli przeglądarka nie działa
node harness/ms.mjs serve --mode ext --label C1-edge          # w tle; READY po połączeniu rozszerzenia
node harness/ms.mjs cmd state
```

- Service worker łączy się sam: przy starcie, przy każdym zdarzeniu z karty oraz co 20–30 s. Zdarzenia sprzed połączenia są kolejkowane. Linia `rozszerzenie połączone` w logu zawiera `swStats` (odebrane, zakolejkowane, wysłane).
- `driver=extension`, `trusted=false`: `key` działa jak `jskey`. Kombinacje (`Ctrl+…`) i `eval` zwracają błąd. `win` i `front` używają `chrome.windows.update`, a `nav` używa `chrome.tabs.update`.
- Content scripty działają tylko na `*.netflix.com` i stronie smoke (`127.0.0.1:9350`); przed S12 trzeba dopisać domeny w `content_scripts.matches`. `host_permissions` to `<all_urls>`, bo wymaga tego `captureVisibleTab` (komenda `screenshot`). Przy instalacji przeglądarka pokaże ostrzeżenie o dostępie do wszystkich stron.

## Smoke test harnessu (bez Netflixa)

```bash
node harness/ms.mjs smoke-page                                                    # w tle
node harness/ms.mjs launch --browser edge --profile smoke-edge --url http://127.0.0.1:9350/
node harness/ms.mjs serve --mode raw --browser edge --quiet                        # w tle
node harness/ms.mjs cmd webdriver; node harness/ms.mjs cmd "focus down"; node harness/ms.mjs cmd quit
node harness/ms.mjs ext-temp --browser chrome --profile smoke-chrome-ext --url http://127.0.0.1:9350/   # C, tymczasowo
```

Strona smoke ma wiersze w stylu Netflixa (`.rowContainer`, `.handle.handleNext`, wiersz 2 z `overflow: clip` wymusza fallback `row-arrow`), wyszukiwarkę zmieniającą `?q=` oraz `<video>` z canvas. Z `?audio=1` gra prawie niesłyszalny ton po pierwszym klawiszu, co pozwala sprawdzić S9.

## Kolejność sesji

Plan, kryteria i miejsca na wyniki są w [spike-results.md](spike-results.md).

- **Sesja 1:** S1 → S4 → S2 → S3 → S6 → S5 → S7, z bramką po S2 i S6. Potem C1–C3, jeśli starczy czasu (bramka decyduje o rekomendacji, a nie o tym, czy testujemy C).
- **Sesja 2:** S10 → S8 → S9 → S11.
- Odłożone: S12 oraz HDR i Atmos na TV.
