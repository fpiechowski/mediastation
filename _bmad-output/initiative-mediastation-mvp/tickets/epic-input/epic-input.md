---
type: epic
status: done
title: "Remote, keyboard & gamepad input"
parent: initiative-mediastation-mvp
covers: [CAP-3]
after: []
assignee: ""
risk: high
---

# Remote, keyboard & gamepad input

## Description

CAP-3: pilot G20S Pro przez dongle USB 2.4G, klawiatura i gamepad sterują MediaStation jednym zestawem Actions, kierowanych według bieżącego trybu (`shell`, `app`, `osk`), a Home działa zawsze. Epika dostarcza InputHub (własny wątek z hookiem `WH_KEYBOARD_LL`, Raw Input, XInput), mapowanie z `input.json`, w tym przycisku pilota i klawisza dla Action `Keyboard`, lokalną kopię routingu z `SetInputRouting` oraz tryb `--calibrate-input`.

## Outcome

Każde wejście dociera do Core jako jedna Action, a Home nie zależy od wątku UI ani okna na wierzchu; sygnałem jest powtórzenie S10 z pilotem na dongle 16/16.

## Done when

1. Jedno naciśnięcie pilota, klawiatury albo gamepada daje jedną Action (AD-8); wyjątki są celowe: trzymana strzałka daje `press` i `repeat`, a D-pad i OK w `app` + `moonlight`/`native` nie dają Actions.
2. Klawisz z przypisanym `long` (OK w `shell` i `osk`) daje `press` dopiero na key-up przed progiem albo samo `long`, od Story 1 (AD-8).
3. Na UM760 Home jest połykany i dostarczany jako Action nad Edge, Moonlightem, pulpitem i przy zawieszonym wątku UI shella; powtórzenie S10 z pilotem na dongle USB 2.4G daje 16/16 (AD-2, AD-8).
4. Logi zawierają tylko Actions; surowe kody pojawiają się wyłącznie w `--calibrate-input` (AD-13).
5. `--calibrate-input` loguje przyciski pilota `remote` przychodzące jako raporty HID Consumer (Home, Back, asystent głosowy), z VK albo bez. Wybrany przycisk Klawiatura daje Action `Keyboard`, a kalibracja zapisuje go w `input.json`: VK do `keys`, usage bez VK do `hid`. Na G20S Pro przyciskiem Klawiatura jest asystent głosowy (strona `0x0C`, usage `0x00CF`), wyzwalacz HID obok VK (AD-8).

## Boundaries

`Platform/Input` i `config/input.json`. Nie: przywrócenie shella na wierzch po Home (epic-windows-audio), decyzja o trybie (Orchestrator, epic-orchestrator-lifecycle).

## References

- parent — _bmad-output/initiative-mediastation-mvp/spec-mediastation/spec-mediastation.md, Capabilities CAP-3, Open Questions (Przycisk Klawiatura na G20S Pro, hook LL)
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-2, AD-8, AD-9 (TextSink, OSK), AD-13 (Logi), AD-14 (czas w Core)
- research — _bmad-output/initiative-mediastation-mvp/research-browser-control-for-streaming-services/digests/spike-um760.md, claims S10
- research — spike/spike-results.md, S10 (kody dongla, przebieg rozstrzygający); spike/win/HomeLogger.cs (referencja zachowania)
- ux — _bmad-output/initiative-mediastation-mvp/ux-mediastation/EXPERIENCE.md, Interaction Primitives

## Notes

- Open question: czy hook LL we własnym wątku dostaje „down” Home i Back, także gdy MediaStation jest na wierzchu. Rozstrzyga przebieg „shell na wierzchu” skryptu ze story 9 na odbiorze epiku.
- Decision (zastąpiona 2026-10-21, niżej): Story 1 to jedna spike/prototyp story: InputHub na własnym wątku, `--calibrate-input`, S10 z dongle i wybór przycisku Klawiatura oraz reguła `long` dla OK (2026-10-18).
- Decision: pilot działa przez dongle USB 2.4G; Bluetooth nie jest wymagany (change-osk-everywhere-github-update, 2026-10-18).
- Decision (zawężona 2026-10-21, niżej): epika idzie równolegle z epic-orchestrator-lifecycle; rekord `SetInputRouting` jest w kontraktach z epic-foundation-contracts (2026-10-18). Równolegle idzie tylko story 1; stories 2 i 5 czekają na 2.1, 2.2 i 2.3.
- Waits on epic-foundation-contracts because: rekordy `Action`/`SetInputRouting`, schemat `input.json`, logger, wejście CLI.
- Waits on epic-orchestrator-lifecycle because: pętla i kolejka Orchestratora w App (2.1), `Platform/` i composition root (2.2), porty i adapter tylko-log `SetInputRouting` (2.3).
- Decision: korekta decyzji z 2026-10-18 o jednej Story 1 — prototyp dzielimy na story 1 (czysta polityka w Core: mapowanie, korelacja HID → VK, jedna Action, reguła `long` dla OK, kolejka Actions), story 2 (tracer w `Platform/Input`: wątek, hooki, Raw Input, log `action`) i story 3 (`--calibrate-input`, przycisk Klawiatura), bo razem przekraczają jedną sesję; reguła `long` zostaje w pierwszej story, jak chce spec; kody Home/Back/OK/strzałek dongla są znane z S10 (2026-10-21).
- Decision: epika jest budowana w pełni automatycznie w pętli (`bmad-build-auto`), bez `plan_checkpoint` i `done_checkpoint`; checki na UM760 (S10 16/16, wybór przycisku Klawiatura, otwarte pytanie o „down” hooka) idą do notatki odbioru epiku z datą, HEAD i maszyną (2026-10-21).
- Decision (zastąpiona 2026-10-25, niżej): przycisk Klawiatura wybieramy tylko spośród przycisków pilota, które dają VK; przycisk z samym HID usage nie jest kandydatem, a AD-8 („wyzwalaczem jest VK”) zostaje bez zmian. Jeśli żaden przycisk z VK nie pasuje, odbiór dopisuje Source conflict do architektury (2026-10-21).
- Decision: epika dostarcza mechanizmy — Command `SendKey{vk}` przez `SendInput` ze znacznikiem w `dwExtraInfo`, hook myszy z polityką `pointer` i `ClipCursor` do okna z nowego opcjonalnego pola `clipHwnd` w `SetInputRouting`; gałęzie Orchestratora (Back → polityka `back`, tryb `osk`, wysyłanie `clipHwnd`) dokładają epic-osk i epic-moonlight-native, jak Home w epic-orchestrator-lifecycle (2026-10-21).
- Decision: jeden lane — każda story zmienia politykę wejścia w Core albo InputHub w `Platform/Input`, więc `after` tworzy łańcuch (2026-10-21).
- Decision: tracer w Platform (story 2) czeka na 2.1 i 2.2: CsWin32, `Platform/` i composition root z pętlą Orchestratora zakłada epic-orchestrator-lifecycle, a epika 3 je rozszerza; story 1 (Core) może iść od razu (2026-10-21).
- Decision: routing (story 5) podmienia adapter tylko-log `SetInputRouting` z 2.3, a adapter tylko-log `IActionSink` ze story 2 zastępuje wkładaniem Actions do kolejki Orchestratora z 2.1 (2026-10-21).
- Decision: S10 16/16 w tej epice = 4 cele (Edge, Moonlight borderless, pulpit, zawieszony UI shella) × 4 naciśnięcia Home; sukces to dokładnie jedna `action {kind: home}` i żadne nowe okno przeglądarki. Powrót shella na wierzch sprawdza epic-windows-audio. Osobny przebieg „shell na wierzchu, niezawieszony” rozstrzyga otwarte pytanie o „down” hooka (2026-10-21).
- Decision: progi są w opcjonalnym `timing` w `input.json`: `longPressMs` 600 (story 1), `repeatDelayMs` 400 i `repeatIntervalMs` 120 (story 4); `repeat` tylko dla strzałek (2026-10-21).
- Decision: kolejka Actions w Core jest bez ograniczenia rozmiaru; zapis z callbacku hooka nigdy nie czeka i nigdy nie gubi Action, także Home (2026-10-21).
- Decision: polityka wejścia, korelacja urządzenia i kolejka są czystym kodem w Core z testami w `MediaStation.Core.Tests`; `Platform/Input` tylko woła Win32 i przekazuje zdarzenia, więc epika nie zakłada projektu testów App (2026-10-21).
- Decision: `clipHwnd` w `SetInputRouting` rozszerza kontrakt AD-8 `{ mode, appType, pointer, epoch }`; do dopisania w AD-8 (`bmad-architecture`) przed epic-osk (2026-10-21).
- Decision: kombinacja `gamepadHome` składa się z przycisków bez własnego mapowania (walidacja odrzuca mapowanie przycisku z kombinacji), więc Home z pada nie daje Actions składowych (2026-10-21).
- Assumption: urządzenie z sygnaturą `*` w `input.json` to fallback dla każdej klawiatury bez własnego wpisu; sample: Enter → ok, Escape → back, strzałki, `0x24` Home → home, `0x71` F2 → keyboard; na padzie Y → keyboard (2026-10-21).
- Decision: zawieszenie UI do checku S10 daje zmienna środowiskowa `MEDIASTATION_DEBUG_HANG_UI=<s>` w każdym buildzie (na produkcji nie jest ustawiona), z wpisem `warn debugHangUi {seconds}`; check idzie na tym samym buildzie Release co reszta odbioru (2026-10-22).
- Decision: decyzja o połknięciu i `kind` zapada po samym VK ze scalonego mapowania urządzeń (walidacja odrzuca ten sam VK z różnym `kind` w dwóch urządzeniach), bo hook LL najpewniej działa przed `WM_INPUT`, a połknięty klawisz może nie dojść do Raw Input. `source` pochodzi z korelacji: wstrzyknięty VK po raporcie HID Consumer tej samej sygnatury w oknie 50 ms → `remote`; inaczej urządzenie z ostatniego `WM_INPUT` w oknie; w ostateczności `keyboard`. Kalibracja (story 3) loguje kolejność hooka i `WM_INPUT` (2026-10-22).
- Decision: po spike'u 3.11 i zmianie AD-8 reguła `source` z 2026-10-22 jest zawężona: w normalnej pracy InputHub nie rejestruje Raw Input klawiatury w procesie hooka; `remote` dla Home/Back wymaga dopasowania sygnatury, czasu i pary HID usage–VK, a zwykłe klawisze pilota bez dowodu urządzenia mogą mieć `source: keyboard`. Kalibracja zachowuje VK i sygnaturę przez diagnostyczną ścieżkę bez jednoczesnego hooka i Raw Input klawiatury w tym samym procesie. Implementuje story 3.12 po ukończonym spike'u 3.11; fizyczny G20S Pro i S10 16/16 pozostają w odbiorze całej epiki na UM760 przez `fp-acceptance` (2026-10-24).
- Decision: story 3.12 jest poprawką po ukończonym sweepie 3.10, nie rozszerzeniem zakresu zakończonego sweepu; jej własny przegląd i weryfikacja obejmują nową zmianę (2026-10-24).
- Decision: kolizje z epiką 2 bez kolejności są akceptowane i scalane przy merge: `ConfigValidation.cs` (stories 1, 4, 6 vs 2.11), rejestr `MessageTypes.All` i eksport schematów (7, 8 vs 2.3+) (2026-10-22).
- Assumption: błędy rejestracji hooka i Raw Input są logowane jako `inputHubError` bez kodów klawiszy; Done when 4 dotyczy zdarzeń wejścia (2026-10-21).
- Decision: breakdown zaakceptowany (10 entries, jeden lane); epika idzie w pełni automatycznie w pętli (`bmad-build-auto`) bez checkpointów (2026-10-22).
- Decision: pass dokumentacyjny 2026-10-22 (retro A2) dopisał do AD-8 `clipHwnd` w `SetInputRouting`, regułę `repeat` trzymanej strzałki, jawne „bez Actions” dla D-pada i OK w `app` + `moonlight`/`native` oraz własność bramki `textFocus` dla `Keyboard` w epic-osk; zamyka DW-7, DW-9 i DW-12, a bramkę `textFocus` śledzi DW-17 (2026-10-22).
- Decision: retro epiki (2026-10-23) — A1 naprawione w 19c5038 (wygaszanie nieświeżego wpisu połkniętego Home/Back/OK/Keyboard) razem z A9 (walidacja `repeatDelayMs`); A3 zaegzekwowane w polityce pętli; A4 wykonane. Odłożone w ledgerze: DW-17 (bramka `textFocus`, epic-osk, rekomendacja: Orchestrator), DW-22 (clip, epic-osk), DW-23 (konwencja linkowanych źródeł), DW-24 (kalibracja), DW-25 (rozbicie InputHub/InputPolicy), DW-26 (zakres sweepów), DW-27 (odkrywanie sygnatury pilota; na odbiorze ręcznie ze `spike/`). Nie powtarzamy prozy: Notes niosą ID z `deferred-work.md` (2026-10-23).
- Odbiór na UM760 (wynik w notatce odbioru epiku z datą, HEAD i maszyną; pilot na donglu USB 2.4G):
  1. Po story 3.12: każde naciśnięcie Home, Back, OK i strzałek daje w logu dokładnie jeden wpis `action` (Home i Back z `source: remote`; zwykłe klawisze pilota mogą mieć `source: keyboard`), długie OK daje samo `long`, Home nad pulpitem nie otwiera przeglądarki, a log nie zawiera surowych kodów (Done when 1, 2, 4). Trzymana strzałka daje `press` i kolejne `repeat`; D-pad i OK w `app` + `moonlight`/`native` są celowo przepuszczane bez wpisu `action`.
  2. Po story 3.12 i 3.15–3.17: `MediaStation.exe --calibrate-input` loguje `rawInput` z `hasVk` dla każdego przycisku pilota przychodzącego jako raport HID Consumer, z VK albo bez (Home, Back, asystent głosowy; OK i strzałki nie przychodzą tym kanałem, bug 3.14), i nie loguje zwykłej klawiatury; wybrany przycisk Klawiatura zapisuje do `input.json`: VK do `keys`, usage bez VK do `hid`. Na G20S Pro to asystent głosowy `0x00CF` (`"hid": { "0x000C:0x00CF": "keyboard" }`) i w trybie głównym daje `action {kind: keyboard, source: remote}`; mapowanie jest też w `tools/samples/config/input.json` (Done when 5).
  3. Po story 8: kursor żyroskopu pilota stoi w `shell`.
  4. Po story 3.12: `tools/check-home-s10.ps1` daje 16/16, a przebieg „shell na wierzchu” daje `action home` (Done when 3).
  5. Po story 3.15–3.17, przycisk asystenta głosowego G20S Pro (`0x00CF`; plik `epic-input-um760-acceptance*.md` przygotowuje `fp-acceptance`, nie edytuje go Sofia):
     - Shell na wierzchu z `textFocus{active:true}`: jedno wciśnięcie daje dokładnie jedną `action {kind: keyboard, source: remote}` (Done when 1, 5).
     - Nad obcym oknem (Edge otwarty ręcznie, pulpit): wciśnięcie nie daje Action (bramka foreground, AD-8).
     - Windows nie reaguje na ten przycisk: brak okien i dźwięku.
     - Pomiar: czy G20S Pro wysyła raport zwolnienia (usage 0) po `0x00CF`, z `--calibrate-input` albo z logu. Przy braku zwolnienia odbiór ustala regułę debounce i zapisuje ją jako wynik (w AD-8 to `[ASSUMPTION]`).
     - Log nie zawiera usage poza `--calibrate-input`: w normalnej pracy tylko `action {kind: keyboard, source: remote}` (Done when 4, AD-13).
- Decision: odbiór UM760 przebieg 2 (2026-10-25, HEAD 2bb3c95) — niezaakceptowana; S1–S8 i S13 zaliczone, S11, S14, S15 nie dotyczą, S9 i S12 oblane (`epic-input-um760-acceptance-2.md`). Dodano bugi 3.13 (S12: `check-home-s10` 4/16 i 8/16) i 3.14 (S9: kalibracja bez OK i strzałek), oba `after` 3.12, do refine przed startem. Epiki nie zamykam.
- Decision: bug 3.14, kryterium 2 — wariant (a) zatwierdzony przez użytkownika (2026-10-25): Done when 5 i punkt 2 odbioru UM760 wymagają logowania w `--calibrate-input` tylko przycisków pilota przychodzących jako raporty HID Consumer (Home, Back, asystent głosowy); OK i strzałki nie przychodzą tym kanałem po story 3.12 (AD-8: hook LL i Raw Input klawiatury nie w jednym procesie). Tryb kalibracji bez hooka (wariant b) nie wchodzi do epiki.
- Decision: przycisk Klawiatura na G20S Pro to asystent głosowy (strona `0x0C`, usage `0x00CF`, bez VK) i jest dodatkowym wyzwalaczem Action `Keyboard` obok VK; reguły (sekcja `hid` w `input.json` tylko na urządzeniu `remote`, `source: remote`, bez połykania, bramka foreground, jedna Action na przejściu „brak → usage”, bez `repeat` i `long`) są w AD-8 (architektura, commit e9fab30). Zastępuje decyzję z 2026-10-21 („przycisk z samym HID usage nie jest kandydatem”) i zamyka pytanie, który przycisk jest Klawiaturą, oraz dawny Source conflict o tym przycisku (2026-10-25).
- Decision: nowy zakres to stories 3.15 (config `hid`), 3.16 (polityka i hub) i 3.17 (kalibracja), jeden lane za 3.14: 3.15 → 3.16 → 3.17; 3.17 czeka na 3.16, bo jej check używa wpisu `hid`, który musi dawać Action, i bo obie zmieniają InputHub/politykę (2026-10-25).
- Decision: bez drugiego Refactor sweep; sweep 3.10 zamknął pierwotne stories, a poprawki 3.12–3.14 też poszły bez niego. Tracerem tych stories jest 3.15 (config, od którego zależą pozostałe); wynik widoczny: sample `input.json` z wpisem `hid` przechodzi walidację (2026-10-25).
- Decision: raport zwolnienia (usage 0) po `0x00CF` jest w architekturze `[ASSUMPTION]`; story nie czekają na pomiar, a wynik i ewentualną regułę debounce zapisuje odbiór UM760 (2026-10-25).
- Decision: epika zaakceptowana przez użytkownika na UM760 (2026-10-26, HEAD 08eaade, `epic-input-um760-acceptance-3.md`) i zamknięta: wszystkie stories done, Done when 1–5 pokryte przebiegiem 3 (S1–S9, S11, S13 pass; S7, S12, S14 potwierdzone; S15 nie dotyczy; S16 pass). Zawężenie zatwierdzone przez użytkownika: zapis wyboru przycisku do `input.json` przez CLI nie był scenariuszem odbioru, bo `KeyboardButtonMapping` nie jest podpięty do `--calibrate-input`; to zostaje poza epiką. Pomiar S10: G20S Pro wysyła raport zwolnienia po `0x00CF`, więc reguła debounce jest niepotrzebna; `[ASSUMPTION]` w AD-8 do zamknięcia w architekturze (2026-10-26).
