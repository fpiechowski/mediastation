---
title: "Change Proposal: uzgodnienie briefu i architektury MediaStation MVP z finalnym UX"
type: change-proposal
status: approved
approved: 2026-10-17
created: 2026-10-17
trigger: ux-mediastation (status final, 2026-10-17)
affects:
  - ../brief-mediastation/brief-mediastation.md
  - ../architecture-mediastation/architecture-mediastation.md
  - ../forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md
scope: minor
mode: batch
---

# Change Proposal: brief i architektura ↔ finalny UX

## 1. Podsumowanie problemu

Run `bmad-ux` zakończył się 2026-10-17 (DESIGN.md i EXPERIENCE.md mają status final). W trakcie journeys z Elizą i w Reviewer Gate zapadły decyzje, które zmieniają albo doprecyzowują brief (`status: final`) i architecture spine (`status: final`). Wymienia je callout pod H1 w EXPERIENCE.md oraz jego sekcje „Handoff do architektury” i „Deferred / poza MVP”.

- **Typ zmiany:** nowe wymagania od stakeholdera (journeys) plus doprecyzowania z Reviewer Gate.
- **Dowody:** `ux-mediastation/.memlog.md` (decision L22, L25, L28, L30, L31, L37–L40, L43, L50, L59; override L33; assumption L62, L66) i EXPERIENCE.md → Handoff do architektury.
- **Status założeń:** użytkownik potwierdził 2026-10-17 wszystkie założenia z EXPERIENCE.md → Open assumptions (R1–R16, A1–A26), zarówno te z wpływem na architekturę, jak i te czysto UX. Od tej chwili traktujemy je jak decyzje.

## 2. Analiza wpływu

| Obszar | Wpływ |
|---|---|
| Epiki i stories | N/A, jeszcze nie istnieją. Zmiana trafia do epików od razu przy ich tworzeniu |
| Brief | Zakres MVP: zakładki na ekranie głównym; OSK zawężona do serwisów webowych; etykieta „Ocenione”. Poza MVP: 4 nowe pozycje. Otwarte pytania: jedno przestaje dotyczyć MVP |
| Forge | Tylko etykieta „Ocenione” |
| Architektura | AD-6, AD-7, AD-8, AD-9, AD-10, AD-11, AD-12, AD-16, tabela „Zakres MVP → architektura”, Deferred i frontmatter. Nowe elementy kontraktu: komunikat `osk`, pole `category`, `focusRequest` w snapshocie, `cdpReconnectTimeout`, reguła `press` na key-up dla klawiszy z `long`. Komunikat `osk` dochodzi wstecznie zgodnie, `v` zostaje 1 |
| UX (DESIGN.md, EXPERIENCE.md) | Bez zmian w tym proposalu. Właścicielem jest `bmad-ux`. Potrzebny Update w `bmad-ux` (sekcja 5) |
| Kod | Brak, kod jeszcze nie powstał |

**Kryterium sukcesu z briefu** („cały wieczór z Netflixem … D-padem, bez trybu myszy”) zostaje nienaruszone. Pointer w OSK jest opcjonalny, a D-pad nadal wystarcza wszędzie.

## 3. Rekomendowane podejście

**Direct adjustment** briefu, forge i spine'u. Rollback nie jest potrzebny, bo nic nie zostało zbudowane. Rewizja MVP też nie, bo zmiana netto zawęża zakres (OSK z pilota idzie do deferred), a jedyne rozszerzenie to zakładki, które kosztują jedno pole w `apps.json` i grupowanie w Shell UI.

- **Effort:** niski (edycje dokumentów).
- **Ryzyko:** niskie. Jedyne realne ryzyko techniczne (kolizja `press`/`long` na OK) zamyka P-A2.
- **Timeline:** bez wpływu.

Odrzuconych zmian brak. Jedno doprecyzowanie mechanizmu (P-A3, `ClipCursor` w `osk`) zawęża override z UX, ale go nie łamie, więc nie wymaga Update w `bmad-ux` poza dopisaniem do memloga.

## 4. Szczegółowe propozycje zmian

Konwencja: **OLD** to tekst dosłowny z bieżącej wersji dokumentu, **NEW** to tekst docelowy.

### 4.1 Architecture spine (`architecture-mediastation.md`)

#### P-A0: frontmatter

OLD:
```
updated: '2026-10-16'
binds: [brief-mediastation, forge-filmweb-rating-prompt]
```
NEW:
```
updated: '2026-10-17'
binds: [brief-mediastation, forge-filmweb-rating-prompt, ux-mediastation]
```
Dopisać do `sources`:
```
  - ../ux-mediastation/EXPERIENCE.md
  - ../ux-mediastation/DESIGN.md
  - ../change-mediastation/change-mediastation.md
```
Uzasadnienie: spine wiąże teraz wartości rozstrzygnięte w UX.

#### P-A1: AD-8, Actions bez `Keyboard` (zmiana 2, R10)

OLD:
```
  - InputHub zamienia źródła na **Actions** `{ kind: Up|Down|Left|Right|Ok|Back|Home|Keyboard|AppMenu, phase: press|repeat|long, source, tsMono }`.
```
NEW:
```
  - InputHub zamienia źródła na **Actions** `{ kind: Up|Down|Left|Right|Ok|Back|Home|AppMenu, phase: press|repeat|long, source, tsMono }`. Action `Keyboard` jest deferred.
```
Uzasadnienie: R10 (potwierdzone). W MVP żadne źródło nie mapuje się na `Keyboard`.

#### P-A2: AD-8, `press` na key-up dla klawiszy z `long` (R12, mapowanie `AppMenu`)

OLD:
```
  - InputHub jest właścicielem timingu (repeat, long-press).
```
NEW:
```
  - InputHub jest właścicielem timingu (repeat, long-press).
  - Klawisz, który w bieżącym trybie ma przypisane `long` (OK w `shell` i w `osk`), dostaje `press` dopiero na key-up przed progiem long-press. Po osiągnięciu progu InputHub emituje `long` i nie emituje już `press`. Pozostałe klawisze dostają `press` na key-down.
```
Uzasadnienie: bez tej reguły `press` na key-down wstawiłby literę bazową przed rzędem wariantów (R12), a na kafelku uruchomiłby aplikację przed otwarciem AppMenu. Zachowanie UX się nie zmienia.

#### P-A3: AD-8, tabela routingu (zmiany 1, 2, A9, A22)

OLD:
```
    | Tryb / typ aktywnej aplikacji | D-pad, OK | Back | Home, Keyboard | Mysz/żyroskop |
    | --- | --- | --- | --- | --- |
    | `shell` (ekran główny) | połykane → Action do Shell | połykane → Action | połykane | wg `pointer` (domyślnie blokowana, kursor ukryty) |
    | `osk` | połykane → OSK | połykane → zamyka OSK | połykane | blokowana |
```
NEW:
```
    | Tryb / typ aktywnej aplikacji | D-pad, OK | Back | Home | Mysz/żyroskop |
    | --- | --- | --- | --- | --- |
    | `shell` (ekran główny) | połykane → Action do Shell | połykane → Action | połykane | wg `pointer` (domyślnie blokowana, kursor ukryty) |
    | `osk` | połykane → OSK | połykane → OSK (zamyka rząd wariantów, a gdy go nie ma, OSK) | połykane → zamyka OSK i pokazuje ekran główny | dozwolona, kursor ograniczony do okna OSK (`ClipCursor`); kliknięcie poza OSK połykane |
```
Pozostałe dwa wiersze (`app` + `web`, `app` + `moonlight` / `native`) zostają bez zmian, poza nagłówkiem kolumny.

Uzasadnienie:
- override AD-8 z UX (memlog L33);
- A22: kursor przenosi jedyny fokus OSK;
- A9: Home zamyka OSK;
- R12: Back zamyka rząd wariantów;
- `ClipCursor` blokuje przypadkowe kliknięcie w stronę Netflixa pod panelem, które zmieniłoby fokus pola.

#### P-A4: AD-8, mapowanie `AppMenu` (zmiana 4, C, A11, A15)

OLD:
```
  - `AppMenu` istnieje tylko w trybie `shell` (np. long-press OK na kafelku).
```
NEW:
```
  - `AppMenu` istnieje tylko w trybie `shell`. Wyzwalacze:
    - long-press OK (`phase: long`) na kafelku aplikacji w stanie `Background` albo `Failed`;
    - OK (`press`) na kafelku w stanie `Failed`. AppMenu otwiera się wtedy z fokusem na „Uruchom ponownie”.
  - Long-press OK na kafelku `NotRunning` nic nie robi. OK na kafelku `Starting` jest ignorowane.
```
Uzasadnienie: decyzje L37, L38, L49, L53. A11 i A15 potwierdzone. Wyjście z `Failed` nadal prowadzi tylko przez AppMenu (AD-10).

#### P-A5: AD-9, OSK tylko automatycznie w webie (zmiany 1, 2, 8, 10)

OLD:
```
- **Binds:** OSK, InputHub, BrowserHost, NativeAppHost
```
NEW:
```
- **Binds:** OSK, InputHub, BrowserHost, PageRuntime
```

OLD:
```
  - Każdy znak trafia do aplikacji od razu, bez bufora: CDP `Input.insertText` (web) albo `SendInput` Unicode (natywne). Tekst nie trafia do logów, Intents ani view-modelu.
  - W serwisach webowych OSK pokazuje się automatycznie na `textFocus{active:true}` aktywnej aplikacji, a w pozostałych aplikacjach przełącza ją Action `Keyboard`.
```
NEW:
```
  - Każdy znak trafia do aplikacji od razu, bez bufora, przez CDP `Input.insertText`. Tekst nie trafia do logów, Intents ani view-modelu.
  - OSK pokazuje się wyłącznie automatycznie, na `textFocus{active:true}` aktywnej aplikacji webowej. W Moonlight i aplikacjach natywnych tekst wpisuje się z klawiatury albo gamepada. Wywołanie OSK z pilota (Action `Keyboard`, `SendInput` Unicode) jest deferred.
  - Pokazanie i schowanie OSK host ogłasza stronie komunikatem `osk{active}` (AD-7). Przy `active:true` PageRuntime przełącza pole docelowe na styl `input-active` (DESIGN.md) i nie rysuje własnego fokusu. Jedyny fokus na ekranie jest na klawiszu OSK.
  - OSK konsumuje OK według fazy: `press` wstawia znak, a `long` otwiera rząd polskich wariantów bez wstawiania litery bazowej (AD-8).
  - Pointer w `osk` przenosi fokus OSK na klawisz pod kursorem, a OK albo klik zatwierdza (AD-8).
```
Uzasadnienie: R10, R1, R12, A22 i override L33. Ścieżka natywna przestaje być potrzebna w MVP.

#### P-A6: AD-7, nowy komunikat `osk` (zmiana 8, R1)

W tabeli komunikatów, po wierszu `prepareClose`, dopisać:
```
    | host → page | `osk` | `{ active: bool }`, przy pokazaniu i schowaniu OSK (AD-9) |
```
Uzasadnienie: bez tego komunikatu PageRuntime nie wie, kiedy przełączyć ring na `input-active`. Zmiana jest addytywna i zgodna z regułą „Pola dodaje się wstecznie zgodnie”, więc `v` zostaje 1.

#### P-A7: AD-6, podświetlenie, profile, limit i error (zmiany 7, 9, C)

OLD:
```
  - PageRuntime ignoruje surowe zdarzenia D-pada z klawiatury. Nawigację steruje wyłącznie przez `action` (AD-7, AD-8).
```
NEW:
```
  - PageRuntime ignoruje surowe zdarzenia D-pada z klawiatury. Nawigację steruje wyłącznie przez `action` (AD-7, AD-8).
  - Własne podświetlenie rysuje według tokenu `focus-ring-injected` z DESIGN.md: biały obrys z ciemną obwódką i promieniem elementu docelowego (`getComputedStyle`, fallback z tokenu), bez skali i bez zmiany layoutu strony. Wartości żyją tylko w DESIGN.md.
  - Ekran `profiles`: driver fokusuje ostatnio używany profil (fallback: pierwszy) i nigdy go nie wybiera. Wybór należy do użytkownika. Sposób zapamiętania ostatniego profilu to wewnętrzna sprawa drivera.
  - Ekrany `limit` i `error`: MediaStation nie dodaje własnego UI. Driver stawia fokus na głównej akcji ekranu (np. „Spróbuj ponownie”).
```
Uzasadnienie: R4, R9 i decyzja L39. Wartości wyglądu nie są duplikowane, bo ich właścicielem jest `bmad-ux`.

#### P-A8: AD-10, `Failed`, CDP, wybudzenie (zmiany 4, 5, 6, 7, C)

OLD:
```
  - Wyjście z `Failed` tylko przez `AppMenu` (zamknij / uruchom ponownie).
```
NEW:
```
  - Wyjście z `Failed` tylko przez `AppMenu` (zamknij / uruchom ponownie). Na kafelku `Failed` AppMenu otwiera także zwykłe OK, z fokusem na „Uruchom ponownie” (AD-8).
  - Gdy w `Failed` przechodzi aplikacja na pierwszym planie, Orchestrator sam przechodzi na ekran główny (tryb `shell`) i wysyła `focusRequest` na kafelek tej aplikacji (AD-16).
```

OLD:
```
  - **Wybudzenie z Modern Standby:** Orchestrator przechodzi na ekran główny i wysyła `RenewSession` do BrowserHost (CDP + `addBinding`). Driver obsługuje wybór profilu [ADOPTED].
  - **Zerwane CDP przy żywym procesie:** reconnect z backoffem.
```
NEW:
```
  - **Wybudzenie z Modern Standby:** Orchestrator przechodzi na ekran główny, wysyła `focusRequest{default}` (AD-16) i `RenewSession` do BrowserHost (CDP + `addBinding`). Na ekranie profili driver tylko fokusuje ostatnio używany profil, a wybiera użytkownik (AD-6) [ADOPTED].
  - **Zerwane CDP przy żywym procesie:** reconnect z backoffem, bez UI. W tym czasie `action` do strony są odrzucane, a nie buforowane. Home działa. Po `cdpReconnectTimeout` (`settings.json`) aplikacja przechodzi w `Failed`.
```

OLD:
```
  - **Restart MediaStation po awarii** robi `--supervise` (AD-2). Stan sesji nie jest trwały: po restarcie start od ekranu głównego plus adopcja.
```
NEW:
```
  - **Restart MediaStation po awarii** robi `--supervise` (AD-2). Stan sesji nie jest trwały: po restarcie start od ekranu głównego z `focusRequest{default}`, plus adopcja.
```
Uzasadnienie: decyzje L31 i L37 oraz R7, R8, R9 i A12. Brakowało limitu reconnectu, bez którego warunek „utrata CDP bez możliwości reconnectu” był nierozstrzygalny.

#### P-A9: AD-11, `category`, domyślne polityki, `settings.json` (zmiana 3, C)

OLD:
```
    - `apps.json` zawiera wpisy `{ id, type: web|moonlight|native, title, icon, back, pointer, … }`:
```
NEW:
```
    - `apps.json` zawiera wpisy `{ id, type: web|moonlight|native, category: video|audio|game, title, icon, back, pointer, … }`:
      - `category` wyznacza zakładkę ekranu głównego, a kolejność wpisów w pliku jest kolejnością kafelków w zakładce. Zakładka bez wpisów (w MVP `audio`) pokazuje empty state;
      - domyślne polityki: `web` → Back przez stronę (AD-7), `pointer: blocked`; `moonlight` → `back: key:Escape`, `pointer: allowed`; `native` → `back: home`, `pointer: allowed`;
```

OLD:
```
    - `settings.json` zawiera wartości polityk (np. `ratingPendingDays`, timeouty).
```
NEW:
```
    - `settings.json` zawiera wartości polityk, m.in. `ratingPendingDays` (domyślnie 7), `cdpReconnectTimeout` (domyślnie 30 s) i pozostałe timeouty.
```
Uzasadnienie:
- decyzje L22, L25, L30, L40 i L43 oraz A21;
- schemat odrzuca nieznane pola, więc `category` musi być w rekordzie;
- `cdpReconnectTimeout` to wartość potwierdzona 2026-10-17.

#### P-A10: AD-12, QR i akcje karty (C, D, R2, R15)

Po linii `- MediaStation nie przechowuje żadnego konta Filmwebu.` dopisać:
```
  - Karta niesie QR z URL-em wyszukiwarki filmweb.pl z tytułem, budowanym lokalnie: minimalny URL, bez zewnętrznego skracacza, ECC L. Dokładny format URL-a ustala story.
  - Akcje karty: „Ocenione” → `rated`, „Pomiń” → `skipped`. W MVP bez undo; undo jest deferred i sprowadza się do zmiany `status`.
```
Uzasadnienie: R2 i R15 (potwierdzone) oraz decyzja L28. Skracacz byłby nową zależnością sieciową i wysyłałby tytuł do trzeciej strony.

#### P-A11: AD-16, `focusRequest` i kategorie w view-modelu (zmiany 3, 5, C)

OLD:
```
  - UI jest właścicielem tylko efemerycznego stanu nawigacji (fokus, scroll), kluczowanego stabilnymi id (`appId`, klucz „Do oceny”), nigdy indeksami.
```
NEW:
```
  - UI jest właścicielem tylko efemerycznego stanu nawigacji (fokus, scroll, aktywna zakładka), kluczowanego stabilnymi id (`appId`, klucz „Do oceny”, `category`), nigdy indeksami.
  - Snapshot niesie `focusRequest { target: default|<appId>, seq }`. UI stosuje każde nowe `seq` dokładnie raz, potem fokus znów należy do UI. `default` = zakładka Video, pierwszy kafelek (zimny start, restart, wybudzenie). `<appId>` = kafelek tej aplikacji (`Failed` z pierwszego planu, AD-10).
  - View-model niesie `category` każdej aplikacji, więc Shell sam grupuje kafelki w zakładki i liczy wskaźnik „działa w tle” per zakładka.
```
Uzasadnienie: Orchestrator nie może sterować fokusem bezpośrednio (UI jest jego właścicielem), a R7 i decyzja L31 wymagają, żeby Core wskazał cel fokusu. Kategorie w view-modelu wynikają z A25.

#### P-A12: Zakres MVP → architektura

OLD:
```
| Ekran główny pilotem, klawiaturą i gamepadem, bez kursora | Shell UI, InputHub | AD-2, AD-8, AD-16 |
```
NEW:
```
| Ekran główny z zakładkami Video / Audio / Game, pilotem, klawiaturą i gamepadem, bez kursora | Shell UI, InputHub, Config | AD-2, AD-8, AD-11, AD-16 |
```

OLD:
```
| Klawiatura ekranowa | OSK, InputHub | AD-8, AD-9 |
```
NEW:
```
| Klawiatura ekranowa (automatyczna w serwisach webowych) | OSK, InputHub, PageRuntime | AD-7, AD-8, AD-9 |
```

#### P-A13: Deferred i otwarte pytania

Usunąć wiersz:
```
| UX ekranu limitu, `ratingPendingDays`, domyślne `back` i `pointer`, mapowanie `AppMenu` | Decyzje `bmad-ux`. Mechanizm już ustalony |
```
Dopisać wiersze:
```
| Action `Keyboard` z pilota (OSK w Moonlight i aplikacjach natywnych) | Nowa Action w AD-8 i ścieżka `SendInput` Unicode w AD-9 |
| Undo po „Pomiń” / „Ocenione” | Rekordy nie są usuwane (AD-12), więc undo to tylko zmiana `status` |
```

OLD:
```
- **Kontrole z forge:** czy QR otwiera aplikację, czy przeglądarkę; język tytułu w wyszukiwarce Filmwebu.
```
NEW:
```
- **Kontrole z forge i UX:** czy QR otwiera aplikację, czy przeglądarkę; język tytułu w wyszukiwarce Filmwebu; skanowanie QR (ECC L) z 3 m na prawdziwym TV dwoma telefonami. Wszystkie w story „Do oceny”.
```

### 4.2 Brief (`brief-mediastation.md`)

#### P-B0: frontmatter

OLD: `updated: 2026-10-16`
NEW: `updated: 2026-10-17`

#### P-B1: „Wieczór oczami żony”, krok 2 (zmiana 3)

OLD:
```
2. Widzi ekran główny MediaStation: kafelki Netflix, Moonlight, Plex i kolejnych aplikacji.
```
NEW:
```
2. Widzi ekran główny MediaStation z zakładkami Video, Audio i Game. Kafelki aplikacji są pogrupowane w zakładki: Netflix w Video, Moonlight w Game.
```

#### P-B2: „Wieczór oczami żony”, krok 4 (zmiana 1)

OLD:
```
4. Wybiera profil, przechodzi po wierszach i wchodzi w odcinek wyłącznie D-padem. Gdy trzeba coś wpisać, pojawia się klawiatura ekranowa obsługiwana strzałkami.
```
NEW:
```
4. Wybiera profil, przechodzi po wierszach i wchodzi w odcinek wyłącznie D-padem. Gdy trzeba coś wpisać, pojawia się klawiatura ekranowa obsługiwana strzałkami, a jeśli ktoś woli, także kursorem pilota.
```

#### P-B3: W MVP, ekran główny (zmiana 3)

OLD:
```
- Pełnoekranowy ekran główny w stylu smart TV, obsługiwany pilotem G20S Pro BT, klawiaturą i gamepadem.
```
NEW:
```
- Pełnoekranowy ekran główny w stylu smart TV, obsługiwany pilotem G20S Pro BT, klawiaturą i gamepadem. Aplikacje są pogrupowane w zakładki Video, Audio i Game. Audio jest w MVP pusta.
```

#### P-B4: W MVP, klawiatura ekranowa (zmiany 1, 2)

OLD:
```
- Klawiatura ekranowa obsługiwana D-padem. W serwisach webowych pojawia się automatycznie, a w pozostałych aplikacjach pokazuje się ją i chowa przyciskiem (potwierdzone w architecture spine, AD-9).
```
NEW:
```
- Klawiatura ekranowa obsługiwana D-padem, opcjonalnie także kursorem pilota. Pojawia się automatycznie w serwisach webowych. W Moonlight i aplikacjach natywnych tekst wpisuje się z klawiatury albo gamepada (architecture spine, AD-9).
```

#### P-B5: W MVP, „Do oceny” (D)

OLD (fragment):
```
Karta czeka, aż ktoś wybierze „Oceniłam” albo „Pomiń”.
```
NEW:
```
Karta czeka, aż ktoś wybierze „Ocenione” albo „Pomiń”.
```

#### P-B6: Poza MVP, nowe pozycje (D)

Dopisać na końcu listy „Poza MVP:”:
```
- Wywołanie klawiatury ekranowej z pilota w Moonlight i aplikacjach natywnych.
- Cofnięcie „Pomiń” i „Ocenione” na karcie „Do oceny”.
- Rodzinne zdjęcia jako tło ekranu głównego.
- Właściwe logo MediaStation. W MVP zostaje znak zastępczy.
```

#### P-B7: Otwarte pytania i ryzyka

OLD:
```
- **Klawiatura ekranowa poza serwisami webowymi:** koszt jej automatycznego pokazywania jest do oszacowania.
```
NEW:
```
- **Klawiatura ekranowa poza serwisami webowymi:** poza MVP. Koszt wywołania z pilota i wpisywania przez `SendInput` do oszacowania w kolejnej inicjatywie.
```

Bez zmian: kryterium sukcesu L56 („bez trybu myszy”). Pointer w OSK jest opcjonalny i D-pad nadal wystarcza.

### 4.3 Forge (`forge-filmweb-rating-prompt.md`)

#### P-F1: etykieta (D)

OLD (L18, fragment):
```
czeka, aż ktoś wybierze „Oceniłam” albo „Pomiń”.
```
NEW:
```
czeka, aż ktoś wybierze „Ocenione” albo „Pomiń”.
```

OLD (L41):
```
- „Oceniłam” jest ręczne, bo MediaStation nie wie, czy ocena została wystawiona.
```
NEW:
```
- „Ocenione” jest ręczne, bo MediaStation nie wie, czy ocena została wystawiona.
```
Uzasadnienie: decyzja L28. Usuwa też sprzeczność z L22 forge („Tekst karty jest neutralny”).

### 4.4 Addendum

Bez zmian. Zapisuje stan wiedzy z 2026-10-15 i nie zawiera zakresu.

### 4.5 UX (DESIGN.md, EXPERIENCE.md)

Bez zmian w tym proposalu. Wymagany Update w `bmad-ux` opisuje sekcja 5.

## 5. Implementation handoff

**Klasyfikacja:** Minor, czyli bezpośrednie edycje dokumentów bez replanu.

| Kto | Co | Kryterium sukcesu |
|---|---|---|
| Developer agent (ta sesja, po zatwierdzeniu) | Zastosować P-A0–P-A13, P-B0–P-B7 i P-F1 dosłownie. Dopisać wpis `(change)` do `.memlog.md` architektury i briefu z odnośnikiem do tego proposalu | Każde OLD zastąpione przez NEW; w spine'ie nie zostaje „np. long-press”, „Action `Keyboard`” w MVP ani wiersz Deferred „Decyzje `bmad-ux`” |
| `bmad-ux` (Update) | 1) Zamknąć Open assumptions: użytkownik potwierdził R1–R16 i A1–A26 (2026-10-17), więc trzeba zamienić `[ASSUMPTION]` na decyzje. 2) Rozstrzygnąć w memlogu konflikt L57 („promień jak w shellu”) z R4 („promień elementu”) wpisem `change` na korzyść R4. 3) Poprawić „decyzja w memlogu” przy R10 i A14 (w memlogu to assumption). 4) Dopisać wpisy dla R8 i dla payloadu QR z R2. 5) Ujednolicić „R1–R15” i „R1–R16”, uzupełnić R11 i R16 w tabeli. 6) Dopisać do memloga doprecyzowanie A22: kursor w `osk` ograniczony do okna OSK | EXPERIENCE.md bez otwartych `[ASSUMPTION]`; memlog bez sprzeczności z Handoff |
| `bmad-ticket` (później) | Brak zmian w epikach i stories, bo jeszcze nie istnieją. Przy slicingu: story „Do oceny” niesie test QR z 3 m, a pierwsza story epiki wejścia niesie regułę `press` na key-up (P-A2) | Epiki budowane na zaktualizowanym spine'ie |

Kolejność: najpierw edycje briefu, forge i spine'u, potem Update w `bmad-ux`, a na końcu slicing w `bmad-ticket`.
