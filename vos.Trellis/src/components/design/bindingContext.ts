import type { Offers } from './designOffers';

/** What the builder offers a binding's fields from, and where the binding stands. */
export interface BindingContext {
  offers: Offers;
  statesOf: (kind: string) => string[];
  /** The subdomains the platform registers endpoints under. */
  endpoints: string[];
  /** Inside a column worked out per row, the kind of the row — which is what `$scope` names there. */
  rowKind?: string;
  /** The keys a row carries, for the fields that name one. */
  rowKeys?: string[];
}
