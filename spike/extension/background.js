// Service worker: WebSocket client of the harness (`node harness/ms.mjs serve --mode ext`). An open WebSocket with
// traffic every <30 s keeps an MV3 service worker alive (Chrome 116+); the alarm reconnects after sleep.
const HARNESS = 'ws://127.0.0.1:9330';
let ws = null;
let lastTabId = null;
const stats = { started: new Date().toISOString(), received: 0, queued: 0, flushed: 0 };

function connect() {
  if (ws && ws.readyState <= WebSocket.OPEN) return;
  ws = new WebSocket(HARNESS);
  ws.onopen = () => {
    ws.send(JSON.stringify({ type: 'hello', ua: navigator.userAgent, version: chrome.runtime.getManifest().version, stats: { ...stats, pending: queue.length } }));
    send({ type: 'ping' });
  };
  ws.onmessage = (e) => handle(JSON.parse(e.data));
  ws.onclose = () => {
    ws = null;
  };
  ws.onerror = () => {}; // harness not running: retry on the next tick
}

// Events produced before the harness connection is open are queued (bounded), not dropped.
const queue = [];
function send(msg) {
  if (ws && ws.readyState === WebSocket.OPEN) {
    while (queue.length) {
      ws.send(queue.shift());
      stats.flushed++;
    }
    ws.send(JSON.stringify(msg));
  } else if (msg.type === 'event' && queue.length < 500) {
    queue.push(JSON.stringify(msg));
    stats.queued++;
  }
}

setInterval(() => {
  connect();
  send({ type: 'ping' });
}, 20000);
chrome.alarms.create('reconnect', { periodInMinutes: 0.5 });
chrome.alarms.onAlarm.addListener(connect);
connect();

chrome.runtime.onMessage.addListener((msg, sender) => {
  if (!msg.evt || !sender.tab) return;
  lastTabId = sender.tab.id;
  stats.received++;
  connect();
  send({ type: 'event', tabId: sender.tab.id, payload: msg.evt });
});

async function targetTab() {
  if (lastTabId !== null) {
    try {
      return await chrome.tabs.get(lastTabId);
    } catch (e) {
      lastTabId = null;
    }
  }
  const [tab] = await chrome.tabs.query({ url: ['*://*.netflix.com/*', 'http://127.0.0.1:9350/*'] });
  return tab;
}

async function handle(cmd) {
  try {
    const tab = await targetTab();
    if (!tab) throw new Error('brak karty Netflixa');
    let value;
    if (cmd.op === 'probe') {
      const res = await chrome.tabs.sendMessage(tab.id, { call: cmd.method, args: cmd.args });
      if (!res || !res.ok) throw new Error((res && res.error) || 'no response from content script');
      value = res.value;
    } else if (cmd.op === 'window') {
      await chrome.windows.update(tab.windowId, { state: cmd.state });
      value = cmd.state;
    } else if (cmd.op === 'screenshot') {
      value = await chrome.tabs.captureVisibleTab(tab.windowId, { format: 'png' });
    } else if (cmd.op === 'front') {
      await chrome.windows.update(tab.windowId, { focused: true, state: 'normal' });
      await chrome.tabs.update(tab.id, { active: true });
      value = true;
    } else if (cmd.op === 'navigate') {
      await chrome.tabs.update(tab.id, { url: cmd.url });
      value = cmd.url;
    } else {
      throw new Error('unknown op: ' + cmd.op);
    }
    send({ type: 'result', id: cmd.id, ok: true, value });
  } catch (e) {
    send({ type: 'result', id: cmd.id, ok: false, error: String(e.message || e) });
  }
}
