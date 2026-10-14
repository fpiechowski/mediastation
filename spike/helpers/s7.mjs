// S7 helper: for each start screen and Back method, set up the screen, run `back <method>`, print before/after.
const cmd = async (line) => (await (await fetch('http://127.0.0.1:9340/cmd', { method: 'POST', body: line })).json());
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const N = 'https://www.netflix.com';
const brief = (s) => s && `${s.route}${s.jbv ? '+jbv' : ''} ${s.path}${s.video ? (s.video.paused ? ' [paused]' : ' [playing]') : ''}`;
// Leaving the profile gate open makes every /browse show "Who's watching?", so pick the profile first.
const profile = async () => { const st = (await cmd('state')).result; if (st?.route?.name === 'profiles') { await cmd(`eval [...document.querySelectorAll('[data-uia^="profile-selector+tile-"]')].find((b) => b.innerText.trim() === 'Filip').click()`); await sleep(3000); } };
const setups = {
  modal: async () => { await cmd(`nav ${N}/browse`); await sleep(3000); await profile(); await cmd(`eval document.querySelector('[data-uia=standard-card]').click()`); await sleep(2500); },
  title: async () => { await cmd(`nav ${N}/browse`); await sleep(2000); await profile(); await cmd(`nav ${N}/title/80021955`); await sleep(3000); },
  search: async () => { await cmd(`nav ${N}/browse`); await sleep(2000); await profile(); await cmd(`nav ${N}/search?q=1670`); await sleep(3000); },
  watch: async () => { await cmd(`nav ${N}/browse`); await sleep(2500); await profile(); await cmd(`eval location.assign('/watch/81771640')`); await sleep(6000); await cmd('play api'); await sleep(2000); },
  browse: async () => { await cmd(`nav ${N}/title/80021955`); await sleep(2500); await cmd(`nav ${N}/browse`); await sleep(3000); await profile(); },
  profiles: async () => { await cmd(`nav ${N}/browse`); await sleep(2000); await profile(); await cmd(`nav ${N}/ProfilesGate`); await sleep(3000); },
};
const only = process.argv.slice(2);
for (const [screen, setup] of Object.entries(setups)) {
  if (only.length && !only.includes(screen)) continue;
  for (const method of ['esc', 'history', 'modal']) {
    await setup();
    await cmd(`mark S7 ${screen} back ${method}`);
    const r = await cmd(`back ${method}`);
    await sleep(1500);
    const late = (await cmd('state')).result;
    const lateBrief = late && `${late.route.name}${late.route.jbv ? '+jbv' : ''} ${late.route.path}`;
    console.log(`${screen} | back ${method} | ${brief(r.result?.before)} -> ${brief(r.result?.after)} | +1.5s: ${lateBrief} | detail=${r.result?.detail ?? '-'}${r.ok ? '' : ' ERR ' + r.error}`);
  }
}
