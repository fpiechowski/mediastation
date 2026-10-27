#!/usr/bin/env bash
# Usage: mode61-next.sh <old-pid> <window-flag> <label>  Graceful close of old Edge (after /browse to free the stream slot), launch next mode, serve, nav /watch.
cd "$(dirname "$0")/.."
OLD=$1; MODE=$2; L=$3
node harness/ms.mjs cmd "nav https://www.netflix.com/browse" >/dev/null 2>&1; sleep 3
node harness/ms.mjs cmd "quit" >/dev/null 2>&1; sleep 1
powershell -NoProfile -Command "\$p=Get-Process -Id $OLD -ErrorAction SilentlyContinue; if(\$p){ Get-Process msedge | Where-Object { \$_.Id -eq $OLD } | ForEach-Object { \$null=\$_.CloseMainWindow() } ; \$p.WaitForExit(20000) | Out-Null; if(-not \$p.HasExited){ 'STILL RUNNING' } else { 'exited gracefully' } }"
sleep 40
rm -rf profiles/edge-netflix/Default/Sessions profiles/edge-netflix/Default/Sessions_Encrypted profiles/edge-netflix/Default/EdgeSessions
node harness/ms.mjs launch --browser edge --window "$MODE" --url https://www.netflix.com/browse "--extra=--autoplay-policy=no-user-gesture-required" --label "61-$L-launch" 2>&1 | grep -o '"pid":[0-9]*' | head -1
(node harness/ms.mjs serve --mode raw --browser edge --label "61-$L" > "logs/serve-61-$L.out" 2>&1 &); sleep 7
node harness/ms.mjs cmd "nav https://www.netflix.com/watch/81771640" >/dev/null 2>&1; sleep 3
