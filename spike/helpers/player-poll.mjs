// S6 helper: samples player state every 400 ms over a second raw CDP connection (no Runtime.enable).
import { appendFileSync } from 'node:fs';
import { Cdp, getJson } from '../harness/drivers.mjs';
const [port, seconds, out] = [Number(process.argv[2]), Number(process.argv[3]), process.argv[4]];
const page = (await getJson(port, '/json/list')).find((t) => t.type === 'page' && !t.url.startsWith('devtools://'));
const cdp = await Cdp.open(page.webSocketDebuggerUrl);
const expr = `(() => { const v = document.querySelector('video'); return JSON.stringify({ path: location.pathname + location.search, paused: v?.paused, t: v && +v.currentTime.toFixed(1), muted: v?.muted, vol: v?.volume, fs: !!document.fullscreenElement, modal: !!document.querySelector('.previewModal--detail,[data-uia="previewModal--container"]'), active: (document.activeElement?.getAttribute('data-uia') || document.activeElement?.tagName) }); })()`;
let last = '';
const end = Date.now() + seconds * 1000;
while (Date.now() < end) {
  try {
    const r = await cdp.send('Runtime.evaluate', { expression: expr, returnByValue: true });
    const s = r.result.value;
    const key = s.replace(/"t":[\d.]+,/, '');
    if (key !== last) { appendFileSync(out, `${new Date().toISOString()} ${s}\n`); last = key; }
  } catch (e) { appendFileSync(out, `${new Date().toISOString()} ERR ${e.message}\n`); }
  await new Promise((r) => setTimeout(r, 400));
}
cdp.close();
