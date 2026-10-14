// S9 helper: report YouTube <video> state and consent dialog presence per CDP port (no awaiting play()).
import { Cdp, getJson } from '../harness/drivers.mjs';
for (const port of process.argv.slice(2).map(Number)) {
  const page = (await getJson(port, '/json/list')).find((t) => t.type === 'page');
  const cdp = await Cdp.open(page.webSocketDebuggerUrl);
  const r = await Promise.race([
    cdp.send('Runtime.evaluate', { expression: `(() => { const v = document.querySelector('video'); const c = [...document.querySelectorAll('button')].find((x) => /Odrzuć wszystko|Reject all/.test(x.innerText)); if (c && ${process.env.CLICK ? 'true' : 'false'}) c.click(); if (v && v.paused && ${process.env.PLAY ? 'true' : 'false'}) v.play().catch(() => {}); return JSON.stringify({ consent: !!c, video: v && { paused: v.paused, muted: v.muted, t: Math.round(v.currentTime) } }); })()`, returnByValue: true }),
    new Promise((r) => setTimeout(() => r({ result: { value: 'timeout' } }), 5000)),
  ]);
  console.log(port, r.result.value);
  cdp.close();
}
process.exit(0);
