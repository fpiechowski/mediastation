// S7: physical remote Back (BrowserBack). 1st press: no blocking (expect /title). Then back to /browse with a
// capture-phase preventDefault listener; 2nd press shows whether the page can stop the browser's history back.
const cmd = async (line) => (await (await fetch('http://127.0.0.1:9340/cmd', { method: 'POST', body: line })).json());
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const N = 'https://www.netflix.com';
const where = async () => { const s = (await cmd('state')).result; return `${s.route.name} ${s.href.replace(N, '')}`; };
await cmd(`nav ${N}/title/80021955`); await sleep(3000); await cmd(`nav ${N}/browse`); await sleep(3000);
console.log('start:', await where());
await cmd('mark S7 fizyczny Back 1 (bez blokady)');
console.log('wait1:', (await cmd('wait key BrowserBack 240')).ok);
await sleep(2500); console.log('po Back 1:', await where());
await cmd(`nav ${N}/title/80021955`); await sleep(3000); await cmd(`nav ${N}/browse`); await sleep(3000);
await cmd('eval (window.addEventListener("keydown", (e) => { if (e.key === "BrowserBack") e.preventDefault(); }, true), "ok")');
await cmd('front');
console.log('przed Back 2:', await where());
await cmd('mark S7 fizyczny Back 2 (preventDefault)');
console.log('wait2:', (await cmd('wait key BrowserBack 240')).ok);
await sleep(2500); console.log('po Back 2:', await where());
