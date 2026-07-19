/**
 * Pure helpers for the generic entity-detail window. No React, no I/O — the hook
 * (useEntityDetail) fetches; these shape the results. Kept model-agnostic: predicate
 * names, archetypes, and property keys arrive via the {@link DetailSpec}, never hardcoded.
 */
import type { ModelIndex } from '../../../api/dashboardApi';
import { effectiveProperties } from '../../../utils/propertyMapper';
import type { RelationSpec } from '../../../types/dashboard';
import type { StateTransition, VosThing } from '../../../types/vos';

/** The one canonical predicate whose name is fixed by the platform (an archetype edge). */
const IS_PREDICATE = 'is';

/** A related Thing surfaced under a relation, with the chosen properties and any nested relations. */
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

/** One relation group on the card: every edge of a single {@link RelationSpec} from an anchor Thing. */
export interface ResolvedRelation {
  label: string;
  predicate: string;
  direction: 'out' | 'in';
  edges: ResolvedEdge[];
}

/** Archetype name for each Thing, from its direct `is`-edge. First writer wins. */
function archetypeNames(idx: ModelIndex): Map<string, string> {
  const isId = idx.predicateNameToId.get(IS_PREDICATE);
  const names = new Map<string, string>();
  if (!isId) return names;
  for (const rel of idx.relationships) {
    if (rel.PredicateId !== isId || names.has(rel.SubjectId)) continue;
    names.set(rel.SubjectId, idx.byId.get(rel.TargetId)?.Name ?? rel.TargetId);
  }
  return names;
}

function selectProperties(thing: VosThing, which: RelationSpec['properties']): [string, unknown][] {
  if (!which) return [];
  const props = effectiveProperties(thing);
  if (which === '*') return Object.entries(props);
  return which.filter((key) => key in props).map((key) => [key, props[key]] as const);
}

/**
 * Resolve the configured relations against the model, following each {@link RelationSpec} from the
 * root Thing outward. Array order is preserved (it is the display order); within a group the edges
 * are sorted by the related Thing's name for stability. Cycle-guarded per path so a relation that
 * loops back (Order references Wave, Wave contains Order) can't recurse forever.
 */
export function resolveRelations(
  rootId: string,
  idx: ModelIndex,
  specs: RelationSpec[] | undefined,
): ResolvedRelation[] {
  if (!specs?.length) return [];
  const archetypeOf = archetypeNames(idx);

  const walk = (anchorId: string, relationSpecs: RelationSpec[], visited: Set<string>): ResolvedRelation[] => {
    const anchorName = idx.byId.get(anchorId)?.Name ?? anchorId;

    return relationSpecs.map((spec) => {
      const direction = spec.direction ?? 'out';
      const predicateId = idx.predicateNameToId.get(spec.predicate);
      const inlineSpecs = spec.relations?.filter((child) => child.inline) ?? [];
      const nestedSpecs = spec.relations?.filter((child) => !child.inline) ?? [];
      const edges: ResolvedEdge[] = [];

      if (predicateId) {
        for (const rel of idx.relationships) {
          if (rel.PredicateId !== predicateId) continue;
          const relatedId =
            direction === 'out'
              ? rel.SubjectId === anchorId
                ? rel.TargetId
                : null
              : rel.TargetId === anchorId
                ? rel.SubjectId
                : null;
          if (!relatedId || visited.has(relatedId)) continue;
          if (spec.archetype && archetypeOf.get(relatedId) !== spec.archetype) continue;

          const related = idx.byId.get(relatedId);
          const relatedName = related?.Name ?? relatedId;
          const nextVisited = new Set(visited).add(relatedId);

          // Hoist each inline child's matched properties onto this row, ahead of the row's own.
          const hoisted = inlineSpecs.flatMap((child) =>
            walk(relatedId, [child], nextVisited).flatMap((group) => group.edges.flatMap((e) => e.properties)),
          );

          edges.push({
            thingId: relatedId,
            subjectName: direction === 'out' ? anchorName : relatedName,
            targetName: direction === 'out' ? relatedName : anchorName,
            relatedName,
            properties: [...hoisted, ...(related ? selectProperties(related, spec.properties) : [])],
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

/** Every related Thing id across the resolved tree — for fetching each one's derived states. */
export function flattenRelatedIds(relations: ResolvedRelation[]): string[] {
  const ids: string[] = [];
  const walk = (rels: ResolvedRelation[]) => {
    for (const group of rels) {
      for (const edge of group.edges) {
        ids.push(edge.thingId);
        walk(edge.children);
      }
    }
  };
  walk(relations);
  return ids;
}

/** A derived-state change of the root Thing, for the handling-history list. */
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
