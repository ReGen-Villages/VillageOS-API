import type { Binding, NumberFormat, Widget } from '../types/dashboard';
import type { HistoryFold, HistoryFunction } from '../types/vos';
import { en } from '../i18n/locales/en';

/**
 * What each widget and each binding is made of, as fields the properties panel draws.
 *
 * The contract is written twice otherwise — once as types, once as controls — and the two drift the
 * day a key is added to one. Here each kind's fields are stated once, the panel draws them, and the
 * types hold the table to the union: a kind without a schema fails the build, and a field key the
 * English words do not name does not compile.
 */

const NUMBER_FORMATS: readonly NumberFormat[] = [
  'integer', 'decimal1', 'decimal2', 'percent', 'percent1', 'pct100', 'hours', 'compact', 'money',
];
const DIRECTIONS = ['up-good', 'down-good', 'neither-good'] as const;
const BOUND_DIRECTIONS = ['up-good', 'down-good'] as const;
const RENDERS = ['text', 'badge', 'agebar', 'id'] as const;
const SEVERITIES = ['good', 'warn', 'crit'] as const;
const SORT_DIRECTIONS = ['asc', 'desc'] as const;
const REDUCTIONS = ['count', 'sum', 'avg', 'min', 'max'] as const;
const FOLDS: readonly HistoryFold[] = ['hour', 'day', 'month', 'year', 'hourOfDay', 'dayOfYear', 'monthOfYear', 'hourOfDay,dayOfYear', 'monthOfYear,hourOfDay', 'all'];
const FUNCTIONS: readonly HistoryFunction[] = ['Min', 'Max', 'Average', 'Sum', 'Count', 'Percentile', 'ShareWithin', 'CountAtOrBelow', 'CountAbove', 'SumAbove', 'SumBelow'];
export const FILTER_OPERATORS = ['=', '!=', '>', '>=', '<', '<=', 'in'] as const;
export const STEP_DIRECTIONS = ['out', 'in'] as const;

export type WidgetFieldKey = keyof typeof en.design.widgetField;
export type BindingFieldKey = keyof typeof en.design.bindingField;

/** Which of the model's words a text field is filled from. */
export type Offering = 'kinds' | 'property' | 'state' | 'thing' | 'endpoint' | 'rowKey' | 'predicate';

/** What a slot expects a binding to resolve to, so the fitting kinds are offered first. */
export type BindingShape = 'number' | 'rows' | 'series' | 'verdicts';

interface FieldBase {
  key: WidgetFieldKey | BindingFieldKey;
  /** Worked out when the page is kept rather than written by hand; shown, never edited. */
  derived?: boolean;
}

export type FieldSpecification =
  | (FieldBase & { kind: 'text'; mono?: boolean; offers?: Offering })
  | (FieldBase & { kind: 'number' })
  | (FieldBase & { kind: 'boolean' })
  | (FieldBase & { kind: 'choice'; options: readonly string[] })
  | (FieldBase & { kind: 'binding'; shape: BindingShape })
  /** A number, or a binding onto the model's own value. */
  | (FieldBase & { kind: 'bound' })
  | (FieldBase & { kind: 'strings'; offers?: Offering })
  | (FieldBase & { kind: 'numberPair' })
  | (FieldBase & { kind: 'json' })
  | (FieldBase & { kind: 'columns' })
  | (FieldBase & { kind: 'list'; of: FieldSpecification[] })
  | (FieldBase & { kind: 'group'; of: FieldSpecification[] })
  | (FieldBase & { kind: 'detail' })
  | (FieldBase & { kind: 'scope' })
  | (FieldBase & { kind: 'steps' })
  | (FieldBase & { kind: 'filters' })
  | (FieldBase & { kind: 'computed' })
  | (FieldBase & { kind: 'asked' })
  | (FieldBase & { kind: 'actionWrites' })
  | (FieldBase & { kind: 'formWrites' })
  | (FieldBase & { kind: 'preview' });

const text = (key: FieldBase['key'], offers?: Offering, mono = false): FieldSpecification => ({ key, kind: 'text', offers, mono });
const number = (key: FieldBase['key']): FieldSpecification => ({ key, kind: 'number' });
const boolean = (key: FieldBase['key']): FieldSpecification => ({ key, kind: 'boolean' });
const choice = (key: FieldBase['key'], options: readonly string[]): FieldSpecification => ({ key, kind: 'choice', options });
const binding = (key: FieldBase['key'], shape: BindingShape): FieldSpecification => ({ key, kind: 'binding', shape });
const bound = (key: FieldBase['key']): FieldSpecification => ({ key, kind: 'bound' });
const strings = (key: FieldBase['key'], offers?: Offering): FieldSpecification => ({ key, kind: 'strings', offers });
const list = (key: FieldBase['key'], of: FieldSpecification[]): FieldSpecification => ({ key, kind: 'list', of });
const group = (key: FieldBase['key'], of: FieldSpecification[]): FieldSpecification => ({ key, kind: 'group', of });

const title = text('title');
const hint = text('hint');
const format = choice('format', NUMBER_FORMATS);
const unit = text('unit');
const floor = number('floor');
const ceiling = number('ceiling');
const scope: FieldSpecification = { key: 'scope', kind: 'scope' };
const archetype = text('archetype', 'kinds', true);
const state = text('state', 'state', true);
const thing = text('thing', 'thing', true);
const property = text('property', 'property', true);
const computed: FieldSpecification = { key: 'computed', kind: 'computed' };
const where: FieldSpecification = { key: 'where', kind: 'filters' };
const steps: FieldSpecification = { key: 'via', kind: 'steps' };

const rangeSeries = [
  binding('recordedHigh', 'number'), binding('designHigh', 'number'), binding('averageHigh', 'number'), binding('mean', 'number'),
  binding('averageLow', 'number'), binding('designLow', 'number'), binding('recordedLow', 'number'),
];
const rangeBand = [text('label'), binding('from', 'number'), binding('to', 'number'), text('colour', undefined, true)];
const namedSeries = [text('label'), binding('value', 'number'), unit, format];
const side = [text('label'), binding('value', 'number'), binding('threshold', 'number')];

export const WIDGET_SCHEMAS: Record<Widget['type'], FieldSpecification[]> = {
  kpi: [
    title, binding('value', 'number'), format, unit, number('target'), text('targetLabel'), choice('direction', DIRECTIONS),
    binding('delta', 'number'), binding('spark', 'series'), binding('sparkBaseline', 'number'), text('sparkBaselineLabel'), text('footnote'),
    binding('origin', 'number'),
  ],
  funnel: [
    title, hint,
    list('stages', [text('label'), text('sublabel'), text('color', undefined, true), binding('count', 'number'), binding('drill', 'rows')]),
    { key: 'drillColumns', kind: 'columns' }, boolean('searchable'), strings('searchKeys', 'rowKey'), text('searchNoun'), boolean('rowDetail'),
  ],
  bullet: [
    title,
    list('rows', [text('label'), binding('value', 'number'), number('max'), number('target'), { key: 'band', kind: 'numberPair' }, choice('direction', BOUND_DIRECTIONS), format]),
  ],
  table: [
    title, hint, { key: 'columns', kind: 'columns' }, binding('rows', 'rows'), number('minWidth'), text('sortKey', 'rowKey', true),
    choice('sortDir', SORT_DIRECTIONS), number('visibleRows'), boolean('searchable'), strings('searchKeys', 'rowKey'), boolean('rowDetail'),
  ],
  gantt: [title, hint, binding('rows', 'rows'), strings('ticks'), number('now')],
  leaderboard: [
    title, hint, binding('entities', 'rows'),
    list('metrics', [text('key', 'rowKey', true), text('label'), format, choice('direction', BOUND_DIRECTIONS), number('weight'), number('best'), number('worst')]),
    text('labelKey', 'rowKey', true), text('sublabelKey', 'rowKey', true),
  ],
  verdict: [title, hint, list('rows', [text('label'), binding('verdicts', 'verdicts'), format, unit])],
  working: [title, hint, list('rows', [text('label'), binding('value', 'number'), binding('working', 'number'), format, unit])],
  exceptionBar: [title, hint, list('buckets', [text('label'), binding('value', 'number'), choice('severity', SEVERITIES)]), text('note')],
  rangeBar: [title, hint, group('months', rangeSeries), group('annual', rangeSeries), list('bands', rangeBand), format, unit, floor, ceiling],
  lineSeries: [title, hint, list('series', [text('label'), binding('value', 'number')]), format, unit, floor, ceiling],
  heatmap: [
    title, hint, binding('value', 'number'),
    group('sun', [binding('latitude', 'number'), binding('longitude', 'number'), binding('utcOffsetSeconds', 'number')]),
    format, unit, floor, ceiling,
  ],
  stackedShares: [title, hint, list('classes', [text('label'), text('colour', undefined, true), binding('share', 'number')])],
  divergingBar: [title, hint, group('up', side), group('down', side), format, unit],
  smallMultiples: [title, hint, group('bars', namedSeries), group('line', namedSeries), group('band', rangeBand)],
  action: [
    title, hint, binding('rows', 'rows'), text('label', 'rowKey', true), strings('shows', 'rowKey'),
    { key: 'asks', kind: 'asked' }, { key: 'writes', kind: 'actionWrites' },
  ],
  form: [title, hint, { key: 'fields', kind: 'asked' }, text('submit'), { key: 'preview', kind: 'preview' }, { key: 'writes', kind: 'formWrites' }],
};

const historyStep = [choice('fold', FOLDS), choice('function', FUNCTIONS), bound('percentile'), bound('from'), bound('to'), bound('threshold')];

export const BINDING_SCHEMAS: Record<Binding['kind'], FieldSpecification[]> = {
  const: [number('value')],
  stateCount: [state, text('excludeState', 'state', true), archetype, scope],
  stateList: [state, text('excludeState', 'state', true), archetype, scope, number('limit'), { ...strings('properties', 'property'), derived: true }, computed],
  thingList: [archetype, scope, number('limit'), computed, text('inState', 'state', true), where],
  aggregate: [archetype, choice('op', REDUCTIONS), property, where, scope],
  property: [thing, property],
  related: [steps, thing, property],
  stateOf: [strings('states', 'state'), thing],
  verdict: [list('states', [state, text('reads'), group('levers', [text('raise'), text('lower')])]), thing, steps],
  working: [property, thing, steps],
  origin: [property, thing, steps, { key: 'reads', kind: 'json' }, group('source', [steps, text('resolvedAt', 'property', true)])],
  ratio: [binding('numerator', 'number'), binding('denominator', 'number')],
  compareEntities: [strings('properties', 'property'), computed],
  timeseries: [archetype, text('happenedAt', 'property', true), property, choice('op', REDUCTIONS), number('bucketSeconds'), number('buckets'), number('bucketsPerPoint'), scope],
  latest: [binding('series', 'series')],
  service: [text('endpoint', 'endpoint', true), { key: 'body', kind: 'json' }, text('select', undefined, true)],
  history: [property, number('windowSeconds'), list('steps', historyStep)],
};

export const BINDING_KINDS = Object.keys(BINDING_SCHEMAS) as Binding['kind'][];

/** What each kind resolves to, so a slot can offer the kinds that fit it first. */
const SHAPES: Record<Binding['kind'], BindingShape[]> = {
  const: ['number'],
  stateCount: ['number'],
  stateList: ['rows'],
  thingList: ['rows'],
  aggregate: ['number'],
  property: ['number'],
  related: ['number'],
  stateOf: ['number'],
  verdict: ['verdicts'],
  working: ['number'],
  origin: ['number'],
  ratio: ['number'],
  compareEntities: ['rows'],
  timeseries: ['series'],
  latest: ['number'],
  service: ['number', 'rows', 'series'],
  history: ['series'],
};

/** The kinds that fit a slot first, then the rest — every kind stays reachable, since a slot's shape
 *  is what it usually wants and not a rule the resolver enforces. */
export function kindsForShape(shape: BindingShape): Binding['kind'][] {
  const fitting = BINDING_KINDS.filter((kind) => SHAPES[kind].includes(shape));
  return [...fitting, ...BINDING_KINDS.filter((kind) => !fitting.includes(kind))];
}

/** The column fields a table's columns and a funnel's drill columns are edited with. */
export const COLUMN_FIELDS: FieldSpecification[] = [text('key', 'rowKey', true), text('label'), boolean('numeric'), format, choice('render', RENDERS)];

/** Every key a schema names, at every depth, so the words for each can be held to exist. */
function keysAtEveryDepth(schemas: FieldSpecification[][]): string[] {
  const keys = new Set<string>();
  const walk = (fields: FieldSpecification[]) => {
    for (const field of fields) {
      keys.add(field.key);
      if (field.kind === 'list' || field.kind === 'group') walk(field.of);
    }
  };
  for (const fields of schemas) walk(fields);
  return [...keys].sort();
}

export const WIDGET_FIELD_KEYS = keysAtEveryDepth([...Object.values(WIDGET_SCHEMAS), COLUMN_FIELDS]);
export const BINDING_FIELD_KEYS = keysAtEveryDepth(Object.values(BINDING_SCHEMAS));
