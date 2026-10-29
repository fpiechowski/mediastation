// Koperta protokołu strony v1 (AD-7): { v: 1, type, id, payload }, każde pole zawsze obecne.
// Page → host z własnej inicjatywy ma `id: null`; host → page ma niepusty string `id`, a odpowiedź go powtarza.
// Brak payloadu to `payload: null`, a nie pominięte pole.

export const ENVELOPE_VERSION = 1;

export interface Envelope<TPayload = unknown> {
  v: number;
  type: string;
  id: string | null;
  payload: TPayload;
}

/** Komunikat host → page po walidacji: `id` jest zawsze niepustym stringiem. */
export interface InboundEnvelope extends Envelope {
  id: string;
}

/** Wynik `deliver` dla komunikatu host → page. */
export type DeliverResult = 'handled' | 'unhandled' | 'duplicate' | 'invalid' | 'version' | 'error';

export type ParsedInbound =
  | { ok: true; envelope: InboundEnvelope }
  | { ok: false; result: Extract<DeliverResult, 'invalid' | 'version'> };

/** Koperta v1 z jawnym `null` zamiast brakującego payloadu. */
export function envelope<TPayload>(type: string, id: string | null, payload?: TPayload): Envelope<TPayload | null> {
  return { v: ENVELOPE_VERSION, type, id, payload: payload === undefined ? null : payload };
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}

/**
 * Czysty odczyt komunikatu host → page, w kolejności: nie-JSON albo nie-obiekt → `invalid`; `v` nie jest liczbą
 * całkowitą → `invalid` (jak `envelope` w C# `PageProtocol.Parse`); całkowite `v` ≠ 1 → `version`; `type` lub `id` nie jest niepustym stringiem albo brak pola `payload` → `invalid`.
 * Nigdy nie rzuca.
 */
export function parseInbound(json: unknown): ParsedInbound {
  if (typeof json !== 'string') return { ok: false, result: 'invalid' };

  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    return { ok: false, result: 'invalid' };
  }

  if (typeof value !== 'object' || value === null || Array.isArray(value)) return { ok: false, result: 'invalid' };

  const e = value as Record<string, unknown>;
  if (typeof e.v !== 'number' || !Number.isInteger(e.v)) return { ok: false, result: 'invalid' };
  if (e.v !== ENVELOPE_VERSION) return { ok: false, result: 'version' };
  if (!isNonEmptyString(e.type) || !isNonEmptyString(e.id) || !Object.hasOwn(e, 'payload')) {
    return { ok: false, result: 'invalid' };
  }

  return { ok: true, envelope: { v: e.v, type: e.type, id: e.id, payload: e.payload } };
}
