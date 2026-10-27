#!/usr/bin/env bash
# Fresh /watch load, window in front, stats overlay, cropped screenshot of the KeySystem block. Usage: keysys61.sh <pid> <label>
cd "$(dirname "$0")/.."; P=$1; L=$2
node harness/ms.mjs cmd "nav https://www.netflix.com/browse" >/dev/null; sleep 45
node harness/ms.mjs cmd "nav https://www.netflix.com/watch/81771640" >/dev/null
powershell -NoProfile -File helpers/winfacts.ps1 -ProcessId $P -Action front >/dev/null; sleep 14
powershell -NoProfile -File helpers/winfacts.ps1 -ProcessId $P -Action front >/dev/null
node harness/ms.mjs cmd "key Ctrl+Alt+Shift+D" >/dev/null; sleep 3
F=$(node harness/ms.mjs cmd "screenshot 61-$L-keysys" | grep -o '2026[0-9-]*61-[^"\]*png')
node harness/ms.mjs cmd "key Ctrl+Alt+Shift+D" >/dev/null
powershell -NoProfile -File helpers/crop61.ps1 -In "logs/$F" -Out "logs/k-$L.png"
echo logs/k-$L.png
