// Command set shared by `serve` (and through it by `cmd` and `repl`). Every command returns plain JSON.
import { mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const fail = (msg) => {
  throw new Error(msg);
};

export const HELP = `Komendy (serve/cmd/repl):
  state | url | webdriver | video   stan strony z probe (trasa, webdriver, fokus, <video>, API Netflixa, błąd)
  key K [K ...]                 zaufane klawisze przez CDP Input.dispatchKeyEvent (w C: niezaufane, jak jskey)
                                K: ArrowUp/Down/Left/Right, Enter, Space, Escape, Backspace, Tab, BrowserBack,
                                litera/cyfra, kombinacje np. Ctrl+Alt+Shift+D (nie w C)
  jskey K [K ...]               niezaufane KeyboardEvent przez dispatchEvent (S6)
  type TEKST | inserttext TEKST CDP Input.insertText (w C: execCommand)
  jstype exec|value TEKST       tekst bez CDP (S6B)
  focus up|down|left|right      programowy fokus z fallbackiem poza ekranem (S6B)
  activate                      el.click() z isTrusted=false (S6B)
  adapter on|off                probe przechwytuje fizyczne strzałki/OK i robi focus/activate (S6B)
  back esc|history|modal        S7; wynik: stan przed i po
  pause video|api, play video|api
  win minimized|normal|maximized|fullscreen
  front                         przeglądarka na pierwszy plan (CDP Page.bringToFront, w C chrome.windows.update)
  screenshot [NAZWA]            PNG widoku strony do logs/ (CDP Page.captureScreenshot, w C captureVisibleTab);
                                obraz wideo z DRM zwykle wychodzi czarny, nakładka statystyk jest widoczna
  s8 MINUTY [video|api] [minimized|none]   S8 w tle procesu serve; wynik trafia do logu JSONL
  drm                           requestMediaKeySystemAccess (S4)
  nav URL | eval JS (nie w C) | mark TEKST
  events [N]                    ostatnie N zdarzeń (domyślnie 20)
  wait TYP [TEKST] [SEKUNDY]    czeka na zdarzenie probe danego typu (opcjonalnie zawierające TEKST)
  quit                          zatrzymuje serve (przeglądarka zostaje, poza trybem pwlaunch)`;

// hub: { info(msg), record(obj), recent(n), waitFor(pred, ms), logsDir }
export function createCommands(d, hub) {
  const state = () => d.probe('state');
  const brief = (s) =>
    s && {
      route: s.route.name,
      path: s.route.path,
      jbv: s.route.jbv,
      focus: s.focus,
      video: s.video && { paused: s.video.paused, w: s.video.w, h: s.video.h, t: s.video.t },
      error: s.error,
    };
  const settle = async (ms = 800) => {
    await sleep(ms);
    return brief(await state());
  };
  const text = (words) => words.join(' ');

  const commands = {
    help: () => HELP,
    state,
    url: async () => {
      const s = await state();
      return s && { route: s.route, href: s.href };
    },
    webdriver: async () => (await state())?.webdriver,
    video: async () => (await state())?.video,
    key: async (...keys) => {
      if (!keys.length) fail('key K [K ...]');
      for (const k of keys) {
        await d.key(k);
        await sleep(350);
      }
      return { keys, trusted: d.trusted, after: await settle() };
    },
    jskey: async (...keys) => {
      if (!keys.length) fail('jskey K [K ...]');
      const targets = [];
      for (const k of keys) {
        targets.push(await d.probe('jsKey', k));
        await sleep(350);
      }
      return { keys, trusted: false, targets, after: await settle() };
    },
    type: async (...words) => {
      const result = await d.insertText(text(words));
      return { text: text(words), via: d.trusted ? 'cdp' : 'execCommand', result: result ?? null, after: await settle() };
    },
    jstype: async (mode, ...words) => {
      if (mode !== 'exec' && mode !== 'value') fail('jstype exec|value TEKST');
      return { result: await d.probe('insertText', text(words), mode), after: await settle() };
    },
    focus: async (dir) => {
      if (!['up', 'down', 'left', 'right'].includes(dir)) fail('focus up|down|left|right');
      return { result: await d.probe('focusNext', dir), after: await settle(300) };
    },
    activate: async () => ({ result: await d.probe('activate'), after: await settle() }),
    adapter: async (onOff = 'on') => ({ adapter: await d.probe('adapter', onOff !== 'off') }),
    back: async (method) => {
      const before = brief(await state());
      let detail = null;
      if (method === 'esc') await d.key('Escape');
      else if (method === 'history') await d.probe('historyBack');
      else if (method === 'modal') detail = await d.probe('closeModal');
      else fail('back esc|history|modal');
      return { method, detail, before, after: await settle(1500) };
    },
    pause: async (mode = 'video') => d.probe('pause', mode),
    play: async (mode = 'video') => d.probe('play', mode),
    win: async (windowState) => {
      if (!['minimized', 'normal', 'maximized', 'fullscreen'].includes(windowState)) fail('win minimized|normal|maximized|fullscreen');
      await d.setWindow(windowState);
      return windowState;
    },
    screenshot: async (...words) => {
      const name = text(words) || 'screenshot';
      const p = (n) => String(n).padStart(2, '0');
      const t = new Date();
      const stamp = `${t.getFullYear()}${p(t.getMonth() + 1)}${p(t.getDate())}-${p(t.getHours())}${p(t.getMinutes())}${p(t.getSeconds())}`;
      const file = join(hub.logsDir, `${stamp}-${name.replace(/[^\w.-]+/g, '_')}.png`);
      const png = Buffer.from(await d.screenshot(), 'base64');
      mkdirSync(hub.logsDir, { recursive: true });
      writeFileSync(file, png);
      hub.record({ kind: 'screenshot', file, bytes: png.length });
      return { file, bytes: png.length };
    },
    front: async () => {
      await d.bringToFront();
      await sleep(500);
      return { visibility: (await state())?.visibility };
    },
    s8: async (minutes = '30', mode = 'video', windowState = 'minimized') => {
      const ms = Number(minutes) * 60_000;
      if (!(ms > 0)) fail('s8 MINUTY [video|api] [minimized|none]');
      const before = brief(await state());
      const paused = await d.probe('pause', mode);
      await sleep(1000);
      if (windowState !== 'none') await d.setWindow(windowState);
      const due = new Date(Date.now() + ms).toISOString();
      setTimeout(async () => {
        try {
          if (windowState !== 'none') await d.setWindow('normal');
          await sleep(2000);
          hub.record({ kind: 's8', phase: 'end', minutes: Number(minutes), mode, windowState, state: await state() });
          hub.info('S8 koniec: wznów odtwarzanie (play api|video albo OK na pilocie) i obserwuj błąd licencji przez ok. 1 min');
        } catch (e) {
          hub.record({ kind: 's8', phase: 'error', error: e.message });
        }
      }, ms);
      hub.record({ kind: 's8', phase: 'start', minutes: Number(minutes), mode, windowState, due, before, paused });
      return { scheduled: true, due, before, paused, hint: 'wait s8 nie działa: czytaj log JSONL (kind=s8, phase=end)' };
    },
    drm: () => d.probe('drm'),
    nav: async (url) => {
      await d.navigate(url ?? fail('nav URL'));
      return { url, after: await settle(2000) };
    },
    eval: async (...code) => {
      if (!d.evaluate) fail('eval niedostępne w wariancie C (CSP Netflixa); używaj komend probe');
      return d.evaluate(text(code));
    },
    mark: (...words) => {
      hub.record({ kind: 'mark', text: text(words) });
      return text(words);
    },
    events: (n = '20') => hub.recent(Number(n)),
    wait: async (type, ...rest) => {
      if (!type) fail('wait TYP [TEKST] [SEKUNDY]');
      const seconds = /^\d+(\.\d+)?$/.test(rest.at(-1) ?? '') ? Number(rest.pop()) : 30;
      const needle = text(rest);
      const event = await hub.waitFor((e) => e.type === type && (!needle || JSON.stringify(e).includes(needle)), seconds * 1000);
      return event ?? fail(`brak zdarzenia ${type}${needle ? ' z "' + needle + '"' : ''} w ciągu ${seconds} s`);
    },
  };
  commands.inserttext = commands.type;

  return async function run(line) {
    const [name, ...args] = line.trim().split(/\s+/);
    const fn = commands[name];
    if (!fn) fail(`nieznana komenda: ${name}; "help"`);
    return fn(...args);
  };
}
