// S6B helper: runs focus/activate commands and prints a compact line per step.
const cmd = async (line) => (await (await fetch('http://127.0.0.1:9340/cmd', { method: 'POST', body: line })).json());
const FV = `(() => { const a=document.activeElement; const r=a.getBoundingClientRect(); return {fv:a.matches(':focus-visible'), x:Math.round(r.left), y:Math.round(r.top)}; })()`;
for (const line of process.argv.slice(2)) {
  await cmd('mark S6B seq ' + line);
  const r = await cmd(line);
  const res = r.result?.result ?? r.result;
  const fv = (await cmd('eval ' + FV)).result;
  const el = res?.el ?? res;
  console.log(`${line} | ok=${r.ok} | ${el?.uia ?? el?.tag ?? '-'} "${(el?.aria ?? el?.text ?? '').slice(0, 30)}" | fallback=${res?.fallback ?? '-'} row=${res?.row ?? '-'} arrow=${res?.arrow ?? '-'} onScreen=${res?.onScreen ?? '-'} ${res?.reason ?? ''} | fv=${fv?.fv} @${fv?.x},${fv?.y} | route=${r.result?.after?.route ?? '-'}${r.error ? ' ERR ' + r.error : ''}`);
}
