import { describe, it, expect } from 'vitest';
import type { Binding, DashboardSpecification, Widget } from '../types/dashboard';
import { checkDesign, type DesignCheckContext, type DesignFindingCode } from './designFindings';

/** A catchment: springs (flow, a long note) and reservoirs (capacity), springs feed reservoirs. */
const context: DesignCheckContext = {
  isKind: (name) => ['Spring', 'Reservoir', 'Village'].includes(name),
  isPredicate: (name) => ['feeds', 'supplies'].includes(name),
  statesOf: (kind) => (kind === 'Spring' ? ['dry', 'flowing'] : kind === 'Reservoir' ? ['full'] : undefined),
  propertiesOf: (kind) =>
    kind === 'Spring'
      ? [{ name: 'flow', declaredBy: 'Spring', example: 12.5, numeric: true }, { name: 'note', declaredBy: 'Spring', example: 'x'.repeat(120), numeric: false }, { name: 'region', declaredBy: 'Spring', example: 'North', numeric: false }]
      : kind === 'Reservoir'
        ? [{ name: 'capacity', declaredBy: 'Reservoir', example: 900, numeric: true }]
        : undefined,
  iconsTaken: new Set(['droplets']),
  compareKind: 'Reservoir',
};

const page = (...widgets: Widget[]): DashboardSpecification => ({ title: 'Springs', icon: 'waves', sections: [{ layout: 'grid', widgets }] });
const figure = (title: string, value: Binding): Widget => ({ type: 'kpi', title, value });
const codes = (specification: DashboardSpecification, name = 'Springs') => checkDesign(specification, name, context).map((finding) => `${finding.severity}:${finding.code}${finding.named ? ':' + finding.named : ''}`);

describe('the page', () => {
  it('is kept as it is when it breaks no rule', () => {
    expect(codes(page(figure('Flowing', { kind: 'stateCount', state: 'flowing', archetype: 'Spring', scope: { viaPredicate: 'feeds', direction: 'in' } })))).toEqual([]);
  });

  it('is refused without an icon, with an icon another page asks for, or with the word the navigation drops in its name', () => {
    expect(codes({ ...page(), icon: undefined })).toEqual(['refusal:pageWithoutAnIcon']);
    expect(codes({ ...page(), icon: 'droplets' })).toEqual(['refusal:iconAlreadyTaken:droplets']);
    expect(codes(page(), 'Springs Dashboard')).toEqual(['refusal:nameCarriesDashboard:Springs Dashboard']);
    expect(codes({ ...page(), compare: { label: 'Dam', archetype: 'Dam' } })).toEqual(['refusal:namesAnUnknownKind:Dam']);
  });
});

describe('a widget', () => {
  const each = (code: DesignFindingCode, specification: DashboardSpecification) => expect(codes(specification).some((found) => found.includes(code)), code).toBe(true);

  it('is warned about when bound to nothing', () => {
    each('widgetBoundToNothing', page({ type: 'kpi', title: 'Empty', value: undefined as never }));
  });

  it('refuses a kind, a predicate or a state the model does not declare, at any depth', () => {
    each('namesAnUnknownKind', page(figure('a', { kind: 'stateCount', state: 'flowing', archetype: 'Dam' })));
    each('namesAnUnknownPredicate', page(figure('a', { kind: 'stateCount', state: 'flowing', archetype: 'Spring', scope: { viaPredicate: 'drains' } })));
    each('namesAnUnknownPredicate', page(figure('a', { kind: 'related', via: [{ predicate: 'drains' }], thing: '$scope' })));
    each('namesAnUnknownState', page(figure('a', { kind: 'stateCount', state: 'gushing', archetype: 'Spring' })));
    each('namesAnUnknownState', page(figure('a', { kind: 'stateOf', states: ['overflowing'], thing: '$scope' })));
    each('namesAnUnknownState', page({ type: 'table', title: 't', columns: [], visibleRows: 5, rows: { kind: 'thingList', archetype: 'Spring', computed: [{ key: 'c', value: { kind: 'stateOf', states: ['gushing'], thing: '$scope' } }] } }));
  });

  it('refuses nothing on a state until the kind holding it has been read', () => {
    expect(codes(page(figure('a', { kind: 'stateCount', state: 'anything', archetype: 'Village', scope: { viaPredicate: 'supplies' } })))).toEqual([]);
    const unread: DesignCheckContext = { ...context, statesOf: () => undefined };
    expect(checkDesign(page(figure('a', { kind: 'stateCount', state: 'gushing', archetype: 'Spring', scope: { viaPredicate: 'feeds' } })), 'Springs', unread)).toEqual([]);
  });

  it('warns about a property the kind does not declare, since a computed value answers to a name the declarations do not list', () => {
    const found = codes(page(figure('a', { kind: 'property', thing: '$scope', property: 'depth' })));
    expect(found).toContain('warning:namesAnUnknownProperty:depth');
    expect(found.filter((each) => each.startsWith('refusal'))).toEqual([]);
  });

  it('holds a table to a row cap, to a sort key a column carries, and to search keys a row carries', () => {
    const rows = { kind: 'thingList' as const, archetype: 'Spring' };
    each('tableWithoutARowCap', page({ type: 'table', title: 't', columns: [], rows }));
    each('sortKeyNoColumnCarries', page({ type: 'table', title: 't', columns: [{ key: 'flow', label: 'Flow' }], rows, visibleRows: 5, sortKey: 'capacity' }));
    each('searchKeyNoRowCarries', page({ type: 'table', title: 't', columns: [{ key: 'flow', label: 'Flow' }], rows, visibleRows: 5, searchKeys: ['depth'] }));
    expect(codes(page({ type: 'table', title: 't', columns: [{ key: 'flow', label: 'Flow' }], rows, visibleRows: 5, sortKey: 'flow', searchKeys: ['region'] }))).toEqual([]);
  });

  it('warns about a column on a value the model puts no bound on', () => {
    each('columnOnAnUnboundedValue', page({ type: 'table', title: 't', columns: [{ key: 'note', label: 'Note' }], rows: { kind: 'thingList', archetype: 'Spring' }, visibleRows: 5 }));
  });

  it('refuses a compared property that reads as text', () => {
    const text: DesignCheckContext = { ...context, propertiesOf: (kind) => (kind === 'Reservoir' ? [{ name: 'name', declaredBy: 'Reservoir', example: 'RES-1', numeric: false }] : undefined) };
    const board: Widget = { type: 'leaderboard', title: 'l', entities: { kind: 'compareEntities', properties: ['name'] }, metrics: [] };
    expect(checkDesign(page(board), 'Springs', text).map((finding) => finding.code)).toContain('comparedPropertyIsText');
  });

  it('warns about a state binding on a comparing page that names no scope, but not for a roster a field picks from', () => {
    each('stateBindingWithoutScope', page(figure('a', { kind: 'stateCount', state: 'flowing', archetype: 'Spring' })));
    const form: Widget = { type: 'form', title: 'f', fields: [{ key: 'to', label: 'To', kind: 'choice', options: { kind: 'stateList', state: 'flowing', archetype: 'Spring' } }], submit: 'Go', writes: { via: 'intake', act: 'move' } };
    expect(codes(page(form))).toEqual([]);
  });
});
