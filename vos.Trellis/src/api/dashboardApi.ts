/**
 * Dashboard discovery + generic binding resolver.
 *
 * Discovery: a model declares Things of archetype `Dashboard` (see
 * DASHBOARD_ARCHETYPE) each with a `spec` JSON property. We find them in the
 * already-loaded model store and parse their specs.
 *
 * Resolution: every widget slot is a {@link Binding}. `resolveBinding` maps the
 * generic binding *kind* to a concrete data source (stateApi / model store /
 * temporalApi / a model-side service). The binding *values* — state names,
 * archetypes, properties — come from the model, never from this file.
 */
import type { VosThing, VosRelationship } from '../types/vos';
import {
  DASHBOARD_ARCHETYPE,
  DASHBOARD_SPEC_PROPERTY,
  type Binding,
  type DashboardDescriptor,
  type DashboardSpec,
  type ScopeEntity,
  type ScopeRef,
  type PropertyFilter,
} from '../types/dashboard';
import { stateApi } from './stateApi';
import { temporalApi } from './temporalApi';
import { apiClient } from './client';
import { effectiveProperties } from '../utils/propertyMapper';

const IS_PREDICATE = 'is';
/** The spec's reference to "the compare entity currently selected in the scope switcher". */
const SCOPE_REF = '$scope';

/** Row shape returned by stateList / aggregate-list / service table bindings. */
export type Row = Record<string, unknown>;
/** A resolved binding value: a scalar, a table, or a series. */
export type BindingResult = number | Row[] | number[] | null;

// ---- model-store indexes ------------------------------------------------

export interface ModelIndex {
  byId: Map<string, VosThing>;
  byName: Map<string, VosThing>;
  relationships: VosRelationship[];
  /** predicate name → id, and id → name */
  predicateNameToId: Map<string, string>;
  predicateIdToName: Map<string, string>;
  /** archetype id → ids of Things directly `is`-linked to it (Bug #5942). */
  isChildren: Map<string, string[]>;
  /** ids that are the target of any `is`-edge — i.e. Things acting as an archetype. */
  isTargets: Set<string>;
  /** Thing id → ids of the archetypes it is directly `is`-linked to (its parents). Used to
   *  resolve inherited property defaults up the `is`-chain (Bug #6048). */
  isParents: Map<string, string[]>;
}

export function buildModelIndex(things: VosThing[], relationships: VosRelationship[]): ModelIndex {
  const byId = new Map<string, VosThing>();
  const byName = new Map<string, VosThing>();
  for (const t of things) {
    byId.set(t.Id, t);
    // First writer wins for duplicate names (archetypes are unique by name).
    if (!byName.has(t.Name)) byName.set(t.Name, t);
  }
  const predicateNameToId = new Map<string, string>();
  const predicateIdToName = new Map<string, string>();
  for (const r of relationships) {
    const p = byId.get(r.PredicateId);
    if (p) {
      predicateIdToName.set(r.PredicateId, p.Name);
      if (!predicateNameToId.has(p.Name)) predicateNameToId.set(p.Name, r.PredicateId);
    }
  }
  // Precompute the `is`-hierarchy once (Bug #5942): children-by-archetype for a
  // transitive walk, and the set of all archetype (is-target) ids to tell types
  // from instances. Replaces a per-query scan of every relationship.
  const isId = predicateNameToId.get(IS_PREDICATE);
  const isChildren = new Map<string, string[]>();
  const isTargets = new Set<string>();
  const isParents = new Map<string, string[]>();
  if (isId) {
    for (const r of relationships) {
      if (r.PredicateId !== isId) continue;
      isTargets.add(r.TargetId);
      const kids = isChildren.get(r.TargetId);
      if (kids) kids.push(r.SubjectId);
      else isChildren.set(r.TargetId, [r.SubjectId]);
      const parents = isParents.get(r.SubjectId);
      if (parents) parents.push(r.TargetId);
      else isParents.set(r.SubjectId, [r.TargetId]);
    }
  }
  return { byId, byName, relationships, predicateNameToId, predicateIdToName, isChildren, isTargets, isParents };
}

/**
 * Ids of the Things that are of the given archetype, **transitively** over the `is`-chain
 * and counting **instances only** (Bug #5942). Archetypes are subtyped (Customer is Party,
 * PickLocation is Location), so a direct-edge match would miss every real instance under a
 * parent archetype. We descend the is-chain; a Thing that is itself an `is`-target is treated
 * as an archetype/sub-type and descended into, not counted. Cycle-guarded.
 */
export function thingIdsOfArchetype(archetype: string, idx: ModelIndex): Set<string> {
  const archThing = idx.byName.get(archetype);
  const out = new Set<string>();
  if (!archThing) return out;
  const seen = new Set<string>();          // archetype nodes already descended (cycle guard)
  const frontier = [archThing.Id];
  while (frontier.length) {
    const current = frontier.pop()!;
    if (seen.has(current)) continue;
    seen.add(current);
    for (const childId of idx.isChildren.get(current) ?? []) {
      if (idx.isTargets.has(childId)) frontier.push(childId);  // a sub-archetype — descend
      else out.add(childId);                                   // a real instance — count it
    }
  }
  return out;
}

export function thingsOfArchetype(archetype: string, idx: ModelIndex): VosThing[] {
  const ids = thingIdsOfArchetype(archetype, idx);
  const out: VosThing[] = [];
  for (const id of ids) {
    const t = idx.byId.get(id);
    if (t) out.push(t);
  }
  return out;
}

// ---- discovery ----------------------------------------------------------

/** Discover dashboards from an already-built model index. Prefer this on the hot path
 *  so the caller can share one index across discovery, scope, and binding resolution
 *  instead of rebuilding it three times per model change. */
export function discoverDashboardsFromIndex(idx: ModelIndex): DashboardDescriptor[] {
  const out: DashboardDescriptor[] = [];
  for (const t of thingsOfArchetype(DASHBOARD_ARCHETYPE, idx)) {
    const raw = effectiveProperties(t, idx)[DASHBOARD_SPEC_PROPERTY];
    const spec = parseSpec(raw);
    if (spec) out.push({ id: t.Id, name: t.Name, spec });
  }
  return out;
}

export function discoverDashboards(
  things: VosThing[],
  relationships: VosRelationship[],
): DashboardDescriptor[] {
  return discoverDashboardsFromIndex(buildModelIndex(things, relationships));
}

function parseSpec(raw: unknown): DashboardSpec | null {
  let obj: unknown = raw;
  if (typeof raw === 'string') {
    try {
      obj = JSON.parse(raw);
    } catch {
      return null;
    }
  }
  if (obj && typeof obj === 'object' && Array.isArray((obj as DashboardSpec).sections)) {
    return obj as DashboardSpec;
  }
  return null;
}

/** Compare entities (e.g. sites) offered in the scope switcher. */
export function scopeEntities(spec: DashboardSpec, idx: ModelIndex): ScopeEntity[] {
  if (!spec.compare) return [];
  return thingsOfArchetype(spec.compare.archetype, idx)
    .map((t) => ({ id: t.Id, name: t.Name }))
    .sort((a, b) => a.name.localeCompare(b.name));
}

// ---- resolution ---------------------------------------------------------

export interface ResolveContext {
  idx: ModelIndex;
  /** Selected compare-entity id, or null for "All". */
  scopeId: string | null;
  /** Archetype of compare entities (for `$scope` averaging + compareEntities). */
  compareArchetype?: string;
  /** Bumped on live events to force re-resolution of server-side bindings. Part of identity only. */
  nonce?: number;
}

/** Members reachable from the scope entity by following a predicate transitively.
 *  The walk is transitive because a scope predicate can nest: the entity relates to
 *  intermediate Things that in turn relate to the ones a widget counts, and a one-hop walk
 *  would stop at the intermediates. The scope entity is never a member of its own scope. */
function scopeMemberIds(scope: ScopeRef | undefined, ctx: ResolveContext): Set<string> | null {
  if (!scope || !ctx.scopeId) return null;
  const pid = ctx.idx.predicateNameToId.get(scope.viaPredicate);
  if (!pid) return new Set();
  const inbound = scope.direction === 'in';
  const adjacency = new Map<string, string[]>();
  for (const r of ctx.idx.relationships) {
    if (r.PredicateId !== pid) continue;
    const [from, to] = inbound ? [r.TargetId, r.SubjectId] : [r.SubjectId, r.TargetId];
    const next = adjacency.get(from);
    if (next) next.push(to);
    else adjacency.set(from, [to]);
  }
  const members = new Set<string>();
  const walked = new Set<string>([ctx.scopeId]);   // seeded so a cycle back to the scope re-adds nothing
  const frontier = [ctx.scopeId];
  while (frontier.length) {
    for (const next of adjacency.get(frontier.pop()!) ?? []) {
      if (walked.has(next)) continue;
      walked.add(next);
      members.add(next);
      frontier.push(next);
    }
  }
  return members;
}

function num(v: unknown): number {
  if (typeof v === 'number') return v;
  if (typeof v === 'string' && v.trim() !== '' && !isNaN(Number(v))) return Number(v);
  if (typeof v === 'boolean') return v ? 1 : 0;
  return NaN;
}

function passesFilters(thing: VosThing, filters: PropertyFilter[] | undefined, idx: ModelIndex): boolean {
  if (!filters) return true;
  for (const f of filters) {
    const v = effectiveProperties(thing, idx)[f.property];
    switch (f.op) {
      case '=': if (v !== f.value) return false; break;
      case '!=': if (v === f.value) return false; break;
      case '>': if (!(num(v) > num(f.value))) return false; break;
      case '>=': if (!(num(v) >= num(f.value))) return false; break;
      case '<': if (!(num(v) < num(f.value))) return false; break;
      case '<=': if (!(num(v) <= num(f.value))) return false; break;
      case 'in': if (!Array.isArray(f.value) || !f.value.includes(v)) return false; break;
    }
  }
  return true;
}

/** Substitute the `$scope` placeholder anywhere in a service binding's body with the selected
 *  compare-entity id — the same reference a `property` binding uses, so a model-side service can be
 *  asked for one entity's numbers. With "All" selected the placeholder resolves to null and the
 *  service answers for everything. */
function withScope(body: unknown, scopeId: string | null): unknown {
  if (body === SCOPE_REF) return scopeId;
  if (Array.isArray(body)) return body.map((item) => withScope(item, scopeId));
  if (body && typeof body === 'object') {
    return Object.fromEntries(
      Object.entries(body as Record<string, unknown>).map(([key, value]) => [key, withScope(value, scopeId)]),
    );
  }
  return body;
}

function selectPath(obj: unknown, path?: string): unknown {
  if (!path) return obj;
  return path.split('.').reduce<unknown>((acc, key) => {
    if (acc && typeof acc === 'object') return (acc as Record<string, unknown>)[key];
    return undefined;
  }, obj);
}

export async function resolveBinding(binding: Binding, ctx: ResolveContext): Promise<BindingResult> {
  switch (binding.kind) {
    case 'const':
      return binding.value;

    case 'property': {
      if (binding.thing === SCOPE_REF) {
        if (ctx.scopeId) {
          const t = ctx.idx.byId.get(ctx.scopeId);
          return t ? num(effectiveProperties(t, ctx.idx)[binding.property]) : null;
        }
        // "All" → average across compare entities.
        const ents = ctx.compareArchetype ? thingsOfArchetype(ctx.compareArchetype, ctx.idx) : [];
        const vals = ents.map((t) => num(effectiveProperties(t, ctx.idx)[binding.property])).filter((n) => !isNaN(n));
        return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : null;
      }
      const t = ctx.idx.byId.get(binding.thing) ?? ctx.idx.byName.get(binding.thing);
      return t ? num(effectiveProperties(t, ctx.idx)[binding.property]) : null;
    }

    case 'aggregate': {
      let items = thingsOfArchetype(binding.archetype, ctx.idx).filter((t) =>
        passesFilters(t, binding.where, ctx.idx),
      );
      const members = scopeMemberIds(binding.scope, ctx);
      if (members) items = items.filter((t) => members.has(t.Id));
      if (binding.op === 'count') return items.length;
      const vals = items
        .map((t) => num(effectiveProperties(t, ctx.idx)[binding.property ?? '']))
        .filter((n) => !isNaN(n));
      if (!vals.length) return 0;
      switch (binding.op) {
        case 'sum': return vals.reduce((a, b) => a + b, 0);
        case 'avg': return vals.reduce((a, b) => a + b, 0) / vals.length;
        case 'min': return Math.min(...vals);
        case 'max': return Math.max(...vals);
      }
      return 0;
    }

    case 'ratio': {
      const [numerator, denominator] = await Promise.all([
        resolveBinding(binding.numerator, ctx),
        resolveBinding(binding.denominator, ctx),
      ]);
      const top = asNumber(numerator);
      const bottom = asNumber(denominator);
      if (top === null || bottom === null || bottom === 0) return null;
      return top / bottom;
    }

    case 'compareEntities': {
      const ents = ctx.compareArchetype ? thingsOfArchetype(ctx.compareArchetype, ctx.idx) : [];
      const computed = binding.computed ?? [];
      return Promise.all(
        ents.map(async (t) => {
          const row: Row = { id: t.Id, name: t.Name };
          for (const p of binding.properties) row[p] = num(effectiveProperties(t, ctx.idx)[p]);
          // Each computed column resolves with the Thing as the scope, so the same binding a
          // $scope-driven widget uses yields that Thing's own value here.
          const values = await Promise.all(
            computed.map((column) => resolveBinding(column.value, { ...ctx, scopeId: t.Id })),
          );
          computed.forEach((column, i) => (row[column.key] = asNumber(values[i])));
          return row;
        }),
      );
    }

    case 'stateCount': {
      const resp = await stateApi.getThingsInState(binding.state);
      const members = scopeMemberIds(binding.scope, ctx);
      const ofArchetype = binding.archetype ? thingIdsOfArchetype(binding.archetype, ctx.idx) : null;
      let list = resp.Things ?? [];
      if (members) list = list.filter((t) => members.has(t.Id));
      if (ofArchetype) list = list.filter((t) => ofArchetype.has(t.Id));
      return list.length;
    }

    case 'stateList': {
      const resp = await stateApi.getThingsInState(binding.state);
      const members = scopeMemberIds(binding.scope, ctx);
      const ofArchetype = binding.archetype ? thingIdsOfArchetype(binding.archetype, ctx.idx) : null;
      // The derived statuses nest (a harvested plot is also growing/planted/…), so a plain
      // stateList for an early stage includes every later one. excludeState removes the things
      // that advanced past this stage — leaving only those that reached it and no further.
      const advanced = binding.excludeState
        ? new Set((await stateApi.getThingsInState(binding.excludeState)).Things?.map((t) => t.Id) ?? [])
        : null;
      let list = resp.Things ?? [];
      if (members) list = list.filter((t) => members.has(t.Id));
      if (ofArchetype) list = list.filter((t) => ofArchetype.has(t.Id));
      if (advanced) list = list.filter((t) => !advanced.has(t.Id));
      if (binding.limit) list = list.slice(0, binding.limit);
      return list.map((ref) => {
        const full = ctx.idx.byId.get(ref.Id);
        return { id: ref.Id, name: ref.Name, ...(full ? effectiveProperties(full, ctx.idx) : {}) } as Row;
      });
    }

    case 'thingList': {
      let list = thingsOfArchetype(binding.archetype, ctx.idx);
      const members = scopeMemberIds(binding.scope, ctx);
      if (members) list = list.filter((t) => members.has(t.Id));
      list.sort((a, b) => a.Name.localeCompare(b.Name));
      if (binding.limit) list = list.slice(0, binding.limit);
      return list.map((t) => ({ id: t.Id, name: t.Name, ...effectiveProperties(t, ctx.idx) }) as Row);
    }

    case 'timeseries':
      return resolveTimeseries(binding, ctx);

    case 'service': {
      try {
        const resp = await apiClient.post<unknown>(binding.endpoint, withScope(binding.body ?? {}, ctx.scopeId));
        const picked = selectPath(resp, binding.select);
        return (picked ?? null) as BindingResult;
      } catch {
        return null;
      }
    }
  }
}

/** Best-effort bucketed series from the temporal API. Returns [] when history is absent. */
async function resolveTimeseries(
  binding: Extract<Binding, { kind: 'timeseries' }>,
  ctx: ResolveContext,
): Promise<number[]> {
  try {
    if (binding.thing && binding.property) {
      const t = ctx.idx.byName.get(binding.thing) ?? ctx.idx.byId.get(binding.thing);
      if (!t) return [];
      const versions = await temporalApi.getPropertyVersions(t.Id, binding.property);
      const points = (versions?.Versions ?? [])
        .map((v) => num((v as { Value?: unknown }).Value))
        .filter((n) => !isNaN(n));
      return points;
    }
    return [];
  } catch {
    return [];
  }
}

// ---- coercion helpers for widgets --------------------------------------

export function asNumber(r: BindingResult): number | null {
  return typeof r === 'number' && !isNaN(r) ? r : null;
}
export function asRows(r: BindingResult): Row[] {
  return Array.isArray(r) && (r.length === 0 || typeof r[0] === 'object') ? (r as Row[]) : [];
}
export function asSeries(r: BindingResult): number[] {
  return Array.isArray(r) && (r.length === 0 || typeof r[0] === 'number') ? (r as number[]) : [];
}

/** Case-insensitive substring filter over table rows. `keys` limits which cells are matched;
 *  omit it to match every value in the row. An empty query returns the rows unchanged. */
export function filterRows(rows: Row[], query: string | undefined, keys?: string[]): Row[] {
  const q = (query ?? '').trim().toLowerCase();
  if (!q) return rows;
  return rows.filter((row) => {
    const values = keys && keys.length ? keys.map((k) => row[k]) : Object.values(row);
    return values.some((v) => v != null && String(v).toLowerCase().includes(q));
  });
}
