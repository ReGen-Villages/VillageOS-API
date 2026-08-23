/**
 * The subscription a dashboard opens: the Things its widgets read, and nothing else.
 *
 * A spec already says what the page is about — the entity it compares, the types its lists draw, the
 * edges its bindings walk. This turns that statement into the one the platform reads, so the page is
 * sent what it draws instead of the whole model. Nothing here names a domain: every type, predicate
 * and Thing name comes out of the spec.
 */
import {
  DASHBOARD_ARCHETYPE,
  IS_PREDICATE,
  SCOPE_REF,
  type Binding,
  type ComputedColumn,
  type DashboardSpec,
  type RelationSpec,
  type RelationStep,
  type ScopeRef,
  type Widget,
} from '../types/dashboard';
import { GUI_SETTINGS_TYPE_NAME } from '../utils/guiSettings';
import type { SubscriptionSelector, TraverseDirection, TraverseRule } from '../types/subscription';

/**
 * What every page reads whatever it shows: the dashboards the navigation lists, the Thing the model
 * states its display settings on, and the `is` edge's predicate. A page that needs no more than
 * this is the cheapest subscription the app opens, and it is what the shell holds until a page says
 * otherwise.
 *
 * A predicate is a Thing, and the client reads an edge's predicate by looking that Thing up, so an
 * edge whose predicate is missing is one nothing can name — which for `is` means no Thing can be
 * told what it is. Every predicate a page follows is asked for alongside the Things it joins.
 *
 * Each is asked for by name as well as by type, because a type with no members is a Thing nothing
 * `is` and would otherwise be selected by neither.
 */
export const NAVIGATION_AND_SETTINGS: SubscriptionSelector = {
  types: [DASHBOARD_ARCHETYPE, GUI_SETTINGS_TYPE_NAME],
  names: [DASHBOARD_ARCHETYPE, GUI_SETTINGS_TYPE_NAME, IS_PREDICATE],
};

/** A scope walk is transitive — the selected entity relates to Things that in turn relate to the
 *  ones a widget reads — and the platform stops a walk as soon as it reaches nothing new. The
 *  largest depth it accepts is therefore how a caller says "follow this edge as far as it goes". */
const UNBOUNDED_DEPTH = 2147483647;

/** The shape the platform's `ids` field reads. A spec's Thing reference may be either an id or a
 *  name, and a name sent as an id is refused rather than looked up, so the two are told apart here.
 *  Shape only, whichever way an id was minted: the platform decides that, not this. */
const IDENTIFIER = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function ruleFor(predicate: string, direction: 'out' | 'in' | undefined, depth: number): TraverseRule {
  const followed: TraverseDirection = direction === 'in' ? 'incoming' : 'outgoing';
  return { predicate, direction: followed, depth };
}

/** The bindings a widget holds, in every slot that takes one. */
function widgetBindings(widget: Widget): (Binding | undefined)[] {
  switch (widget.type) {
    case 'kpi': return [widget.value, widget.delta, widget.spark, widget.sparkBaseline, widget.origin];
    case 'funnel': return widget.stages.flatMap((stage) => [stage.count, stage.drill]);
    case 'bullet': return widget.rows.map((row) => row.value);
    case 'table': return [widget.rows];
    case 'gantt': return [widget.rows];
    case 'leaderboard': return [widget.entities];
    case 'verdict': return widget.rows.map((row) => row.verdicts);
    case 'working': return widget.rows.flatMap((row) => [row.value, row.working]);
    case 'exceptionBar': return widget.buckets.map((bucket) => bucket.value);
  }
}

/** A binding and everything nested in it: a ratio's two halves, and the columns a row-producing
 *  binding derives per row. A nested binding reads the model as much as the one holding it. */
function withNested(binding: Binding): Binding[] {
  const inner: Binding[] = [];
  if (binding.kind === 'ratio') inner.push(binding.numerator, binding.denominator);
  if ('computed' in binding) {
    for (const column of (binding.computed ?? []) as ComputedColumn[]) inner.push(column.value);
  }
  return [binding, ...inner.flatMap(withNested)];
}

function specBindings(spec: DashboardSpec): Binding[] {
  return spec.sections
    .flatMap((section) => section.widgets)
    .flatMap(widgetBindings)
    .filter((binding): binding is Binding => !!binding)
    .flatMap(withNested);
}

/** Each root-to-leaf path through a detail card's nested relations. A card walks them one after the
 *  other, so each path is a walk in its own right. */
function detailWalks(relations: RelationSpec[] | undefined): RelationStep[][] {
  if (!relations?.length) return [];
  return relations.flatMap((relation) => {
    const step: RelationStep = { predicate: relation.predicate, direction: relation.direction };
    const below = detailWalks(relation.relations);
    return below.length ? below.map((path) => [step, ...path]) : [[step]];
  });
}

/**
 * The steps one binding walks, as a single path.
 *
 * An origin binding walks twice in sequence: `via` to the Thing holding the value, then `source.via`
 * from there to what says where the value came from. Those are one path, not two — the second starts
 * where the first ended, and asked for as a walk of its own the source edge would be applied to the
 * scope entity, reach nothing, and select nothing (Bug #6701).
 */
function bindingWalk(binding: Binding): RelationStep[] | undefined {
  if (binding.kind === 'origin') return [...(binding.via ?? []), ...(binding.source?.via ?? [])];
  return 'via' in binding ? binding.via : undefined;
}

/** The walks the spec's bindings take, each as its ordered steps. */
function specWalks(spec: DashboardSpec): RelationStep[][] {
  const fromBindings = specBindings(spec)
    .map(bindingWalk)
    .filter((via): via is RelationStep[] => !!via?.length);
  return [...fromBindings, ...detailWalks(spec.detail?.relations)];
}

/** The scope predicates the spec narrows by, each followed as far as it reaches. */
function scopeRules(spec: DashboardSpec): TraverseRule[] {
  const seen = new Set<string>();
  const rules: TraverseRule[] = [];
  for (const binding of specBindings(spec)) {
    const scope = ('scope' in binding ? binding.scope : undefined) as ScopeRef | undefined;
    if (!scope) continue;
    const key = `${scope.viaPredicate}:${scope.direction ?? 'out'}`;
    if (seen.has(key)) continue;
    seen.add(key);
    rules.push(ruleFor(scope.viaPredicate, scope.direction, UNBOUNDED_DEPTH));
  }
  return rules;
}

/**
 * The walks' steps as traversal rules, asked for in the order the walks take them.
 *
 * The platform applies each rule to everything selected before it and applies it once, so a walk of
 * two steps is reproduced by asking for its first step's edge before its second's — not by asking
 * for both edges in whatever order the spec happened to mention them.
 */
function walkRules(walks: RelationStep[][]): TraverseRule[] {
  const deepest = walks.reduce((longest, walk) => Math.max(longest, walk.length), 0);
  const rules: TraverseRule[] = [];
  for (let position = 0; position < deepest; position++) {
    const seen = new Set<string>();
    for (const walk of walks) {
      const step = walk[position];
      if (!step) continue;
      const key = `${step.predicate}:${step.direction ?? 'out'}`;
      if (seen.has(key)) continue;
      seen.add(key);
      rules.push(ruleFor(step.predicate, step.direction, 1));
    }
  }
  return rules;
}

/**
 * The binding kinds that read a type's members out of what the page holds.
 *
 * Naming them rather than taking every binding with an `archetype` is what keeps the page from
 * asking for Things it never reads: `stateCount` and `timeseries` also name a type, and the
 * platform answers both — the count from the state endpoint, the series from the reduction — so
 * their members would arrive to be counted a second time and thrown away. A type with a member per
 * event is exactly where that is worst.
 *
 * `stateList` stays even though its rows now arrive complete, because two things still read the
 * row's own Thing out of the page's set: a `computed` column resolves with that Thing as its scope
 * and walks its edges, and a detail card opened on the row reads its properties and relations.
 * Dropping it needs both of those to ask for what the page does not hold.
 */
const READS_ITS_TYPE_LOCALLY = new Set(['thingList', 'aggregate', 'stateList']);

/** The types the page's list-shaped bindings draw.
 *
 *  A binding that narrows to the selected entity reaches its rows by that entity's edges, so its
 *  type is asked for only while no entity is selected — when "All" is showing, the binding does run
 *  over every member of the type. */
function drawnTypes(spec: DashboardSpec, scopeId: string | null): string[] {
  const types = new Set<string>(NAVIGATION_AND_SETTINGS.types);
  if (spec.compare) types.add(spec.compare.archetype);
  for (const binding of specBindings(spec)) {
    if (!READS_ITS_TYPE_LOCALLY.has(binding.kind)) continue;
    const archetype = 'archetype' in binding ? binding.archetype : undefined;
    if (!archetype) continue;
    const narrowed = 'scope' in binding && !!binding.scope;
    if (!narrowed || scopeId === null) types.add(archetype);
  }
  return [...types];
}

/** The Things the spec names outright, split into the identifiers and the names the platform reads
 *  as two different questions. */
function namedThings(spec: DashboardSpec): { ids: string[]; names: string[] } {
  const referenced = new Set<string>();
  for (const binding of specBindings(spec)) {
    const thing = 'thing' in binding ? binding.thing : undefined;
    if (thing && thing !== SCOPE_REF) referenced.add(thing);
  }
  return {
    ids: [...referenced].filter((ref) => IDENTIFIER.test(ref)),
    names: [...referenced].filter((ref) => !IDENTIFIER.test(ref)),
  };
}

/**
 * What one dashboard needs the platform to send it, with `scopeId` naming the compare entity the
 * scope switcher currently shows — or null for "All", which is the page reading across every
 * compared entity rather than one.
 */
export function subscriptionForSpec(spec: DashboardSpec, scopeId: string | null): SubscriptionSelector {
  const named = namedThings(spec);
  const traverse = [...scopeRules(spec), ...walkRules(specWalks(spec))];
  const selector: SubscriptionSelector = {
    types: drawnTypes(spec, scopeId),
    names: [...new Set([
      ...NAVIGATION_AND_SETTINGS.names ?? [],
      ...traverse.map((rule) => rule.predicate),
      ...named.names,
    ])],
  };
  const ids = scopeId ? [...new Set([scopeId, ...named.ids])] : named.ids;
  if (ids.length) selector.ids = ids;
  if (traverse.length) selector.traverse = traverse;
  return selector;
}
