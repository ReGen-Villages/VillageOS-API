// The shape Tributary writes when an endpoint keeps what it fetched. Only a whole string counts, because a
// sentence mentioning a ticket is prose, and only a plain value, because a ticket is never a structure.
const TICKET_SHAPE = /^sha256:[0-9a-fA-F]{64}$/;

export function assetTicketOf(value: unknown): string | null {
  return typeof value === 'string' && TICKET_SHAPE.test(value) ? value : null;
}
