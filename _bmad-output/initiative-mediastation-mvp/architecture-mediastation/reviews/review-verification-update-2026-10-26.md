---
title: 'Review: weryfikacja aktualizacji AD-8, AD-11, AD-15, AD-16 i Stack (2026-10-26)'
target: ../architecture-mediastation.md
decision-log: ../.memlog.md
date: 2026-10-26
lenses: [verification]
scope: niezacommitowany diff spine na HEAD 931568a (AppMenu w Shell, Action do Shell, ikony przez drugi virtual host, guard mostu, eksport schematów w przebiegu testów, Vitest jako jedyny runner web, linkowane źródła App w testach)
machine: UM760 (COMPUTERNAME MEDIASTATION, płyta F7BSI, model EliteMini Series)
---

# Review: weryfikacja aktualizacji AD-8, AD-11, AD-15, AD-16 i Stack

**Werdykt:** API WebView2, na których opiera się delta, zachowują się tak, jak pisze spine. Składnia filtra działa z xunit.v3 4.0.2 na Microsoft.Testing.Platform, a Vitest 5.0.5 pasuje do stacku. Do poprawy przed implementacją są dwie rzeczy. Pierwsza to łańcuch `BuildWeb` → npm → `dotnet test`: to zagnieżdżony, nieskoordynowany build tych samych projektów i grozi blokadami plików. Druga to uzasadnienie DW-28, które trzeba złagodzić. Na tej maszynie Smart App Control dziś zablokował świeży `MediaStation.exe`, a projekt testów sam jest świeżym, niepodpisanym exe. Trzy mniejsze punkty to doprecyzowania treści.

Źródła odczytane 2026-10-26. „Lokalnie” = komendy na UM760 (SDK 10.0.402, HEAD 931568a). Spine nie był edytowany.

## Podsumowanie

| # | Status | Punkt | Wniosek |
| --- | --- | --- | --- |
| V1 | confirmed | AD-11: ikony przez `SetVirtualHostNameToFolderMapping` + `DenyCors` | `<img>` z innego origin: Allow; `fetch`/XHR: Deny; jeden virtual host na folder jest zalecany |
| V2 | confirmed | AD-11: Debug `http://localhost:5173` ładuje `https://icons…` | HTTP → HTTPS to nie mixed content; localhost i tak jest secure context |
| V3 | caveat (low) | AD-11 Prevents: „plik z `config/icons/` wykonany w origin shella” | `DenyCors` przepuszcza też `<script src>` i `<iframe src>`, nie tylko `<img>`; ochronę daje dopiero guard nawigacji i CSP |
| V4 | confirmed | AD-16: `e.Source` jako guard origin | `Source` = „URI of the document that sent this web message”; `CoreWebView2.WebMessageReceived` dostaje tylko dokument najwyższego poziomu |
| V5 | confirmed + caveat | AD-16: `NavigationStarting`/`FrameNavigationStarting`, `NewWindowRequested`, `OperationCanceled` | Wszystko potwierdzone; `srcdoc` w ramce nie da się anulować; anulowanie ramki raportuje zdarzenie ramki, nie główne `NavigationCompleted` |
| V6 | caveat (medium) | AD-16: `action` „w kolejności i każda dokładnie raz” | `PostWebMessageAsJson` gubi wiadomość, gdy przed doręczeniem nastąpi nawigacja; brak kolejności między `action` a snapshotem z latest-wins slotu |
| V7 | confirmed | AD-16: `dotnet test tests/MediaStation.Core.Tests --filter Category=SchemaExport` | Działa lokalnie (MTP, xunit.v3 4.0.2): `--filter` = składnia VSTest; zero dopasowań → kod 8 |
| V8 | **problem (high)** | AD-16/AD-15: `BuildWeb` → `npm run build` → `npm run contracts` → `dotnet test` | Zagnieżdżony, nieskoordynowany build Core i Core.Tests w osobnym procesie; ryzyko blokad plików przy buildzie i teście solucji |
| V9 | caveat (medium) | AD-16: „Nie ma osobnego exe ani dll eksportera … blokada eksportu = widoczna blokada testów” | Kierunek dobry, ale klasa problemu zostaje: Core.Tests to świeży, niepodpisany **exe**; dziś (3077, polityka SAC) zablokowany był świeży `MediaStation.exe` |
| V10 | confirmed + caveat (low) | Stack: Vitest 5.0.5 | `latest` = 5.0.5; peer `vite ^6.4 \|\| ^7 \|\| ^8`; Node `^24` pasuje do `engines >=24.22.0`; brak pinu środowiska DOM i biblioteki testów React |

## V1. Ikony przez drugi virtual host: confirmed

- `CoreWebView2HostResourceAccessKind.DenyCors`: tabela w dokumentacji mówi, że dostęp „From DOM like src of img, script or iframe element” jest dozwolony (Allow), a „From Script like Fetch or XMLHttpRequest” zabroniony (Deny) [S1]. To dokładnie zachowanie zapisane w AD-11 (`<img>` działa, `fetch` nie).
- Kilka mapowań naraz: dokumentacja metody zaleca „one unique virtual host per folder” i osobne nazwy domen dla folderów, które mają być od siebie odizolowane [S2]. Dwa mapowania (`shell.mediastation.example` i `icons.mediastation.example`) są więc wzorcem zalecanym.
- `.example` to nazwa zarezerwowana przez RFC 6761 i polecana w dokumentacji [S2].
- Mapowanie obejmuje nawigacje top-level i iframe oraz subresources [S2], więc origin ikon jest pełnym originem nawigowalnym. Dlatego blokada nawigacji na niego (AD-16) jest potrzebna.
- Uwaga z dokumentacji: „media files accessed using virtual host name can be very slow to load” [S2]. Dotyczy audio i wideo, nie PNG; nie ma wpływu na kafelki.
- Stan kodu: `ShellWindow.xaml.cs` dziś mapuje virtual host tylko w Release (`#if !SHELL_DEV_SERVER`). Reguła „w Debug i w Release” dla ikon oznacza, że drugie mapowanie musi być poza tym `#if`. To uwaga do implementacji, nie błąd spine.

## V2. Strona HTTP ładuje obrazek HTTPS: confirmed

MDN: mixed content to „securely loaded web pages that use resources to be fetched via HTTP”, a ładowanie zasobu bezpiecznego do strony niezabezpieczonej nie podlega restrykcjom. `http://localhost` też jest traktowany jako secure context [S3]. Debug (`http://localhost:5173`) może więc ładować `https://icons.mediastation.example/...`.

## V3. Prevents w AD-11 jest szerszy niż zapewnia `DenyCors`: caveat (low)

Prevents mówi o pliku z `config/icons/` „wykonanym w origin shella”. `DenyCors` przepuszcza jednak także `<script src="https://icons…/x.js">` (skrypt z obcego origin wykonuje się w kontekście strony shella) i `<iframe src>` [S1]. Przed tym chronią dziś tylko trzy rzeczy: dyscyplina kodu Shell (składa wyłącznie `<img>`), anulowanie nawigacji ramek (AD-16) i walidacja nazwy `icon` (bez separatorów, bez `..`).

**Poprawka:** dopisać do AD-16 lub AD-11 CSP shella, np. `script-src 'self'; img-src 'self' https://icons.mediastation.example; frame-src 'none'` (w Debug z originem Vite i `ws:` dla HMR). Alternatywnie zawęzić Prevents do „plik z `config/icons/` jako dokument w origin shella”.

## V4. `e.Source` jako guard origin: confirmed

- `CoreWebView2WebMessageReceivedEventArgs.Source`: „Gets the URI of the document that sent this web message” [S4].
- `CoreWebView2.WebMessageReceived` jest zgłaszane, gdy „the top-level document of the WebView runs window.chrome.webview.postMessage” [S5]. Ramki mają osobne `CoreWebView2Frame.WebMessageReceived`, więc przez ten handler przychodzą tylko wiadomości z dokumentu głównego.
- Uwaga implementacyjna: porównywać origin po sparsowaniu `Uri` (scheme, host, port), nie prefiksem stringa. W Debug origin to `http://localhost:5173` z portem.

## V5. Nawigacje, nowe okna, `OperationCanceled`: confirmed + caveat

- `NavigationStartingEventArgs.Cancel`: po ustawieniu „the navigation is no longer present and the content of the current page is intact”. Dokumentacja dodaje, że „Cancellation of frame navigation to `srcdoc` is not supported and will be ignored” [S6].
- `CoreWebView2WebErrorStatus.OperationCanceled` (14) jest używany m.in. „when the app cancels a navigation via NavigationStarting event”. Ten sam kod dostaje też pierwotna nawigacja, gdy app szybko nawiguje dalej [S7]. Pominięcie `OperationCanceled` w `navigationFailed` jest więc zgodne z dokumentacją. Skutek uboczny: znika też log tego drugiego przypadku (akceptowalne dla shella).
- Nawigacje same w dokumencie (fragment, `pushState`) nie wywołują `NavigationStarting` [S8]. To dobrze dla routingu React, który zostaje w originie.
- Ramki: anulowanie przez `FrameNavigationStarting` kończy się zdarzeniem ramki (`FrameNavigationCompleted`), nie głównym `NavigationCompleted` [S8]. Reguła „nie loguje jako `navigationFailed`” dotyczy więc tylko ramki głównej, chyba że implementacja podepnie też zdarzenie ramek.
- `NewWindowRequested`: przy `Handled = true` bez `NewWindow` dla `window.open()` powstaje „dummy window object, but this window does not load and is immediately closed” [S9]. Kod `ShellWindow.OnNewWindowRequested` już tak robi.

**Poprawka (low):** w AD-16 dopisać „`srcdoc` jest poza zasięgiem anulowania; Shell nie używa ramek” albo dodać `frame-src 'none'` w CSP (V3).

## V6. `action` „w kolejności i każda dokładnie raz”: caveat (medium)

- `PostWebMessageAsJson`: „The message is sent asynchronously. If a navigation occurs before the message is posted to the page, the message is not be sent” [S10]. Po reloadzie albo restarcie renderera Action przepadnie. Gwarancja z mostu to więc „co najwyżej raz”, nie „dokładnie raz”.
- Snapshot idzie przez latest-wins slot i `Dispatcher.BeginInvoke(Flush)` (`ShellBridge.cs`). Jeśli `action` pójdzie inną ścieżką, Shell może dostać long-press OK przed snapshotem, który zmienił stan kafelka. A to od stanu kafelka zależy decyzja o AppMenu (AD-8). Spine nie mówi nic o kolejności `action` względem `snapshot`.

**Poprawka:** zmienić na „w kolejności, co najwyżej raz; Action wysłana przed `ready` albo w trakcie nawigacji przepada i nie jest powtarzana”. Dopisać, że `action` i flush snapshotu idą jedną kolejką na wątku UI, więc Action nigdy nie wyprzedza snapshotu opublikowanego wcześniej. Alternatywnie Action niesie `rev` ostatniego snapshotu.

## V7. Filtr `Category=SchemaExport` w xunit.v3 4.0.2: confirmed

Konfiguracja: `global.json` ma `"test": { "runner": "Microsoft.Testing.Platform" }`, a `MediaStation.Core.Tests.csproj` ma `OutputType Exe`, `xunit.v3 4.0.2` i brak `Directory.Build.props`.

Wyniki lokalne na UM760, SDK 10.0.402:

- `dotnet test tests/MediaStation.Core.Tests --filter Category=SchemaExport`: opcja przyjęta, ścieżka pozycyjna działa, kod 8 („Uruchomiono zero testów”), bo testu z tą cechą jeszcze nie ma;
- `--filter "FullyQualifiedName~CoreMessageContractTests"`: 78 testów, sukces, czyli filtr VSTest faktycznie filtruje;
- pomoc runnera xunit.v3 pod MTP: „`--filter` Filter using the VSTest filter syntax … You may only specify a single VSTest filter”; obok są `--filter-trait`, `--filter-class` itd., których nie można łączyć z `--filter`. W składni VSTest `Category=X` dopasowuje cechę `[Trait("Category","X")]` [S11].

Wniosek: polecenie ze spine jest poprawne. Dwie uwagi:

- jeśli test straci cechę albo ktoś zrobi literówkę, MTP zwróci kod 8 i `npm run contracts` się wyłoży. To pożądane i widoczne. Nie dodawać `--ignore-exit-code 8`;
- trait w xunit v3 to `[Trait("Category", "SchemaExport")]` na klasie lub metodzie.

## V8. Zagnieżdżony build `BuildWeb` → npm → `dotnet test`: problem (high)

Stan obecny: `BuildWeb` (`BeforeTargets="BeforeBuild"`) buduje eksporter **taskiem `<MSBuild>`**, w tym samym grafie i schedulerze, a potem `Exec`-uje gotowy dll. Nowy zapis w spine każe `BuildWeb` wołać „tylko npm”, a `npm run contracts` sam uruchamia `dotnet test`. Ryzyka:

1. **Osobny proces poza schedulerem MSBuild.** `dotnet test` z `Exec` robi własny restore i build `MediaStation.Core` i `MediaStation.Core.Tests`. Zewnętrzny build o tym nie wie.
2. **`BeforeBuild` biegnie przed `ResolveProjectReferences`.** W `Microsoft.Common.targets` `CoreBuild` (z `ResolveReferences`) jest po `BeforeBuild`. Gdy App wchodzi do `BuildWeb`, Core może być budowany w innym węźle. `MediaStation.slnx` zawiera Core, App i Core.Tests, więc `dotnet build MediaStation.slnx` (domyślnie `-m`) buduje Core.Tests równolegle z wewnętrznym `dotnet test`, który pisze do tych samych `bin/Debug` i `obj/`. Typowe objawy to `MSB3026`/`MSB3027` (nie można skopiować, plik używany) i `CS2012`. Dochodzi zapis `obj/project.assets.json` podczas restore.
3. **`dotnet test MediaStation.slnx`** (weryfikacja per ticket) buduje też App, więc `BuildWeb` odpali wewnętrzny `dotnet test` Core.Tests. Zewnętrzny przebieg uruchamia w tym czasie `MediaStation.Core.Tests.exe`, co grozi blokadą exe/dll albo podwójnym uruchomieniem testów.
4. **Konfiguracja.** Wewnętrzne `dotnet test` buduje Debug także przy `dotnet publish -c Release` App. To nie błąd, ale dodatkowy build i dodatkowe świeże binarki przy każdym publishu.
5. Debug App woła `npm run contracts` przy każdym buildzie, więc i `dotnet test` przy każdym F5.

**Poprawka (do AD-16 i AD-15):** zostawić koordynację w MSBuild.

- `BuildWeb` buduje `tests/MediaStation.Core.Tests` **taskiem `<MSBuild>`** (jak dziś eksporter, z tym samym `RemoveProperties`).
- Potem `Exec` uruchamia eksport bez buildu, np. `dotnet test --project tests/MediaStation.Core.Tests --no-build --no-restore -c <cfg> --filter Category=SchemaExport`, albo bezpośrednio wynikowy exe z `--filter …`.
- Potem leci npm z samym generowaniem TS. Skrypt npm trzeba rozdzielić: `contracts:export` (dotnet) i `contracts:gen` (json-schema-to-typescript). `npm run contracts` = oba, do ręcznego użytku w `web/`. `BuildWeb` woła tylko `contracts:gen` i `vite build`.
- Reguła w spine: „`BuildWeb` nigdy nie uruchamia `dotnet build`/`dotnet test` w osobnym procesie dla projektów z solucji”.

Przy okazji (stan repo, nie spine): `MediaStation.slnx` nadal listuje `tools/MediaStation.SchemaExport`, a `web/scripts/contracts.mjs` w komunikatach mówi „uruchom `dotnet build`”. Do usunięcia razem ze zmianą.

## V9. Uzasadnienie DW-28: caveat (medium)

Spine: „Assembly testów też jest świeże i niepodpisane, ale weryfikacja i tak musi je uruchomić, więc blokada eksportu oznacza widoczną blokadę testów, a nie ciche stare typy”.

Dowody:

- Smart App Control blokuje binaria bez ważnego podpisu i bez reputacji w chmurze. Microsoft pisze, że „reputation might not be available for newly published binaries”, a jedyną pewną drogą jest podpis [S12]. Event 3077 = enforcement [S12]. Policy ID `{0283ac0f-fff1-49ae-ada1-8a933130cad6}` to enforcement SAC (`VerifiedAndReputableDesktop`) według tabeli polityk inbox [S13].
- **Lokalnie na UM760, 2026-10-26:** 34 zdarzenia 3077 w `CodeIntegrity/Operational`, m.in. 08:08 i 08:11. Blokowany był `…\src\MediaStation.App\bin\Release\net10.0-windows\win-x64\publish\MediaStation.exe` z komunikatem „did not meet the Enterprise signing level requirements … (Policy ID:{0283ac0f-fff1-49ae-ada1-8a933130cad6})”. Wartość `HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy\VerifiedAndReputablePolicyState` odczytana teraz = `0` (Off), co nie zgadza się ze zdarzeniami. `citool -lp` wymaga administratora (`0x80070005`), więc aktywnej polityki nie potwierdziłem; rozbieżność zostaje otwarta.
- Plan AD-8 i DW-28 opisują blokadę jako „przypadkową (zależy od skrótu pliku)”.
- `MediaStation.Core.Tests` ma `OutputType Exe`. Przebieg tworzy świeże `MediaStation.Core.Tests.exe` i `.dll` (lokalnie w `bin/Debug/net10.0`). W tym przebiegu testy ruszyły bez blokady.

Ocena:

- Teza „nie ma osobnego exe ani dll” jest prawdziwa tylko w wąskim sensie. Ubywa jeden świeży binarny, ale eksport nadal zależy od świeżego, niepodpisanego exe i dll; tyle że od tych, które i tak trzeba uruchomić.
- Część „widoczna blokada zamiast cichych starych typów” jest trafna i to jest realny zysk. Typy nie rozjadą się po cichu, bo `npm run contracts` padnie razem z testami.
- Spine powinien jednak powiedzieć wprost, że blokada **wyłoży też build App** (przez `BuildWeb`), tak jak dziś blokada eksportera. Problem z DW-28, gdzie publish wymaga `-p:SkipWebBuild=true`, nie znika, tylko zmienia binarkę. Tym bardziej że dziś zablokowany był sam `MediaStation.exe`, którego żadna zmiana eksportu nie dotyczy.

**Poprawka:** przeformułować na mniej więcej „Eksport nie ma osobnej binarki. Zależy od świeżego, niepodpisanego `MediaStation.Core.Tests.exe/.dll`, tak jak cała weryfikacja. Blokada CI/SAC wykłada testy i build App jednocześnie, więc nie ma cichych starych typów. DW-28 (podpis albo reguła App Control dla buildów deweloperskich, publish z `SkipWebBuild`) pozostaje otwarty”. DW-28 nie zamykać tą zmianą.

## V10. Vitest 5.0.5: confirmed + caveat (low)

`npm view vitest`: `dist-tags.latest = 5.0.5` (V4 = 4.1.12, beta 5.1.0-beta.1). `vitest@5.0.5`:

- `peerDependencies.vite = ^6.4.0 || ^7.0.0 || ^8.0.0`, więc Vite 8.3.4 pasuje;
- `engines.node = ^22.12.0 || ^24.0.0 || >=26.0.0`, więc pasuje do `web/package.json` `>=24.22.0` (z wyjątkiem Node 25, którego engines `>=24.22.0` nie wyklucza);
- `@vitejs/plugin-react@6.1.2` wymaga `vite ^8.0.0`, co jest spójne.

React 19 nie jest peerem Vitest. Testy komponentów (kafelki, AppMenu, model fokusu z DOM) wymagają jednak środowiska DOM (`jsdom` albo `happy-dom`, opcjonalne peery Vitest) i zwykle `@testing-library/react`. Stack ich nie pinuje, a `web/package.json` nie ma jeszcze ani `vitest`, ani skryptu `test`.

**Poprawka:** dopisać do Stack środowisko DOM (np. `jsdom` z wersją) i ewentualnie `@testing-library/react`. Albo zapisać, że testy Shell to czysta logika bez DOM.

## Źródła

- [S1] CoreWebView2HostResourceAccessKind — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2hostresourceaccesskind
- [S2] CoreWebView2.SetVirtualHostNameToFolderMapping — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.setvirtualhostnametofoldermapping
- [S3] MDN, Mixed content — https://developer.mozilla.org/en-US/docs/Web/Security/Mixed_content
- [S4] CoreWebView2WebMessageReceivedEventArgs.Source — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2webmessagereceivedeventargs.source
- [S5] CoreWebView2.WebMessageReceived — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.webmessagereceived
- [S6] CoreWebView2NavigationStartingEventArgs.Cancel — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2navigationstartingeventargs.cancel
- [S7] CoreWebView2WebErrorStatus — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2weberrorstatus
- [S8] Navigation events for WebView2 apps — https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/navigation-events
- [S9] CoreWebView2NewWindowRequestedEventArgs.Handled — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2newwindowrequestedeventargs.handled
- [S10] CoreWebView2.PostWebMessageAsJson — https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.postwebmessageasjson
- [S11] Lokalnie: `dotnet test tests/MediaStation.Core.Tests --no-build --help` (xunit.v3 4.0.2, MTP) i przebiegi z `--filter`; składnia VSTest — https://learn.microsoft.com/dotnet/core/testing/selective-unit-tests?pivots=xunit
- [S12] Test App Signatures with Smart App Control — https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/test-your-app-with-smart-app-control
- [S13] Inbox App Control policies — https://learn.microsoft.com/en-us/windows/security/application-security/application-control/windows-defender-application-control/operations/inbox-wdac-policies
- Projekt: `src/MediaStation.App/MediaStation.App.csproj` (target `BuildWeb`), `src/MediaStation.App/Shell/ShellWindow.xaml.cs`, `src/MediaStation.App/Shell/ShellBridge.cs`, `tests/MediaStation.Core.Tests/MediaStation.Core.Tests.csproj`, `global.json`, `MediaStation.slnx`, `web/package.json`, `web/scripts/contracts.mjs`, `_bmad-output/initiative-mediastation-mvp/deferred-work.md` (DW-28), `_bmad-output/plan-ad8-foreground-gate.md`
