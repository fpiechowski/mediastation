// C2 helper (no eval in variant C): runs commands and prints probe state before/after (route, video, focus).
const cmd = async (line) => (await (await fetch('http://127.0.0.1:9340/cmd', { method: 'POST', body: line })).json());
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const st = async () => { const s = (await cmd('state')).result; return s && `${s.route.name}${s.route.jbv ? '+jbv' : ''} ${s.video ? `p=${s.video.paused} t=${s.video.t}` : ''} focus=${s.focus?.uia ?? s.focus?.tag}`; };
const gap = Number(process.env.GAP ?? 3000);
for (const line of process.argv.slice(2)) {
  await sleep(gap);
  const b = await st();
  await cmd('mark C seq ' + line);
  const r = await cmd(line);
  await sleep(1200);
  const res = r.result?.result ?? r.result;
  const extra = res && typeof res === 'object' ? ` fb=${res.fallback ?? '-'} el=${res.el?.uia ?? res.uia ?? '-'} "${(res.el?.aria ?? res.aria ?? res.text ?? '').slice(0, 25)}"${res.value !== undefined ? ' value=' + res.value : ''}` : ` ${String(JSON.stringify(res)).slice(0, 60)}`;
  console.log(`${line} | ok=${r.ok}${r.error ? ' ERR ' + r.error : ''} | ${b} -> ${await st()} |${extra}`);
}
