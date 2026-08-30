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
import type {
  VosThing,
  VosRelationship,
  ThingsInStateResponse,
  ThingRangesResponse,
  TemporalAggregateQuery,
  DerivedDefinition,
} from '../types/vos';
import {
  DASHBOARD_ARCHETYPE,
  DASHBOARD_SPEC_PROPERTY,
  IS_PREDICATE,
  SCOPE_REF,
  type Binding,
  type ComputedColumn,
  type DashboardDescriptor,
  type DashboardSpec,
  type OriginSource,
  type RelationStep,
  type ScopeEntity,
  type ScopeRef,
  type PropertyFilter,
} from '../types/dashboard';
import { stateApi, thingsInStatePath, type StateNarrowing } from './stateApi';
import { temporalApi } from './temporalApi';
import { rangeApi } from './rangeApi';
import { apiClient } from './client';
import { effectiveProperties, effectiveDerivedDefinitions } from '../utils/propertyMapper';
import { valueOrigin } from '../utils/propertyOrigin';
import { findRange } from '../utils/rangeHelpers';

/** Row shape returned by stateList / aggregate-list / service table bindings. */
export type Row = Record<string, unknown>;
/** A resolved binding value: a scalar, a table, or a series. */
export type BindingResult = number | string | Row[] | number[] | null;

// ---- model-store indexes ------------------------------------------------

export interface ModelIndex {
  byId: Map<string, VosThing>;
  byName: Map<string, VosThing>;
  relationships: VosRelationship[];
  /** predicate id → the relationships asserting it, so following one predicate reads its own
   *  edges instead of scanning every edge in the model. */
  relationshipsByPredicate: Map<string, VosRelationship[]>;
  /** `predicate id:direction` → Thing id → the ids that predicate reaches from it. Built the
   *  first time a walk asks for it and discarded with the index. A per-row binding walks the same
   *  predicate once per row, so grouping its edges per row would make a page cost grow with the
   *  square of the row count. */
  adjacencyByPredicate: Map<string, Map<string, string[]>>;
  /** predicate name → id, and id → name */
  predicateNameToId: Map<string, string>;
  predicateIdToName: Map<string, string>;
  /** archetype id → ids of Things directly `is`-linked to it (Bug #5942). */
  isChildren: Map<string, string[]>;
  /** ids of the Things that declared themselves archetypes (#6218) — read, never inferred from edges. */
  archetypeIds: Set<string>;
  /** Thing id → ids of the archetypes it is directly `is`-linked to (its parents). Used to
   *  resolve inherited property defaults up the `is`-chain (Bug #6048). */
  isParents: Map<string, string[]>;
  /** Archetype name → its instance ids, filled the first time each archetype is asked for and
   *  discarded with the index it belongs to. The walk is over a hierarchy that cannot change
   *  without a new index, and a per-row binding repeats the same question once per row. */
  archetypeMembers: Map<string, Set<string>>;
  /** The model's dashboards, parsed the first time they are asked for and discarded with the index.
   *  The navigation and the page both ask, and a spec is JSON in a property — parsing every one of
   *  them twice per model change is work neither reader needs done again. */
  dashboards: DashboardDescriptor[] | null;
}

export function buildModelIndex(things: VosThing[], relationships: VosRelationship[]): ModelIndex {
  const byId = new Map<string, VosThing>();
  const byName = new Map<string, VosThing>();
  const archetypeIds = new Set<string>();
  for (const t of things) {
    byId.set(t.Id, t);
    // First writer wins for duplicate names (archetypes are unique by name).
    if (!byName.has(t.Name)) byName.set(t.Name, t);
    if (t.IsArchetype) archetypeIds.add(t.Id);
  }
  const predicateNameToId = new Map<string, string>();
  const predicateIdToName = new Map<string, string>();
  const relationshipsByPredicate = new Map<string, VosRelationship[]>();
  for (const r of relationships) {
    const asserted = relationshipsByPredicate.get(r.PredicateId);
    if (asserted) asserted.push(r);
    else relationshipsByPredicate.set(r.PredicateId, [r]);
    const p = byId.get(r.PredicateId);
    if (p) {
      predicateIdToName.set(r.PredicateId, p.Name);
      if (!predicateNameToId.has(p.Name)) predicateNameToId.set(p.Name, r.PredicateId);
    }
  }
  // Precompute the `is`-hierarchy once (Bug #5942): children-by-archetype for a
  // transitive walk. Replaces a per-query scan of every relationship.
  const isId = predicateNameToId.get(IS_PREDICATE);
  const isChildren = new Map<string, string[]>();
  const isParents = new Map<string, string[]>();
  if (isId) {
    for (const r of relationships) {
      if (r.PredicateId !== isId) continue;
      const kids = isChildren.get(r.TargetId);
      if (kids) kids.push(r.SubjectId);
      else isChildren.set(r.TargetId, [r.SubjectId]);
      const parents = isParents.get(r.SubjectId);
      if (parents) parents.push(r.TargetId);
      else isParents.set(r.SubjectId, [r.TargetId]);
    }
  }
  return {
    byId, byName, relationships, relationshipsByPredicate,
    predicateNameToId, predicateIdToName, isChildren, archetypeIds, isParents,
    archetypeMembers: new Map(),
    adjacencyByPredicate: new Map(),
    dashboards: null,
  };
}

/** Keyed on the two arrays a model is held in, so an index — and everything it went on to
 *  remember — is collected with the model it describes rather than outliving it. */
const indexesByModel = new WeakMap<VosThing[], WeakMap<VosRelationship[], ModelIndex>>();

/** The one index built for a given model, shared by everything that reads it. Two components hold
 *  the same model arrays and would otherwise each walk the whole model on every change; sharing one
 *  index also shares the answers it remembers as they are asked for. */
export function modelIndexFor(things: VosThing[], relationships: VosRelationship[]): ModelIndex {
  let byRelationships = indexesByModel.get(things);
  if (!byRelationships) {
    byRelationships = new WeakMap();
    indexesByModel.set(things, byRelationships);
  }
  const built = byRelationships.get(relationships);
  if (built) return built;
  const index = buildModelIndex(things, relationships);
  byRelationships.set(relationships, index);
  return index;
}

/** Thing id → the ids one predicate reaches from it, in the asked-for direction. */
function adjacency(predicateId: string, inbound: boolean, idx: ModelIndex): Map<string, string[]> {
  const key = `${predicateId}:${inbound ? 'in' : 'out'}`;
  const built = idx.adjacencyByPredicate.get(key);
  if (built) return built;
  const edges = new Map<string, string[]>();
  for (const r of idx.relationshipsByPredicate.get(predicateId) ?? []) {
    const [from, to] = inbound ? [r.TargetId, r.SubjectId] : [r.SubjectId, r.TargetId];
    const next = edges.get(from);
    if (next) next.push(to);
    else edges.set(from, [to]);
  }
  idx.adjacencyByPredicate.set(key, edges);
  return edges;
}

/**
 * Ids of the Things that are of the given archetype, **transitively** over the `is`-chain
 * and counting **instances only** (Bug #5942). Archetypes are subtyped (Customer is Party,
 * PickLocation is Location), so a direct-edge match would miss every real instance under a
 * parent archetype. We descend the is-chain; a Thing that declares itself an archetype is a
 * sub-type and is descended into, not counted. Cycle-guarded.
 *
 * Descending on the declaration rather than on "does anything `is` this Thing" is what makes it
 * right for a type with no members yet: the old rule returned such a type as a row of its own (#6218).
 *
 * The answer is remembered on the index and handed out by reference, so callers read it and
 * never write to it.
 */
export function thingIdsOfArchetype(archetype: string, idx: ModelIndex): Set<string> {
  const answered = idx.archetypeMembers.get(archetype);
  if (answered) return answered;
  const archThing = idx.byName.get(archetype);
  const out = new Set<string>();
  idx.archetypeMembers.set(archetype, out);
  if (!archThing) return out;
  const seen = new Set<string>();          // archetype nodes already descended (cycle guard)
  const frontier = [archThing.Id];
  while (frontier.length) {
    const current = frontier.pop()!;
    if (seen.has(current)) continue;
    seen.add(current);
    for (const childId of idx.isChildren.get(current) ?? []) {
      if (idx.archetypeIds.has(childId)) frontier.push(childId);  // a sub-archetype — descend
      else out.add(childId);                                      // a real instance — count it
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
 *  instead of rebuilding it three times per model change.
 *
 *  Ordered by name: the archetype walk answers in no order a reader chose, so without this a
 *  dashboard's position in the navigation would move whenever the model changed. */
export function discoverDashboardsFromIndex(idx: ModelIndex): DashboardDescriptor[] {
  if (idx.dashboards) return idx.dashboards;
  // Every Dashboard Thing is listed, including one whose spec did not read. A spec is model data
  // and can be authored wrong; dropping such a Thing here leaves its author a page that never
  // appears and nothing anywhere saying why.
  const found = thingsOfArchetype(DASHBOARD_ARCHETYPE, idx).map((thing) => ({
    thing,
    spec: parseSpec(effectiveProperties(thing, idx)[DASHBOARD_SPEC_PROPERTY]),
  }));
  found.sort((a, b) => a.thing.Name.localeCompare(b.thing.Name));
  const slugs = found.map((d) => slugOf(d.thing.Name));
  const bearers = new Map<string, number>();
  for (const slug of slugs) bearers.set(slug, (bearers.get(slug) ?? 0) + 1);
  const out = found.map((d, i) => ({
    id: d.thing.Id,
    name: d.thing.Name,
    routeKey: slugs[i] && bearers.get(slugs[i]) === 1 ? slugs[i] : d.thing.Id,
    spec: d.spec,
  }));
  idx.dashboards = out;
  return out;
}

/** A Thing's name reduced to what a URL segment can carry: accents folded onto their base letters,
 *  everything else run together with single hyphens. Empty when the name is written in a script
 *  this leaves nothing of, which is why the caller keeps the Thing's id as the fallback. */
function slugOf(name: string): string {
  return name
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '');
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

/** Compare entities offered in the scope switcher. */
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
  /** The state reads of one refresh generation: everything resolved with this context asks the
   *  broker once per state name, however many rows and widgets want that state. Held in flight
   *  rather than as a value, so readers running in parallel share one request. Its lifetime is
   *  the context's own — a map outliving the generation would serve a membership the model has
   *  since moved past. */
  stateMembers?: Map<string, Promise<ThingsInStateResponse>>;
  /** The range reads of one refresh generation, shared and scoped exactly as `stateMembers` is. */
  thingRanges?: Map<string, Promise<ThingRangesResponse | null>>;
}

/** One state read, shared with everything in this generation asking the same question. The question
 *  is the request, not the state name: two widgets narrowing one state differently must not be
 *  handed each other's answer, which would put a wrong number on screen with nothing to say so. */
function thingsInState(
  state: string,
  ctx: ResolveContext,
  narrowing?: StateNarrowing,
): Promise<ThingsInStateResponse> {
  const question = thingsInStatePath(state, narrowing);
  const inFlight = ctx.stateMembers?.get(question);
  if (inFlight) return inFlight;
  const request = stateApi.getThingsInState(state, narrowing);
  ctx.stateMembers?.set(question, request);
  // A failed read is dropped rather than shared: one broker hiccup would otherwise stick to
  // every later reader of this generation, with nothing to retry it until the next refresh.
  request.catch(() => ctx.stateMembers?.delete(question));
  return request;
}

/** The scope as the state and temporal endpoints express it: the selected compare entity as a
 *  container, with the predicate its containment is written with. Both walk outward from the
 *  container, so an inbound scope has no server expression and resolves to nothing here. */
function containerFor(
  scope: ScopeRef | undefined,
  ctx: ResolveContext,
): { within: string; withinPredicate: string } | null {
  if (!scope || !ctx.scopeId || scope.direction === 'in') return null;
  return { within: ctx.scopeId, withinPredicate: scope.viaPredicate };
}

async function stateMemberIds(state: string, ctx: ResolveContext): Promise<Set<string>> {
  return new Set((await thingsInState(state, ctx)).Things?.map((t) => t.Id) ?? []);
}

/** A Thing's ranges, own and inherited, shared across the widgets of one refresh the way state
 *  reads are. The ranges that judge a Thing sit on its archetype, so several verdict rows on one page
 *  ask about one Thing and would otherwise each fetch the same answer. A failed read resolves to null
 *  rather than rejecting: a verdict the model holds still reads, without the target it names. */
function thingRanges(thingId: string, ctx: ResolveContext): Promise<ThingRangesResponse | null> {
  const inFlight = ctx.thingRanges?.get(thingId);
  if (inFlight) return inFlight;
  const request = rangeApi.getAll(thingId).catch(() => null);
  ctx.thingRanges?.set(thingId, request);
  return request;
}

/** A cell value as a number, or null where it has none — `num`'s rule about what counts as a number,
 *  kept in one place so a widget reading a resolved row cannot answer that question differently from
 *  the resolver that filled it. */
export function nullableNumber(value: unknown): number | null {
  const asNumber = num(value);
  return isNaN(asNumber) ? null : asNumber;
}

/** Members reachable from the scope entity by following a predicate transitively.
 *  The walk is transitive because a scope predicate can nest: the entity relates to
 *  intermediate Things that in turn relate to the ones a widget counts, and a one-hop walk
 *  would stop at the intermediates. The scope entity is never a member of its own scope. */
function scopeMemberIds(scope: ScopeRef | undefined, ctx: ResolveContext): Set<string> | null {
  if (!scope || !ctx.scopeId) return null;
  const pid = ctx.idx.predicateNameToId.get(scope.viaPredicate);
  if (!pid) return new Set();
  const edges = adjacency(pid, scope.direction === 'in', ctx.idx);
  const members = new Set<string>();
  const walked = new Set<string>([ctx.scopeId]);   // seeded so a cycle back to the scope re-adds nothing
  const frontier = [ctx.scopeId];
  while (frontier.length) {
    for (const next of edges.get(frontier.pop()!) ?? []) {
      if (walked.has(next)) continue;
      walked.add(next);
      members.add(next);
      frontier.push(next);
    }
  }
  return members;
}

/**
 * A value's number, or NaN when it has none.
 *
 * Text is never parsed, however numeric it looks. The platform states a type for every property
 * and the value it sends already carries that answer — a text property arrives as text, and every
 * numeric type arrives as a number — so parsing would replace an answer with a guess about how the
 * characters look, and read an order number or a door number as a measurement. A spec that
 * compares against a number must therefore write it as one, not as quoted text.
 */
function num(v: unknown): number {
  if (typeof v === 'number') return v;
  if (typeof v === 'boolean') return v ? 1 : 0;
  return NaN;
}

function passesFilters(thing: VosThing, filters: PropertyFilter[] | undefined, idx: ModelIndex): boolean {
  if (!filters) return true;
  const properties = effectiveProperties(thing, idx);
  for (const f of filters) {
    const v = properties[f.property];
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

/** The Thing a binding starts from: the one it names by id or name, or — absent a name, or for the
 *  `$scope` reference — the selected compare entity, which inside a computed column is the row's
 *  own Thing.
 *
 *  Every binding that names a Thing asks here, so one reference means one Thing wherever a spec
 *  spends it. The id is tried first because it is exact: Thing names are not unique in this model
 *  and the index keeps whichever Thing of a name it saw first, so a name is the weaker answer and
 *  belongs in the fallback. */
function referencedThing(ref: string | undefined, ctx: ResolveContext): VosThing | null {
  if (!ref || ref === SCOPE_REF) return ctx.scopeId ? (ctx.idx.byId.get(ctx.scopeId) ?? null) : null;
  return ctx.idx.byId.get(ref) ?? ctx.idx.byName.get(ref) ?? null;
}

/** The Things one step of a binding's path reaches from the Things reached so far. */
async function followStep(fromIds: string[], step: RelationStep, ctx: ResolveContext): Promise<string[]> {
  const pid = ctx.idx.predicateNameToId.get(step.predicate);
  if (!pid) return [];
  const edges = adjacency(pid, step.direction === 'in', ctx.idx);
  const reached = new Set<string>();
  for (const id of fromIds) {
    for (const target of edges.get(id) ?? []) reached.add(target);
  }
  let ids = [...reached];
  if (step.archetype) {
    const ofArchetype = thingIdsOfArchetype(step.archetype, ctx.idx);
    ids = ids.filter((id) => ofArchetype.has(id));
  }
  if (step.inState && ids.length) {
    const inState = await stateMemberIds(step.inState, ctx);
    ids = ids.filter((id) => inState.has(id));
  }
  if (step.notInState && ids.length) {
    const excluded = await stateMemberIds(step.notInState, ctx);
    ids = ids.filter((id) => !excluded.has(id));
  }
  return ids;
}

export type Lever = { term: string; memberArchetype?: string; direction: 'raise' | 'lower' };

/** Every input at the bottom of a figure's derivation, each with the way the figure moves as it
 *  rises. A term with its own definition is expanded into that definition's terms with the signs
 *  composed, so the offers name what a person could actually change — a term whose definition names
 *  it back is read as a leaf rather than walked forever. A term the definition declares no
 *  direction for is dropped with its subtree: under a shortfall a wrong direction is worse than
 *  none. Reduction inputs live on each member, so those carry the members' archetype. */
function leafInfluences(
  property: string,
  definitions: Readonly<Record<string, DerivedDefinition>>,
  sign: 1 | -1,
  visiting: Set<string>,
): { term: string; memberArchetype?: string; sign: 1 | -1 }[] {
  const definition = definitions[property];
  if (!definition) return [];
  visiting.add(property);
  const rises = new Set(definition.RisesWith ?? []);
  const falls = new Set(definition.FallsWith ?? []);
  const leaves: { term: string; memberArchetype?: string; sign: 1 | -1 }[] = [];
  for (const term of definition.Reads ?? []) {
    const direction = rises.has(term) ? 1 : falls.has(term) ? -1 : 0;
    if (!direction) continue;
    const composed = (sign * direction) as 1 | -1;
    if (definition.Expression === undefined) {
      leaves.push({ term, memberArchetype: definition.RelatedType ?? undefined, sign: composed });
    } else {
      const below = definitions[term] && !visiting.has(term)
        ? leafInfluences(term, definitions, composed, visiting)
        : [];
      leaves.push(...(below.length ? below : [{ term, sign: composed }]));
    }
  }
  visiting.delete(property);
  return leaves;
}

/**
 * What would move a judged figure toward leaving the state it holds, in the order the derivation
 * reads them. The held comparison says which way the result must move — a state held by sitting
 * below its target is left by rising, one held by sitting above it by falling — and each leaf's
 * composed sign says which way that input goes. An input reached along two agreeing routes is one
 * offer; one whose routes disagree is dropped, since offering either direction would be the client
 * taking a side.
 */
function leversFor(
  property: string,
  operator: string,
  definitions: Readonly<Record<string, DerivedDefinition>>,
): Lever[] {
  const resultMustMove = operator === '<' || operator === '<=' ? 1
    : operator === '>' || operator === '>=' ? -1
    : null;
  if (resultMustMove === null) return [];
  const routes = new Map<string, { term: string; memberArchetype?: string; sign: 1 | -1 | 0 }>();
  for (const leaf of leafInfluences(property, definitions, 1, new Set())) {
    const key = `${leaf.term}\u0000${leaf.memberArchetype ?? ''}`;
    const seen = routes.get(key);
    if (!seen) routes.set(key, { ...leaf });
    else if (seen.sign !== leaf.sign) seen.sign = 0;
  }
  return [...routes.values()]
    .filter((leaf) => leaf.sign !== 0)
    .map((leaf) => ({
      term: leaf.term,
      ...(leaf.memberArchetype !== undefined ? { memberArchetype: leaf.memberArchetype } : {}),
      direction: resultMustMove * leaf.sign > 0 ? 'raise' as const : 'lower' as const,
    }));
}

/** The Things a binding reads: the one its reference names, or — when it declares a path — the
 *  Things that path reaches from there. A page can only be scoped to one Thing, and what it wants
 *  to say is rarely all on that Thing, so a binding says how to get from the scope to its subject.
 *
 *  Ordered by name, so a walk reaching several reads the same on every refresh whatever order the
 *  relationship list happened to be in. Narrow with a step's `archetype`, `inState` or `notInState`
 *  when a predicate reaches more than the binding means. */
async function thingsReached(
  ref: string | undefined,
  via: RelationStep[] | undefined,
  ctx: ResolveContext,
): Promise<VosThing[]> {
  const start = referencedThing(ref, ctx);
  if (!start) return [];
  if (!via?.length) return [start];
  let reached = [start.Id];
  for (const step of via) {
    reached = await followStep(reached, step, ctx);
    if (!reached.length) return [];
  }
  return reached
    .map((id) => ctx.idx.byId.get(id))
    .filter((t): t is VosThing => !!t)
    .sort((a, b) => a.Name.localeCompare(b.Name));
}

/**
 * What the model says produced a value: the Things the source path reaches from the one holding it,
 * and when the source was last resolved.
 *
 * A walk reaching several sources names them all and reports no instant. A set of sources resolved
 * at different times has no one instant, and showing one of them would attribute the figure to a
 * source that may not have produced it — the confusion this vocabulary exists to end.
 */
async function recordedSourceOf(
  carrying: VosThing,
  source: OriginSource | undefined,
  ctx: ResolveContext,
): Promise<{ source: string | null; resolvedAt: string | null }> {
  if (!source) return { source: null, resolvedAt: null };
  const reached = await thingsReached(carrying.Id, source.via, ctx);
  if (!reached.length) return { source: null, resolvedAt: null };
  const named = reached.map((t) => t.Name).join(', ');
  if (reached.length > 1 || !source.resolvedAt) return { source: named, resolvedAt: null };
  const resolved = effectiveProperties(reached[0], ctx.idx)[source.resolvedAt];
  return { source: named, resolvedAt: resolved == null ? null : String(resolved) };
}

/** Resolve each computed column once per row, with that row's Thing as the scope — so the binding
 *  a `$scope`-driven widget uses yields this row's own value here. The rows share their state
 *  reads, which is what keeps a state-reading column at one request per state rather than per row
 *  even when the caller gave no context to share through. */
async function withComputedColumns(
  rows: Row[],
  computed: ComputedColumn[] | undefined,
  ctx: ResolveContext,
): Promise<Row[]> {
  if (!computed?.length) return rows;
  const stateMembers = ctx.stateMembers ?? new Map<string, Promise<ThingsInStateResponse>>();
  return Promise.all(
    rows.map(async (row) => {
      const rowCtx: ResolveContext = { ...ctx, scopeId: (row.id as string) ?? null, stateMembers };
      const values = await Promise.all(computed.map((column) => resolveBinding(column.value, rowCtx)));
      computed.forEach((column, i) => (row[column.key] = asCell(values[i])));
      return row;
    }),
  );
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
      const t = referencedThing(binding.thing, ctx);
      return t ? num(effectiveProperties(t, ctx.idx)[binding.property]) : null;
    }

    case 'aggregate': {
      const members = scopeMemberIds(binding.scope, ctx);
      const ofArchetype = thingIdsOfArchetype(binding.archetype, ctx.idx);
      // Start from the scope's members when there is a scope, not from the archetype: as a
      // per-row column this asks about one row's handful of members, while the archetype can
      // hold every Thing the page lists.
      const candidates = members ? [...members].filter((id) => ofArchetype.has(id)) : [...ofArchetype];
      const items = candidates
        .map((id) => ctx.idx.byId.get(id))
        .filter((t): t is VosThing => !!t && passesFilters(t, binding.where, ctx.idx));
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

    case 'related': {
      const reached = await thingsReached(binding.thing, binding.via, ctx);
      if (!reached.length) return null;
      const values = reached
        .map((t) => (binding.property ? effectiveProperties(t, ctx.idx)[binding.property] : t.Name))
        .filter((v) => v != null && v !== '');
      if (!values.length) return null;
      if (values.length === 1 && typeof values[0] === 'number') return values[0];
      // Sorted so a cell that names several Things reads the same on every refresh, whatever
      // order the relationship list happened to be in.
      return [...new Set(values.map(String))].sort((a, b) => a.localeCompare(b)).join(', ');
    }

    case 'stateOf': {
      const t = referencedThing(binding.thing, ctx);
      if (!t) return null;
      for (const state of binding.states) {
        if ((await stateMemberIds(state, ctx)).has(t.Id)) return state;
      }
      return null;
    }

    case 'verdict': {
      const judged = await thingsReached(binding.thing, binding.via, ctx);
      if (!judged.length) return [];
      // Asked all at once rather than in turn: unlike `stateOf` there is no priority order to stop
      // early on, so asking in turn would cost one round trip per candidate for no answer it changes.
      // One answer per state serves every judged Thing, so a walk reaching several adds no state reads.
      const membership = await Promise.all(binding.states.map((c) => stateMemberIds(c.state, ctx)));
      const verdictsOf = await Promise.all(judged.map(async (thing) => {
        const held = binding.states.filter((_, i) => membership[i].has(thing.Id));
        if (!held.length) return [];

        const ranges = await thingRanges(thing.Id, ctx);
        const properties = effectiveProperties(thing, ctx.idx);
        return held.map((candidate) => {
          // The first comparison, because a judge-range tests one value; a range that tests none —
          // the criteria for a balance nobody assessed — leaves the whole sentence without a figure.
          const judgedAgainst = ranges ? findRange(candidate.state, ranges)?.Comparisons?.[0] : undefined;
          const levers = candidate.levers && judgedAgainst
            ? leversFor(judgedAgainst.PropertyName, judgedAgainst.Operator,
                effectiveDerivedDefinitions(thing, ctx.idx))
            : [];
          return {
            state: candidate.state,
            reads: candidate.reads,
            property: judgedAgainst ? judgedAgainst.PropertyName : null,
            operator: judgedAgainst ? judgedAgainst.Operator : null,
            target: judgedAgainst ? nullableNumber(judgedAgainst.Value) : null,
            value: judgedAgainst ? nullableNumber(properties[judgedAgainst.PropertyName]) : null,
            ...(levers.length ? { levers } : {}),
          };
        });
      }));
      return verdictsOf.flat();
    }

    case 'origin': {
      const carrying = await thingsReached(binding.thing, binding.via, ctx);
      return Promise.all(carrying.map(async (thing) => {
        const { origin, assumedFrom } = valueOrigin(thing, binding.property, ctx.idx);
        // An assumption already names its source — the archetype it was inherited from — so it needs
        // no path to one. Every other origin asks the model what produced the value.
        const recorded = assumedFrom !== null
          ? { source: assumedFrom, resolvedAt: null }
          : await recordedSourceOf(thing, binding.source, ctx);
        return { origin, reads: binding.reads[origin] ?? null, ...recorded } as Row;
      }));
    }

    case 'working': {
      const computing = await thingsReached(binding.thing, binding.via, ctx);
      return computing.flatMap((thing) => {
        const definition = effectiveDerivedDefinitions(thing, ctx.idx)[binding.property];
        if (!definition) return [];
        const terms = definition.Reads ?? [];
        const formula = definition.Expression;
        // A reduction reads its input off each member, so the Thing computing the figure holds no
        // value for it and a property of that name there is some other property. The row names the
        // archetype those members are of in place of a value, and carries no formula: a function over
        // a set put into words here would be Trellis saying what a figure means.
        if (formula === undefined) {
          return terms.map((term) => ({
            formula: null,
            term,
            memberArchetype: definition.RelatedType ?? null,
          }) as Row);
        }
        const properties = effectiveProperties(thing, ctx.idx);
        return terms.map((term) => ({
          formula,
          term,
          value: nullableNumber(properties[term]),
        }) as Row);
      });
    }

    case 'compareEntities': {
      const ents = ctx.compareArchetype ? thingsOfArchetype(ctx.compareArchetype, ctx.idx) : [];
      const rows = ents.map((t) => {
        const row: Row = { id: t.Id, name: t.Name };
        for (const p of binding.properties) row[p] = num(effectiveProperties(t, ctx.idx)[p]);
        return row;
      });
      return withComputedColumns(rows, binding.computed, ctx);
    }

    case 'stateCount': {
      const container = containerFor(binding.scope, ctx);
      const resp = await thingsInState(binding.state, ctx, { type: binding.archetype, ...container });
      // Only what the request could not carry is narrowed here.
      const members = container ? null : scopeMemberIds(binding.scope, ctx);
      const list = resp.Things ?? [];
      return members ? list.filter((t) => members.has(t.Id)).length : list.length;
    }

    case 'stateList': {
      const container = containerFor(binding.scope, ctx);
      const members = container ? null : scopeMemberIds(binding.scope, ctx);
      const resp = await thingsInState(binding.state, ctx, {
        type: binding.archetype,
        // The derived statuses nest — a Thing that reached a later stage still holds the earlier
        // ones — so a plain stateList for an early stage includes every later one. A funnel stage
        // names the next stage's state as one that disqualifies, leaving the Things that reached
        // this stage and no further.
        notIn: binding.excludeState ? [binding.excludeState] : undefined,
        // A cap the server applies takes the first rows of its answer, which is the wrong subset
        // when the scope is still narrowed here afterwards. That answer then carries the columns
        // of every Thing in the state and keeps the few this scope reaches — the price of a scope
        // the endpoint cannot express, paid on the path that was already reading them all.
        limit: members ? undefined : binding.limit,
        properties: binding.properties,
        ...container,
      });
      let list = resp.Things ?? [];
      if (members) {
        list = list.filter((t) => members.has(t.Id));
        if (binding.limit) list = list.slice(0, binding.limit);
      }
      // The row is what the platform sent, not what a local index could be asked for afterwards.
      // Reading it here is what made a table of ten rows cost every Thing in the model.
      const rows = list.map((ref) => ({ id: ref.Id, name: ref.Name, ...(ref.Properties ?? {}) }) as Row);
      return withComputedColumns(rows, binding.computed, ctx);
    }

    case 'thingList': {
      let list = thingsOfArchetype(binding.archetype, ctx.idx);
      const members = scopeMemberIds(binding.scope, ctx);
      if (members) list = list.filter((t) => members.has(t.Id));
      list.sort((a, b) => a.Name.localeCompare(b.Name));
      if (binding.limit) list = list.slice(0, binding.limit);
      const rows = list.map((t) => ({ id: t.Id, name: t.Name, ...effectiveProperties(t, ctx.idx) }) as Row);
      return withComputedColumns(rows, binding.computed, ctx);
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

/** The platform's reduction names, which the binding says in the spec's own words. */
const REDUCTIONS: Record<Extract<Binding, { kind: 'timeseries' }>['op'], TemporalAggregateQuery['function']> = {
  count: 'Count', sum: 'Sum', avg: 'Average', min: 'Min', max: 'Max',
};

/** The platform's bucketed reduction, asked for at the granularity the widget wants. One bucket is
 *  the trailing-window figure a tile shows; more than one is the trace. A question the platform
 *  refuses resolves to nothing rather than to an empty series, which on a chart reads as "nothing
 *  happened". */
async function resolveTimeseries(
  binding: Extract<Binding, { kind: 'timeseries' }>,
  ctx: ResolveContext,
): Promise<number[] | number | null> {
  // The one refusal worth saying out loud: every other binding resolving to nothing is a value the
  // model has not got, while this is a question the spec cannot ask, and an author has no other sign
  // of it.
  if (binding.scope?.direction === 'in') {
    console.warn(
      `timeseries over ${binding.archetype}: the platform narrows to what a container reaches, so a ` +
        'scope reaching the other way cannot be asked for.',
    );
    return null;
  }
  try {
    const answer = await temporalApi.aggregate({
      function: REDUCTIONS[binding.op],
      memberType: binding.archetype,
      timestampProperty: binding.happenedAt,
      // A count reduces the members themselves, so naming a measure would ask for a value the
      // question is not about.
      measureProperty: binding.op === 'count' ? undefined : binding.property,
      windowSeconds: binding.bucketSeconds * binding.buckets,
      bucketSeconds: binding.bucketSeconds,
      ...containerFor(binding.scope, ctx),
    });
    const buckets = answer?.Buckets ?? [];
    if (binding.buckets > 1) return buckets;
    return buckets.length ? buckets[0] : null;
  } catch {
    return null;
  }
}

// ---- coercion helpers for widgets --------------------------------------

export function asNumber(r: BindingResult): number | null {
  return typeof r === 'number' && !isNaN(r) ? r : null;
}
/** A resolved binding narrowed to what one table cell can hold. A number stays a number so the
 *  column can format and sort it as one; text stays text; a table or a series is not a cell value
 *  and lands empty rather than as its stringified self. */
function asCell(r: BindingResult): number | string | null {
  if (typeof r === 'string') return r;
  return asNumber(r);
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
