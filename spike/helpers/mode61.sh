#!/usr/bin/env bash
# 6.1 measurement sequence for one window-mode flag. Usage: mode61.sh <mode> <browser-pid>. Needs serve running (ctl 9340) on the watch page.
M=$1; P=$2; cd "$(dirname "$0")/.."
cm() { node harness/ms.mjs cmd "$1" | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>{const j=JSON.parse(s);const r=j.result||{};const st=r.after||r;console.log(JSON.stringify(j.ok?{vis:r.visibility,video:(st.video||r.video),err:st.error,fs:r.fullscreen,res:r}:j).slice(0,380))})"; }
wf() { powershell -NoProfile -File helpers/winfacts.ps1 -ProcessId $P -Action "$1"; }
vs() { node harness/ms.mjs cmd "eval ({vis:document.visibilityState,fs:!!document.fullscreenElement,inner:[innerWidth,innerHeight],outer:[outerWidth,outerHeight],scr:[screen.width,screen.height],paused:document.querySelector('video')&&document.querySelector('video').paused,t:document.querySelector('video')&&document.querySelector('video').currentTime,vw:document.querySelector('video')&&document.querySelector('video').videoWidth})" | node -e "let s='';process.stdin.on('data',d=>s+=d).on('end',()=>console.log(JSON.stringify(JSON.parse(s).result)))"; }
wf front >/dev/null; echo "== $M initial"; wf facts; vs
echo "== $M DRM/quality overlay"; node harness/ms.mjs cmd "key Ctrl+Alt+Shift+D" >/dev/null; sleep 2; node harness/ms.mjs cmd "screenshot 61-$M-overlay" | cut -c1-200
node harness/ms.mjs cmd "key Ctrl+Alt+Shift+D" >/dev/null
echo "== $M minimize (ShowWindow 6)"; wf min; sleep 3; vs
echo "== $M restore (ShowWindow 9)"; wf restore; wf front >/dev/null; sleep 4; vs; wf facts
echo "== $M key f"; node harness/ms.mjs cmd "key f" >/dev/null; sleep 2; wf facts; vs; powershell -NoProfile -File win/screenshot.ps1 -Name "61-$M-after-f" | tail -1
echo "== $M key Escape"; node harness/ms.mjs cmd "key Escape" >/dev/null; sleep 2; wf facts; vs
