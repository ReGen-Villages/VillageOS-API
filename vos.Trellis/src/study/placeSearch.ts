import { positionFromText } from './coordinateText';

export interface SearchRequest {
  readonly text: string;
  readonly now: number;
  readonly lastSearchAt: number | null;
  readonly currentAddress: string | null;
}

export type SearchDecision =
  | { readonly kind: 'empty' }
  | { readonly kind: 'unchanged' }
  | { readonly kind: 'position'; readonly latitude: number; readonly longitude: number }
  | { readonly kind: 'search'; readonly query: string; readonly afterMilliseconds: number };

/** The place search the intake forwards to allows one request a second, so each waits a little past that. */
const SEARCH_SPACING_MILLISECONDS = 1100;

export function searchOnSubmit({ text, now, lastSearchAt, currentAddress }: SearchRequest): SearchDecision {
  const query = text.trim();
  if (!query) return { kind: 'empty' };
  if (query === currentAddress) return { kind: 'unchanged' };
  const typed = positionFromText(query);
  if (typed.kind === 'position') return typed;
  const since = lastSearchAt === null ? SEARCH_SPACING_MILLISECONDS : now - lastSearchAt;
  return { kind: 'search', query, afterMilliseconds: Math.max(0, SEARCH_SPACING_MILLISECONDS - since) };
}
