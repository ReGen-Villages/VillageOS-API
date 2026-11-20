/** Names no predicate, archetype or property key; each arrives in the {@link DetailSpec}. */
import type { ModelIndex } from '@tendril-ui/tendril';
import { effectiveProperties } from '@tendril-ui/tendril';
import type { RelationSpecification } from '@tendril-ui/tendril';
import type { StateTransition, VosThing } from '@tendril-ui/tendril';

/** The one canonical predicate whose name is fixed by the platform (an archetype relationship). */
const IS_PREDICATE = 'is';

interface ResolvedEdge {
  thingId: string;
  /** Subject and target of the underlying relationship, so the card can render "A —predicate→ B". */
  subjectName: string;
  targetName: string;
  /** The related Thing — the endpoint that is not the anchor the relation was followed from. */
  relatedName: string;
  properties: [string, unknown][];
  children: ResolvedRelation[];
}

export interface ResolvedRelation {
  label: string;
  predicate: string;
  direction: 'out' | 'in';
  edges: ResolvedEdge[];
}

/** Archetype name for each Thing, from its direct `is`-relationship. First writer wins. */
function archetypeNames(index: ModelIndex): Map<string, string> {
  const isId = index.predicateNameToId.get(IS_PREDICATE);
  const names = new Map<string, string>();
  if (!isId) return names;
  for (const relationship of index.relationships) {
    if (relationship.PredicateId !== isId || names.has(relationship.SubjectId)) continue;
    names.set(relationship.SubjectId, index.byId.get(relationship.TargetId)?.Name ?? relationship.TargetId);
  }
  return names;
}

function selectProperties(thing: VosThing, which: RelationSpecification['properties'], index: ModelIndex): [string, unknown][] {
  if (!which) return [];
  const props = effectiveProperties(thing, index);
  if (which === '*') return Object.entries(props);
  return which.filter((key) => key in props).map((key) => [key, props[key]] as const);
}

/** The configured order is the display order; within a group, by the related Thing's name, so it stays put. Each
 *  path guards against looping back, as a site containing the building that refers to it would. */
export function resolveRelations(
  rootId: string,
  index: ModelIndex,
  specifications: RelationSpecification[] | undefined,
): ResolvedRelation[] {
  if (!specifications?.length) return [];
  const archetypeOf = archetypeNames(index);

  const walk = (anchorId: string, relationSpecifications: RelationSpecification[], visited: Set<string>): ResolvedRelation[] => {
    const anchorName = index.byId.get(anchorId)?.Name ?? anchorId;

    return relationSpecifications.map((specification) => {
      const direction = specification.direction ?? 'out';
      const predicateId = index.predicateNameToId.get(specification.predicate);
      const inlineSpecifications = specification.relations?.filter((child) => child.inline) ?? [];
      const nestedSpecifications = specification.relations?.filter((child) => !child.inline) ?? [];
      const edges: ResolvedEdge[] = [];

      if (predicateId) {
        for (const relationship of index.relationships) {
          if (relationship.PredicateId !== predicateId) continue;
          const relatedId =
            direction === 'out'
              ? relationship.SubjectId === anchorId
                ? relationship.TargetId
                : null
              : relationship.TargetId === anchorId
                ? relationship.SubjectId
                : null;
          if (!relatedId || visited.has(relatedId)) continue;
          if (specification.archetype && archetypeOf.get(relatedId) !== specification.archetype) continue;

          const related = index.byId.get(relatedId);
          const relatedName = related?.Name ?? relatedId;
          const nextVisited = new Set(visited).add(relatedId);

          const hoisted = inlineSpecifications.flatMap((child) =>
            walk(relatedId, [child], nextVisited).flatMap((group) => group.edges.flatMap((e) => e.properties)),
          );

          edges.push({
            thingId: relatedId,
            subjectName: direction === 'out' ? anchorName : relatedName,
            targetName: direction === 'out' ? relatedName : anchorName,
            relatedName,
            properties: [...hoisted, ...(related ? selectProperties(related, specification.properties, index) : [])],
            children: nestedSpecifications.length ? walk(relatedId, nestedSpecifications, nextVisited) : [],
          });
        }
      }

      edges.sort((a, b) => a.relatedName.localeCompare(b.relatedName));
      return { label: specification.label ?? specification.predicate, predicate: specification.predicate, direction, edges };
    });
  };

  return walk(rootId, specifications, new Set([rootId]));
}

export interface StateChange {
  at: string;
  entered: string[];
  exited: string[];
}

/** A transition that neither enters nor leaves a state, a property write that crossed no boundary, is dropped. */
export function buildStateChanges(transitions: StateTransition[]): StateChange[] {
  return transitions
    .filter((t) => t.Entered.length || t.Exited.length)
    .map((t) => ({ at: t.At, entered: [...t.Entered], exited: [...t.Exited] }))
    .sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime());
}
