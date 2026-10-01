// The exact shape Tributary writes when an endpoint keeps what it fetched: "sha256:" and the
// 64 hex digits of the content. Only the whole string counts — a sentence mentioning a ticket
// is prose, not a reference the store can resolve — and only a scalar string, because a ticket
// is an ordinary property value, never a structure.
const TICKET_SHAPE = /^sha256:[0-9a-fA-F]{64}$/;

export function assetTicketOf(value: unknown): string | null {
  return typeof value === 'string' && TICKET_SHAPE.test(value) ? value : null;
}
