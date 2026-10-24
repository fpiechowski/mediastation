---
epic: epic-input
type: tooling-dryrun-report
machine: UM760
head: ac448ad
date: 2026-10-24
---

# Próba narzędzi fp-acceptance na UM760 (2026-10-24, HEAD ac448ad)

To **nie jest odbiór epiki**. Próba skilla `fp-acceptance` w trybie run, na kopii planu (`epic-input-um760-acceptance-dryrun-copy.md` obok), tylko P0 i S1. Plan w repo (`epic-input-um760-acceptance.md`) nie został zmieniony. Kodu i skilla nie poprawiano; tylko obserwacje.

## Co działało

| Obszar | Obserwacja |
|---|---|
| uv | `resolve_customization.py` i wszystkie komendy `acceptance.py` (`next`, `new-pass`, `meta`, `fill`, `check`, `close`, `focus`, `foreground`, `launch`, `wait`, `say`, `overlay`) wykonały się; `check` zwrócił exit 1 tylko dla nieprzechodzącej asercji. uv 0.13.5. |
| Build | Zwykły `dotnet publish src/MediaStation.App -c Release -r win-x64 --self-contained true`: exit 0 w ok. 11 s, bez blokady Application Control i bez obejścia `npm run build`. `MediaStation.exe` 162304 B. `git status` po buildzie czysty. |
| Asercja wersji | `start` miał `data.version` `1.0.0+ac448addd06d49f054b9ebaa6d739c0005ab9845`; `msg=start,data.version~=ac448ad` spełnione. |
| P0 | HEAD ac448ad ≥ 55c0dfe; config w `%LOCALAPPDATA%\MediaStation\config` identyczny z `tools/samples/config` (apps, input, settings); dongle VID_1915&PID_1025 obecny; `check-home-s10.ps1 -SelfTest` exit 0 "Self-test passed."; Edge i `Moonlight.exe` w Program Files. |
| launch / focus / foreground | `launch --ready msg=snapshotPublished` pid 4616, mark `2026-10-24T19:11:59.718Z`; `focus --pid` `foreground: true`; `foreground --pid` `is_pid: true` przed i po `wait`. |
| Overlay | `overlay start` pid 5412; `wait --say --beep --countdown 3` zwrócił `cue {captioned, spoken, beep: true}`; shell po napisie dalej `is_pid: true` (fokus niezabrany wg skryptu; nie sprawdzano wzrokiem). |
| Głos pl-PL | `say --voice pl-PL` `spoken: true` (słyszalność do potwierdzenia przez użytkownika). |
| Zamknięcie | `close --pid 4616`: `exited true, exit_code 0`; log `stop {exitCode 0}` 19:15:17Z. W oknie 0 wpisów `warn`/`error`, 0 `inputHubError`. |
| check, fill (na kopii) | Wiersze P0 i S1 zapisane; status kopii przechodził `aborted` → `approved` → `in-progress` → `aborted`. |

## Co nie działało

### Wykrycie OK z pilota w S1 (dodatkowy test)

- `wait --for "msg=action,data.kind=ok" --say ... --voice pl-PL --beep --countdown 3 --timeout 60` → `found: false`, `waited_s: 60.9`, `missing: ["msg=action,data.kind=ok"]`. Okno przepadło (użytkownik kliknął kilka razy niechcący).
- Po poprawnym jednym naciśnięciu OK (`wait --since 2026-10-24T19:11:59.718Z --timeout 5`) → `found: false`, `seen` tylko pięć wpisów startu (`start`, `managedShell`, `inputRouting {mode shell, pointer blocked, epoch 1}`, `powerSignalsRegistered`, `snapshotPublished {rev 1}`).
- Shell był przez cały czas na pierwszym planie, proces żył.
- `check --expect "msg=action,data.kind=ok:1"` → `count 0, ok false`, `verdict: fail`. Pozostałe asercje (start, wersja, `inputRouting`, `powerSignalsRegistered`, `snapshotPublished`, `inputHubError:0`, `stop exitCode 0`, brak `warn`/`error`) przeszły.
- Wiersz S1 w kopii: fail, obserwowany. **Przyczyny nie diagnozowano.** Wygląda jak ta sama obserwacja co S2 i spike 3.11 (hook LL milczy przy shellu na wierzchu), ale to przypuszczenie.

### Inne uwagi

- `acceptance.py next` zgłasza problem: tabela scenariuszy ma 5 kolumn, bez Actor i Assertion; trzeba je wyprowadzać przy każdym ogłoszeniu.
- Nieznane: czy nakładka widoczna była na pełnym ekranie shella (nie sprawdzano screenshotem) i czy sygnały dźwiękowe były słyszalne (skrypt tylko zgłosił `beep: true`).
- Konsola PowerShell 5.1 wyświetla polskie znaki jako krzaki (kodowanie wyjścia); pliki na dysku są poprawne.
- Jedno wywołanie `wait` zostało odrzucone przez użytkownika, bo nacisnął OK w trakcie; skorzystano z przeszukania logu wstecz od pierwotnego `mark`.

## Co dalej

1. `bmad-build` / `bmad-architecture`: brak `action` z pilota przy shellu na wierzchu (powiązane z S2 FAIL i spike 3.11).
2. Powtórzyć S1 z testem pilota po poprawce.
3. Ewentualnie poprawki skilla: Actor/Assertion w tabeli planu; `wait` nie odróżnia „nie nacisnął" od „log milczy".
