/** Max log lines kept in the Log page view; older lines are dropped so the DOM stays bounded. */
export const MAX_LOG_LINES = 5000;

/**
 * Append incoming lines to the existing buffer, keeping only the most recent `max`.
 * Returns a new array (never mutates the input) so React state updates cleanly.
 */
export function appendLines(existing: string[], incoming: string[], max = MAX_LOG_LINES): string[] {
  if (incoming.length === 0) return existing;
  const combined = existing.concat(incoming);
  return combined.length > max ? combined.slice(combined.length - max) : combined;
}
