---
title: 'Spike 6.1: tryb okna przeglądarki i źródło wersji poza CDP'
type: 'chore'
ticket: '1'
created: '2026-10-27'
status: done
baseline_revision: '78610779292fd0961480ae5132a8a6aa817df757'
route: 'oneshot'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: [blind-hunter, edge-case-hunter, verification-gap, intent-alignment]
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      Notatki w plikach ticketów (epic-browserhost, epic-osk, epic-windows-audio) nadal opisują tryb okna i źródło wersji jako otwarte pytania.
    evidence: |-
      Edge-case-hunter wskazał epic-browserhost.md (Notes: dwa Open question), epic-osk.md i epic-windows-audio.md ("czeka na story 1"). Workflow zabrania zapisu w plikach ticketów, więc to zadanie dla Sofii po oznaczeniu 6.1 jako done.
    location: >-
      tickets/epic-browserhost/epic-browserhost.md:43-44
    severity: low
  - summary: >-
      Gdy BrowserHost padnie po starcie przeglądarki, a przed zalogowaniem BrowserVersionChanged, następny start widzi from==to i zmiana wersji ginie.
    evidence: |-
      Last Version zapisuje przeglądarka przy starcie, a zdarzenie loguje BrowserHost. Do rozstrzygnięcia w story BrowserHost (np. log from/to przed launchem albo własny zapis ostatnio zalogowanej wersji).
    location: >-
      architecture-mediastation.md AD-13 (Aktualizacja przeglądarki)
    severity: low
  - summary: >-
      Kiosk nie był sprawdzony przy kilku monitorach, po zmianie rozdzielczości lub wyłączeniu TV, ani z nakładką OSK nad oknem.
    evidence: |-
      Pomiar miał jeden monitor 1920x1080. Nakładka nad pełnoekranowym Edge jest pierwszą story epiki OSK (Deferred w spine), a zachowanie okna po wygaszeniu ekranu należy do WindowManager.
    location: >-
      architecture-mediastation.md AD-1
    severity: medium (unverified)
---

<intent-contract>

## Intent

**Problem:** Spine (AD-1, AD-13) ma dwa otwarte pytania blokujące stories WindowManager i OSK: którą flagę trybu okna Edge wybrać (`--app`, `--start-fullscreen`, `--kiosk`) oraz skąd czytać wersję przeglądarki poza CDP.

**Approach:** Zmierzyć trzy flagi na Edge na bieżącej maszynie (UM760) harnessem ze `spike/` z flagami AD-1 (minimalizacja i przywrócenie bez `SW_HIDE`, pełny ekran, `f`/`Escape`, pasek adresu, jakość i DRM), sprawdzić kandydatów na źródło wersji, zapisać wyniki w tym planie i przenieść rozstrzygnięcie do AD-1 i AD-13 w architekturze.

</intent-contract>

## Implementation Notes

Oneshot: spike pomiarowy, bez kodu produktu; narzędzia pomiarowe w `spike/helpers/` (poza produktem). Maszyna: **UM760** (host MEDIASTATION, płyta F7BSI, model EliteMini Series), Windows 11, monitor 1920×1080, Edge 156.0.4314.42. Netflix `/watch/81771640`, profil `spike/profiles/edge-netflix`, flagi AD-1 (`--user-data-dir`, `--remote-debugging-port=9223`, `--no-first-run`, `--no-default-browser-check`, `--autoplay-policy=no-user-gesture-required`) plus jedna flaga trybu; klient: surowy CDP bez `Runtime.enable` (`webdriver=false` w każdym przebiegu). Pomiar okna: `spike/helpers/winfacts.ps1` (style, prostokąt vs monitor, `IsIconic`), minimalizacja i przywrócenie przez `ShowWindow(6)` i `ShowWindow(9)`, nigdy `SW_HIDE`; `f` i `Escape` jako zaufane CDP `Input.dispatchKeyEvent`.

### Wyniki: tryb okna

| Pomiar | `--app` | `--start-fullscreen` | `--kiosk` |
|---|---|---|---|
| Okno po starcie | 945×1012, pasek tytułu (caption, thickframe), nie pełny ekran | cały monitor 1920×1080, bez caption | cały monitor 1920×1080, bez caption, jedno okno |
| Pasek adresu i zakładki | brak, ale stały pasek tytułu z min/max/zamknij | ukryte, **odsłaniane po najechaniu na górną krawędź** (zakładki i adres `netflix.com/watch/…`, zrzut `spike/logs/61-topedge-fullscreen.png`) | **brak** także na górnej krawędzi (tylko własna strzałka Netflixa, `spike/logs/61-topedge-kiosk.png`) |
| Minimalizacja `SW_MINIMIZE` | `IsIconic=True`, strona `hidden`, odtwarzanie biegnie (t 581→590 s) | `IsIconic=True`, odtwarzanie biegnie (t 685 s) | `IsIconic=True`, strona `hidden`, odtwarzanie biegnie (t 764→774 s) |
| Przywrócenie `SW_RESTORE` | wraca do 945×1012, strona `visible`, odtwarzanie dalej | wraca na cały monitor | wraca na cały monitor, `visible`, `paused=false` (t 780 s) |
| `f` w playerze | pełny ekran HTML na cały monitor, bez caption, plus osobne okno dymka „wyjście z pełnego ekranu” | `document.fullscreenElement` ustawione, okno bez zmian | `document.fullscreenElement` ustawione, okno bez zmian, bez dymka |
| `Escape` | wraca do **małego** okna 945×1012 | wyłącza pełny ekran HTML, zostaje pełny ekran przeglądarki | wyłącza pełny ekran HTML, zostaje kiosk |
| Inne | po `f` i `Escape` nie ma drogi do trybu pełnoekranowego bez dodatkowego wywołania (CDP `Browser.setWindowBounds` albo F11) | dymek „Przywróć strony” na górze po poprzednim zamknięciu (także `CloseMainWindow`, nie tylko `taskkill`) | brak dymka w żadnym przebiegu (po `taskkill /F` poprzedniego okna i po `CloseMainWindow`, ale przy usuniętych plikach sesji między przebiegami; kiosk zamknięty przez `CloseMainWindow` bez błędów) |
| Jakość i DRM | 1920×1080, 0 klatek utraconych, brak błędu DRM | 1920×1080, 0–3 klatki utracone, brak błędu | 1920×1080, 0 klatek utraconych, brak błędu |

**Rozstrzygnięcie: `--kiosk`.** Jedyna flaga, która daje pełny monitor bez żadnego UI przeglądarki i bez dodatkowego wywołania, zachowuje `f` i `Escape` w obrębie playera oraz przeżywa minimalizację i przywrócenie bez `SW_HIDE`. `--app` odpada (małe okno z paskiem tytułu, `Escape` wraca do niego), `--start-fullscreen` odpada (górna krawędź odsłania pasek adresu, dymek „Przywróć strony”).

**DRM i jakość (uwaga).** Pierwszy przebieg `--app` miał w nakładce statystyk (Ctrl+Alt+Shift+D) HEVC 1920×1080 5198 kb/s i PlayReady `com.microsoft.playready.recommendation.3000`. Od następnych przebiegów (w tym ponownie `--app`, `--start-fullscreen` i `--kiosk`) nakładka pokazuje H.264 `avc1.640028` 1080p z `com.widevine.alpha.SW_SECURE_DECODE` i `HDR support: false (disabled)`. Zmiana nastąpiła w tym samym trybie (`--app`), więc nie wynika z flagi. Zbiegła się w czasie z błędami D7361-1253 i D7353-5102 przy szybkich przeładowaniach `/watch`, ale przyczyna nie jest ustalona, podobnie jak to, co przywraca PlayReady. Zakres pomiaru: jedno konto, jeden tytuł, jeden monitor, brak HDR, jedna próba na tryb; minimalizacja w `--start-fullscreen` ma tylko jeden punkt czasu. Dowody (zrzuty, JSONL) leżą w `spike/logs/`, które jest w `.gitignore`, więc są lokalne na UM760. Chrome: odczytano tylko `Last Version`, `FileVersion` Chrome nie był mierzony (ta sama zasada). Ścieżka przez `bmad-architecture` nie była uruchamiana: AD-1, AD-13 i Deferred zmieniono bezpośrednio (`status` spine'u bez zmian, `updated` 2026-10-27).

**Odstępstwa od pomiaru.** Na UM760 na pierwszym planie jest aplikacja Claude i odbiera fokus po chwili, więc okno było przez większość pomiarów zasłonięte, a strona zgłaszała `visibilityState=hidden` (Netflix wtedy nie startuje odtwarzania). Pomiary wykonano po `SetForegroundWindow` z symulowanym Alt albo (`--app`) po tymczasowym `HWND_TOPMOST`; dla `--start-fullscreen` `HWND_TOPMOST` się nie ustawia, wystarczył `front`. Między przebiegami Edge był zamykany przez `taskkill /F` albo `CloseMainWindow`, a pliki sesji profilu były usuwane (Edge odtwarzał poprzednie okno `--app` obok nowego).

### Wyniki: źródło wersji poza CDP

Odczyt na UM760 (Edge 156.0.4314.42):

| Kandydat | Odczyt | Uwagi |
|---|---|---|
| `FileVersion` pliku `C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe` | `156.0.4314.42` (`ProductVersion` taki sam) | czytane bez uruchamiania; zgodne z `Browser.getVersion` (`Edg/156.0.4314.42`) w przebiegu |
| `Last Version` w `user-data-dir` | profile nieuruchomione od aktualizacji: `155.0.4283.45` (edge-pw, edge-ext, edge-fresh1; Chrome `155.0.8059.40`); `edge-netflix` po starcie: `156.0.4314.42` | jedna linia tekstu w katalogu głównym profilu; mtime pliku = czas ostatniego startu przeglądarki, więc plik jest nadpisywany przy starcie |
| Rejestr `HKCU\Software\Microsoft\Edge\BLBeacon\version` | `156.0.4314.42` | tylko Edge, niepotrzebne |
| Katalog `Application\<wersja>` | `156.0.4314.42` | to samo, wymaga skanowania |

**Rozstrzygnięcie:** bieżąca wersja = `FileVersion` binarki użytej do startu; poprzednia = `profiles\<appId>\Last Version`, czytana przed startem (brak pliku = pierwszy start, bez zdarzenia). `Last Version` profili sprzed aktualizacji (155) i binarka 156 dają realne `BrowserVersionChanged`.

### Zmienione pliki

- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md`: AD-1 (flaga `--kiosk` z pomiarami), AD-13 (źródła wersji), Deferred (pytania rozstrzygnięte), `updated`.
- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/.memlog.md`: wpis decyzji.
- `spike/helpers/winfacts.ps1`, `mode61.sh`, `mode61-next.sh`, `keysys61.sh`, `crop61.ps1`, `topedge61.ps1`: narzędzia pomiarowe spike'a (poza produktem).

## Plan Change Log

## Review Triage Log

### 2026-10-27 — Review pass
- verdicts: 13 rows — high 0, medium 3, low 6, false 1, maybe-false 3 (blind-hunter i edge-case-hunter zgłosiły łącznie ok. 25 uwag, intent-alignment 6 rozbieżności; wiersze grupują uwagi o tej samej przyczynie, a rozproszone uwagi o skryptach i metadanych są w jednym wierszu każda)
- findings:
  - `[medium]` `[patch]` AD-1 twierdzi, że DRM/jakość nie zależą od flagi i że 1080p gra „bez błędów” w każdym trybie, a plan przyznaje nieustaloną zmianę PlayReady na Widevine SW i błędy D7361-1253/D7353-5102 (blind-hunter, edge-case-hunter, intent-alignment) — przepisano AD-1 na fakty z zakresem pomiaru i bez przypisywania przyczyny.
  - `[medium]` `[patch]` „SW_MINIMIZE i SW_RESTORE wystarczają” pominęło, że pomiar używał `front` po przywróceniu, a zasłonięte okno daje `visibilityState=hidden` (blind-hunter, edge-case-hunter, intent-alignment) — dopisano w AD-1, że wierzch nadal wynosi WindowManager (S10).
  - `[medium]` `[patch]` Pliki `Last Version` i `FileVersion` mogą być brakujące, puste, niesparsowane albo o innym formacie (blind-hunter, edge-case-hunter) — dopisano w AD-13: brak wersji do porównania, log `unknown`, porównanie po `System.Version`.
  - `[low]` `[patch]` Brak dymka w kiosku mierzono przy usuniętych plikach sesji, a kiosk nie miał zapisanego wyniku zamknięcia (blind-hunter, edge-case-hunter) — dopisano w planie warunki i zamknięcie przez `CloseMainWindow`.
  - `[low]` `[patch]` Dowody w `spike/logs` są poza repo, Chrome `FileVersion` niemierzony, ścieżka `bmad-architecture` nieużyta (blind-hunter, intent-alignment) — dopisano do planu.
  - `[low]` `[defer]` Notatki ticketów epiki nadal mówią „otwarte pytanie” (edge-case-hunter) — zabroniony zapis w tickecie, zadanie dla Sofii; w `deferred`.
  - `[low]` `[defer]` `from==to` po awarii BrowserHost między startem przeglądarki a logiem (edge-case-hunter) — decyzja projektowa story BrowserHost; w `deferred`.
  - `[maybe-false]` `[defer]` Kiosk przy wielu monitorach, zmianie rozdzielczości, wygaszeniu TV i z nakładką OSK (blind-hunter, edge-case-hunter) — wymagałoby pomiarów, których spike nie obejmował; w `deferred`.
  - `[maybe-false]` `[reject]` `FileVersion` stub vs załadowana wersja po aktualizacji w tle (blind-hunter, edge-case-hunter) — AD-13 już porównuje z `Browser.getVersion` przy podpięciu i smoke nie przechodzi przy różnicy; rozbieżność jest wykrywana.
  - `[maybe-false]` `[reject]` Zapis `Last Version` „przy starcie”, nie „przy wyjściu” (edge-case-hunter) — mtime pliku `edge-netflix` równał się czasowi ostatniego startu, a profil po `taskkill /F` też miał świeżą wersję 156; dowód wystarczający.
  - `[low]` `[reject]` Higiena skryptów pomiarowych (przenośność `pwsh`, zwalnianie bitmap, DPI, twardo wpisane sleepy i ID tytułu, brak sprawdzania pid, `rm -rf` sesji, `keybd_event` Alt, wspólna nazwa `Top`, ślad „zrodo” w nazwie pliku) (blind-hunter, edge-case-hunter) — jednorazowe narzędzia w `spike/helpers/`, poza produktem, uruchamiane ręcznie na jednej maszynie; poprawki dodałyby złożoność bez wpływu na wynik.
  - `[low]` `[reject]` Metadane planu (`risk`, `ticket: '1'`, pusta weryfikacja automatyczna) i nierówność komórek tabeli (jeden punkt czasu minimalizacji `--start-fullscreen`) (blind-hunter, intent-alignment) — `ticket: '1'` to id wpisu w drzewie, spike nie ma kodu do testów automatycznych, a nierówność opisano w zakresie pomiaru.
  - `[false]` `[reject]` verification-gap: brak luk — lens zwrócił zero ustaleń; nic do triage'u poza tym wierszem.

## Verification

**Manual checks (if no CLI):**
- Sekcja „Wyniki” w Implementation Notes zawiera pomiary dla każdej z trzech flag (minimalizacja i przywrócenie, pełny ekran, `f`/`Escape`, jakość i DRM) oraz odczyt źródła wersji na UM760; AD-1 i AD-13 w architekturze nie mają otwartego pytania (`git diff` na `architecture-mediastation.md`).

## Auto Run Result

Status: built

**Summary.** Spike 6.1 rozstrzygnął oba pytania na UM760 (host MEDIASTATION, płyta F7BSI, model EliteMini Series, Edge 156.0.4314.42). Tryb okna: `--kiosk` (pełny monitor bez UI przeglądarki także na górnej krawędzi, `f` i `Escape` w obrębie playera, minimalizacja i przywrócenie bez `SW_HIDE`); `--app` i `--start-fullscreen` odrzucone z pomiarami. Źródło wersji: `FileVersion` binarki (bieżąca) i `Last Version` w `profiles\<appId>\` (poprzednia, czytana przed startem). Rozstrzygnięcia są w AD-1 i AD-13 spine'u.

**Files changed.**
- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md`: AD-1, AD-13, Deferred, `updated`.
- `_bmad-output/initiative-mediastation-mvp/architecture-mediastation/.memlog.md`: wpis decyzji.
- `_bmad-output/initiative-mediastation-mvp/tickets/epic-browserhost/tickets/spike-tryb-okna-przegladarki-i-zrodo-wersji-poza-cdp-plan.md`: ten plan z wynikami.
- `spike/helpers/winfacts.ps1`, `mode61.sh`, `mode61-next.sh`, `keysys61.sh`, `crop61.ps1`, `topedge61.ps1`: narzędzia pomiarowe (poza produktem).

**Review findings.** Patches applied: 5 (3 medium, 2 low), wszystkie w dokumentach (AD-1: fakty o DRM i wierzchu okna, AD-13: brak/niesparsowana wersja, plan: warunki pomiaru). Deferred: 3 (notatki ticketów epiki dla Sofii, `from==to` po awarii BrowserHost, kiosk przy wielu monitorach/OSK). Rejected: `FileVersion` vs wersja załadowana (wykrywa to porównanie z `Browser.getVersion`), moment zapisu `Last Version` (mtime po starcie, także po `taskkill /F`), higiena skryptów pomiarowych i metadane planu (jednorazowe narzędzia poza produktem), pusty wynik verification-gap.

**Follow-up review recommended:** false (3 patche medium, żaden high).

**Verification.** Pomiary na UM760 opisane w Implementation Notes (okno: `winfacts.ps1`, CDP; nakładka statystyk; zrzuty górnej krawędzi); odczyt `FileVersion`, `Last Version`, `BLBeacon`; `git diff` na spine potwierdza rozstrzygnięcia. Brak testów automatycznych (spike bez kodu produktu). Wszystkie przeglądarki uruchomione przez spike zostały zamknięte, port CDP 9223 nie nasłuchuje.

**Residual risks.** DRM po `--kiosk` mierzone tylko w stanie Widevine SW (PlayReady zniknął w trakcie spike'a z nieustaloną przyczyną), jedna próba na tryb, jeden monitor. Pozostałe uwagi w `deferred`. Epic acceptance na UM760 należy do użytkownika; checks do dołączenia do `fp-acceptance` dla epiki BrowserHost: start Edge z `--kiosk`, minimalizacja i przywrócenie z pilota, brak paska adresu, log `BrowserVersionChanged` po aktualizacji.
