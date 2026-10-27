---
id: 8
type: bug
title: "Moonlight po Home i powrocie stoi na „Uruchamianie input stream establishment…”"
parent: epic-windows-audio
covers: [R2]
after: []
assignee: ""
refined: true
hitl: false
risk: high
severity: P1
---

# Moonlight po Home i powrocie stoi na „Uruchamianie input stream establishment…”

## Description

Pierwsze OK na kafelku PC uruchamia strumień z hosta. Po Home (okno Moonlighta schowane na pasku) i ponownym OK na kafelku wraca okno Moonlighta, ale bez transmisji: widać tylko UI Moonlighta z zamrożonym komunikatem „Uruchamianie input stream establishment…”. Tak samo po ręcznym restarcie Moonlighta. Powinno być: po powrocie do kafelka PC strumień działa dalej, za każdym razem (R2, Done when 2). To jedyny powód, dla którego epika nie została zaakceptowana. Działający strumień po powrocie dowodzi się automatycznie (log Moonlighta albo zrzuty okna); wzrokowo potwierdza go ponowny odbiór na UM760 (S6).

## Reproduction

Środowisko: UM760 (2026-10-27, HEAD 3d29fee), MediaStation z `MEDIASTATION_DEV_HARNESS=1` i `MEDIASTATION_HARNESS_MOONLIGHT` wskazującym `Moonlight.exe`; kafelek `moonlight-pc` (Game, app `Desktop`) z hostem Sunshine 192.168.0.64, osiągalnym na porcie 47989.

1. OK na kafelku PC. Strumień pulpitu hosta startuje.
2. Home na pilocie. Ekran główny na wierzchu, okno Moonlighta zminimalizowane.
3. OK na kafelku PC.
4. Actual: okno Moonlighta na wierzchu, UI Moonlighta z zamrożonym „Uruchamianie input stream establishment…”, bez obrazu strumienia; powtarzalne w każdym cyklu, także po ręcznym restarcie Moonlighta. Expected: strumień pulpitu, jak po kroku 1.

Log z odbioru (S6, okno 11:02:30–11:03:27): `appWindowAttached appId=moonlight-pc` 1×, `ActivateWindow completed` 5× przy 4× Home, `MinimizeWindow completed` 4×, `ShowShell completed` 4×, `processExited` 0×, zero `warn`/`error`.

## Cause Hypothesis

Nie zdiagnozowano w odbiorze; nie wiadomo, czy przyczyna jest w kodzie epiki, czy w zachowaniu Moonlight-qt po minimalizacji. Do sprawdzenia, bez narzucania poprawki:

- (a) Odblokowanie foreground symulowanym Alt (`SendInput` ze znacznikiem, AD-8) trafia do Moonlighta, który przechwytuje klawiaturę w strumieniu.
- (b) Moonlight w trybie borderless z `--mute-on-focus-loss` przy minimalizacji okna zrywa albo odtwarza sesję i po przywróceniu wraca na etap łączenia.
- (c) Ponowne OK uruchamia nowy proces `Moonlight.exe stream …`, który zaczyna drugie połączenie z hostem z żywą sesją; wskazuje na to nadmiarowe `ActivateWindow` (5 przy 4 Home).

## Acceptance Criteria

1. **Strumień działa po powrocie**
   **Given** dev harness, kafelek PC i osiągalny host Sunshine
   **When** po pierwszym OK na kafelku PC przebieg cztery razy robi Home, potem OK na kafelku PC (wstrzyknięte klawisze, jak `check-home-s10.ps1`)
   **Then** po każdym powrocie strumień działa: log Moonlighta pokazuje, że połączenie przeszło etap „input stream establishment” i nie zostało zerwane, a gdy log tego nie pokazuje, dwa zrzuty okna Moonlighta w odstępie kilku sekund po powrocie się różnią (zapisane w planie); log MediaStation każdego cyklu ma `MinimizeWindow` i `ShowShell` po Home oraz `ActivateWindow` po OK, wszystkie `completed`; `appWindowAttached` dokładnie 1× w całym przebiegu i bez `warn`/`error` (dozwolone jak w S6: `appIconMissing`, `devHarnessEnabled`)
2. **Opuszczone okno Moonlighta zostaje zminimalizowane**
   **Given** strumień schowany przez Home
   **When** sprawdzamy jego okno
   **Then** `IsIconic` i `IsWindowVisible` są prawdziwe (R1); poprawka nie ukrywa okna i nie zamyka Moonlighta
3. **Testy pokrywają przyczynę**
   **Given** zestaw testów
   **When** się wykonuje
   **Then** test, który nie przechodzi na defekcie i przechodzi po poprawce, pokrywa znalezioną przyczynę (w części poza Win32), a jeden test pokrywa każdy powiązany przypadek, którego poprawka dotknęła
4. **Albo: przyczyna jest poza kodem epiki albo zmiana nie jest potrzebna, z dowodem**
   **Given** reprodukcja
   **When** diagnoza pokazuje, że Moonlight sam zrywa strumień przy minimalizacji albo przy utracie fokusu, albo reprodukcja na bieżącym kodzie nie występuje
   **Then** w pierwszym przypadku w Notes epiki jest `Source conflict:` z dowodem (log Moonlighta, przebieg z i bez poszczególnych kroków), a decyzja trafia do `bmad-architecture`; w drugim dowód (przebieg z kryterium 1) jest w planie; to zastępuje 1–3

## References

- parent — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio.md, R1, R2, Done when 2
- acceptance — _bmad-output/initiative-mediastation-mvp/tickets/epic-windows-audio/epic-windows-audio-um760-acceptance.md, Wyniki S6, Uwagi użytkownika U1
- ledger — _bmad-output/initiative-mediastation-mvp/deferred-work.md, DW-43
- architecture — _bmad-output/initiative-mediastation-mvp/architecture-mediastation/architecture-mediastation.md, AD-5 (Moonlight borderless, `--mute-on-focus-loss`), AD-8 (znacznik `SendInput`)

## Notes

- Decision: `hitl: false`, żeby bug szedł w pętli: działający strumień dowodzi log Moonlighta albo różniące się zrzuty okna, a wzrokowe potwierdzenie przechodzi do ponownego odbioru S6 na UM760 (AGENTS.md: najmocniejszy dowód automatyczny); zastępuje `hitl: true` z tego samego dnia (2026-10-27).
- Open question: gdzie Moonlight-qt zapisuje log na Windows i czy loguje etapy połączenia; ustala pierwszy krok buildu.
- Decision: poprawka nie może odejść od minimalizacji (R1, AD-5); jeśli tylko to naprawia strumień, build zatrzymuje się na kryterium 4 (2026-10-27).
- Risk high: blokuje akceptację epiki (Done when 2).
- Check beyond criteria (risk high): po poprawce Home nad Edge i Chrome dalej 4/4 (S2, S4), bo odblokowanie foreground jest wspólne.
- Decision: host Sunshine 192.168.0.64 jest dostępny na maszynie budowy, więc reprodukcja i kryterium 1 idą na żywym strumieniu; potwierdzenie użytkownika (2026-10-27).
