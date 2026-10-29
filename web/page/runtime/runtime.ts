// PageRuntime (AD-6, AD-7): start mostu w głównej ramce i `hello` po każdym nowym dokumencie.
// Skrypt inicjalizujący hosta wykonuje się w każdym dokumencie, więc każdy dokument ma własny runtime,
// własną kolejkę i własne `hello`. Przywrócenie z bfcache (`pageshow` z `persisted: true`) nie uruchamia skryptu
// ponownie, dlatego ten sam runtime wysyła wtedy `hello` jeszcze raz.
import type { PageHello } from '../../contracts/generated/page-hello';
import { createBridge, type PageBridge } from './bridge';
import type { DeliverResult } from './envelope';

/** Config od hosta (`BrowserLaunch.BuildInitScript`): nazwa bindingu, serwis i wersja drivera z manifestu. */
export interface PageConfig {
  binding: string;
  service: string;
  driverVersion: string | null;
}

/** Część `window`, której używa runtime; testy podstawiają fałszywe okno. */
export interface PageWindow {
  readonly top: unknown;
  readonly navigator: { readonly webdriver?: boolean };
  addEventListener(type: 'pageshow', listener: (event: { readonly persisted: boolean }) => void): void;
}

export interface PageRuntime {
  bridge: PageBridge;
  deliver(json: unknown): DeliverResult;
}

/** Uruchamia runtime w głównej ramce; w iframe zwraca `null` i nie wysyła niczego. */
export function start(config: PageConfig, win: PageWindow): PageRuntime | null {
  if (win !== win.top) return null;

  const { bridge, deliver, restartWaiting } = createBridge(win, config.binding);

  const hello = (): void => {
    const payload: PageHello = {
      service: config.service,
      driverVersion: config.driverVersion,
      webdriver: win.navigator.webdriver === true,
    };
    bridge.send('hello', payload);
  };

  win.addEventListener('pageshow', (event) => {
    if (event.persisted !== true) return;
    restartWaiting();
    hello();
  });

  hello();
  return { bridge, deliver };
}
