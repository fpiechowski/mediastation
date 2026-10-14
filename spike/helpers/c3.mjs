// C3: S7 Back matrix through the extension (no eval): setups use nav/focus/activate only.
const cmd = async (line) => (await (await fetch('http://127.0.0.1:9340/cmd', { method: 'POST', body: line })).json());
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const N = 'https://www.netflix.com';
const brief = (s) => s && `${s.route}${s.jbv ? '+jbv' : ''} ${s.path}`;
const route = async () => (await cmd('state')).result?.route?.name;
const toCard = async () => { for (let i = 0; i < 6; i++) { const r = await cmd('focus down'); const u = r.result?.result?.el?.uia; if (/card/.test(u ?? '')) return true; } return false; };
const setups = {
  modal: async () => { await cmd(`nav ${N}/browse`); await sleep(3000); await toCard(); await cmd('activate'); await sleep(2500); },
  title: async () => { await cmd(`nav ${N}/browse`); await sleep(2000); await cmd(`nav ${N}/title/80021955`); await sleep(3000); },
  search: async () => { await cmd(`nav ${N}/browse`); await sleep(2000); await cmd(`nav ${N}/search?q=1670`); await sleep(3000); },
  watch: async () => { await cmd(`nav ${N}/browse`); await sleep(2500); await cmd(`nav ${N}/watch/81771640`); await sleep(6000); await cmd('play api'); await sleep(2000); },
};
for (const [screen, setup] of Object.entries(setups)) {
  for (const method of ['esc', 'history', 'modal']) {
    await setup();
    if ((await route()) === 'profiles') console.log('UWAGA: bramka profili');
    await cmd(`mark C3 ${screen} back ${method}`);
    const r = await cmd(`back ${method}`);
    await sleep(1500);
    const s = (await cmd('state')).result;
    console.log(`${screen} | back ${method} | ${brief(r.result?.before)} -> ${brief(r.result?.after)} | +1.5s: ${s.route.name}${s.route.jbv ? '+jbv' : ''} ${s.route.path} | detail=${r.result?.detail ?? '-'}${r.ok ? '' : ' ERR ' + r.error}`);
  }
}
