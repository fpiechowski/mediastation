---
id: 11
type: spike
title: "Dlaczego hook WH_KEYBOARD_LL nie dostaje klawiszy, gdy shell MediaStation jest na wierzchu na UM760?"
parent: epic-input
covers: [CAP-3]
after: []
refined: true
hitl: true
risk: high
---

# Dlaczego hook WH_KEYBOARD_LL nie dostaje klawiszy, gdy shell MediaStation jest na wierzchu na UM760?

## Description

Na UM760 przy shellu na wierzchu (WebView2) hook LL nie dostaje żadnego klawisza, a Home z G20S Pro (HID Consumer `0x0223` → wstrzyknięty `VK_BROWSER_HOME`) otwiera Edge przez powłokę (odbiór S2). Spike S10 zapisał tę słabość i uznał za przyczynę wątek UI zajęty `WM_INPUT`. InputHub ma już osobny wątek Input, a hooki i Raw Input `RIDEV_INPUTSINK` nadal działają na jednym wątku, więc przyczyna jest niepotwierdzona.

Czekają na odpowiedź: odbiór `epic-input` (Done when 3) oraz `epic-windows-audio`, `epic-netflix-dpad`, `epic-osk` i `epic-moonlight-native`, które zakładają działające Home. Odpowiedź trafia do `bmad-architecture` (AD-8), potem poprawki w `bmad-build`.

To spike, nie odbiór: nie zmienia statusu odbioru epiki ani nie zamyka jej. `hitl`: człowiek naciska Home na pilocie G20S Pro na UM760 i patrzy, czy otwiera się Edge.

## Approach

1. Wariant bez `foregroundGate` (zmienna `MS_NO_GATE` nie istnieje w kodzie, więc dodaje ją spike). Cel: czy winna jest bramka, czy hook.
2. Hook bez `RegisterRawInputDevices`.
3. Raw Input na osobnym wątku od hooków.
4. Puste okno bez WebView2 z tym samym hookiem.
5. Jeśli 1–4 nie dadzą rozwiązania: alternatywy dla połykania Home (`WM_APPCOMMAND`, `RIDEV_NOLEGACY`, filtr HID).
6. Po każdym eksperymencie jeden wiersz w tabeli wyników: co było na wierzchu, czy hook dostał „down", liczba wpisów `action`, czy otworzył się Edge. Eksperyment, który wskaże przyczynę, kończy kolejne.
7. Kod eksperymentów tylko na gałęzi spike'u; zmiany w `master` wchodzą dopiero po decyzji dla AD-8.

Zakończenie per-ticket: sesja best-effort na ALDER; hipoteza i ograniczenia są zapisane. Próby z fizycznym pilotem należą do późniejszego odbioru całej epiki na UM760.

## Acceptance Criteria

1. **Każdy wykonany eksperyment ma wynik: hook dostaje „down" przy shellu na wierzchu albo nie.**
   Verify: tabela wyników w sekcji „Wynik" tego pliku z dowodami z ALDER; próby sprzętowe są oznaczone jako N/A z powodem.
2. **Per-ticket best-effort na ALDER (zgodnie z `AGENTS.md`): wybrany wariant daje jedną `action {kind: home}` w trzech uruchomieniach pod rząd przy shellu na wierzchu.** Fizyczny pilot i brak nowych okien przy `VK_BROWSER_HOME` są N/A dla tego ticketu na ALDER; sprawdza je użytkownik podczas odbioru całej epiki na UM760.
   Verify: logi `mediastation-*.jsonl` dla trzech procesów ALDER oraz tabela `spike-3-11-20261024-201421.md` w sekcji „Wynik". Wejście syntetyczne nie rozstrzyga zachowania fizycznego Home ani nowych okien przeglądarki.
3. **Decyzja i jej skutki dla AD-8 są zapisane tak, że `bmad-architecture` i `bmad-build` mogą z nich skorzystać.**
   Verify: sekcja „Wynik" w tym pliku oraz `Source conflict:` w Notes AD-8, jeśli decyzja zmienia architekturę.
4. **Użytkownik zaakceptował wynik.**
   Verify: linia `Decision:` w Notes z datą.

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input.md, Done when 3, Notes (otwarte pytanie o „down" hooka)
- research — spike/spike-results.md, S10 (przebieg rozstrzygający i słabość przy oknie z hookiem na wierzchu)
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-input/epic-input-um760-acceptance.md, S2 i „Wstrzymanie odbioru (2026-10-24)"
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-8

## Notes

- Assumption: ticket nie zmienia statusu odbioru epiki (2026-10-24).
- Assumption: per-ticket weryfikacja odbywa się na ALDER; fizyczny pilot i UM760 należą do późniejszego odbioru całej epiki zgodnie z `AGENTS.md` (2026-10-24).
- Resolved: eksperymenty sprawdzają Home, Back i OK; powtarzane przebiegi `raw-hid-only` na ALDER dały po jednej Action dla każdego klawisza.
- HITL: AC 4 czeka na jawną akceptację użytkownika i nie jest blokadą sprzętową. Po decyzji zapisać `Decision:` z datą.

## Wynik

Stan na 2026-10-24, gałąź `spike-3-11` (kod eksperymentów niescalony do `master`). Pomiar: `tools/spike-3-11.ps1` na ALDER (Windows 11), wejście syntetyczne (`keybd_event`/`SendInput`, `LLKHF_INJECTED`), po 3 naciśnięcia Home (`VK_HOME`), Back (Esc) i OK (Enter) na wariant, okno docelowe sprawdzone jako foreground przed każdym wstrzyknięciem. „down" to wpis `keyEnter` (logowany przed polityką) albo linia `down` sondy. Brak pilota G20S Pro i HID Consumer, więc ścieżka sprzętowa UM760 nie jest tu odtworzona.

### Tabela wyników (ALDER)

| # | wariant | na wierzchu | hook „down" (Home/Back/OK) | wpisy `action` (z 3×3) | nowe okna Edge |
|---|---|---|---|---|---|
| 0 | baseline (bez zmiennych) | shell MediaStation (WebView2) | nie / nie / nie | 0 | 0 |
| 1 | `MS_NO_GATE=1` (bramka zawsze „managed") | shell | nie / nie / nie | 0 | 0 |
| 2 | `MS_HUB_NO_RAWINPUT=1` (bez `RegisterRawInputDevices`) | shell | tak / tak / tak | 9 (3 na klawisz) | 0 |
| 3 | `MS_HUB_RAW_THREAD=1` (Raw Input na osobnym wątku) | shell | nie / nie / nie | 0 | 0 |
| 4 | `tools/hook-probe` (puste okno, bez WebView2, bez Raw Input) | puste okno | tak / tak / tak | nie dotyczy | 0 |
| 4a | `hook-probe --rawinput` (klawiatura + HID Consumer, `RIDEV_INPUTSINK`) | puste okno | nie / nie / nie | nie dotyczy | 0 |
| 4b | `hook-probe --raw-kbd` (tylko klawiatura 0x01/0x06) | puste okno | nie / nie / nie | nie dotyczy | 0 |
| 4c | `hook-probe --raw-hid` (tylko HID Consumer 0x0C/0x01) | puste okno | tak / tak / tak | nie dotyczy | 0 |
| 5 | `MS_HUB_RAW_HID_ONLY=1` (InputHub: tylko HID Consumer) | shell | tak / tak / tak | 9 (3 na klawisz) | 0 |
| 6 | alternatywy (`WM_APPCOMMAND`, `RIDEV_NOLEGACY`, filtr HID) | — | nie dotyczy na ALDER: brak pilota (nie wykonano) | — | — |
| UM760 | wszystkie powyższe z pilotem G20S Pro | shell | nie dotyczy na ALDER: brak pilota i sprzętu UM760 | — | — |

Weryfikacja bez zmiennych (2026-10-24): `dotnet build src/MediaStation.App -c Release` 0 ostrzeżeń i 0 błędów, `dotnet test tests/MediaStation.Core.Tests` 892/892, `tools/spike-3-11.ps1 -SelfTest` przechodzi. Wiersze 4a–4c i 5 dodano po zatwierdzeniu planu, żeby zawęzić przyczynę.

Powtórzenie w innej kolejności (no-rawinput, baseline, raw-thread, no-rawinput, hook-probe; 2 naciśnięcia) dało te same wyniki, więc kolejność uruchomień nie wpływa. Dowody: `%LOCALAPPDATA%\MediaStation\logs\spike-3-11-alder-final.md` (wiersze 0–4a), `spike-3-11-alder-order2.md`, `spike-3-11-alder-rawsplit.md` (4b, 4c), `spike-3-11-alder-hidonly.md` (5), logi aplikacji `mediastation-2026-10-24.jsonl` (wpisy `inputHubVariant`, `keyEnter`, `action`) oraz `hook-probe-*.jsonl` w tym samym katalogu. Wariant 3 nie dał `inputHubError`, rejestracja na osobnym wątku się powiodła.

### Powtórzone przebiegi wybranego wariantu (ALDER)

Po wyborze `MS_HUB_RAW_HID_ONLY=1` wykonano trzy osobne uruchomienia aplikacji pod rząd, po jednym syntetycznym Home na przebieg, przy shellu na wierzchu. Każdy proces miał jeden wpis `inputHubVariant` z `rawHidOnly=true`, jeden `keyEnter` down dla `VK_HOME`, jedną `action {kind: home}` i zero `inputHubError`. Back i OK także dały po jednej Action w każdym procesie. Asercja JSONL po PID: 19388, 3304, 2164. Pełna tabela: `%LOCALAPPDATA%\MediaStation\logs\spike-3-11-20261024-201421.md`; aplikacyjne wpisy: `%LOCALAPPDATA%\MediaStation\logs\mediastation-2026-10-24.jsonl`.

Nowe okna przeglądarki nie są oceniane w tej próbie: syntetyczny `VK_HOME` nie odtwarza pilota, który wysyła HID Consumer `0x0223` i `VK_BROWSER_HOME`. To sprawdza odbiór całej epiki na UM760.

### Wniosek

- Na ALDER, na wejściu syntetycznym, zaobserwowano wyciszenie hooka (hipoteza przyczyny, niepotwierdzona na UM760): **rejestracja Raw Input klawiatury (`usagePage 0x01`, `usage 0x06`, `RIDEV_INPUTSINK`) w tym samym procesie wycisza `WH_KEYBOARD_LL`, gdy okno tego procesu jest na wierzchu.** Nie jest to WebView2 (puste okno bez niego też milknie, 4a/4b), nie bramka foreground (1; `keyEnter` jest logowany przed polityką, więc callback hooka w ogóle nie był wołany), nie wątek UI zajęty `WM_INPUT` (3: osobny wątek nic nie zmienia). Rejestracja samego HID Consumer hooka nie wycisza (4c, 5).
- Wariant InputHub z samym HID Consumer (`MS_HUB_RAW_HID_ONLY`) daje na ALDER po jednym `action` na naciśnięcie we wszystkich trzech klawiszach i zero okien Edge.
- Granice wniosku: kolumna „nowe okna Edge" nie niesie informacji na ALDER (wstrzyknięty `VK_HOME` nie uruchamia Edge, a detekcja po `pid:uchwyt` nie widzi nowego okna w działającym procesie), więc zera w niej nie są wynikiem; liczy się kolumna „down" i `action`. Wejście syntetyczne, jeden komputer (ALDER, B760). Czy to samo wyciszenie powoduje brak Home na UM760 z fizycznym pilotem, rozstrzyga przebieg użytkownika. Mechanizm po stronie systemu nie został ustalony (obserwacja, nie wyjaśnienie).

### Skutki dla AD-8 (wejście dla `bmad-architecture`)

- Hipotezę z S10 („wątek UI zajęty `WM_INPUT`") jest do wycofania, jeśli UM760 potwierdzi wynik z ALDER: osobny wątek Raw Input nie pomaga. Odnotować w Notes AD-8 jako `Source conflict:` wobec S10, jeśli decyzja zmieni architekturę.
- Kandydat na decyzję: nie rejestrować Raw Input klawiatury w procesie z hookiem; zostawić `WH_KEYBOARD_LL` plus HID Consumer. Koszt do rozstrzygnięcia: korelacja `source` dla klawiszy pilota przychodzących jako zwykła klawiatura (strzałki, OK z VID_1915&PID_1025) opiera się na `WM_INPUT` klawiatury, więc przy braku rejestracji `source` spada do `keyboard`, a wiersz `remote` w `input.json` nie dopasuje się dla tych klawiszy (Home z HID Consumer zachowuje korelację przez raport 0x0223). Architektura musi wybrać: (a) osobny proces pomocniczy z Raw Input klawiatury, (b) korelacja bez Raw Input klawiatury (np. wyłącznie czasowa po HID Consumer), (c) inny sposób wykrywania urządzenia.
- Nie testowano: `RIDEV_NOLEGACY`, `WM_APPCOMMAND`, filtr HID (punkt 5 listy) ani rejestracja klawiatury bez `RIDEV_INPUTSINK`; nie były potrzebne, bo wcześniejsze eksperymenty wskazały przyczynę na ALDER.

### Handoff na UM760 (całość epiki, użytkownik)

1. Zbudować Release (`dotnet build src/MediaStation.App -c Release`) na gałęzi `spike-3-11`.
2. Wariant bazowy: shell na wierzchu, 3× Home z pilota: spodziewane dziś 0 wpisów `action {kind: home}` i otwarty Edge (powtórzenie błędu), z `MS_INPUT_TRACE=1` brak `keyEnter`.
3. Wariant `MS_HUB_RAW_HID_ONLY=1` (kandydat), potem `MS_HUB_NO_RAWINPUT=1` (kontrola): 3× Home z pilota przy shellu na wierzchu → oczekiwana jedna `action {kind: home}` na naciśnięcie, zero nowych okien przeglądarki. Dodatkowo Back i OK z pilota (czy `source` jest `remote`, czy spadł do `keyboard`).
4. Logi `mediastation-*.jsonl` i `check-home-s10-*.log` z trzech przebiegów dołączyć do sekcji „Wynik" (AC 2). Akceptacja wyniku (`Decision:` z datą, AC 4) należy do użytkownika.
