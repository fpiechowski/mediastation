// ISOLATED world: relays probe events to the service worker and probe calls back into the MAIN world.
const pending = new Map();
let seq = 0;

document.addEventListener('__ms_evt', (e) => chrome.runtime.sendMessage({ evt: e.detail }).catch(() => {}));
document.addEventListener('__ms_hello', () => document.dispatchEvent(new CustomEvent('__ms_ready')));
document.addEventListener('__ms_res', (e) => {
  const res = JSON.parse(e.detail);
  const reply = pending.get(res.id);
  if (reply) {
    pending.delete(res.id);
    reply(res);
  }
});
document.dispatchEvent(new CustomEvent('__ms_ready'));

chrome.runtime.onMessage.addListener((msg, _sender, reply) => {
  if (!msg.call) return false;
  const id = ++seq;
  pending.set(id, reply);
  setTimeout(() => pending.delete(id) && reply({ ok: false, error: 'probe timeout' }), 15000);
  document.dispatchEvent(new CustomEvent('__ms_cmd', { detail: JSON.stringify({ id, method: msg.call, args: msg.args }) }));
  return true; // reply asynchronously
});
