# Addendum: MediaStation

Supporting detail for downstream work (architecture, PRD) that does not belong in the brief itself.

## Technical landscape

Source: web research, 2026-10-10. Items marked **?** are unverified.

### DRM and video quality on Windows

- Netflix in Chrome: 4K available since ~Aug 2026 (Widevine validated on Win11). HDR and Dolby Atmos still require Edge (PlayReady). 4K also requires the Premium plan, the HEVC extension, and HDCP 2.2.
- Prime Video / Disney+ / HBO Max in desktop browsers: commonly reported at ~720p (Widevine L3). ? Not verified for HBO Max; Microsoft Store apps or Edge may do better.
- Implication: "which browser for which service" is a real product question, not just an implementation detail.

### Browser control

- Playwright's default arguments work against a media launcher: `--disable-component-update` (blocks Widevine CDM updates; Netflix error M7701-1003), `--force-color-profile=srgb` (color/HDR), `--disable-extensions`, `--enable-automation` (banner, `navigator.webdriver`). All are removable via `ignoreDefaultArgs`.
- Alternative: launch Chrome/Edge directly with `--remote-debugging-port` and a dedicated `--user-data-dir` (required since Chrome 136), then attach via CDP (`connectOverCDP`).
- Alternative: a browser extension that injects focus/pause scripts and talks to the launcher via native messaging or a localhost WebSocket. No automation fingerprint.
- ? Whether Netflix/Prime Video/HBO Max detect or block automated browsers. Must be tested.

### Remote navigation inside web apps

- Netflix web: keyboard playback controls exist (Space/Enter, arrows for seek/volume, F, Esc); browsing rows relies on Tab. No evidence of arrow-key navigation in the Netflix/Prime Video/HBO Max browse UIs, so custom injection is needed.
- Options: Chrome `--enable-spatial-navigation` (?), the WICG spatial-navigation polyfill (old; check maintenance status), per-site focus maps (most reliable, highest maintenance).
- YouTube TV UI (youtube.com/tv) blocks desktop browsers; VacuumTube (an Electron wrapper) is a maintained alternative.

### Pausing on switch

- Most deterministic: inject `video.pause()` via CDP or the extension; synthetic Space as a fallback.
- Windows SMTC (`GlobalSystemMediaTransportControlsSessionManager.TryPauseAsync`) works per app; ? not per tab.
- Avoid suspending browser processes (DRM heartbeat risk; inferred, untested).

### Moonlight

- Ctrl+Alt+Shift+Q ends the session (the game keeps running on the host); "Quit App" terminates it on the host.
- ? CLI: `Moonlight.exe stream|list|quit <host>`. Verify with `--help`.
- Exit detection via process handle; bringing the window to front needs `SetForegroundWindow` with foreground-lock workarounds.

### Comparables

- Flex Launcher (OSS, 10-foot UI, remote/gamepad, INI config): launch/close only; no multi-app or pause.
- Playnite fullscreen: games first; streaming only as browser shortcuts.
- Xbox mode on Win11 (rolling out in 2026): a game library, not a streaming hub.
- Steam Big Picture: can launch browsers; no navigation inside web apps.
- Kodi: weak for Netflix and similar services; users glue it together with AutoHotkey.
- Plex HTPC: actively maintained. ? Jellyfin Media Player renamed to "Jellyfin Desktop".
- Gap: nothing combines a remote-driven home screen, multi-app keep-alive with auto-pause, D-pad navigation in DRM web apps, and Moonlight.

### Early risks to spike

1. Automation detection on streaming sites.
2. Widevine state in a dedicated browser profile.
3. Arrow-key navigation inside Prime Video and HBO Max.

## Observations from the user's hands-on test (2026-10-11)

- Netflix from the Microsoft Store installs as an Edge PWA, not a native app. This matters for how MediaStation launches and controls it, and for HDR via Edge.
- G20S Pro BT gyro-mouse navigation in the Netflix web UI: workable, but scrolling rows is tedious. Tested on a monitor without starting playback (HDR and quality on the TV untested).

## Browser-control approach: open for research

The approach for controlling streaming services (D-pad navigation, pause, playback detection, Back handling) is intentionally undecided. Early ideas, none tested:

- Playwright driving a real Chrome or Edge (the user's first idea, from brief initial thinking only).
- Launching the browser directly and attaching over CDP with a dedicated profile.
- A browser extension that talks to MediaStation over native messaging or a localhost WebSocket.

Decide on the basis of dedicated technical research and early spikes against Netflix (see "Early risks to spike" above), not on the basis of this list.

## Post-MVP feature ideas (2026-10-15)

Ideas raised after the brief was finalized. Triaged against the success criterion (does the wife choose MediaStation over VIDAA without help).

### Home-screen recommendation widgets

- Goal: a home screen with content widgets (recommendations, continue watching), not only app tiles.
- Scraping the streaming services is fragile and adds to two existing risks: D-pad navigation breaking on UI changes and automation detection.
- Before deciding, compare API-based sources (e.g. TMDB, Trakt, JustWatch) with `bmad-deep-recon`. ? Coverage and terms not verified.
- Out of MVP: the wife already gets recommendations inside Netflix, so it does not move the success criterion.

### Moonlight tiles per host and per game

- Goal: one tile per paired host instead of a generic Moonlight icon; in a Games category, one tile per game on the host.
- Likely path: read paired hosts from Moonlight's config, list apps with `Moonlight.exe list <host>`, launch with `Moonlight.exe stream <host> <app>`. ? CLI not verified; check `Moonlight.exe --help` on the UM760.
- Out of MVP: serves the author, not the success criterion. Probably cheap, so a good first ticket in the next initiative.

### Rating prompt after watching (resolved: in MVP, minimal variant)

- The brief holds the scope. The forge holds the rationale, the rejected options and the implementation checks: [forge-filmweb-rating-prompt.md](../forge-filmweb-rating-prompt/forge-filmweb-rating-prompt.md). IMDb is out of scope, because the wife rates only on Filmweb.
- Neither Filmweb nor IMDb has an official rating API. The alternatives are unofficial mobile-app APIs, scrapers or session cookies, which are fragile and sit in a ToS grey area. That is why rating happens on the phone through a QR code and not from the remote.
- Title-page mapping (next initiative): Wikidata Netflix ID (P1874) → Filmweb film ID (P5032). ? Coverage of new titles is not verified.
- Netflix `/watch/<id>` carries the episode id, not the series id. The series id is only available if the user went through `/title/<id>` or `jbv` first, so mapping has to work at series level.
