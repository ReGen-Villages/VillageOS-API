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

const IS_PREDICATE = 'is';

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
  return { byId, byName, relationships, predicateNameToId, predicateIdToName };
}

/** Ids of Things that are `is`-linked to the given archetype name. */
export function thingIdsOfArchetype(archetype: string, idx: ModelIndex): Set<string> {
  const archThing = idx.byName.get(archetype);
  const isId = idx.predicateNameToId.get(IS_PREDICATE);
  const out = new Set<string>();
  if (!archThing || !isId) return out;
  for (const r of idx.relationships) {
    if (r.PredicateId === isId && r.TargetId === archThing.Id) out.add(r.SubjectId);
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

export function discoverDashboards(
  things: VosThing[],
  relationships: VosRelationship[],
): DashboardDescriptor[] {
  const idx = buildModelIndex(things, relationships);
  const out: DashboardDescriptor[] = [];
  for (const t of thingsOfArchetype(DASHBOARD_ARCHETYPE, idx)) {
    const raw = t.Properties?.[DASHBOARD_SPEC_PROPERTY];
    const spec = parseSpec(raw);
    if (spec) out.push({ id: t.Id, name: t.Name, spec });
  }
  return out;
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

/** Members related to the scope entity via a predicate (for scoped counts). */
function scopeMemberIds(scope: ScopeRef | undefined, ctx: ResolveContext): Set<string> | null {
  if (!scope || !ctx.scopeId) return null;
  const pid = ctx.idx.predicateNameToId.get(scope.viaPredicate);
  if (!pid) return new Set();
  const members = new Set<string>();
  const inbound = scope.direction === 'in';
  for (const r of ctx.idx.relationships) {
    if (r.PredicateId !== pid) continue;
    if (inbound) {
      if (r.TargetId === ctx.scopeId) members.add(r.SubjectId);
    } else if (r.SubjectId === ctx.scopeId) {
      members.add(r.TargetId);
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

function passesFilters(thing: VosThing, filters: PropertyFilter[] | undefined): boolean {
  if (!filters) return true;
  for (const f of filters) {
    const v = thing.Properties?.[f.property];
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
      if (binding.thing === '$scope') {
        if (ctx.scopeId) {
          const t = ctx.idx.byId.get(ctx.scopeId);
          return t ? num(t.Properties?.[binding.property]) : null;
        }
        // "All" → average across compare entities.
        const ents = ctx.compareArchetype ? thingsOfArchetype(ctx.compareArchetype, ctx.idx) : [];
        const vals = ents.map((t) => num(t.Properties?.[binding.property])).filter((n) => !isNaN(n));
        return vals.length ? vals.reduce((a, b) => a + b, 0) / vals.length : null;
      }
      const t = ctx.idx.byId.get(binding.thing) ?? ctx.idx.byName.get(binding.thing);
      return t ? num(t.Properties?.[binding.property]) : null;
    }

    case 'aggregate': {
      let items = thingsOfArchetype(binding.archetype, ctx.idx).filter((t) =>
        passesFilters(t, binding.where),
      );
      const members = scopeMemberIds(binding.scope, ctx);
      if (members) items = items.filter((t) => members.has(t.Id));
      if (binding.op === 'count') return items.length;
      const vals = items
        .map((t) => num(t.Properties?.[binding.property ?? '']))
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

    case 'compareEntities': {
      const ents = ctx.compareArchetype ? thingsOfArchetype(ctx.compareArchetype, ctx.idx) : [];
      return ents.map((t) => {
        const row: Row = { id: t.Id, name: t.Name };
        for (const p of binding.properties) row[p] = num(t.Properties?.[p]);
        return row;
      });
    }

    case 'stateCount': {
      const resp = await stateApi.getThingsInState(binding.state);
      const members = scopeMemberIds(binding.scope, ctx);
      const list = resp.Things ?? [];
      return members ? list.filter((t) => members.has(t.Id)).length : list.length;
    }

    case 'stateList': {
      const resp = await stateApi.getThingsInState(binding.state);
      const members = scopeMemberIds(binding.scope, ctx);
      let list = resp.Things ?? [];
      if (members) list = list.filter((t) => members.has(t.Id));
      if (binding.limit) list = list.slice(0, binding.limit);
      return list.map((ref) => {
        const full = ctx.idx.byId.get(ref.Id);
        return { id: ref.Id, name: ref.Name, ...(full?.Properties ?? {}) } as Row;
      });
    }

    case 'timeseries':
      return resolveTimeseries(binding, ctx);

    case 'service': {
      try {
        const resp = await apiClient.post<unknown>(binding.endpoint, binding.body ?? {});
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
