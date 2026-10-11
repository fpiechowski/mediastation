// MediaStation spike probe. Injected at document start (top frame only) through CDP, Playwright
// or the variant C extension (MAIN-world content script; this file is shared by all three).
// Reports via window.__msBridge(json): navigator.webdriver, <video> events, URL routes, focus, keys.
// Must stay a single expression (no trailing semicolon) so Playwright can evaluate it as a string.
(() => {
  if (window.top !== window || window.__msProbe) return;

  const send = (type, data = {}) => {
    try {
      window.__msBridge(JSON.stringify({ type, url: location.href, ...data }));
    } catch (e) {
      // bridge not installed yet or detached: drop the event
    }
  };

  const describe = (el) => {
    if (!el || !el.tagName) return null;
    const attr = (n) => el.getAttribute(n);
    return {
      tag: el.tagName.toLowerCase(),
      uia: attr('data-uia'),
      aria: attr('aria-label'),
      role: attr('role'),
      cls: typeof el.className === 'string' ? el.className.slice(0, 80) : null,
      text: (el.innerText || el.value || '').trim().slice(0, 60),
    };
  };

  const isEditable = (el) =>
    !!el &&
    (el.isContentEditable ||
      el.tagName === 'TEXTAREA' ||
      (el.tagName === 'INPUT' && !/^(button|checkbox|radio|submit|range|file|image|reset|color)$/i.test(el.type)));

  // Candidate selectors; the logs show which one matched so the focus map can be pinned down later.
  const PROFILE_SEL = ['.profiles-gate-container', '[data-uia="profile-choices-page"]', '.list-profiles', '[data-uia="profile-link"]'];
  const MODAL_SEL = ['.previewModal--detail', '[data-uia="previewModal--container"]', '.detail-modal'];
  const MODAL_CLOSE_SEL = ['[data-uia="previewModal-closebtn"]', '.previewModal-close', 'button[aria-label="close"]', '[data-uia*="close"]'];
  const ERROR_SEL = '.nfp-fatal-error-view, .error-page, [data-uia*="error"], [class*="ErrorView"]';
  const firstMatch = (sels) => sels.find((s) => document.querySelector(s)) || null;

  const route = () => {
    const path = location.pathname.replace(/^\/[a-z]{2}(-[a-z]{2})?(?=\/)/i, ''); // drop /pl/ style locale prefix
    const q = new URLSearchParams(location.search);
    const profiles = firstMatch(PROFILE_SEL);
    let name = 'other';
    if (path.startsWith('/watch')) name = 'watch';
    else if (path.startsWith('/title')) name = 'title';
    else if (path.startsWith('/search')) name = 'search';
    else if (path.startsWith('/browse')) name = profiles ? 'profiles' : q.has('jbv') ? 'browse-modal' : 'browse';
    else if (path.startsWith('/login')) name = 'login';
    else if (profiles) name = 'profiles';
    return { name, path: location.pathname, jbv: q.get('jbv'), profileSel: profiles, modalSel: firstMatch(MODAL_SEL) };
  };

  const video = () => document.querySelector('video');
  const videoInfo = () => {
    const v = video();
    if (!v) return null;
    const q = v.getVideoPlaybackQuality ? v.getVideoPlaybackQuality() : {};
    return {
      paused: v.paused,
      ended: v.ended,
      t: Math.round(v.currentTime),
      w: v.videoWidth,
      h: v.videoHeight,
      readyState: v.readyState,
      dropped: q.droppedVideoFrames,
      frames: q.totalVideoFrames,
      eme: !!v.mediaKeys,
    };
  };

  // Undocumented Netflix player API (also used by Mozilla's PiP wrapper); may disappear any time.
  const nfPlayer = () => {
    try {
      const vp = window.netflix.appContext.state.playerApp.getAPI().videoPlayer;
      const ids = vp.getAllPlayerSessionIds();
      const id = ids.find((s) => s.startsWith('watch')) || ids[0];
      return id ? vp.getVideoPlayerBySessionId(id) : null;
    } catch (e) {
      return null;
    }
  };

  const errorText = () => {
    const el = document.querySelector(ERROR_SEL);
    const text = el ? (el.innerText || '').trim() : '';
    return text ? text.slice(0, 200) : null;
  };

  // Media events do not bubble, but capture listeners on document still see them.
  ['play', 'playing', 'pause', 'ended', 'waiting', 'stalled', 'error', 'emptied', 'loadedmetadata', 'encrypted'].forEach((ev) =>
    document.addEventListener(
      ev,
      (e) => {
        if (e.target instanceof HTMLMediaElement) send('video', { ev, ...videoInfo(), mediaError: e.target.error && e.target.error.code });
      },
      true,
    ),
  );

  document.addEventListener('focusin', (e) => send(isEditable(e.target) ? 'text-focus' : 'focus', { el: describe(e.target) }), true);
  document.addEventListener('focusout', (e) => isEditable(e.target) && send('text-blur', { el: describe(e.target) }), true);
  document.addEventListener('visibilitychange', () => send('visibility', { state: document.visibilityState }), true);

  // S6: capture phase sees the event before Netflix, bubble phase on window sees whether Netflix handled it.
  document.addEventListener('keydown', (e) => send('key', { key: e.key, code: e.code, keyCode: e.keyCode, trusted: e.isTrusted, target: describe(e.target) }), true);
  window.addEventListener('keydown', (e) => send('key-after', { key: e.key, trusted: e.isTrusted, prevented: e.defaultPrevented }), false);

  let lastRoute = '';
  let lastError = null;
  let lastSample = 0;
  setInterval(() => {
    const r = route();
    const routeKey = location.href + '|' + r.name + '|' + r.modalSel;
    if (routeKey !== lastRoute) {
      lastRoute = routeKey;
      send('route', r);
    }
    const err = errorText();
    if (err && err !== lastError) send('page-error', { text: err });
    lastError = err;
    const v = video();
    if (v && !v.paused && Date.now() - lastSample > 10000) {
      lastSample = Date.now();
      send('video-sample', videoInfo());
    }
  }, 300);

  // S6 part B: what variant C would use instead of synthetic keys. Programmatic focus is a crude
  // geometric spatial navigation; when nothing is on screen in the requested direction it falls back to
  // DOM order inside the row, then to clicking the row's paging arrow. The result names the fallback used.
  const FOCUSABLE = 'a[href], button, input, textarea, select, [tabindex]:not([tabindex="-1"]), [role="button"], [role="link"]';
  const ROW_SEL = ['.lolomoRow', '.rowContainer', '.slider', '[data-list-context]', '[role="list"]'];
  const ROW_NEXT_SEL = ['.handle.handleNext', '.handleNext', 'button[aria-label*="next" i]', '[aria-label*="następ" i]'];
  const ROW_PREV_SEL = ['.handle.handlePrev', '.handlePrev', 'button[aria-label*="previous" i]', '[aria-label*="poprzed" i]'];
  const ROW_ARROWS = ROW_NEXT_SEL.concat(ROW_PREV_SEL).join(',');
  const rendered = (el) => {
    const r = el.getBoundingClientRect();
    return r.width > 0 && r.height > 0 ? r : null;
  };
  const onScreen = (el) => {
    const r = rendered(el);
    return r && r.bottom > 0 && r.right > 0 && r.top < innerHeight && r.left < innerWidth ? r : null;
  };
  // Row paging arrows are never focus targets; left/right stays within the current row (vertical overlap).
  const nearest = (dir, from, cur, rectOf, scope = document) => {
    const horizontal = dir === 'left' || dir === 'right';
    let best = null;
    let bestScore = Infinity;
    for (const el of scope.querySelectorAll(FOCUSABLE)) {
      if (el === cur || el.disabled || el.matches(ROW_ARROWS)) continue;
      const r = rectOf(el);
      if (!r) continue;
      let score;
      if (!from) {
        score = r.top * 10 + r.left; // nothing focused yet: take the top-left element
      } else {
        if (horizontal && !(r.top < from.bottom && r.bottom > from.top)) continue;
        const dx = r.left + r.width / 2 - (from.left + from.width / 2);
        const dy = r.top + r.height / 2 - (from.top + from.height / 2);
        const main = { up: -dy, down: dy, left: -dx, right: dx }[dir];
        if (!(main > 1)) continue;
        score = main + 2 * (dir === 'up' || dir === 'down' ? Math.abs(dx) : Math.abs(dy));
      }
      if (score < bestScore) {
        best = el;
        bestScore = score;
      }
    }
    return best;
  };
  const focusEl = (el, fallback, extra) => {
    el.focus();
    el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
    return { el: describe(el), fallback, onScreen: !!onScreen(el), ...extra };
  };
  const rowOf = (el) => {
    for (const sel of ROW_SEL) {
      const row = el.closest(sel);
      if (row) return { row, sel };
    }
    return null;
  };

  const focusNext = async (dir) => {
    const cur = document.activeElement && document.activeElement !== document.body ? document.activeElement : null;
    const from = cur && cur.getBoundingClientRect();
    const vertical = dir === 'up' || dir === 'down';
    const r = cur && !vertical ? rowOf(cur) : null;
    const best = nearest(dir, from, cur, onScreen, r ? r.row : document);
    if (best) return focusEl(best, 'none', r ? { row: r.sel } : {});
    if (!cur) return { el: null, fallback: 'none', reason: 'nothing focusable on screen' };
    if (vertical) {
      const off = nearest(dir, from, cur, rendered);
      return off ? focusEl(off, 'offscreen-geometric') : { el: null, fallback: 'none-found' };
    }

    if (!r) return { el: null, fallback: 'none-found', reason: 'no row container matched ' + ROW_SEL.join(' | ') };
    const items = [...r.row.querySelectorAll(FOCUSABLE)].filter((el) => !el.disabled && !el.matches(ROW_ARROWS) && rendered(el));
    const i = items.indexOf(cur);
    const step = dir === 'right' ? 1 : -1;
    const next = items[i + step];
    if (next) {
      const res = focusEl(next, 'dom-order', { row: r.sel });
      if (res.onScreen) return res;
    }
    // Still off screen (Netflix pages rows with transforms, not scrolling): click the row's paging arrow.
    for (const sel of dir === 'right' ? ROW_NEXT_SEL : ROW_PREV_SEL) {
      const arrow = r.row.querySelector(sel) || (r.row.parentElement && r.row.parentElement.querySelector(sel));
      if (!arrow) continue;
      arrow.click(); // isTrusted=false
      // Wait for the paging transition (up to 2 s; it never runs while the page is hidden or occluded).
      const pick = () => (dir === 'right' ? items.slice(i + 1).find(onScreen) : items.slice(0, Math.max(i, 0)).reverse().find(onScreen));
      let after = null;
      for (let t = 0; t < 2000 && !after; t += 100) {
        await new Promise((done) => setTimeout(done, 100));
        after = pick();
      }
      if (after) return focusEl(after, 'row-arrow', { row: r.sel, arrow: sel });
      return {
        el: describe(document.activeElement),
        fallback: 'row-arrow',
        row: r.sel,
        arrow: sel,
        reason: 'no on-screen item after paging (visibility=' + document.visibilityState + ')',
      };
    }
    return { el: next ? describe(next) : null, fallback: next ? 'dom-order' : 'none-found', onScreen: false, row: r.sel, reason: 'no row arrow matched' };
  };

  // Adapter mode: the way MediaStation would really work in B and C. The remote's arrows/OK reach the
  // foreground browser as real (trusted) keys; the in-page adapter swallows them and moves focus itself.
  let adapterOn = false;
  const ADAPTER_DIRS = { ArrowUp: 'up', ArrowDown: 'down', ArrowLeft: 'left', ArrowRight: 'right' };
  window.addEventListener(
    'keydown',
    (e) => {
      if (!adapterOn || !e.isTrusted || route().name === 'watch') return;
      const dir = ADAPTER_DIRS[e.key];
      const editable = isEditable(document.activeElement);
      if (dir && !(editable && (dir === 'left' || dir === 'right'))) {
        e.preventDefault();
        e.stopPropagation();
        focusNext(dir).then((res) => send('adapter', { key: e.key, action: 'focus ' + dir, ...res }));
      } else if (e.key === 'Enter' && !editable && document.activeElement) {
        e.preventDefault();
        e.stopPropagation();
        const el = document.activeElement;
        el.click(); // isTrusted=false click: does Netflix accept it?
        send('adapter', { key: e.key, action: 'click', el: describe(el) });
      }
    },
    true,
  );

  const KEYS = {
    Space: [' ', 'Space', 32],
    Enter: ['Enter', 'Enter', 13],
    Escape: ['Escape', 'Escape', 27],
    Backspace: ['Backspace', 'Backspace', 8],
    Tab: ['Tab', 'Tab', 9],
    ArrowUp: ['ArrowUp', 'ArrowUp', 38],
    ArrowDown: ['ArrowDown', 'ArrowDown', 40],
    ArrowLeft: ['ArrowLeft', 'ArrowLeft', 37],
    ArrowRight: ['ArrowRight', 'ArrowRight', 39],
  };

  window.__msProbe = {
    state: () => ({
      href: location.href,
      route: route(),
      webdriver: navigator.webdriver,
      visibility: document.visibilityState,
      focus: describe(document.activeElement),
      video: videoInfo(),
      nfApi: !!nfPlayer(),
      error: errorText(),
      historyLength: history.length,
    }),
    // S6: untrusted (isTrusted=false) key events, as an extension content script would send them.
    jsKey: (name) => {
      const up = name.length === 1 ? name.toUpperCase() : null;
      const [key, code, keyCode] = KEYS[name] || (up ? [name.toLowerCase(), /\d/.test(up) ? 'Digit' + up : 'Key' + up, up.charCodeAt(0)] : [name, name, 0]);
      const target = document.activeElement || document.body;
      for (const type of ['keydown', 'keyup']) {
        const e = new KeyboardEvent(type, { key, code, bubbles: true, cancelable: true, composed: true });
        Object.defineProperty(e, 'keyCode', { get: () => keyCode });
        Object.defineProperty(e, 'which', { get: () => keyCode });
        target.dispatchEvent(e);
      }
      return describe(target);
    },
    pause: (mode) => {
      if (mode === 'api') {
        const p = nfPlayer();
        return p ? (p.pause(), 'api: paused') : 'api: player not found';
      }
      const v = video();
      return v ? (v.pause(), 'video: paused') : 'video: no <video>';
    },
    play: (mode) => {
      if (mode === 'api') {
        const p = nfPlayer();
        return p ? (p.play(), 'api: play') : 'api: player not found';
      }
      const v = video();
      return v ? (v.play().catch((e) => send('play-rejected', { error: String(e) })), 'video: play') : 'video: no <video>';
    },
    // S7: synthetic click (isTrusted=false) on the detail modal's close button.
    closeModal: () => {
      for (const s of MODAL_CLOSE_SEL) {
        const el = document.querySelector(s);
        if (el) {
          el.click();
          return s;
        }
      }
      return null;
    },
    focusNext,
    // Untrusted click on the focused element (variant C's replacement for a trusted Enter).
    activate: () => {
      const el = document.activeElement;
      if (!el || el === document.body) return null;
      el.click();
      return describe(el);
    },
    // Text without CDP: 'exec' = execCommand('insertText') (fires real input events),
    // 'value' = native value setter + synthetic input event (what React-style inputs need).
    insertText: (text, mode) => {
      const el = document.activeElement;
      if (!isEditable(el)) return { ok: false, reason: 'no editable element focused', focus: describe(el) };
      let ok = true;
      if (mode === 'value') {
        const proto = el.tagName === 'TEXTAREA' ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
        Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, el.value + text);
        el.dispatchEvent(new Event('input', { bubbles: true }));
      } else {
        ok = document.execCommand('insertText', false, text);
      }
      return { ok, mode: mode || 'exec', value: el.value };
    },
    adapter: (on) => {
      adapterOn = !!on;
      return adapterOn;
    },
    historyBack: () => {
      history.back();
      return true;
    },
    navigate: (url) => {
      location.href = url;
      return url;
    },
    // S4: which key systems the browser exposes right now.
    drm: async () => {
      const cfg = [{ initDataTypes: ['cenc'], videoCapabilities: [{ contentType: 'video/mp4; codecs="avc1.640028"', robustness: '' }] }];
      const out = {};
      for (const ks of ['com.widevine.alpha', 'com.microsoft.playready.recommendation', 'com.microsoft.playready.recommendation.3000']) {
        const t0 = performance.now();
        try {
          await navigator.requestMediaKeySystemAccess(ks, cfg);
          out[ks] = 'ok ' + Math.round(performance.now() - t0) + ' ms';
        } catch (e) {
          out[ks] = 'ERR ' + e.name + ': ' + e.message;
        }
      }
      return out;
    },
  };

  send('probe-loaded', { webdriver: navigator.webdriver, ua: navigator.userAgent, route: route() });
})()
