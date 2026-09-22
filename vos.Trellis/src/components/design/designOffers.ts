import type { ModelIndex } from '../../api/dashboardApi';
import type { RelationStep } from '../../types/dashboard';
import { edgesFrom, kindsOffered, propertiesOf, type EdgeCandidate, type PropertyCandidate } from '../../api/modelDeclaration';

/**
 * The model's own words, as the binding builder offers them: the three readings the Compose page
 * built, remembered per kind for the life of one index, and the kind a path has reached so far —
 * which is what the next hop and the property at the end are offered for.
 */
export interface Offers {
  kinds: string[];
  /** The kind the page compares, which is what `$scope` names at the top of a page. */
  compareKind?: string;
  propertiesOf: (kind: string) => PropertyCandidate[];
  edgesFrom: (kind: string) => EdgeCandidate[];
  /** Every predicate the model holds a relationship under, `is` aside. */
  predicates: string[];
  /** The kind a Thing named here is one of, for a binding that reads a Thing by name. */
  kindOfThing: (name: string) => string | undefined;
}

export function offersFor(modelIndex: ModelIndex, compareKind?: string): Offers {
  const properties = new Map<string, PropertyCandidate[]>();
  const edges = new Map<string, EdgeCandidate[]>();
  const remembered = <T>(held: Map<string, T>, kind: string, read: () => T): T => {
    const known = held.get(kind);
    if (known) return known;
    const found = read();
    held.set(kind, found);
    return found;
  };
  return {
    kinds: kindsOffered(modelIndex),
    compareKind,
    propertiesOf: (kind) => remembered(properties, kind, () => propertiesOf(kind, modelIndex)),
    edgesFrom: (kind) => remembered(edges, kind, () => edgesFrom(kind, modelIndex)),
    predicates: [...modelIndex.predicateNameToId.keys()].filter((name) => name !== 'is').sort((a, b) => a.localeCompare(b)),
    kindOfThing: (name) => {
      const thing = modelIndex.byName.get(name);
      const parent = thing ? modelIndex.isParents.get(thing.Id)?.[0] : undefined;
      return parent ? modelIndex.byId.get(parent)?.Name : undefined;
    },
  };
}

/** The kind at the end of a path from the start kind: what a step states, or else what the links the
 *  model holds say that predicate reaches from there. Nothing where a step reaches no kind the model
 *  knows, and then no property is offered at the end — a free name is still taken. */
export function kindReachedBy(start: string | undefined, steps: RelationStep[], offers: Offers): string | undefined {
  let kind = start;
  for (const step of steps) {
    if (step.archetype) {
      kind = step.archetype;
      continue;
    }
    if (!kind) return undefined;
    const direction = step.direction ?? 'out';
    const reached = offers.edgesFrom(kind).find((edge) => edge.predicate === step.predicate && edge.direction === direction);
    kind = reached?.reaches;
    if (!kind) return undefined;
  }
  return kind;
}

/** The links of one kind grouped by predicate, for a list that offers each predicate once with every
 *  way it runs. */
export function edgesByPredicate(edges: EdgeCandidate[]): Map<string, EdgeCandidate[]> {
  const grouped = new Map<string, EdgeCandidate[]>();
  for (const edge of edges) grouped.set(edge.predicate, [...(grouped.get(edge.predicate) ?? []), edge]);
  return grouped;
}
