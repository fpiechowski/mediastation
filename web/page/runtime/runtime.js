// Minimalny PageRuntime (story 6.2, AD-6, AD-7): ręcznie pisane wyrażenie funkcji strzałkowej ES2022.
// BrowserHost czyta ten plik z dysku przy każdym starcie i wstrzykuje jako `(<ten plik>)(<config JSON>);`
// przez Page.addScriptToEvaluateOnNewDocument, więc plik jest jednym wyrażeniem bez średnika na końcu.
// config: { binding, service, driverVersion }. Story 6.6 zastępuje go bundlem IIFE z TS/esbuild.
(config) => {
  // Tylko główna ramka: iframe'y serwisu nie mówią do hosta.
  if (window !== window.top) {
    return;
  }

  const retryMs = 50;
  const maxAttempts = 600; // 600 × 50 ms = 30 s; potem strona przestaje czekać na binding.
  const queue = [];
  let attempts = 0;
  let timer = null;

  // Binding pojawia się dopiero po Runtime.addBinding hosta (ponawianym po nawigacji głównej ramki),
  // więc koperty czekają w kolejce i wychodzą w kolejności, gdy binding stanie się funkcją.
  const flush = () => {
    const binding = globalThis[config.binding];
    if (typeof binding === 'function') {
      while (queue.length > 0) {
        binding(queue.shift());
      }
      return;
    }
    if (timer === null && attempts < maxAttempts) {
      attempts += 1;
      timer = setTimeout(() => {
        timer = null;
        flush();
      }, retryMs);
    }
  };

  const send = (type, payload) => {
    queue.push(JSON.stringify({ v: 1, type, id: null, payload }));
    flush();
  };

  send('hello', {
    service: config.service,
    driverVersion: config.driverVersion,
    webdriver: navigator.webdriver === true,
  });
}
