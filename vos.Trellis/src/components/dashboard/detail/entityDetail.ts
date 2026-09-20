/**
 * Pure helpers for the generic entity-detail window. No React, no I/O — the hook
 * (useEntityDetail) fetches; these shape the results. Kept model-agnostic: predicate
 * names, archetypes, and property keys arrive via the {@link DetailSpec}, never hardcoded.
 */
import type { ModelIndex } from '../../../api/dashboardApi';
import { effectiveProperties } from '../../../utils/propertyMapper';
import type { RelationSpec } from '../../../types/dashboard';
import type { StateTransition, VosThing } from '../../../types/vos';

/** The one canonical predicate whose name is fixed by the platform (an archetype relationship). */
const IS_PREDICATE = 'is';

export interface ResolvedEdge {
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

function selectProperties(thing: VosThing, which: RelationSpec['properties'], index: ModelIndex): [string, unknown][] {
  if (!which) return [];
  const props = effectiveProperties(thing, index);
  if (which === '*') return Object.entries(props);
  return which.filter((key) => key in props).map((key) => [key, props[key]] as const);
}

/**
 * Resolve the configured relations against the model, following each {@link RelationSpec} from the
 * root Thing outward. Array order is preserved (it is the display order); within a group the relationships
 * are sorted by the related Thing's name for stability. Cycle-guarded per path so a relation that
 * loops back (a project references a phase, the phase contains the project) can't recurse forever.
 */
export function resolveRelations(
  rootId: string,
  index: ModelIndex,
  specs: RelationSpec[] | undefined,
): ResolvedRelation[] {
  if (!specs?.length) return [];
  const archetypeOf = archetypeNames(index);

  const walk = (anchorId: string, relationSpecs: RelationSpec[], visited: Set<string>): ResolvedRelation[] => {
    const anchorName = index.byId.get(anchorId)?.Name ?? anchorId;

    return relationSpecs.map((spec) => {
      const direction = spec.direction ?? 'out';
      const predicateId = index.predicateNameToId.get(spec.predicate);
      const inlineSpecs = spec.relations?.filter((child) => child.inline) ?? [];
      const nestedSpecs = spec.relations?.filter((child) => !child.inline) ?? [];
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
          if (spec.archetype && archetypeOf.get(relatedId) !== spec.archetype) continue;

          const related = index.byId.get(relatedId);
          const relatedName = related?.Name ?? relatedId;
          const nextVisited = new Set(visited).add(relatedId);

          const hoisted = inlineSpecs.flatMap((child) =>
            walk(relatedId, [child], nextVisited).flatMap((group) => group.edges.flatMap((e) => e.properties)),
          );

          edges.push({
            thingId: relatedId,
            subjectName: direction === 'out' ? anchorName : relatedName,
            targetName: direction === 'out' ? relatedName : anchorName,
            relatedName,
            properties: [...hoisted, ...(related ? selectProperties(related, spec.properties, index) : [])],
            children: nestedSpecs.length ? walk(relatedId, nestedSpecs, nextVisited) : [],
          });
        }
      }

      edges.sort((a, b) => a.relatedName.localeCompare(b.relatedName));
      return { label: spec.label ?? spec.predicate, predicate: spec.predicate, direction, edges };
    });
  };

  return walk(rootId, specs, new Set([rootId]));
}

export interface StateChange {
  at: string;
  entered: string[];
  exited: string[];
}

/**
 * The root Thing's derived-state changes, oldest first. Transitions that neither enter nor exit a
 * state (a property write that didn't move a boundary) are dropped — only actual state moves show.
 */
export function buildStateChanges(transitions: StateTransition[]): StateChange[] {
  return transitions
    .filter((t) => t.Entered.length || t.Exited.length)
    .map((t) => ({ at: t.At, entered: [...t.Entered], exited: [...t.Exited] }))
    .sort((a, b) => new Date(a.at).getTime() - new Date(b.at).getTime());
}
