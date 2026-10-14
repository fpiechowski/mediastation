// S6 helper: sends serve commands and records player state before/after each one.
// usage: node s6-seq.mjs "jskey Space" "key Space" ...   (gap 6 s so transient user activation does not leak)
const cmd = async (line) => (await (await fetch('http://127.0.0.1:9340/cmd', { method: 'POST', body: line })).json());
const P = `(() => { const v=document.querySelector('video'); return {p:v?.paused,t:v&&+v.currentTime.toFixed(1),m:v?.muted,fs:!!document.fullscreenElement,path:location.pathname+location.search,modal:!!document.querySelector('.previewModal--detail,[data-uia="previewModal--container"]'),act:document.activeElement?.getAttribute('data-uia')||document.activeElement?.tagName}; })()`;
const st = async () => JSON.stringify((await cmd('eval ' + P)).result);
const gap = Number(process.env.GAP ?? 6000);
for (const line of process.argv.slice(2)) {
  await new Promise((r) => setTimeout(r, gap));
  const b = await st();
  await cmd('mark S6 seq ' + line);
  const r = await cmd(line);
  await new Promise((r) => setTimeout(r, 1200));
  console.log(`${line} | ok=${r.ok} | ${b} -> ${await st()}`);
}
