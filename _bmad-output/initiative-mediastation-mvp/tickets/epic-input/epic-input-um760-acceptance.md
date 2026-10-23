---
epic: epic-input
type: acceptance-plan
machine: UM760
base_head: 55c0dfe
created: 2026-10-23
status: planned
---

# Odbiór epiki 3 (epic-input) na UM760 — plan i scenariusz

Całościowy test UM760 przy `epic-boundary` (AGENTS.md). Zakres to to, czego ALDER nie pokaże: na ALDER wstrzyknięty input nie dociera do hooka LL i nie ma pilota ani pada, więc **wszystkie obserwacje sprzętowe** są tutaj. Mechanizmy pokrywa 865/865 testów i przebiegi retro (`epic-input-retrospective.md`, sekcja „Behavior verification”).

## Źródła

- Done when 1–5 epiki (`epic-input.md`) i lista „Odbiór na UM760”.
- Retro: F8 (naprawione w `19c5038`), Q4 (DW-27), werdykt maszynowy `accepted-with-open-items`.
- Ledger: DW-10 (realny `SendInput`), DW-13/14/15 (S10 z Edge i Moonlightem), DW-17/22 (epic-osk, poza odbiorem).

## Przygotowanie (P0)

| # | Krok | Oczekiwane |
|---|------|------------|
| P0.1 | `git pull`, HEAD ≥ `55c0dfe` | `git rev-parse --short HEAD` |
| P0.2 | `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true` | sukces; ścieżka `src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe` |
| P0.3 | Config w `%LOCALAPPDATA%\MediaStation\config` z `tools/samples/config`; **dongle USB 2.4G pilota G20S Pro podpięty**, Bluetooth niepotrzebny | pliki na miejscu |
| P0.4 | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` | `Self-test passed`, exit 0 |
| P0.5 | Edge zainstalowany; Moonlight (borderless) zainstalowany do celu `moonlight` | okna do uruchomienia przez skrypt |

Jeśli `Set-ExecutionPolicy` blokuje skrypty: `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` (jak przy epice 2).

## Scenariusze

Log: `%LOCALAPPDATA%\MediaStation\logs\mediastation-<data>.jsonl`. „Dobry log” = wpisy `start`, `inputRouting {mode shell, epoch 1}`, `powerSignalsRegistered`, `snapshotPublished`, potem tylko `action` z naciśnięć i `stop {exitCode 0}`; zero `inputHubError`, `warn`, `error`.

| # | Scenariusz | Oczekiwane | Rodzaj | Done when |
|---|------------|------------|--------|-----------|
| S1 | Start `MediaStation.exe` (bez argumentów) | dobry log, okno shella widoczne, brak `inputHubError` (oba hooki, Raw Input i XInput zarejestrowane) | pass/fail | 4 |
| S2 | Po story 2: w `shell` naciśnij kolejno Home, Back, OK, strzałki (po jednym razie) z pilota | dokładnie jeden wpis `action` na naciśnięcie; Home i Back z `source: remote`; log bez `vk`/`scan`/`usage` | pass/fail | 1, 4 |
| S3 | Długie OK (trzymaj ≈1 s) w `shell` | samo `long`, bez `press` | pass/fail | 2 |
| S4 | Krótkie OK (<0,6 s) w `shell` | `press` na key-up, bez `long` | pass/fail | 2 |
| S5 | Trzymana strzałka (≈2 s) | `press`, potem kolejne `repeat`; po puszczeniu stop | pass/fail | 1 (wyjątek) |
| S6 | Home nad pulpitem (Windows), przy zamkniętym Edge | `action home`, **żadne** nowe okno przeglądarki | pass/fail | 3 |
| S7 | Kalibracja: zamknij aplikację, uruchom `MediaStation.exe --calibrate-input`, naciśnij każdy przycisk pilota po razie, potem zwykłą klawiaturę | `rawInput` z `hasVk` dla przycisków pilota; **zwykła klawiatura bez wpisów**; kolejność hooka i `WM_INPUT` zalogowana; proces kończy się sam (exit 0 po ~60 s) | pass/fail + pomiar | 4, 5 |
| S8 | Wybór przycisku Klawiatura: z S7 wybierz przycisk z `hasVk: true`, wpisz jego VK jako `"keyboard"` do `%LOCALAPPDATA%\MediaStation\config\input.json` **i** do `tools/samples/config/input.json`, uruchom bez `--calibrate-input` i naciśnij go w `shell` | `action {kind: keyboard}`; jeśli żaden przycisk z VK nie pasuje, zapisz Source conflict do architektury (decyzja 2026-10-21) | pass/fail + wybór | 5 |
| S9 | S10: `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1` i po sygnale dźwiękowym naciskaj Home na pilocie (nic więcej nie dotykaj) | przebieg 1: **16/16** (edge, moonlight, desktop, shell-hung × 4); przebieg 2 „shell na wierzchu”: 4/4 `action home`; exit 0 | pass/fail + wynik | 3 |
| S10 | Home nad Moonlightem borderless po S9 (ręcznie, 1×) | `action home`, Moonlight nie dostaje Home | pass/fail | 3 |
| S11 | D-pad i OK w `app` + `moonlight`/`native` (jeśli host jest dostępny; jeśli nie — nie dotyczy) | **brak** wpisu `action`, klawisz dociera do aplikacji (cel pass-through) | obserwacja | 1 (wyjątek) |
| S12 | Pad (XInput): Y → `keyboard`, D-pad/A, kombinacja `gamepadHome` | po jednym `action` na przycisk, kombinacja daje jeden `home` (`source: gamepad`) | pass/fail jeśli pad jest, inaczej nie dotyczy | 1 |
| S13 | Kursor żyroskopu pilota w `shell` | kursor stoi (pointer `blocked`) | pass/fail | — |
| S14 | Zamknięcie okna aplikacji | `stop {exitCode 0}`; w całym przebiegu 0 `warn`/`error` | pass/fail | 4 |

**Wynik S9 szczegółowo:** zapisz, które naciśnięcia odpadły i z jakim werdyktem (`no action`, `double action`, `browser window`, `target not foreground`). To rozstrzyga DW-13/14/15 (wielookienność Edge, foreground po uchwycie, okna z `GW_OWNER`). Skrypt zapisuje log przebiegu do `%LOCALAPPDATA%\MediaStation\logs\check-home-s10-<czas>.log`; dołącz go do wyników.

**Otwarte pytania epiki do rozstrzygnięcia na odbiorze:**
- *Który przycisk G20S Pro jest Klawiaturą* — S7/S8.
- *Czy hook LL w swoim wątku dostaje „down” Home i Back także gdy MediaStation jest na wierzchu* — przebieg 2 z S9 (shell na wierzchu, niezawieszony).
- *Kolejność hooka i `WM_INPUT`* — zapis z S7 (czy połknięty klawisz dochodzi do Raw Input).

## Uwagi

- **Kalibracja nic nie loguje:** `--calibrate-input` filtruje po sygnaturach z `input.json` (DW-27). Jeśli nie ma żadnego `rawInput`, sprawdź VID/PID dongla w `spike/spike-results.md` (S10) i dopisz sygnaturę `remote` do `input.json`; potem powtórz S7. To nie jest porażka epiki, ale zapisz wynik.
- **Zawieszony Home po utraconym key-up (F8):** naprawione w `19c5038` dla Home, Back, OK i Keyboard; na odbiorze nie ma osobnego scenariusza, bo wymaga utraty key-up w systemie. Jeśli Home kiedykolwiek przestanie reagować, zapisz czas i log (DW-6).
- **Nie sprawdzamy tu:** OSK, `clipHwnd` w użyciu, bramkowania `Keyboard` po `textFocus` (DW-17, DW-22 — epic-osk), realnego `SendInput` przez hook (DW-10 — wymaga hosta Moonlight/OSK, jeśli wyjdzie przy S10 — odnotuj jako obserwację).

## Kryterium odbioru

Epika jest zaakceptowana, gdy S1, S2, S3, S4, S9 (16/16 i przebieg 2), S8 (wybrany przycisk) i S14 przechodzą. S5, S6 i S10 muszą przejść lub dać wyjaśnioną różnicę. S11, S12 i S13 to obserwacje (nie dotyczy, gdy brakuje hosta lub pada). Jeśli S7/S8 pokażą, że żaden przycisk z VK nie pasuje, to nie jest porażka — dopisujemy Source conflict do architektury i zamykamy epikę z tą decyzją.

## Wyniki

Wypełniamy w trakcie (data, HEAD, maszyna UM760):

| # | Wynik | Dowód |
|---|-------|-------|
| P0 | | |
| S1 | | |
| S2 | | |
| S3 | | |
| S4 | | |
| S5 | | |
| S6 | | |
| S7 | | |
| S8 | | |
| S9 | | |
| S10 | | |
| S11 | | |
| S12 | | |
| S13 | | |
| S14 | | |

**Pomiar / decyzje:** (wybrany przycisk Klawiatura, odpowiedź na pytanie o „down” hooka, kolejność hooka i `WM_INPUT`)
