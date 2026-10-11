// MAIN world: gives probe.js the same window.__msBridge it gets from CDP/Playwright, and runs probe
// methods on request. Talks to bridge.js (ISOLATED world) through DOM CustomEvents with JSON strings.
(() => {
  if (window.top !== window || window.__msBridge) return;
  const queue = [];
  let ready = false;
  const emit = (json) => document.dispatchEvent(new CustomEvent('__ms_evt', { detail: json }));

  window.__msBridge = (json) => (ready ? emit(json) : queue.push(json));

  // The two worlds start in no guaranteed order: hello/ready handshake, events are queued until then.
  document.addEventListener('__ms_ready', () => {
    ready = true;
    queue.splice(0).forEach(emit);
  });

  document.addEventListener('__ms_cmd', async (e) => {
    const { id, method, args } = JSON.parse(e.detail);
    let res;
    try {
      const probe = window.__msProbe;
      if (!probe || typeof probe[method] !== 'function') throw new Error('unknown probe method: ' + method);
      res = { id, ok: true, value: await probe[method](...args) };
    } catch (err) {
      res = { id, ok: false, error: String((err && err.message) || err) };
    }
    document.dispatchEvent(new CustomEvent('__ms_res', { detail: JSON.stringify(res) }));
  });

  document.dispatchEvent(new CustomEvent('__ms_hello'));
})();
