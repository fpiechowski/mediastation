# Prompt: uruchomienie spike'u na UM760

Wklej poniższy blok jako pierwszą wiadomość w sesji Claude Code na UM760 (po checkoucie repo).

```
Jesteś na UM760, docelowym HTPC MediaStation: Windows 11, monitor FHD z HDR (nie TV), pilot G20S Pro
podłączony przez dongle USB 2.4G (nie Bluetooth). Twoje zadanie: przeprowadzić spike i zapisać wyniki.
Nie budujesz produktu. Skille BMad nie są tu zainstalowane i nie są potrzebne.

Przeczytaj najpierw:
- spike/README.md, zwłaszcza sekcję „Dla agenta”: serve, cmd, log JSONL, skrypty win/ w tle,
  instalacja rozszerzenia;
- spike/spike-results.md: testy, kryteria, bramki, miejsce na wyniki;
- sekcje „Rekomendacja” i „Spike na Netfliksie” w
  _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/research-browser-control-for-streaming-services.md
  (dlaczego te testy i co rozstrzygają). spike-results.md jest nowszy i ma pierwszeństwo, np. S6
  został przebudowany, a C1–C3 dodane.

Zasady:
1. Środowisko: najpierw sprawdź według README wersje Node, Chrome, Edge i Moonlight, sprawdź, czy dongle
   pilota jest widoczny jako urządzenie HID, i uruchom `Moonlight.exe --help`. Wynik --help porównaj
   z flagami w spike/win/ (--mute-on-focus-loss, --background-gamepad i inne) i popraw rozbieżności.
   Wypisz braki i poczekaj, aż je uzupełnię. Nic nie instaluj bez pytania.
2. Testy przeprowadzasz sam przez `ms.mjs serve` i `ms.mjs cmd` oraz skrypty win/ uruchamiane w tle.
   Nie używaj interaktywnego REPL. Mnie proś tylko o:
   - logowanie do Netflixa w każdym dedykowanym profilu (hasło wpisuję sam w oknie przeglądarki, nie
     podaję go tobie),
   - ręczne „Load unpacked” rozszerzenia w profilach edge-ext i chrome-ext, kiedy przyjdzie pora na C,
   - fizyczne naciśnięcia na pilocie („naciśnij teraz Home”), zawsze z informacją, co jest teraz na wierzchu,
   - fizyczne Ctrl+Alt+Shift+D (nakładka statystyk) w wariancie C, gdzie nie masz zaufanego wejścia,
   - ocenę wzrokową lub słuchową, gdy pomiar nie wystarcza: jedno konkretne pytanie z odpowiedzią tak/nie.
   Używaj mojego profilu Netflixa, nie profilu żony.
3. Kolejność:
   - Sesja 1 (B): S1, S4, S2, S3, S6, S5, S7.
     - W S1 sprawdź orientacyjnie HDR w Edge i w Chrome przez nakładkę statystyk. Brak HDR na FHD nie
       jest porażką testu.
     - Po każdym `--mode pw` zrestartuj przeglądarkę przed kolejnym pomiarem raw, bo Playwright wywołał
       już Runtime.enable.
     - S6 ma części A i B według spike-results.md. Fizyczne klawisze pilota są zaufane w obu wariantach.
       Część B sprawdza zamienniki dla C (focus, activate, jstype, adapter on). Zapisuj, czy `focus`
       użył fallbacku poza ekranem.
   - BRAMKA po S2 i S6 według spike-results.md (B, C albo „żaden”). Jeśli wynik to „przechodzimy na C”
     albo „żaden wariant nie działa”, zatrzymaj się, opisz wynik i zapytaj o dalszy krok.
   - Wariant C: C1–C3 przeprowadź po sesji 1 zawsze, jeśli starczy czasu. Bramka decyduje
     o rekomendacji, nie o tym, czy C testujemy.
   - Sesja 2: S10, S8, S9, S11.
     - S10: uruchom logger w tle i zmierz rzeczywisty sposób, w jaki przychodzi Home: VK, Raw Input
       HID Consumer (0x0C/0x0223), WM_APPCOMMAND. Nie zakładaj VK_BROWSER_HOME. Sprawdź z Edge, Chrome
       i Moonlightem na wierzchu. Zapisz, że pomiar dotyczy dongla USB, nie BT.
     - S11: zapytaj, czy host Moonlight jest gotowy.
   - Poza zakresem: S12 (Prime, Max, Disney+) oraz 4K i Atmos na TV.
4. Wyniki zapisuj w spike/spike-results.md od razu po każdym teście, nie na koniec:
   - PASS, FAIL albo PARTIAL,
   - zmierzone wartości: rozdzielczość, bitrate, kodek, HDR, wersje przeglądarek, czasy,
     navigator.webdriver,
   - dowody: ścieżki do logów i zrzutów w spike/,
   - jedno zdanie wniosku dla architektury.
   Selektory DOM Netflixa są kandydatami. Zapisz, które faktycznie pasują: profile, modal, zamknięcie
   modala, komunikaty błędów, przewijanie wiersza.
5. Jeśli harness lub skrypt ma błąd, popraw go minimalnie i opisz poprawkę w spike-results.md. Nie
   przebudowuj go. Jeśli test jest źle zaprojektowany, zaproponuj zmianę, zanim ją wprowadzisz. Pliki
   w spike/win/ muszą zostać w UTF-8 z BOM, bo bez BOM PowerShell 5.1 ich nie sparsuje.
6. Nie zapisuj w repo haseł, cookies, zawartości profili ani logów z danymi sesji. Przed commitem sprawdź
   .gitignore i `git status`.
7. Na koniec dopisz w spike-results.md podsumowanie:
   - czy rekomendacja B się utrzymuje, czy przechodzimy na C (z argumentami z S2, S6 i C1–C3),
   - jak przychodzi Home z dongla i czy MediaStation odzyska pierwszy plan,
   - ustalenia dla architektury: flagi uruchomienia, profil, Widevine, mapa fokusu, reguła Back, pauza,
     audio, Moonlight,
   - otwarte pytania, w tym co trzeba powtórzyć na TV i przez Bluetooth.
   Zacommituj spike/ na master (bez profili i dużych plików). Zapytaj mnie, zanim zrobisz push.

Pisz do mnie po polsku i krótko: co robisz teraz, czego potrzebujesz ode mnie.
```
