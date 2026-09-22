import type { BindingContext } from './bindingContext';
import type { Offers } from './designOffers';

/** A catchment model's words for the design tests: springs feed a reservoir, a reservoir supplies a village. */
export function catchmentOffers(compareKind?: string): Offers {
  return {
    kinds: ['Reservoir', 'Spring', 'Village'],
    compareKind,
    propertiesOf: (kind) =>
      kind === 'Spring'
        ? [{ name: 'flow', declaredBy: 'Spring', example: 12.5, numeric: true }, { name: 'name', declaredBy: 'Spring', example: 'SPRING-1', numeric: false }]
        : kind === 'Reservoir'
          ? [{ name: 'capacity', declaredBy: 'Reservoir', example: 900, numeric: true }, { name: 'level', declaredBy: 'Store', example: 0.4, numeric: true }]
          : [],
    edgesFrom: (kind) =>
      kind === 'Spring'
        ? [{ predicate: 'feeds', direction: 'out', reaches: 'Reservoir', count: 2 }]
        : kind === 'Reservoir'
          ? [{ predicate: 'feeds', direction: 'in', reaches: 'Spring', count: 2 }, { predicate: 'supplies', direction: 'out', reaches: 'Village', count: 1 }]
          : [],
    predicates: ['feeds', 'supplies'],
    kindOfThing: (name) => (name === 'SPRING-1' ? 'Spring' : undefined),
  };
}

export function catchmentContext(compareKind?: string): BindingContext {
  return {
    offers: catchmentOffers(compareKind),
    statesOf: (kind) => (kind === 'Spring' ? ['dry', 'flowing'] : kind === 'Reservoir' ? ['full'] : []),
    endpoints: ['intake'],
  };
}
