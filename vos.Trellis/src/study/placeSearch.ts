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

export function searchOnSubmit(_request: SearchRequest): SearchDecision {
  throw new Error('not built yet');
}
