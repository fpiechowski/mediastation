// Most PageRuntime ↔ host (AD-6, AD-7), niezależny od transportu.
// Page → host: koperty czekają w kolejce FIFO, dopóki binding hosta (`Runtime.addBinding`, ponawiany po nawigacji
// głównej ramki) nie stanie się funkcją. Host → page: `deliver(json)` z dowolnego transportu (dokłada go
// epic-netflix-dpad); tu jest tylko kontrakt strony: walidacja, duplikaty, jeden handler na `type`
// i odrzucanie spóźnionych odpowiedzi.
import { envelope, parseInbound, type DeliverResult } from './envelope';

/** Limit kolejki wychodzącej; przy pełnej kolejce wypada najstarsza koperta. */
export const QUEUE_LIMIT = 256;

/** Odstęp kolejnych prób w epizodzie czekania na binding. */
export const RETRY_MS = 50;

/** Liczba prób w jednym epizodzie: 600 × 50 ms = 30 s. */
export const MAX_ATTEMPTS = 600;

/** Ile ostatnich `id` host → page pamiętamy do odrzucania duplikatów. */
export const SEEN_IDS_LIMIT = 64;

/** Odpowiedź na żądanie hosta; `false`, gdy żądanie jest nieaktualne albo już ma odpowiedź (nic nie idzie). */
export type Reply = (replyType: string, payload?: unknown) => boolean;

/** Handler komunikatu host → page danego `type`. */
export type Handler = (payload: unknown, reply: Reply) => void;

/** API mostu dla ServiceDrivera (AD-6): driver nie widzi bindingu ani CDP. */
export interface PageBridge {
  /** Page → host z `id: null`. */
  send(type: string, payload?: unknown): void;
  /** Jedyny handler dla `type` (zastępuje wcześniejszy); zwraca funkcję wyrejestrowującą. */
  receive(type: string, handler: Handler): () => void;
}

/** Most z wejściem dla transportu hosta i wznowieniem czekania (bfcache). */
export interface BridgeRuntime {
  bridge: PageBridge;
  /** Komunikat host → page jako tekst JSON; nigdy nie rzuca, a przy odrzuceniu nic nie wysyła. */
  deliver(json: unknown): DeliverResult;
  /** Zaczyna nowy epizod czekania na binding (30 s od teraz). */
  restartWaiting(): void;
}

/** Okno widziane przez most: tylko binding o nazwie z configu. */
export type BindingHost = object;

export function createBridge(win: BindingHost, bindingName: string): BridgeRuntime {
  const queue: string[] = [];
  let timer: ReturnType<typeof setTimeout> | null = null;
  let attempts = 0;

  const handlers = new Map<string, Handler>();
  const seenIds: string[] = [];
  const latestIdByType = new Map<string, string>();

  const binding = (): unknown => (win as Record<string, unknown>)[bindingName];

  // Ponowienie w bieżącym epizodzie; po MAX_ATTEMPTS epizod się kończy, a kolejka zostaje do następnego `send`.
  const scheduleRetry = (): void => {
    if (timer !== null || attempts >= MAX_ATTEMPTS) return;
    attempts += 1;
    timer = setTimeout(() => {
      timer = null;
      flush();
    }, RETRY_MS);
  };

  // Wysyła kolejkę po kolei. Rzucający binding zostawia kopertę na początku kolejki i planuje ponowienie.
  const flush = (): void => {
    const target = binding();
    if (typeof target !== 'function') {
      scheduleRetry();
      return;
    }

    while (queue.length > 0) {
      try {
        target(queue[0]);
      } catch {
        scheduleRetry();
        return;
      }
      queue.shift();
    }
    attempts = 0;
  };

  const enqueue = (type: string, id: string | null, payload: unknown): void => {
    if (queue.length >= QUEUE_LIMIT) queue.shift();
    queue.push(JSON.stringify(envelope(type, id, payload)));
    // Bez trwającego epizodu (po rezygnacji albo przed pierwszym czekaniem) `send` zaczyna nowy.
    if (timer === null) attempts = 0;
    flush();
  };

  const restartWaiting = (): void => {
    attempts = 0;
  };

  const bridge: PageBridge = {
    send(type, payload) {
      enqueue(type, null, payload);
    },
    receive(type, handler) {
      handlers.set(type, handler);
      return () => {
        if (handlers.get(type) === handler) handlers.delete(type);
      };
    },
  };

  const deliver = (json: unknown): DeliverResult => {
    try {
      const parsed = parseInbound(json);
      if (!parsed.ok) return parsed.result;

      const { type, id, payload } = parsed.envelope;
      if (seenIds.includes(id)) return 'duplicate';
      seenIds.push(id);
      if (seenIds.length > SEEN_IDS_LIMIT) seenIds.shift();
      latestIdByType.set(type, id);

      const handler = handlers.get(type);
      if (!handler) return 'unhandled';

      let answered = false;
      // Spóźniona odpowiedź (AD-7): nowsze żądanie tego samego `type` albo druga odpowiedź nie wysyłają nic.
      const reply: Reply = (replyType, replyPayload) => {
        if (answered || latestIdByType.get(type) !== id) return false;
        answered = true;
        enqueue(replyType, id, replyPayload);
        return true;
      };

      try {
        handler(payload, reply);
      } catch {
        return 'error';
      }
      return 'handled';
    } catch {
      return 'error';
    }
  };

  return { bridge, deliver, restartWaiting };
}
