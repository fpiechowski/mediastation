---
title: "Change Proposal: OSK wszędzie, aktualizacja z GitHub Releases, pilot przez dongle"
type: change-proposal
status: approved
approved: 2026-10-18
created: 2026-10-18
trigger: decyzje użytkownika po przeglądzie specu i UX (2026-10-18)
affects:
  - ../spec-mediastation/spec-mediastation.md
  - ../architecture-mediastation/architecture-mediastation.md
  - ../ux-mediastation/EXPERIENCE.md
  - ../ux-mediastation/DESIGN.md
  - ../initiative-mediastation-mvp.md
  - ../epic-input/epic-input.md
  - ../epic-osk/epic-osk.md
  - ../epic-moonlight-native/epic-moonlight-native.md
  - ../epic-deploy-resilience/epic-deploy-resilience.md
  - ../epic-home-screen/epic-home-screen.md
supersedes: [R10 z ../change-mediastation/change-mediastation.md]
scope: moderate
mode: batch
---

# Change Proposal: OSK wszędzie, aktualizacja z GitHub Releases, pilot przez dongle

## 1. Podsumowanie problemu

Użytkownik przejrzał spec i spine'y UX, które wcześniej zaakceptował bez czytania (2026-10-18), i podjął cztery decyzje:

1. **OSK wszędzie.** Klawiatura ekranowa ma działać w UI Netflixa (jak dziś), w UI Plexa, w UI klienta Moonlight (lista hostów, dialogi; **nie** w trakcie sesji streamu) i w przyszłych polach tekstowych UI MediaStation. Wywołanie z pilota przez Action `Keyboard`. Koszt drugiej drogi wpisywania (`SendInput` Unicode) jest zaakceptowany. Odwraca R10 i zawężenie AD-9 z `change-mediastation`.
2. **Aktualizacja z ekranu głównego.** Przycisk na Home widoczny tylko, gdy na GitHub Releases jest nowsza wersja. Release powstaje w **GitHub Actions** na tagu, co cofa decyzję „bez CI” z inicjatywy. Repo `fpiechowski/mediastation` zostało upublicznione 2026-10-18 (przed zmianą historia sprawdzona: brak sekretów), więc Updater nie potrzebuje tokenu.
3. **Pilot przez dongle USB 2.4G.** Bluetooth nie jest wymagany. Konfiguracja docelowa jest tą zmierzoną w spike S10 (dongle, 16/16, wybudzanie działa).
4. **J3 bez zmian.** Wyszukiwarka to wyszukiwarka Netflixa. MediaStation dostarcza tylko klawiaturę. Potrzebne jest doprecyzowanie copy, bo czytelnik odebrał J3 jako integrację wyszukiwarek.

- **Typ zmiany:** nowe wymagania od stakeholdera (1, 2), doprecyzowanie środowiska (3), nieporozumienie w lekturze (4).
- **Dowody:** rozmowa z 2026-10-18; spike S10 (`research-browser-control-for-streaming-services/digests/spike-um760.md`) dla punktu 3.

## 2. Analiza wpływu

| Obszar | Wpływ |
|---|---|
| Spec | CAP-3 (dongle), CAP-9 (zakres OSK), CAP-12 (aktualizacja), Constraints (trwały stan, sieć), Non-goals (OSK w Moonlight, open source), Open Questions (BT zamknięte, dwa nowe) |
| Architektura | AD-2 (`--supervise` robi podmianę wersji), AD-8 (Action `Keyboard`, kolumna routingu), AD-9 (przepisany: `ShowOsk{target}` i `TextSink`), AD-13 (Build, Deploy, Hotfix), **nowe AD-17** (aktualizacja), Design Paradigm, Conventions, Structural Seed, Zakres MVP, Deferred, Open questions |
| UX | EXPERIENCE.md: Foundation, IA, Component Patterns, State Patterns, Interaction Primitives (OSK, model fokusu), Accessibility Floor, J3, Voice and Tone, Deferred, Handoff, R10. DESIGN.md: nowy komponent przycisku aktualizacji (właściciel: `bmad-ux`) |
| Inicjatywa | Notes: decyzja „bez CI” zastąpiona; nowy touch point GitHub Releases |
| Epiki | epic-input, epic-osk, epic-moonlight-native, epic-deploy-resilience, epic-home-screen (granica). Kolejność epik bez zmian |
| Kod | Story 1.1 (niezacommitowana, w toku) nie jest dotknięta. Story 1.2 dostanie `Keyboard` w enumie `Action.kind` automatycznie z AD-8. Story 1.4: `settings.json` dostaje sekcję `update` |
| CI/CD | Nowy `.github/workflows/release.yml` (pierwszy CI w projekcie) |

**MVP i kryterium sukcesu** zostają. Zakres rośnie o OSK w aplikacjach natywnych i Updater, a oba mieszczą się w istniejących epikach.

## 3. Rekomendowane podejście

**Direct adjustment.** Rollback nie jest potrzebny, bo żaden ukończony kod nie zależy od zmienianych decyzji. Rewizja MVP też nie, bo success signal się nie zmienia.

- **Effort:** średni. Dokumenty plus nowe stories: 1 w epic-moonlight-native, 3 w epic-deploy-resilience, rozszerzenie stories w epic-input i epic-osk.
- **Ryzyko:** średnie, w trzech miejscach:
  - polskie znaki przez `SendInput` w Plexie i Moonlighcie (do zmierzenia);
  - nakładka nad pełnoekranowym Plexem;
  - handoff supervisora przy podmianie wersji (ryzyko zamknięte rollbackiem).
- **Timeline:** dochodzi praca w epikach 3, 8, 10 i 11, kolejność się nie zmienia.

Rozważone i odrzucone:

- **Jedna droga wpisywania** (`SendInput` także dla Netflixa). Upraszcza kod, ale CDP `Input.insertText` jest zmierzone w spike'u i nie zależy od foreground. Zostają dwie drogi.
- **Wykrywanie sesji streamu w Moonlight**, żeby blokować tam OSK. Koszt bez zysku: w trakcie streamu OSK nie jest wymagana, więc działa bez gwarancji.
- **Lokalny skrypt release'u bez CI.** Użytkownik wybrał GitHub Actions.

## 4. Szczegółowe propozycje zmian

Konwencja: **OLD** to tekst dosłowny z bieżącej wersji dokumentu, **NEW** to tekst docelowy.

### 4.1 Spec (`spec-mediastation.md`)

#### P-S1: CAP-3 intent i success (dongle)

OLD:
```
  - **intent:** Pilot G20S Pro BT, klawiatura i gamepad sterują MediaStation jednym zestawem Actions, kierowanych według bieżącego trybu (`shell`, `app`, `osk`). Home działa zawsze.
```
NEW:
```
  - **intent:** Pilot G20S Pro przez dongle USB 2.4G, klawiatura i gamepad sterują MediaStation jednym zestawem Actions, kierowanych według bieżącego trybu (`shell`, `app`, `osk`). Home działa zawsze, a przycisk Klawiatura otwiera OSK.
```
OLD:
```
    - Home jest połykany i obsłużony nad Edge, Moonlightem, pulpitem i przy zawieszonym UI shella. Powtórzenie S10 przez Bluetooth daje 16/16.
```
NEW:
```
    - Home jest połykany i obsłużony nad Edge, Moonlightem, pulpitem i przy zawieszonym UI shella. Powtórzenie S10 z pilotem na dongle USB 2.4G daje 16/16.
    - Przycisk pilota wybrany w `--calibrate-input` daje Action `Keyboard`.
```
Uzasadnienie: decyzja 3 i nowa Action z decyzji 1.

#### P-S2: CAP-9 (OSK wszędzie)

OLD:
```
  - **intent:** Gdy w serwisie webowym trzeba coś wpisać, pojawia się jedna klawiatura ekranowa MediaStation, obsługiwana D-padem, a opcjonalnie także kursorem pilota. Polskie znaki są dostępne.
```
NEW:
```
  - **intent:** Gdy trzeba coś wpisać w Netflixie, w Plexie, w UI klienta Moonlight albo w polu MediaStation, pojawia się jedna klawiatura ekranowa MediaStation, obsługiwana D-padem, a opcjonalnie także kursorem pilota. Polskie znaki są dostępne. W Netflixie OSK otwiera się sama na polu tekstowym, a wszędzie indziej przyciskiem Klawiatura.
```
Do listy **success** CAP-9 dopisać na końcu:
```
    - Przycisk Klawiatura otwiera OSK nad Plexem i nad UI klienta Moonlight. Znaki trafiają do pola aplikacji, w tym polskie warianty, a fokus systemowy zostaje w aplikacji.
    - Kontrakt przewiduje pola tekstowe w UI MediaStation (`textFocus` z Shell). W MVP takich pól nie ma.
```
Uzasadnienie: decyzja 1. Sesja streamu Moonlight jest wyłączona przez Non-goals (P-S5).

#### P-S3: CAP-12 (aktualizacja)

OLD:
```
    - `tools/install.ps1` instaluje build i rejestruje zadanie „at logon” z `--supervise`.
```
NEW:
```
    - `tools/install.ps1` instaluje pierwszą wersję z paczki release'u i rejestruje zadanie „at logon” z `--supervise`.
    - Tag `v<semver>` w repo uruchamia GitHub Actions, które buduje, testuje i publikuje paczkę `win-x64` z sumą sha256 jako GitHub Release.
    - Gdy na GitHub Releases jest nowsza wersja, na ekranie głównym pojawia się przycisk aktualizacji. Jedno OK pobiera ją, weryfikuje i uruchamia nową wersję, z adopcją żywych aplikacji. Bez nowszej wersji przycisku nie ma.
    - Nowa wersja, która 3 razy z rzędu nie dojdzie do ekranu głównego, wraca automatycznie do poprzedniej (AD-17).
```
OLD:
```
    - Podmiana pliku drivera w `<install>/page/` i „Uruchom ponownie” z pilota wdrażają hotfix bez ponownej instalacji.
```
NEW:
```
    - Podmiana pliku drivera w `page/` bieżącej wersji i „Uruchom ponownie” z pilota wdrażają hotfix bez ponownej instalacji.
```
OLD (linia **AD:** CAP-12):
```
  - **AD:** AD-2, AD-13
```
NEW:
```
  - **AD:** AD-2, AD-13, AD-17
```
Uzasadnienie: decyzja 2.

#### P-S4: Constraints (trwały stan, sieć)

OLD:
```
- Stan sesji nie jest trwały. Jedyny trwały stan w `data/` to kolejka „Do oceny”: jeden dokument, w którym rekordy `pending` są zwykłym statusem, a żadnego rekordu się nie usuwa. Poza `data/` MediaStation trwale trzyma tylko poprzednią wersję przeglądarki w `profiles\<appId>\` (AD-13).
```
NEW:
```
- Stan sesji nie jest trwały. Jedyny trwały stan w `data/` to kolejka „Do oceny”: jeden dokument, w którym rekordy `pending` są zwykłym statusem, a żadnego rekordu się nie usuwa. Poza `data/` MediaStation trwale trzyma tylko poprzednią wersję przeglądarki w `profiles\<appId>\` (AD-13) i poprzednią wersję instalacji do rollbacku (AD-17).
```
Do listy **Prywatność** dopisać:
```
  - sprawdzanie aktualizacji łączy się wyłącznie z publicznym API GitHub Releases repo z `settings.json`, bez tokenu i bez telemetrii.
```

#### P-S5: Non-goals

OLD:
```
- Wywołanie klawiatury ekranowej z pilota (Action `Keyboard`, `SendInput` Unicode) w Moonlight i aplikacjach natywnych.
```
NEW:
```
- Klawiatura ekranowa w trakcie sesji streamu Moonlight (może działać, bez gwarancji) i automatyczne otwieranie OSK w aplikacjach natywnych.
```
OLD:
```
- Wydanie open source.
```
NEW:
```
- Wydanie open source (licencja, społeczność, wsparcie). Repo jest publiczne od 2026-10-18 tylko jako źródło GitHub Releases.
```

#### P-S6: Open Questions

OLD:
```
- **G20S Pro BT:** kody przycisków i wybudzanie PC przez Bluetooth (dotąd sprawdzone tylko przez dongle). Rozstrzyga pierwsza story epiki wejścia (CAP-3): powtórzenie S10 z `--calibrate-input`.
```
NEW:
```
- **Przycisk Klawiatura na G20S Pro:** który przycisk pilota (na dongle USB 2.4G) mapuje się na Action `Keyboard`. Rozstrzyga pierwsza story epiki wejścia (CAP-3) z `--calibrate-input`. Bluetooth nie jest wymagany (decyzja 2026-10-18).
- **OSK w aplikacjach natywnych:** czy nakładka działa nad pełnoekranowym Plexem i czy polskie znaki przez `SendInput` Unicode dochodzą do Plexa i UI Moonlighta. Rozstrzyga story „OSK w Plexie i UI Moonlighta” w epice CAP-10.
```

### 4.2 Architecture spine (`architecture-mediastation.md`)

#### P-A0: frontmatter

OLD:
```
updated: '2026-10-18'
```
NEW (bez zmiany daty, bo jest ta sama). Do `sources` dopisać:
```
  - ../change-osk-everywhere-github-update/change-osk-everywhere-github-update.md
```

#### P-A1: Design Paradigm (Updater)

OLD:
```
Wykonawcy (AppHosts, WindowManager, AudioRouter, InputHub, SystemEvents, Store) implementują porty
```
NEW:
```
Wykonawcy (AppHosts, WindowManager, AudioRouter, InputHub, SystemEvents, Store, Updater) implementują porty
```
W tabeli warstw, wiersz Store / Config:

OLD:
```
| Store / Config | `src/MediaStation.App/Store`, `Config` | pliki JSON |
```
NEW:
```
| Store / Config / Update | `src/MediaStation.App/Store`, `Config`, `Update` | pliki JSON; sprawdzanie i pobieranie release'ów (AD-17) |
```
W diagramie mermaid dodać węzeł i dwie strzałki:
```
    UP["Updater<br/>GitHub Releases"]
  UP -- Event --> O
  O -- Command --> UP
```

#### P-A2: AD-2 (`--supervise`)

OLD:
```
  - Jedyny dodatkowy proces to ten sam exe w trybie `--supervise`: minimalny rodzic, który restartuje dziecko po awarii z backoffem i nie ma żadnej innej logiki.
```
NEW:
```
  - Jedyny dodatkowy proces to ten sam exe w trybie `--supervise`: minimalny rodzic, który restartuje dziecko po awarii z backoffem i wykonuje przełączenie wersji z rollbackiem (AD-17). Nie ma żadnej innej logiki.
```

#### P-A3: AD-8 (Action `Keyboard`)

OLD:
```
  - InputHub zamienia źródła na **Actions** `{ kind: Up|Down|Left|Right|Ok|Back|Home|AppMenu, phase: press|repeat|long, source, tsMono }`. Action `Keyboard` jest deferred.
```
NEW:
```
  - InputHub zamienia źródła na **Actions** `{ kind: Up|Down|Left|Right|Ok|Back|Home|AppMenu|Keyboard, phase: press|repeat|long, source, tsMono }`. `Keyboard` daje przycisk pilota z `input.json` (wybrany w `--calibrate-input`) i klawisz konfigurowalny dla klawiatury i pada.
```
Tabela routingu: nagłówek i wiersze dostają kolumnę `Keyboard`.

OLD:
```
    | Tryb / typ aktywnej aplikacji | D-pad, OK | Back | Home | Mysz/żyroskop |
    | --- | --- | --- | --- | --- |
    | `shell` (ekran główny) | połykane → Action do Shell | połykane → Action | połykane | wg `pointer` (domyślnie blokowana, kursor ukryty) |
    | `osk` | połykane → OSK | połykane → OSK (zamyka rząd wariantów, a gdy go nie ma, OSK) | połykane → zamyka OSK i pokazuje ekran główny | dozwolona, kursor ograniczony do okna OSK (`ClipCursor`); kliknięcie poza OSK połykane |
    | `app` + `web` | połykane → `action` do strony | połykane → `back` do strony | połykane | wg `pointer` (domyślnie blokowana) |
    | `app` + `moonlight` / `native` | przechodzą do aplikacji | połykane → polityka `back` (`home` \| `key:<VK>`) | połykane | wg `pointer` (domyślnie dozwolona) |
```
NEW:
```
    | Tryb / typ aktywnej aplikacji | D-pad, OK | Back | Home | Keyboard | Mysz/żyroskop |
    | --- | --- | --- | --- | --- | --- |
    | `shell` (ekran główny) | połykane → Action do Shell | połykane → Action | połykane | połykane → OSK, gdy Shell zgłosił `textFocus{active:true}`; inaczej nic | wg `pointer` (domyślnie blokowana, kursor ukryty) |
    | `osk` | połykane → OSK | połykane → OSK (zamyka rząd wariantów, a gdy go nie ma, OSK) | połykane → zamyka OSK i pokazuje ekran główny | połykane → zamyka OSK | dozwolona, kursor ograniczony do okna OSK (`ClipCursor`); kliknięcie poza OSK połykane |
    | `app` + `web` | połykane → `action` do strony | połykane → `back` do strony | połykane | połykane → OSK, gdy strona zgłosiła `textFocus{active:true}`; inaczej nic | wg `pointer` (domyślnie blokowana) |
    | `app` + `moonlight` / `native` | przechodzą do aplikacji | połykane → polityka `back` (`home` \| `key:<VK>`) | połykane | połykane → OSK | wg `pointer` (domyślnie dozwolona) |
```
Uzasadnienie: decyzja 1. W `moonlight` i `native` MediaStation nie wie, czy fokus jest w polu tekstowym, więc `Keyboard` zawsze otwiera OSK. W trakcie streamu Moonlight działa to bez gwarancji (Non-goals).

#### P-A4: AD-9 (przepisany)

OLD (cała reguła AD-9 od `- **Binds:**` do linii z fallbackiem):
```
- **Binds:** OSK, InputHub, BrowserHost, PageRuntime
- **Prevents:** dwie implementacje klawiatury; kradzież fokusu z pola tekstowego; wyciek wpisywanego tekstu
- **Rule:**
  - OSK to okno nakładki MediaStation (topmost, `WS_EX_NOACTIVATE`). Fokus zostaje w aplikacji docelowej.
  - Pokazanie OSK przełącza tryb na `osk`, a Back ją zamyka (AD-8).
  - Każdy znak trafia do aplikacji od razu, bez bufora, przez CDP `Input.insertText`. Tekst nie trafia do logów, Intents ani view-modelu.
  - OSK pokazuje się wyłącznie automatycznie, na `textFocus{active:true}` aktywnej aplikacji webowej. W Moonlight i aplikacjach natywnych tekst wpisuje się z klawiatury albo gamepada. Wywołanie OSK z pilota (Action `Keyboard`, `SendInput` Unicode) jest deferred.
  - Pokazanie i schowanie OSK host ogłasza stronie komunikatem `osk{active}` (AD-7). Przy `active:true` PageRuntime przełącza pole docelowe na styl `input-active` (DESIGN.md) i nie rysuje własnego fokusu. Jedyny fokus na ekranie jest na klawiszu OSK.
  - OSK konsumuje OK według fazy: `press` wstawia znak, a `long` otwiera rząd polskich wariantów bez wstawiania litery bazowej (AD-8).
  - Pointer w `osk` przenosi fokus OSK na klawisz pod kursorem, a OK albo klik zatwierdza (AD-8).
  - Fallback, jeśli nakładka nad pełnoekranowym Edge zawiedzie: dla webu OSK rysowana przez PageRuntime, z tym samym trybem `osk` i tymi samymi Actions.
```
NEW:
```
- **Binds:** OSK, InputHub, BrowserHost, PageRuntime, Shell UI, Orchestrator
- **Prevents:** dwie implementacje klawiatury; kradzież fokusu z pola tekstowego; wyciek wpisywanego tekstu; tekst płynący przez kolejkę Orchestratora
- **Rule:**
  - OSK to okno nakładki MediaStation (topmost, `WS_EX_NOACTIVATE`), jedno dla wszystkich aplikacji. Fokus zostaje w aplikacji docelowej.
  - O pokazaniu decyduje Orchestrator, wysyłając `ShowOsk{ target }` i `HideOsk`. Pokazanie przełącza tryb na `osk`, a Back, Home i `Keyboard` ją zamykają (AD-8). Wyzwalacze:
    - `textFocus{active:true}` aktywnej aplikacji webowej (automatycznie, AD-7);
    - Action `Keyboard` w `app` + `web`, gdy pole strony ma `textFocus`, oraz zawsze w `app` + `moonlight` / `native`;
    - Action `Keyboard` w `shell`, gdy Shell UI zgłosił Intent `textFocus{active}` (bez treści pola). W MVP Shell nie ma pól tekstowych.
  - `target` wskazuje drogę wpisywania (`TextSink`):
    - `web(appId)`: BrowserHost, CDP `Input.insertText`;
    - `foreground`: `Platform/Input`, `SendInput` z `KEYEVENTF_UNICODE` do okna na pierwszym planie (Plex, UI Moonlighta, pole Shell). Wstrzyknięcia mają znacznik w `dwExtraInfo`, więc hook je ignoruje (AD-8).
  - OSK wysyła znaki bezpośrednio do `TextSink` z `target`, od razu i bez bufora. To jedyny kanał, który omija kolejkę Orchestratora: tekst nie trafia do logów, Intents, Events ani view-modelu.
  - Pokazanie i schowanie OSK nad aplikacją webową host ogłasza stronie komunikatem `osk{active}` (AD-7). Przy `active:true` PageRuntime przełącza pole docelowe na styl `input-active` (DESIGN.md) i nie rysuje własnego fokusu. Pole aplikacji natywnej zachowuje własny styl, a jedyny fokus MediaStation stoi na klawiszu OSK.
  - OSK konsumuje OK według fazy: `press` wstawia znak, a `long` otwiera rząd polskich wariantów bez wstawiania litery bazowej (AD-8).
  - Pointer w `osk` przenosi fokus OSK na klawisz pod kursorem, a OK albo klik zatwierdza (AD-8).
  - Fallback, jeśli nakładka nad pełnoekranowym Edge zawiedzie: dla webu OSK rysowana przez PageRuntime, z tym samym trybem `osk` i tymi samymi Actions. Dla aplikacji natywnych fallbacku nie ma: jeśli nakładka nie działa nad pełnoekranową aplikacją, rozwiązaniem jest jej tryb okna bez ramki z `apps.json`.
```
Uzasadnienie: decyzja 1. `ShowOsk{target}` wyjaśnia, kto wybiera drogę wpisywania, a `TextSink` nazywa świadomy wyjątek od zasady „wszystko przez Orchestrator”, który dotąd był niejawny.

#### P-A5: AD-13 (Build, Deploy, Hotfix)

OLD:
```
    - page bundle trafia jako pliki do `<install>/page/`.
```
NEW:
```
    - page bundle trafia jako pliki do `page/` katalogu wersji;
    - **release:** tag `v<semver>` uruchamia `.github/workflows/release.yml` na runnerze Windows: `dotnet test`, testy web, `dotnet publish` self-contained win-x64 z `Version` z tagu, zip `MediaStation-<wersja>-win-x64.zip` i plik `.sha256`, publikacja jako GitHub Release. To jedyny CI w projekcie.
```
OLD:
```
    - `tools/install.ps1` kopiuje build do `%LOCALAPPDATA%\Programs\MediaStation`;
    - rejestruje zadanie Task Scheduler „at logon” (tylko zalogowany użytkownik), które uruchamia `MediaStation.exe --supervise`;
    - aktualizacja = ponowne uruchomienie skryptu.
```
NEW:
```
    - `tools/install.ps1` (pierwsza instalacja) rozpakowuje paczkę release'u do `%LOCALAPPDATA%\Programs\MediaStation\<wersja>\`;
    - rejestruje zadanie Task Scheduler „at logon” (tylko zalogowany użytkownik), które uruchamia `<wersja>\MediaStation.exe --supervise`;
    - kolejne aktualizacje idą z ekranu głównego (AD-17). Skrypt zostaje do pierwszej instalacji i naprawy.
```
OLD:
```
  - **Hotfix drivera:** page bundle jest czytany z dysku przy każdym wstrzyknięciu. Hotfix = podmiana pliku w `<install>/page/` + „uruchom ponownie” z pilota.
```
NEW:
```
  - **Hotfix drivera:** page bundle jest czytany z dysku przy każdym wstrzyknięciu. Hotfix = podmiana pliku w `page/` bieżącej wersji + „uruchom ponownie” z pilota. Następna aktualizacja go nadpisuje, więc poprawka musi trafić też do repo.
```

#### P-A6: nowe AD-17 (wstawić po AD-16)

NEW:
```
### AD-17: Aktualizacja z GitHub Releases

- **Binds:** Updater, Orchestrator, Shell UI, `--supervise`, `tools/install.ps1`, `.github/workflows/release.yml`
- **Prevents:** nadpisywanie działającego exe; aktualizacja, która zostawia Elizę przed pulpitem; przycisk, który nic nie robi; ręczne kopiowanie buildów na UM760
- **Rule:**
  - Updater to wykonawca (AD-4). `CheckForUpdate` wysyła Orchestrator przy starcie i raz na dobę (`TimerElapsed`, AD-14). Updater pyta publiczne API `releases/latest` repo z `settings.json` (`update.repo`), bez tokenu, i porównuje semver z własną wersją. Wynik: Event `UpdateAvailable{ version }` albo nic. Błąd sieci trafia tylko do logu.
  - Snapshot niesie `update{ available: bool, version, phase: idle|downloading|applying|failed }`. Shell pokazuje przycisk aktualizacji wyłącznie przy `available:true` (EXPERIENCE.md).
  - Intent `InstallUpdate` → Command `DownloadUpdate` → Updater pobiera zip i `.sha256` do `%LOCALAPPDATA%\MediaStation\staging\`, weryfikuje sumę i rozpakowuje do `Programs\MediaStation\<wersja>\`. Wynik: `UpdateStaged{ version, path }` albo `CommandFailed{ UpdateDownloadFailed | UpdateChecksumMismatch }`.
  - Po `UpdateStaged` Orchestrator wysyła `ApplyUpdate`. Dziecko kończy się kodem wyjścia „zastosuj wersję <path>”, a przeglądarki i Moonlight żyją dalej (adopcja, AD-10).
  - `--supervise` robi **handoff**: przepina akcję zadania Task Scheduler na `<wersja>\MediaStation.exe --supervise`, uruchamia nowego supervisora z flagą `--rollback-to <poprzednia>` i sam się kończy. Działający exe nigdy nie jest nadpisywany, bo każda wersja ma własny katalog.
  - **Gotowość:** dziecko sygnalizuje supervisorowi gotowość (named event) po pierwszym wyrenderowanym snapshotcie ekranu głównego. Jeśli nowa wersja 3 razy z rzędu nie zgłosi gotowości, supervisor robi rollback tym samym handoffem do poprzedniej wersji. Zła wersja nie jest ponownie proponowana.
  - Na dysku zostają bieżąca i poprzednia wersja. Starsze katalogi usuwa supervisor po zgłoszeniu gotowości. `config/`, `data/`, `profiles/` i `logs/` leżą poza katalogami wersji i aktualizacja ich nie rusza.
  - Debug (Vite dev server) nie sprawdza aktualizacji.
```
Uzasadnienie: decyzja 2. Wersjonowane katalogi i handoff rozwiązują problem nadpisywania działającego `--supervise`, a rollback chroni gwarancję „Eliza nie zostaje przed pulpitem”.

#### P-A7: Consistency Conventions, Structural Seed, Zakres MVP, Deferred, Open questions

Consistency Conventions, wiersz Nazwy komponentów:

OLD:
```
| Nazwy komponentów | Orchestrator, InputHub, WindowManager, AudioRouter, SystemEvents, BrowserHost, MoonlightHost, NativeAppHost, Store, Shell, OSK, PageRuntime, ServiceDriver. Słowo „Adapter” nie oznacza kodu serwisu |
```
NEW:
```
| Nazwy komponentów | Orchestrator, InputHub, WindowManager, AudioRouter, SystemEvents, BrowserHost, MoonlightHost, NativeAppHost, Store, Updater, Shell, OSK, TextSink, PageRuntime, ServiceDriver. Słowo „Adapter” nie oznacza kodu serwisu |
```
Structural Seed:

OLD:
```
      Store/  Config/  Logging/  Smoke/
```
NEW:
```
      Store/  Config/  Logging/  Smoke/  Update/
```
oraz pod `tools/install.ps1` dopisać:
```
  .github/workflows/release.yml # release na tagu v<semver> (AD-13, AD-17)
```
Zakres MVP → architektura:

OLD:
```
| Klawiatura ekranowa (automatyczna w serwisach webowych) | OSK, InputHub, PageRuntime | AD-7, AD-8, AD-9 |
```
NEW:
```
| Klawiatura ekranowa (Netflix automatycznie; Plex, UI Moonlighta i pola shella przyciskiem Klawiatura) | OSK, TextSink, InputHub, PageRuntime | AD-7, AD-8, AD-9 |
| Aktualizacja z ekranu głównego (GitHub Releases) | Updater, `--supervise`, Shell UI, `release.yml` | AD-13, AD-17 |
```
Deferred, usunąć wiersz:
```
| Action `Keyboard` z pilota (OSK w Moonlight i aplikacjach natywnych) | Nowa Action w AD-8 i ścieżka `SendInput` Unicode w AD-9 |
```
i dopisać:
```
| OSK w trakcie sesji streamu Moonlight | Działa bez gwarancji. Wymagałaby wykrywania sesji i przekazania tekstu do hosta |
```
Otwarte pytania, wymiana:

OLD:
```
- **G20S Pro BT:** kody przycisków oraz wybudzanie PC przez Bluetooth (dotąd sprawdzone tylko przez dongle). Pierwsza story epiki wejścia: powtórzenie S10 z `--calibrate-input`.
```
NEW:
```
- **Przycisk Klawiatura na G20S Pro (dongle USB 2.4G):** pierwsza story epiki wejścia, `--calibrate-input`. Bluetooth nie jest wymagany.
- **OSK nad pełnoekranowym Plexem i polskie znaki przez `SendInput` w Plexie i UI Moonlighta:** story „OSK w Plexie i UI Moonlighta” w epice Moonlight & native.
```

### 4.3 UX (`EXPERIENCE.md`, `DESIGN.md`), właściciel: `bmad-ux` (Update)

#### P-U1: Foundation

OLD:
```
| Wejście | Pilot G20S Pro BT (strzałki, OK, Back, Home), klawiatura, gamepad. Bez myszy, kursor ukryty. Żyroskop pilota w UI MediaStation tylko w OSK |
| Model wejścia | InputHub → Actions `{kind: Up\|Down\|Left\|Right\|Ok\|Back\|Home\|AppMenu, phase: press\|repeat\|long}`; Orchestrator zmienia tryby `shell\|app\|osk` (AD-8). Action `Keyboard` jest deferred (AD-8, R10) |
```
NEW:
```
| Wejście | Pilot G20S Pro przez dongle USB 2.4G (strzałki, OK, Back, Home, przycisk Klawiatura), klawiatura, gamepad. Bez myszy, kursor ukryty. Żyroskop pilota w UI MediaStation tylko w OSK |
| Model wejścia | InputHub → Actions `{kind: Up\|Down\|Left\|Right\|Ok\|Back\|Home\|AppMenu\|Keyboard, phase: press\|repeat\|long}`; Orchestrator zmienia tryby `shell\|app\|osk` (AD-8) |
```

#### P-U2: Information Architecture

OLD:
```
| **Home** (Shell, tryb `shell`) | Zimny start, wybudzenie, klawisz Home, Back z korzenia aplikacji, awaria aplikacji na pierwszym planie (R7) | Header: logo + „MediaStation”, avatar + powitanie (dekoracja, bez kont), zegar. Zakładki **Video / Audio / Game** |
```
NEW:
```
| **Home** (Shell, tryb `shell`) | Zimny start, wybudzenie, klawisz Home, Back z korzenia aplikacji, awaria aplikacji na pierwszym planie (R7) | Header: logo + „MediaStation”, avatar + powitanie (dekoracja, bez kont), zegar; **przycisk aktualizacji tylko, gdy jest nowsza wersja**. Zakładki **Video / Audio / Game** |
```
OLD:
```
| **OSK** (okno nakładki, tryb `osk`) | Otwiera się automatycznie tylko na `textFocus` w serwisie webowym (Netflix) | QWERTY, „Usuń”, „Spacja”, „123”, „Gotowe”, warianty PL |
```
NEW:
```
| **OSK** (okno nakładki, tryb `osk`) | Automatycznie na `textFocus` w serwisie webowym (Netflix); przycisk Klawiatura w Plexie, w UI Moonlighta i na polu MediaStation (w MVP brak pól) | QWERTY, „Usuń”, „Spacja”, „123”, „Gotowe”, warianty PL |
```

#### P-U3: Voice and Tone (nowe copy)

Dopisać wiersze:
```
| Przycisk aktualizacji | „Zaktualizuj” + linia „Nowa wersja <wersja>” | decyzja (change 2026-10-18) |
| Aktualizacja w toku | „Pobieram aktualizację…”, potem „Uruchamiam nową wersję…” | decyzja (change 2026-10-18) |
| Aktualizacja nieudana | „Nie udało się pobrać aktualizacji. Wciśnij OK, żeby spróbować jeszcze raz.” | decyzja (change 2026-10-18) |
```

#### P-U4: Component Patterns

OLD (wiersz Focus ring, końcówka):
```
W trybie `osk` pole Netflixa dostaje `{components.input-active}`, a jedyny fokus stoi na klawiszu OSK. Nigdy nie znika podczas nawigacji |
```
NEW:
```
W trybie `osk` pole Netflixa dostaje `{components.input-active}`, a jedyny fokus stoi na klawiszu OSK. W Plexie i UI Moonlighta pole zachowuje własny styl aplikacji (świadomy wyjątek, patrz Accessibility Floor). Nigdy nie znika podczas nawigacji |
```
OLD (wiersz OSK):
```
| **OSK** | Patrz Interaction Primitives → OSK. Znaki trafiają od razu do aplikacji przez CDP `Input.insertText` (AD-9); tekst nie trafia do view-modelu. `SendInput` Unicode jest deferred razem z Action `Keyboard` |
```
NEW:
```
| **OSK** | Patrz Interaction Primitives → OSK. Znaki trafiają od razu do aplikacji: w Netflixie przez CDP `Input.insertText`, w Plexie, UI Moonlighta i polach MediaStation przez `SendInput` Unicode (AD-9); tekst nie trafia do view-modelu |
```
Dopisać wiersz:
```
| **Przycisk aktualizacji** | Widoczny tylko przy dostępnej nowszej wersji, w nagłówku Home; jedyny fokusowalny element nagłówka, osiągany Górą z rzędu zakładek. OK → „Pobieram aktualizację…” → „Uruchamiam nową wersję…” → MediaStation wraca na Home z fokusem jak przy zimnym starcie; żywe aplikacje zostają w tle. Błąd → przycisk zostaje z linią błędu, OK ponawia. Bez potwierdzenia: jedno OK, bo dojście wymaga świadomego ruchu w górę |
```

#### P-U5: State Patterns → Powierzchnie

Dopisać wiersze:
```
| Nowsza wersja dostępna | Home | Przycisk aktualizacji w nagłówku; fokus startowy bez zmian (pierwszy kafelek Video) — przycisk nie kradnie fokusu |
| Aktualizacja w toku / nieudana | Home | Copy z Voice and Tone; po restarcie nowej wersji przycisk znika |
```

#### P-U6: Interaction Primitives

Model fokusu, dopisać punkt:
```
- Góra z rzędu zakładek → przycisk aktualizacji, gdy jest widoczny; inaczej nic.
```
OSK, punkt 1:

OLD:
```
1. Otwiera się automatycznie tylko na `textFocus{active}` w serwisie webowym (np. OK na polu wyszukiwania Netflixa). Wywołanie z pilota (Action `Keyboard`) nie jest w MVP; w Moonlight i Plex tekst tylko z klawiatury lub gamepada.
```
NEW:
```
1. Otwiera się automatycznie na `textFocus{active}` w serwisie webowym (np. OK na polu wyszukiwania Netflixa). W Plexie i UI Moonlighta (lista hostów, dialogi) otwiera ją przycisk Klawiatura na pilocie; Eliza najpierw ustawia fokus aplikacji na polu. Ponowne wciśnięcie Klawiatury zamyka OSK. W trakcie sesji streamu Moonlight OSK nie jest wspierana.
```

#### P-U7: Accessibility Floor

OLD:
```
- Dokładnie jeden fokus, widoczny z 3 m i rozpoznawalny bez koloru (obrys + skala na kafelku; odwrócona pigułka na małych kontrolkach; obrys + ciemna obwódka w Netflixie).
```
NEW:
```
- Dokładnie jeden fokus, widoczny z 3 m i rozpoznawalny bez koloru (obrys + skala na kafelku; odwrócona pigułka na małych kontrolkach; obrys + ciemna obwódka w Netflixie). Świadomy wyjątek: przy OSK nad Plexem i UI Moonlighta pole aplikacji zachowuje własny wskaźnik fokusu, bo MediaStation go nie rysuje.
```
(Pozostała część punktu o kontraście bez zmian.)

#### P-U8: J3 (doprecyzowanie)

Pod nagłówkiem J3, po linii z ilustracją, dopisać:
```
Wyszukiwarka to wyszukiwarka Netflixa; MediaStation nie ma własnej. Eliza wchodzi w Netflixa kafelkiem z Home, a MediaStation dostarcza tylko klawiaturę ekranową do pola Netflixa.
```

#### P-U9: Deferred, Handoff, R10

Deferred, usunąć:
```
- Action `Keyboard` z pilota (wywołanie OSK poza serwisami webowymi).
```
dopisać:
```
- OSK w trakcie sesji streamu Moonlight.
```
Handoff do architektury:

OLD:
```
| Action `Keyboard` z pilota | Nie w MVP: OSK tylko auto na `textFocus` w serwisach webowych; w Moonlight/Plex tekst z klawiatury lub gamepada. **Zawężenie AD-9 (i briefu) dla pilota** przyjęte w `change-mediastation` (R10) | AD-8, AD-9, brief |
```
NEW:
```
| Action `Keyboard` z pilota | W MVP: otwiera OSK w Plexie, UI Moonlighta i na polach shella; w Netflixie OSK nadal otwiera się sama. R10 zastąpione w `change-osk-everywhere-github-update` | AD-8, AD-9 |
| Przycisk aktualizacji | Snapshot `update{available, version, phase}`, Intent `InstallUpdate` | AD-17 |
```
R10 w tabeli Rozstrzygnięcia R/A:

OLD:
```
| R10 | Action `Keyboard` z pilota nie w MVP (memlog L62, potwierdzone 2026-10-17); OSK tylko auto w webie, Moonlight/Plex z klawiatury lub gamepada; zawężenie AD-9 przyjęte w `change-mediastation` | EXPERIENCE → Foundation, IA, OSK, Deferred, Handoff |
```
NEW:
```
| R10 | **Zastąpione 2026-10-18** (`change-osk-everywhere-github-update`): Action `Keyboard` jest w MVP; OSK w Netflixie auto, w Plexie, UI Moonlighta i polach shella przyciskiem Klawiatura | EXPERIENCE → Foundation, IA, OSK, Deferred, Handoff |
```

#### P-U10: DESIGN.md (do zaprojektowania w `bmad-ux`)

Nowy komponent `update-button` w nagłówku Home: pozycja względem zegara i avatara w title-safe, stan spoczynku, `focus-pill` w fokusie, linia „Nowa wersja …” i linia błędu, ikona Material Symbols Rounded. Wartości ustala `bmad-ux`, a ten proposal nie wymyśla tokenów. Bez zmian dla OSK: ten sam panel nad każdą aplikacją.

### 4.4 Inicjatywa (`initiative-mediastation-mvp.md`)

OLD:
```
- Decision: bez CI; do epiki Deploy & resilience build trafia na UM760 przez ręczne `dotnet publish` i kopię (2026-10-18).
```
NEW:
```
- Decision: jedyny CI to `release.yml` w GitHub Actions na tagu `v<semver>` (change-osk-everywhere-github-update, 2026-10-18); do jego powstania w epice Deploy & resilience build trafia na UM760 przez ręczne `dotnet publish` i kopię. Repo publiczne od 2026-10-18.
```
Boundaries, dopisać touch point:
```
- Touch point: GitHub Releases i Actions repo `fpiechowski/mediastation` (publiczne; źródło aktualizacji) — owner: epic-deploy-resilience
```

### 4.5 Epiki (do wprowadzenia przez `bmad-ticket`)

#### P-E1: epic-input

- Description: „pilot G20S Pro BT” → „pilot G20S Pro przez dongle USB 2.4G”; dopisać mapowanie Action `Keyboard`.
- Outcome: „sygnałem jest powtórzenie S10 przez Bluetooth 16/16” → „sygnałem jest powtórzenie S10 z pilotem na dongle 16/16”.
- Done when 3: „powtórzenie S10 przez Bluetooth daje 16/16” → „powtórzenie S10 z pilotem na dongle USB 2.4G daje 16/16”.
- Done when, nowy punkt 5: „Przycisk pilota wybrany w `--calibrate-input` daje Action `Keyboard`, a jego mapowanie jest w `input.json` (AD-8).”
- Boundaries: usunąć „Action `Keyboard` (Non-goals)”.
- Notes: open question o BT zastąpić „Open question: który przycisk G20S Pro jest Klawiaturą. Rozstrzyga Story 1 (`--calibrate-input`).”; decyzję o Story 1 zmienić z „S10 przez BT” na „S10 z dongle i wybór przycisku Klawiatura”.

#### P-E2: epic-osk

- Description: „gdy w serwisie webowym trzeba coś wpisać” → „gdy trzeba coś wpisać w Netflixie, Plexie, UI Moonlighta albo polu MediaStation”; dopisać `ShowOsk{target}`, `TextSink` z dwiema drogami (`web`, `foreground`) i obsługę Action `Keyboard` w Orchestratorze.
- Done when, nowe punkty:
  - „6. Action `Keyboard` otwiera i zamyka OSK według tabeli routingu AD-8; w `shell` bez zgłoszonego `textFocus` nic nie robi (AD-8, AD-9).”
  - „7. `TextSink` `foreground` wpisuje znaki, w tym polskie warianty, do okna na pierwszym planie przez `SendInput` Unicode ze znacznikiem `dwExtraInfo`, a hook ich nie łapie; sprawdzone na dev harness z Notatnikiem (AD-8, AD-9).”
- Boundaries: „Nie: OSK w Moonlight i aplikacjach natywnych (Action `Keyboard`, Non-goals)” → „Nie: weryfikacja nad prawdziwym Plexem i UI Moonlighta (epic-moonlight-native); OSK w sesji streamu (Non-goals)”.

#### P-E3: epic-moonlight-native, nowa story

- **Story: „OSK w Plexie i UI Moonlighta”.** Przycisk Klawiatura otwiera OSK nad Plexem uruchomionym z kafelka i nad UI klienta Moonlight (lista hostów, dodanie hosta, PIN parowania). Znaki, w tym polskie warianty, trafiają do pola przez `TextSink` `foreground`. Rozstrzyga open question o nakładce nad pełnoekranowym Plexem i o polskich znakach.
  - verify: na UM760 wpisanie „Pamiętniki wampirów” w wyszukiwarkę Plexa i adresu hosta w UI Moonlighta samym pilotem; fokus systemowy zostaje w aplikacji; log nie zawiera tekstu.
  - covers: CAP-9, CAP-10; after: istniejące stories startu Plexa i Moonlighta.
- Epika: covers dopisać CAP-9; Done when dopisać „5. OSK działa nad Plexem i UI Moonlighta (CAP-9, AD-9).”

#### P-E4: epic-deploy-resilience, nowe stories

1. **„Release workflow w GitHub Actions”.** `.github/workflows/release.yml` na tagu `v<semver>`: testy Core i web, `dotnet publish` self-contained win-x64 z `Version` z tagu, zip + `.sha256`, GitHub Release. Verify: tag testowy daje release z dwoma assetami, a `MediaStation.exe` raportuje wersję z tagu.
2. **„Wersjonowana instalacja, handoff i rollback”.** `install.ps1` z paczki release'u do `<wersja>\`, handoff supervisora z przepięciem zadania, sygnał gotowości, rollback po 3 nieudanych startach, sprzątanie starszych wersji. Verify: na UM760 instalacja wersji N, ręczne zastosowanie N+1 kończy się Home z adopcją Netflixa; celowo zepsuta N+2 wraca do N+1 sama.
3. **„Updater i przycisk aktualizacji na Home”.** Updater (`CheckForUpdate` przy starcie i raz na dobę, `DownloadUpdate` z weryfikacją sha256), pole `update` w snapshocie, przycisk w Shell wg DESIGN.md i EXPERIENCE.md, `settings.json` → `update.repo`. Verify: przy nowszym release przycisk się pojawia, jedno OK kończy się nową wersją na Home; bez nowszego release przycisku nie ma; zła suma daje stan błędu i brak podmiany.

Epika: Description i Done when dopisać aktualizację (CAP-12 success z P-S3); References dopisać AD-17; Notes: „Decision: release przez GitHub Actions, repo publiczne, Updater bez tokenu (2026-10-18).” Kolejność: stories 1 → 2 → 3, po istniejących stories `install.ps1` i `--supervise` (story 2 je rozszerza).

#### P-E5: epic-home-screen

- Boundaries dopisać: „Nie: przycisk aktualizacji w nagłówku (epic-deploy-resilience); nagłówek ma do niego miejsce w layoucie z DESIGN.md.”

#### P-E6: epic-foundation-contracts

Bez zmian w ticketach. Story 2 bierze enum `Action.kind` z AD-8, więc `Keyboard` wchodzi automatycznie. Story 4 bierze schemat `settings.json` ze spine'u, więc wejdzie `update.repo`.

## 5. Implementation Handoff

**Scope: moderate.** Backlog dostaje nowe stories w istniejących epikach, kolejność epik się nie zmienia, a architektura dostaje jedno nowe AD.

| Krok | Kto | Co |
|---|---|---|
| 1 | Developer / BMad (ten proposal) | Wprowadzić P-S1–P-S6 (spec), P-A0–P-A7 (architecture spine), P-I (inicjatywa) |
| 2 | `bmad-ux` (Update) | Wprowadzić P-U1–P-U9 w EXPERIENCE.md i zaprojektować `update-button` w DESIGN.md (P-U10); dopisać decyzje do memloga |
| 3 | `bmad-ticket` | P-E1–P-E5: edycje epik i nowe stories (1 w epic-moonlight-native, 3 w epic-deploy-resilience), potem walidacja `tickets.py` |
| 4 | Developer (`bmad-build`) | Kontynuacja story 1.1 bez zmian |

Zmiany w backlogu (dla `bmad-ticket`):

- **Dodane:**
  - epic-moonlight-native: „OSK w Plexie i UI Moonlighta”;
  - epic-deploy-resilience: „Release workflow w GitHub Actions”, „Wersjonowana instalacja, handoff i rollback”, „Updater i przycisk aktualizacji na Home”.
- **Przeskalowane:**
  - epic-input Story 1 (dongle zamiast BT, wybór przycisku Klawiatura);
  - epic-osk (Action `Keyboard`, `TextSink` z dwiema drogami);
  - epic-deploy-resilience (`install.ps1` z paczki release'u do katalogu wersji).
- **Usunięte / przesunięte:** brak.

**Kryteria sukcesu wdrożenia zmiany:**

- spec, spine i EXPERIENCE.md nie zawierają już „Action `Keyboard` jest deferred” ani „przez Bluetooth” jako wymagania;
- AD-17 istnieje i jest referowane z CAP-12;
- `tickets.py` waliduje epiki bez nowych konfliktów kolejności (poza zamierzonym `order_conflict` epic-windows-audio → epic-browserhost).
