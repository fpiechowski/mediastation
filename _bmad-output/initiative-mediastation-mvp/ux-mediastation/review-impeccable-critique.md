⚠️ DEGRADED: single-context (lens uruchomiony jako pojedynczy sub-agent BMad UX Reviewer Gate; Assessment A wykonany przed odczytem detektora, Assessment B = `impeccable detect --json`; bez browser overlay — oceniane na zwalidowanych screenshotach 1920 px)

# Impeccable critique — MediaStation key screens (Operate, TV 10-foot)

**Zakres:** `.working/key-home-video.html` (2 ekrany), `key-home-game.html` (2), `key-home-failed-appmenu.html` (3), `key-netflix-search-osk.html` (3) + screenshoty w `.working/review/`. Viewport wyłącznie 1920×1080, kanapa ~3 m, przyciemniony ciepły salon, tylko D-pad.
**Poprzeczka:** kategoria Google TV / Apple TV zagrana na serio (świadomy wybór usera) — nie oceniam „za mało wyróżniające się”, oceniam, czy dorównuje standardowi kategorii.
**Kontrakt:** `DESIGN.md`, `EXPERIENCE.md`, `.working/PRODUCT.md`.

## Verdict

**PASS WITH FIXES.** Shell trzyma kanon kategorii czysto: jeden biały fokus z liftem, spokojna grafitowa paleta, ludzkie stany po polsku. Do poprawy przed implementacją: podwójny fokus w OSK, skanowalność QR z kanapy oraz upchnięty wiersz „Do oceny”, który wychodzi poza title-safe. Nie ma blokerów, ale te trzy rzeczy uderzają w jedyną funkcję ponad parytet z VIDAA i w regułę „dokładnie jeden fokus”.

## Design Health Score

| # | Heurystyka | Wynik | Główny problem |
|---|---|---|---|
| 1 | Visibility of System Status | 3 | Background czytelny (kropka + linia + chip); Failed i Starting z 3 m wyglądają prawie tak samo (ten sam chip, różni je ikona 22 px) |
| 2 | Match System / Real World | 3 | Polski, ciepły, na ty; „Ocenione” na przycisku czyta się jak stan, nie jak akcja (decyzja zablokowana — tylko obserwacja) |
| 3 | User Control and Freedom | 3 | Home/Back ratują zawsze; „Pomiń” usuwa kartę bez cofnięcia |
| 4 | Consistency and Standards | 2 | Fokus kafelka (ring + skala + cień) vs fokus przycisku/klawisza (sam ring na ciemnej pigułce); ring wstrzyknięty z promieniem 22 px na kwadratowych elementach Netflixa; dwa ringi naraz w OSK; Failed bez linii stanu, choć Background ją ma |
| 5 | Error Prevention | 2 | Przypadkowe OK na „Pomiń” = karta znika na zawsze; ghost-obrys „Pomiń” udaje ring fokusu |
| 6 | Recognition Rather Than Recall | 3 | Long-press OK (AppMenu, polskie znaki) niewidoczny; dla Failed jest droga przez zwykłe OK, dla ą/ę nie ma żadnej podpowiedzi |
| 7 | Flexibility and Efficiency | 3 | D-pad + żyroskop w OSK + long-press jako skróty; OK |
| 8 | Aesthetic and Minimalist Design | 3 | Czysto, zero marketingu; wiersz „Do oceny” ściśnięty i przyklejony do dolnej krawędzi |
| 9 | Error Recovery | 3 | Failed → OK → „Uruchom ponownie” zaznaczone = naprawa jednym OK; komunikat w najmniejszym stopniu pisma (chip 24 px) |
| 10 | Help and Documentation | 2 | Jedyna podpowiedź to „Zeskanuj telefonem…”; brak hintu dla polskich znaków w OSK |
| **Total** | | **27/40** | **Acceptable (górna granica, 1 pkt do Good)** |

## Design Specificity Verdict

**LLM assessment:** Zgodnie z wyborem usera to kanon Google TV wykonany bez ironii — i to się udało. Zakładki-pigułki, kafelki 16:9 z liftem, biały fokus, grafit + jeden chłodny akcent tylko na wskaźnikach stanu. Z zasłoniętą nazwą ekran czyta się jako „normalny, ładny telewizor”, czyli dokładnie to, czego chce Eliza. Signature move to wiersz „Do oceny” z QR i właśnie on jest najsłabiej dopracowanym elementem kompozycji (ciasno, mały QR, brak oddechu). Product character dają copy („Tu jeszcze cisza. Muzyka dojdzie wkrótce.”, „Coś się zacięło. Wciśnij OK”), a nie warstwa wizualna, co przy tej poprzeczce jest poprawne.

**Deterministic scan (`impeccable detect --json`, exit 2, 8 findings):**
- `cramped-padding` ×5 na `.tile` (video, game, failed-appmenu ×3) — **false positive**: wordmark jest wycentrowany gridem w kafelku 306 px, a detektor liczy rozciągnięty grid item.
- `cramped-padding` ×2 na `.nf-field` (OSK) — dotyczy przybliżenia UI Netflixa, poza naszym zakresem; pomijam.
- `flat-type-hierarchy` (advisory, video): body 26 / h2 32 / h3 34, najwyższy skok 1.23:1. **Częściowo trafne** — pokrywa się z F3: „Do oceny” (32/600) ledwo odróżnia się od nazwy kafelka „Netflix” (30/600) tuż nad nim.

**Visual overlays:** brak (nie uruchamiano live-server/injection; ocena na screenshotach).

## Overall Impression

Home jest na poziomie kategorii. Fokus kafelka widać z kanapy bez wysiłku, stany Background są rozpisane wzorcowo (kropka na zakładce + kropka i tekst pod kafelkiem + chip). Największa szansa: dać wierszowi „Do oceny” miejsce, na jakie zasługuje jako jedyna funkcja ponad VIDAA — większy QR, oddech i scroll wiersza do góry, gdy fokus na nim staje, jak w Google TV.

## What's Working

1. **Fokus kafelka** (`video` #1, `game` #1, `failed` #1/#3): biały obrys 5 px + scale 1.08 + miękki cień. Z 3 m nie da się go przeoczyć, rozpoznawalny bez koloru. Na Home jest zawsze dokładnie jeden.
2. **Język stanów:** „W tle · Pamiętniki wampirów, wstrzymane”, „Uruchamiam…”, „Coś się zacięło. Wciśnij OK”. Mówi po ludzku, co się dzieje i co zrobić jednym OK. Kropka na zakładce Game/Video zdradza aplikację w tle bez dodatkowego chromu.
3. **AppMenu dla Failed** (`failed` #2): „Uruchom ponownie” już zaznaczone, mocny scrim, dwie pozycje. To naprawa jednym OK w duchu zasady „Eliza nie musi wiedzieć, co jest pod spodem”.

## Findings

### F1 — [high] Dwa ringi fokusu naraz w OSK
- **Lokalizacja:** `key-netflix-search-osk.html` ekran #1 (pole Netflix + klawisz „e”), ekran #2 (pole + wariant „ę”).
- **Problem:** Pole wyszukiwania dostaje wstrzyknięty ring 5 px biały (`.inj`), a klawisz OSK dostaje identyczny ring 5 px biały. Łamie to regułę „dokładnie jeden fokus na ekranie” (EXPERIENCE → Focus ring). Z kanapy oko nie wie, gdzie jest kursor, a pole jest ~650 px wyżej i większe niż klawisz, więc wygrywa uwagą. Google TV/Apple TV w tym miejscu pokazują pole jako *aktywne pole wprowadzania* (jaśniejsze tło / podkreślenie / kursor), nigdy jako drugi fokus.
- **Fix:** Gdy tryb = `osk`, PageRuntime przełącza ring pola na odrębny styl „pole aktywne”: brak białego 5 px; zamiast tego np. 2 px `text-2` lub samo jaśniejsze tło + migający caret. Biały 5 px ring tylko na klawiszu OSK. Dopisać do DESIGN.md token `focus-ring-injected--input-active` i regułę w EXPERIENCE → OSK.

### F2 — [high] QR za mały na realne skanowanie z kanapy (~3 m)
- **Lokalizacja:** `key-home-video.html` ekran #1 i #2, karta „Do oceny” (QR 220 px, pole kodu 192 px).
- **Problem:** Mock ma 25 modułów (wersja 2). Realny URL wyszukiwarki Filmwebu z tytułem (np. `https://www.filmweb.pl/search#/?query=Pami%C4%99tniki%20wampir%C3%B3w`, ~65 znaków) da wersję 4–5 (33–37 modułów) → ~5,2–5,8 px/moduł. Na 55" (piksel ≈ 0,63 mm) to moduł ~3,5 mm i kod ~12 cm. Reguła kciuka: kod skanuje się z odległości ~10× swojego boku, czyli **realnie z ~1–1,5 m**. Z 3 m zadziała tylko na telefonach z auto-zoomem QR (nowsze iPhone/Pixel), i to nie zawsze w słabym świetle z odblaskiem. Quiet zone 14 px ≈ 2,5 modułu (norma: 4). Efekt: Eliza wstaje i podchodzi, a kulminacja J1 traci „z kanapy”.
- **Fix:** (a) Skrócić payload: minimalny URL (`filmweb.pl/search?q=…` bez `https://www.` i fragmentu `#/`), ECC L → cel: wersja 2–3 (25–29 modułów). (b) Gdy fokus jest w wierszu „Do oceny”, wiersz scrolluje do góry (patrz F3) i QR rośnie do ~320–360 px (≥10 px/moduł). (c) Quiet zone ≥4 moduły. (d) Zweryfikować na sprzęcie: skan z 3 m na Hisense przy ciepłej lampie, 2 telefony (iOS/Android, aparat systemowy). Jeżeli (a)+(b) nie dadzą 3 m, zmienić hint na uczciwy: „Zeskanuj telefonem z bliska i oceń na Filmwebie”.

### F3 — [high] Wiersz „Do oceny” upchnięty i poza title-safe
- **Lokalizacja:** `key-home-video.html` ekran #1 i #2.
- **Problem:** Linia stanu Netflixa kończy się ~y 700, nagłówek „Do oceny” zaczyna się na 732 (odstęp ~30 px nad), a karty na ~790 (20 px pod nagłówkiem). Nagłówek wizualnie przykleja się do metadanych Netflixa, a nie do swojego wiersza (proximity odwrócona — craft-floor: więcej miejsca nad nagłówkiem niż pod). Dół kart ~1058 px → 22 px od krawędzi, poza 5% title-safe (54 px). Na TV z overscanem obetnie przyciski, bez overscanu wygląda na wciśnięte. Detektor dorzuca `flat-type-hierarchy`: nagłówek 32/600 vs nazwa kafelka 30/600.
- **Fix:** Pionowy model Google TV: w spoczynku wiersz „Do oceny” *wystaje* (peek) spod kafelków z nagłówkiem ≥48 px pod linią stanu. Gdy D-pad schodzi w dół, cała strona przesuwa się w górę (header i zakładki mogą zjechać poza ekran), a karta dostaje pełną wysokość i większy QR (F2). Dolny brzeg zawsze ≥54 px od krawędzi. Nagłówek sekcji podnieść do ~36–40 px albo zostawić 32 px, ale z wyraźnym odstępem 48/20. Dopisać do EXPERIENCE → Interaction Primitives regułę „scroll wiersza w pionie”.

### F4 — [medium] Fokus małych kontrolek słabszy niż kanon; ghost-obrys „Pomiń” udaje ring
- **Lokalizacja:** `key-home-video.html` #2 („Ocenione”), `key-home-failed-appmenu.html` #2 (pozycje menu), `key-netflix-search-osk.html` #1–#2 (klawisze), `key-home-game.html` #2 (zakładka Audio).
- **Problem:** Na przyciskach, klawiszach i pozycjach menu fokus to sam biały obrys na ciemnej pigułce, a wypełnienie zostaje bez zmian. Po liftcie kafelka to duży spadek salience. Google TV i Apple TV na małych kontrolkach odwracają pigułkę (biała / jasna, ciemny tekst). Dodatkowo „Pomiń” (ghost) ma stały obrys 2 px `line`. Z 3 m w półmroku to „ring obok ringu”, który przy szybkim przejściu Lewo/Prawo myli. W OSK tylko jeden z ~40 klawiszy ma ring, bez zmiany wypełnienia, co jest trudniejsze do wyłapania niż inwersja.
- **Fix:** Token `focus-fill` dla kontrolek < kafelek: tło `#FFFFFF`, tekst/ikona `ground`, ring opcjonalny. Dotyczy przycisków karty, AppMenu, klawiszy OSK i zakładki w fokusie. Ghost „Pomiń” bez stałego obrysu (np. tylko `text-2` na `surface-2` z niższą jasnością) albo z obrysem znacznie ciemniejszym niż jakikolwiek stan fokusu. Zaktualizować DESIGN → Components (Button, OSK key, AppMenu, Tab).

### F5 — [medium] Failed i Starting nieodróżnialne z kanapy; komunikat błędu w najmniejszym tekście
- **Lokalizacja:** `key-home-failed-appmenu.html` #1 (Failed) vs #3 (Starting).
- **Problem:** Oba stany to ten sam chip 44 px, tekst 24 px, ikona 22 px (ⓘ vs spinner). Z 3 m różnią się tylko długością tekstu. Najważniejsza instrukcja („Wciśnij OK”) jest w najmniejszym stopniu pisma w systemie, wciśnięta w pigułkę. Pod kafelkiem Failed nie ma linii stanu, a Background ją ma, więc wzorzec jest niespójny.
- **Fix:** Chip Failed krótki („Nie działa” + ikona ≥28 px). Pełny komunikat jako linia stanu pod kafelkiem (26 px, `text`), tak jak „W tle · …”: „Coś się zacięło. Wciśnij OK, uruchomię ponownie.” Starting: chip z animowanym spinnerem (w mocku statyczny) i linia stanu „Uruchamiam…”. Bez czerwieni (zgodnie z DESIGN), różnicę niesie kształt ikony + miejsce + treść.

### F6 — [medium] Wstrzyknięty ring ma promień 22 px na kwadratowych elementach Netflixa
- **Lokalizacja:** `key-netflix-search-osk.html` #1–#2 (pole wyszukiwania, radius 4 px), #3 (pierwszy wynik, kwadratowe rogi).
- **Problem:** `focus-ring-injected` wymusza `{rounded.tile}` 22 px. Na elementach Netflixa z rogami ~4 px rogi elementu wystają poza zaokrąglony ring. Wygląda to jak nakładka, która nie pasuje, a nie jak fokus tego elementu. Spójność z shellem ma iść przez kolor, grubość i obecność ringu, nie przez promień.
- **Fix:** PageRuntime bierze `border-radius` z `getComputedStyle` celu (fallback 4 px) i dodaje `outline-offset: 3–4px`, żeby ring nie zjadał treści. DESIGN.md: zmienić `focus-ring-injected.radius` na „promień elementu docelowego”.

### F7 — [medium] Polskie znaki w OSK są nieodkrywalne
- **Lokalizacja:** `key-netflix-search-osk.html` #1.
- **Problem:** ą ć ę ł ń ó ś ź ż są dostępne tylko przez long-press OK (świadomy wyjątek w Accessibility Floor), ale panel nie mówi o tym ani słowem. Eliza szuka „Pamiętniki” i utknie na „ę”. To dokładnie ten typ wiedzy „spod spodu”, którego zasada 4 ma oszczędzić.
- **Fix:** Stała, cicha podpowiedź w panelu OSK (26 px, `text-2`, np. w rzędzie z „123”/„Gotowe” albo nad klawiaturą): „Przytrzymaj OK, żeby wpisać ą, ę, ł…”. Dodatkowo na klawiszach z wariantami mały znacznik (kropka/kreska w rogu, `text-3`).

### F8 — [medium] „Pomiń” bez cofnięcia
- **Lokalizacja:** `key-home-video.html` #2 (akcje karty); EXPERIENCE → Card „Do oceny” (karta znika bez potwierdzenia).
- **Problem:** Lewo/Prawo po przyciskach i kartach przy `phase: repeat` łatwo kończy się OK na złym przycisku. Karta znika trwale, a jedyna funkcja ponad parytet traci tytuł.
- **Fix:** Po „Pomiń”/„Ocenione” karta przez ~5 s zostaje w miejscu w stanie zwiniętym: „Pominięte · Cofnij” z fokusem na „Cofnij”. Potem znika. Bez modala i bez potwierdzenia.

### F9 — [low] AppMenu zasłania tożsamość kafelka
- **Lokalizacja:** `key-home-failed-appmenu.html` #2.
- **Problem:** Panel menu przykrywa wordmark „Netflix”, a nazwa pod kafelkiem jest wyszarzona scrimem. Menu nie mówi, czego dotyczy.
- **Fix:** Nagłówek panelu „Netflix” (26–30 px, `text-2`) albo przesunięcie panelu pod/obok kafelka, tak by wordmark został widoczny nad scrimem.

### F10 — [low] Podwójny komunikat „wstrzymane”
- **Lokalizacja:** `key-home-video.html` #1–#2.
- **Problem:** Chip „Wstrzymane” + linia „W tle · Pamiętniki wampirów, wstrzymane” mówią to samo dwa razy.
- **Fix:** Linia stanu: „W tle · Pamiętniki wampirów” (chip niesie pauzę), albo chip tylko z ikoną pauzy.

### F11 — [low] Hint karty łamie się z sierotą i dotyka przycisków
- **Lokalizacja:** `key-home-video.html` #1–#2 (obie karty).
- **Problem:** „Zeskanuj telefonem i oceń na / Filmwebie” — sierota w drugiej linii. Ring fokusu „Ocenione” (#2) startuje ~15 px pod linią bazową hintu.
- **Fix:** `text-wrap: balance` lub krótszy hint („Zeskanuj i oceń na Filmwebie”). Odstęp hint → przyciski ≥24 px. Załatwia się razem z F3.

### F12 — [low] Popup wariantu diakrytycznego wisi nad treścią Netflixa
- **Lokalizacja:** `key-netflix-search-osk.html` #2.
- **Problem:** Pigułka „ę” z 8 px paddingu `surface` siedzi na tle wyników („Lucyfer”, „Riverdale”), więc wygląda na doklejoną.
- **Fix:** Wyświetlać rząd wariantów *wewnątrz* panelu OSK (dodatkowy rząd nad QWERTY albo zastąpienie rzędu) albo dać popupowi cień `focus-shadow` i większy padding.

### F13 — [low] Pusta zakładka Audio — tekst mały i przyklejony do zakładek
- **Lokalizacja:** `key-home-game.html` #2.
- **Problem:** „Tu jeszcze cisza. Muzyka dojdzie wkrótce.” (świetne copy) stoi w 26 px tuż pod zakładkami, a reszta ekranu jest pusta. Wygląda jak niedokończony stan, nie jak zaplanowany empty state.
- **Fix:** Umieścić na wysokości `apps-top`, 30–34 px, `text-2`, ewentualnie z jedną cichą ikoną (nuta). Fokus zostaje na zakładce (zgodnie ze spec).

### F14 — [low] Etykiety-adnotacje w mocku OSK
- **Lokalizacja:** `key-netflix-search-osk.html` #1–#3 (prawy górny róg, „Netflix (poza naszym UI) — przybliżenie”).
- **Problem:** OK jako dokumentacja, ale 20 px tekstu w kadrze 1920×1080 może przeciec do referencji implementacyjnej.
- **Fix:** Oznaczyć klasą `.doc-note` i wykluczyć z handoffu / opisać w README mocków.

### F15 — [low] „Ocenione” czyta się jak stan, nie jak akcja
- **Lokalizacja:** `key-home-video.html` #1–#2.
- **Problem:** Na przycisku imiesłów („Ocenione”) brzmi jak etykieta statusu. Decyzja jest świadoma (forma neutralna), więc nie pcham zmiany.
- **Fix (opcjonalny):** Jeśli testy z Elizą pokażą wahanie: „Już ocenione” albo „Gotowe, ocenione”. Inaczej zostawić.

## Persona Red Flags

**Eliza (główna, bez tła technicznego, wieczór, 3 m):** Na ekranie wyszukiwania widzi dwa białe ringi i nie wie, gdzie jest „kursor” (F1). Na „ę” w „Pamiętniki” utknie, bo nic nie mówi o przytrzymaniu OK (F7). Kartę „Do oceny” zeskanuje dopiero po podejściu do TV (F2). Przypadkowe „Pomiń” zabiera jej kartę bez śladu (F8).

**Jordan (first-timer / gość na kanapie):** Failed vs Starting z kanapy wygląda tak samo, więc nie wie, czy czekać, czy wcisnąć OK (F5). Po długim przytrzymaniu OK na kafelku dostaje menu bez nazwy aplikacji (F9).

**Autor (power user):** Skróty (long-press, żyroskop w OSK) są, a Home zawsze ratuje — bez czerwonych flag. Overscan TV może mu obciąć dolny wiersz (F3) — sprawdzić ustawienie „Just Scan/Overscan off” na Hisense.

## Cognitive Load

Checklist: 1 failure (*single focus* w OSK — F1). Punkty decyzyjne ≤4 opcje wszędzie poza OSK (klawiatura z natury). Niskie obciążenie.

## Minor Observations

- Kontrast zgodny z AA z zapasem (`text-3` 7.2:1 na `ground`). Biały 100% na `#0F1012` w ciemnym pokoju może lekko „świecić” (halation) na dużych wordmarkach — `tile-mark` #E6E8EB już to łagodzi; rozważyć to samo dla tytułów kart.
- Spinner „Uruchamiam…” w mocku statyczny; w implementacji obrót ≤1 obrót/s, `prefers-reduced-motion` nie dotyczy TV, ale zgodnie z Accessibility Floor ruch tylko tam, gdzie niesie stan.
- Kropka akcentu na zakładce Video gdy fokus na Game (`game` #1) — poprawnie informuje o Netflixie w tle; dobrze.

## Questions to Consider

- Czy „Do oceny” nie powinno być pierwszym wierszem, w który ląduje fokus po powrocie z Netflixa po obejrzeniu sezonu? (Spec mówi: nie kradnie fokusu — ale może zasługuje na scroll-peek wyżej.)
- Czy QR musi prowadzić bezpośrednio do Filmwebu, czy krótki redirect pozwoliłby na wersję 1–2 kodu i skan z 3 m?
- Jak wyglądałby fokus małych kontrolek, gdyby był równie pewny jak lift kafelka?

---
Questions skipped: lens w BMad UX Reviewer Gate — pytania do usera zadaje orkiestrator bramki. Snapshot `.impeccable/critique/` nie zapisany (zakres zapisu ograniczony do tego pliku).
