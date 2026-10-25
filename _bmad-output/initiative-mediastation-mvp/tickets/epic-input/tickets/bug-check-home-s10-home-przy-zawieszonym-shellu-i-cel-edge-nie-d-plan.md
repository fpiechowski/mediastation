---
title: 'check-home-s10: Home przy zawieszonym shellu i cel Edge nie dają 16/16 na UM760'
type: 'bugfix'
ticket: '13'
created: '2026-10-25'
status: blocked
operator_actions:
  - "Na UM760 otwórz Moonlight (borderless) i zostaw okno Edge albo pozwól skryptowi otworzyć about:blank, potem uruchom `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1` i po każdym sygnale naciśnij Home na pilocie G20S Pro (dongle USB 2.4G); oczekiwane 16/16, 4/4 i exit 0."
  - "Powtórz ten sam przebieg drugi raz (osobne uruchomienie, ten sam build) i zapisz oba wyniki z logami `check-home-s10-*.log` w notatce odbioru epiki."
  - "Jeśli shell-hung nadal pokazuje `DWM ghost over the shell` albo `browser window`, zapisz log skryptu i log aplikacji i zgłoś to do decyzji `bmad-architecture` (uprawnienia hooka LL, AD-8)."
baseline_revision: '3d9830df65ceae3961d9b9eec6098963292f277c'
route: 'oneshot'
route_source: 'auto'
risk: 'high'
review: 'thorough'
review_source: 'pinned'
lenses_ran: ['blind-hunter', 'edge-case-hunter', 'verification-gap', 'intent-alignment']
review_loop_iteration: 0
followup_review_recommended: false
context: []
warnings: []
deferred:
  - summary: >-
      Zawieszony shell zasłonięty oknem-duchem `dwm` nie dostaje Home od hooka LL (brak `keyEnter`/`action` w logu aplikacji), więc Home otwiera przeglądarkę.
    evidence: |-
      Logi przebiegów 1 i 2 z 2026-10-25 (brak wpisów w oknach shell-hung #1 i #2, na wierzchu `Ghost` procesu `dwm`) oraz sonda na UM760: z duchem na wierzchu wstrzyknięte Enter nie dało `keyEnter`, bez ducha dało. Wstrzyknięcie mogła zablokować UIPI, więc dla fizycznego pilota rozstrzygnie dopiero przebieg użytkownika; ewentualna poprawka to zmiana uprawnień hooka (AD-8).
    location: >-
      src/MediaStation.App/Platform/Input/InputHub.cs (hook LL)
    severity: medium (unverified)
  - summary: >-
      Domyślny cel Edge to pierwsze okno `msedge` z tytułem, więc może to być obce okno użytkownika (np. DevTools, PWA).
    evidence: |-
      `Select-EdgeWindow` nie odróżnia okien tego samego procesu; zawężenie daje `-EdgeTitle`. Skutek: Home wysyłany nad niewłaściwym oknem Edge, wynik nadal mierzy log aplikacji.
    location: >-
      tools/check-home-s10.ps1 Select-EdgeWindow
    severity: low
blocked_at: "2026-10-25"
blocked_reason: "awaiting operator: Na UM760 otwórz Moonlight (borderless) i zostaw okno Edge albo pozwól skryptowi otworzyć about:blank, potem uruchom `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1` i po każdym sygnale naciśnij Home na pilocie G20S Pro (dongle USB 2.4G); oczekiwane 16/16, 4/4 i exit 0.; Powtórz ten sam przebieg drugi raz (osobne uruchomienie, ten sam build) i zapisz oba wyniki z logami `check-home-s10-*.log` w notatce odbioru epiki.; Jeśli shell-hung nadal pokazuje `DWM ghost over the shell` albo `browser window`, zapisz log skryptu i log aplikacji i zgłoś to do decyzji `bmad-architecture` (uprawnienia hooka LL, AD-8)."
---

<intent-contract>

## Intent

**Problem:** `tools/check-home-s10.ps1` daje na UM760 4/16 i 8/16, więc Done when 3 epiki nie jest spełnione. Diagnoza z logów (maszyna UM760, build 1.0.0+2bb3c95): (1) Edge `no target window` to skrypt, bo cel Edge szuka okna z tytułem `Netflix`; (2) shell-hung `browser window` i (3) `target not foreground` to skutek kolejności celów w skrypcie: krok `desktop` (MinimizeAll) zostawia zawieszony shell na ~30 s w tle, Windows podstawia wtedy okno-ducha `Ghost` procesu `dwm`, a skrypt uznaje je za „zawieszony shell na wierzchu”; przy duchu na wierzchu hook LL nie dostaje klawiszy (w logu aplikacji zero `keyEnter`/`foregroundGate`/`action`), Home trafia do Windows i otwiera Edge, a okno dialogowe `#32770` przejmuje foreground w naciśnięciach 3 i 4.

**Approach:** Skrypt przygotowuje okno Edge (dowolne okno `msedge` klasy `Chrome_WidgetWin_*` z tytułem, opcjonalnie `-EdgeTitle`; gdy brak, otwiera `about:blank`), uruchamia `shell-hung` pierwszy i `desktop` ostatni, a okno-ducha nad zawieszonym shellem liczy jako porażkę z nazwanym powodem zamiast jako sukces ustawienia. Produkt bez zmian.

</intent-contract>

## Implementation Notes

Oneshot: jedna zmiana w jednym pliku (`tools/check-home-s10.ps1`, około 80 linii), bez zmiany produktu ani kontraktów; diagnoza niżej jest dowodem do AC1.

**Diagnoza (AC1), UM760, logi `check-home-s10-20261025-184223.log` i `-184645.log` + `mediastation-2026-10-25.jsonl`:**
- Edge `no target window` × 4 w obu przebiegach: skrypt. Porażka w 4 ms, w logu aplikacji nie ma żadnego wpisu z tego okna; `TargetWindow` szukał `msedge` z tytułem `Netflix` (`check-home-s10.ps1:607`), a okno Edge użytkownika miało inny tytuł. Moonlight i pulpit z tym samym mechanizmem Home przeszły 4/4.
- Shell-hung `browser window` × 2: wymóg skryptu, nie Home. W oknie pomiaru 18:42:47–18:43:34 log aplikacji nie ma żadnego wpisu (ani `foregroundGate`, ani `action`), a skrypt zobaczył 2 nowe okna `msedge` (pid 9192); na wierzchu było `Ghost` procesu `dwm` (pid 21472), nie okno shella. Wyjaśnienie: hook LL nie dostaje klawiszy, gdy na wierzchu jest okno-duch procesu systemowego. Dowód uzupełniający na UM760: z duchem na wierzchu wstrzyknięte `Enter` ×3 nie dały żadnego `keyEnter` w logu (`MS_INPUT_TRACE=1`), a ten sam kod bez ducha (zawieszony shell na wierzchu, 8 s, 27 s i 28 s zawieszenia) dał `keyEnter`, `foregroundGate {managed: true}` i `action` przy każdym naciśnięciu. Ograniczenie: wstrzyknięcie z procesu o niższym poziomie integralności może blokować UIPI, więc dowód dla fizycznego pilota daje dopiero przebieg użytkownika.
- Shell-hung `target not foreground` × 2: skrypt (skutek). Po dwóch niepołkniętych Home na wierzchu było okno dialogowe `#32770` obcego procesu (pid 12172 / 10060), nie okno shella.
- Ghost powstaje od kroku `desktop`: w `ghostprobe` z `-Targets desktop,shell-hung` pierwszy odczyt foreground to `Ghost` (pid `dwm`); w `-Targets shell-hung` samym (bez `desktop`) foreground to prawdziwe okno shella i hook widzi klawisze.
- Produkt: bez zmiany. Zostaje ryzyko poza zakresem: zawieszony shell, który zostanie zasłonięty duchem (np. długo po minimalizacji), nie dostaje Home od hooka bez podniesienia uprawnień; zmiana AD-8 (uprawnienia hooka) wymaga decyzji `bmad-architecture`.

## Verification

**Commands:**
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -SelfTest` -- expected: exit 0, w tym przypadki wyboru okna Edge, kolejności celów i ducha `dwm`.
- `dotnet build MediaStation.slnx -c Release` i `dotnet test MediaStation.slnx -c Release` -- expected: bez błędów i ostrzeżeń, wszystkie testy zielone.
- `powershell -NoProfile -ExecutionPolicy Bypass -File tools/check-home-s10.ps1 -Targets shell-hung,desktop -Presses 1 -PressTimeoutSeconds 5 -SkipShellTopPass` bez pilota -- expected: shell-hung dochodzi do sygnału dźwiękowego z prawdziwym oknem shella na wierzchu (nie `Ghost`), kod 1 tylko z powodu braku `action`.

**Manual checks (operator, UM760):**
- `tools/check-home-s10.ps1` z pilotem na donglu: 16/16 i 4/4, exit 0, w co najmniej dwóch uruchomieniach (AC2).

## Review Triage Log

### 2026-10-25 — Review pass
- verdicts: 25 findings — high 0, medium 4, low 7, false 2, maybe-false 2, informational 10 (blind-hunter 6 grouped, edge-case-hunter 9, verification-gap 2, intent-alignment 6 notes)
- findings:
  - `[medium]` `[patch]` Kolejność celów nie jest wymuszana dla własnego `-Targets` (desktop przed shell-hung odtwarza błąd) — `Initialize-Targets` ostrzega `WARN`; sprawdzone na UM760: `-Targets desktop,shell-hung` daje `Ghost` i `target not foreground (DWM ghost over the shell)`.
  - `[medium]` `[patch]` `Start-Process msedge` pod `ErrorActionPreference = Stop` przerywa cały przebieg — owinięte w try/catch z `WARN`, press Edge zostaje porażką z powodem.
  - `[low]` `[patch]` Powód ducha znał tylko `shell-hung` i nie miał asercji — `Get-NotForegroundReason` (czysta funkcja) obejmuje też `shell-top`, cztery przypadki w `-SelfTest`.
  - `[low]` `[patch]` Okna z przywracania sesji Edge po `Wait-For` mogły trafić do pomiaru — 2 s ustatkowania po uruchomieniu okna.
  - `[low]` `[patch]` `-EdgeTitle` to regex, a pomoc tego nie mówiła — dopisane w nagłówku.
  - `[medium]` `[defer]` Duch `dwm` nad zawieszonym shellem poza kontrolą skryptu (blind-hunter, edge-case-hunter: brak odzyskania/retry) i diagnoza oparta na wstrzykniętym wejściu (intent-alignment 2) — produkt/AD-8, w `deferred` jako niezweryfikowane.
  - `[low]` `[defer]` Domyślny cel Edge może być obcym oknem — `-EdgeTitle` jest obejściem, w `deferred`.
  - `[low]` `[reject]` Brak testów gałęzi `TargetWindow` 'edge' i `Initialize-Targets` (verification-gap, blind-hunter) — wymagałoby wstrzykiwania dostawcy okien w skrypcie jednorazowego użytku; funkcja wyboru okna ma testy, a przebieg UM760 jest weryfikacją przypięcia.
  - `[low]` `[reject]` Fail-fast przy braku Moonlight/Edge (blind-hunter, edge-case-hunter) — celowo: plan i ticket chcą porażki z powodem `no target window` i procedury w opisie, `WARN` wyprzedza przebieg.
  - `[false]` `[reject]` Zminimalizowane okno Edge jest wybierane i nie wraca (verification-gap, edge-case-hunter) — `Set-FrontWindow` robi `ShowWindow(9)` dla `IsIconic`, a zminimalizowane okna są `IsWindowVisible`.
  - `[false]` `[reject]` Puste okno `about:blank` bez tytułu (blind-hunter, edge-case-hunter) — sprawdzone na UM760: okno z `about:blank` zostało znalezione w ułamku sekundy, a ramka Chromium ma zawsze tytuł karty.
  - `[maybe-false]` `[reject]` Wybór najwyższego okna Edge nie jest stabilny między pressami — handle jest wybierany przed każdym pressem, a środowisko UM760 ma jedno okno; rozstrzygnie przebieg użytkownika (poniżej progu `medium`).
  - `[maybe-false]` `[reject]` Narzędzie asercji „bez zmiany asercji po fakcie” (intent-alignment 6) — zmiana reguły ducha i tytułu Edge jest przed przebiegiem po poprawce, nie po nim; nazwana w Intent.
  - `[low]` `[reject]` Nieaktualne `check-home-s10.ps1:607` i „około 80 linii” w Implementation Notes, brak definicji AC w ciele planu (blind-hunter) — plan jest dokumentem diagnozy; zmiana planu nie jest naprawą kodu, a numery linii dotyczą stanu sprzed poprawki.
  - `[low]` `[reject]` Notatki intent-alignment (czytania A–D, dowód AC2 u operatora, Moonlight poza skryptem) — opisowe, zgodne z planem; AC2 przechodzi do `operator_actions`.

## Auto Run Result

**Status:** awaiting-operator

**Summary:** Diagnoza (UM760, build 1.0.0+2bb3c95): Edge `no target window` to skrypt (hardcodowany tytuł `Netflix`); shell-hung `browser window` i `target not foreground` to skutek kolejności celów w skrypcie, która stawia nad zawieszonym shellem okno-ducha `dwm` (po kroku `desktop`/MinimizeAll), a skrypt uznawał je za shell na wierzchu; przy duchu hook LL nie dostaje klawiszy. Produkt bez zmian. Skrypt: `Select-EdgeWindow` (dowolne okno Edge z tytułem, `-EdgeTitle`, przygotowanie okna `about:blank`), domyślna kolejność `shell-hung, edge, moonlight, desktop`, duch `dwm` jako nazwana porażka, ostrzeżenie o złej kolejności i o braku Moonlight.

**Files changed:**
- `tools/check-home-s10.ps1` — wybór okna Edge, kolejność celów, duch jako porażka z powodem, `Initialize-Targets`, nowe przypadki `-SelfTest`.
- `_bmad-output/initiative-mediastation-mvp/tickets/epic-input/tickets/bug-check-home-s10-home-przy-zawieszonym-shellu-i-cel-edge-nie-d-plan.md` — ten plan (diagnoza, triage, wynik).

**Review findings:** patches 5 (medium 2, low 3); deferred 2 (medium unverified: duch nad shellem i hook LL; low: obce okno Edge); rejected 8 z powodami w Review Triage Log.

**Follow-up review recommended:** false — żaden patch `high`, dwa `medium` poprawione.

**Verification (UM760, 2026-10-25, branch master):** `tools/check-home-s10.ps1 -SelfTest` — exit 0 (w tym przypadki Edge, kolejności i ducha); `dotnet build MediaStation.slnx -c Release` — 0 ostrzeżeń, 0 błędów; `dotnet test` — 899/899; przebieg bez pilota `-Targets shell-hung,edge,desktop`: shell-hung i Edge z prawdziwym oknem na wierzchu (nie `Ghost`), tylko `no action`; `-Targets desktop,shell-hung`: odtworzony `Ghost` i nazwana porażka. AC2 (16/16 i 4/4 w dwóch uruchomieniach z pilotem) wymaga fizycznego G20S Pro — `operator_actions`.

**Residual risks:** diagnoza „hook nie widzi klawiszy przy duchu” opiera się na logach fizycznego pilota z przebiegów 1 i 2 oraz na wstrzykniętym wejściu (UIPI mogło je zablokować); jeśli ghost pojawi się mimo nowej kolejności, wymaga to decyzji `bmad-architecture` o uprawnieniach hooka (AD-8). Okno Moonlight musi przygotować operator.
