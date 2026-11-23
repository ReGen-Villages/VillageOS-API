/** Older lines are dropped so the page's element count stays bounded. */
export const MAX_LOG_LINES = 5000;

/** A new array, so React sees the change. */
export function appendLines(existing: string[], incoming: string[], max = MAX_LOG_LINES): string[] {
  if (incoming.length === 0) return existing;
  const combined = existing.concat(incoming);
  return combined.length > max ? combined.slice(combined.length - max) : combined;
}
