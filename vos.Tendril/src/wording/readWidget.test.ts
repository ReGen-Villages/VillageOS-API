import { describe, it, expect } from 'vitest';
import { buildModelIndex, type ResolveContext } from '../api/dashboardApi';
import type { ModelReads } from '../api/modelReads';
import type { Widget } from '../types/dashboard';
import type { VosRelationship, VosThing } from '../types/vos';
import { readWidget } from './readWidget';

const thing = (Id: string, Name: string, extra: Partial<VosThing> = {}): VosThing =>
  ({ Id, Name, IsArchetype: false, Properties: {}, ...extra }) as unknown as VosThing;
const edge = (SubjectId: string, PredicateId: string, TargetId: string): VosRelationship =>
  ({ Id: `${SubjectId}-${PredicateId}-${TargetId}`, SubjectId, PredicateId, TargetId, Properties: {} }) as VosRelationship;

const context = (language?: string): ResolveContext => ({
  index: buildModelIndex(
    [
      thing('is', 'is'),
      thing('reservoir-kind', 'Reservoir', { IsArchetype: true }),
      thing('r1', 'Spring reservoir', {
        Properties: { storedLitres: 12500.5, drawnLitresPerDay: 900, status: 'Running low' },
        PropertyWriteKinds: { storedLitres: 'ObservationOnly' },
        DerivedProperties: { daysOfSupply: { Expression: 'storedLitres / drawnLitresPerDay', Reads: ['storedLitres', 'drawnLitresPerDay'] } },
      }),
    ],
    [edge('r1', 'is', 'reservoir-kind')],
  ),
  scopeId: 'r1',
  reads: {
    thingsInState: async (state: string) => ({ StateName: state, Things: state === 'Short' ? [{ Id: 'r1', Name: 'Spring reservoir' }] : [] }),
    thingRanges: async () => null,
  } as unknown as ModelReads,
  language,
});

describe('a widget read out for a reader with no screen', () => {
  it('words a figure, its unit and where it came from as the console does, in the language of the context', async () => {
    const widget: Widget = {
      type: 'kpi',
      title: 'Stored',
      value: { kind: 'property', thing: '$scope', property: 'storedLitres' },
      format: 'decimal1',
      unit: 'L',
      origin: { kind: 'origin', thing: '$scope', property: 'storedLitres', reads: { measured: 'measured on site' } },
    };

    expect(await readWidget(widget, context('de'))).toEqual({
      type: 'kpi',
      title: 'Stored',
      value: 12500.5,
      worded: '12.500,5',
      unit: 'L',
      origin: [{ origin: 'measured', sentence: 'measured on site' }],
    });
  });

  it('gives a figure that is a word no unit, and words in English where no language is given', async () => {
    const widget: Widget = { type: 'kpi', title: 'Status', value: { kind: 'related', thing: '$scope', via: [], property: 'status' }, unit: 'L' };

    expect(await readWidget(widget, context())).toEqual({ type: 'kpi', title: 'Status', value: 'Running low', worded: 'Running low', origin: [] });
  });

  it('reads each verdict a row holds as its sentence', async () => {
    const widget: Widget = {
      type: 'verdict',
      rows: [{ label: 'Supply', verdicts: { kind: 'verdict', thing: '$scope', states: [{ state: 'Short', reads: 'running short' }, { state: 'Covered', reads: 'covered' }] } }],
    };

    expect(await readWidget(widget, context('en'))).toEqual({
      type: 'verdict',
      rows: [{ label: 'Supply', verdicts: [{ state: 'Short', sentence: 'running short', levers: [] }] }],
    });
  });

  it('reads a worked figure with its value, its formula and each input', async () => {
    const widget: Widget = {
      type: 'working',
      title: 'How it was worked out',
      rows: [{
        label: 'Stored',
        value: { kind: 'property', thing: '$scope', property: 'storedLitres' },
        working: { kind: 'working', thing: '$scope', property: 'daysOfSupply' },
        format: 'integer',
        unit: 'L',
      }],
    };

    expect(await readWidget(widget, context('en'))).toEqual({
      type: 'working',
      title: 'How it was worked out',
      rows: [{
        label: 'Stored',
        value: 12500.5,
        worded: '12,501',
        unit: 'L',
        runs: [{
          formula: 'storedLitres / drawnLitresPerDay',
          inputs: [{ term: 'storedLitres', value: '12,500.5' }, { term: 'drawnLitresPerDay', value: '900' }],
        }],
      }],
    });
  });

  it('reads a table as the rows its binding resolved to and the columns it declares, with no sentence', async () => {
    const columns = [{ key: 'name', label: 'Reservoir' }, { key: 'storedLitres', label: 'Stored', numeric: true, format: 'integer' as const }];
    const widget: Widget = { type: 'table', title: 'Reservoirs', columns, rows: { kind: 'thingList', archetype: 'Reservoir' } };

    const read = await readWidget(widget, context('en'));

    expect(read).toMatchObject({ type: 'table', title: 'Reservoirs', columns });
    expect(read).toHaveProperty('slots', [{ kind: 'thingList', result: [expect.objectContaining({ id: 'r1', name: 'Spring reservoir', storedLitres: 12500.5 })] }]);
  });

  it('reads a profile line as its lowest, highest and difference within the span, in words', async () => {
    const levels = { originLatitude: 25, originLongitude: 55, cellSizeMetres: 30, latitudeStepDegrees: 0.0003, longitudeStepDegrees: 0.0003, rows: 1, columns: 4, values: [84, 112, 96, 70] };
    const alongTheLine: ResolveContext = {
      ...context('en'),
      index: buildModelIndex([thing('r1', 'Spring reservoir', { Properties: { levels, spanFrom: 30, spanTo: 60 } })], []),
    };
    const widget: Widget = {
      type: 'profileLine', title: 'West to east', profile: { kind: 'value', property: 'levels' }, unit: 'm', format: 'integer',
      span: { from: { kind: 'property', thing: '$scope', property: 'spanFrom' }, to: { kind: 'property', thing: '$scope', property: 'spanTo' } },
    };

    expect(await readWidget(widget, alongTheLine)).toEqual({
      type: 'profileLine', title: 'West to east',
      span: { from: 30, to: 60 },
      figures: { lowest: 96, highest: 112, difference: 16 },
      worded: { lowest: '96 m', highest: '112 m', difference: '16 m' },
    });
  });

  it('reads a grid map as the grid and outline the page\'s own Thing holds', async () => {
    const depths = { originLatitude: 25, originLongitude: 55, cellSizeMetres: 30, latitudeStepDegrees: 0.001, longitudeStepDegrees: 0.001, rows: 1, columns: 2, values: [3.5, null] };
    const boundary = { type: 'Polygon', coordinates: [[[55, 25], [55.001, 25], [55.001, 25.001], [55, 25]]] };
    const withAGrid: ResolveContext = {
      ...context('en'),
      index: buildModelIndex([thing('r1', 'Spring reservoir', { Properties: { depths, boundary: { Json: JSON.stringify(boundary) } } })], []),
    };
    const widget: Widget = { type: 'gridMap', title: 'Depth', grid: { kind: 'value', property: 'depths' }, outline: { kind: 'value', property: 'boundary' } };

    expect(await readWidget(widget, withAGrid)).toEqual({
      type: 'gridMap', title: 'Depth', slots: [{ kind: 'value', result: depths }, { kind: 'value', result: boundary }],
    });
  });
});

describe('a widget holding a value binding, read out for a reader with no screen', () => {
  const shoreline = { type: 'Polygon', coordinates: [[[0, 0], [1, 0], [1, 1], [0, 0]]] };
  const holdingAShape: ResolveContext = {
    index: buildModelIndex([thing('r1', 'Spring reservoir', { Properties: { shoreline: { Json: JSON.stringify(shoreline) } } })], []),
    scopeId: 'r1',
    reads: {} as ModelReads,
    language: 'en',
  };

  it('reads a shape a widget draws as the object the model holds, with no sentence', async () => {
    const widget = {
      type: 'densityGrid',
      title: 'Level against inflow',
      cells: { kind: 'const', value: 0 },
      across: {},
      up: {},
      outlines: [{ shape: { kind: 'value', thing: '$scope', property: 'shoreline' } }],
    } as unknown as Widget;

    expect(await readWidget(widget, holdingAShape)).toEqual({
      type: 'densityGrid',
      title: 'Level against inflow',
      slots: [{ kind: 'const', result: 0 }, { kind: 'value', result: shoreline }],
    });
  });

  it('gives a figure slot holding an object no wording of its own', async () => {
    const widget: Widget = { type: 'kpi', title: 'Shoreline', value: { kind: 'value', thing: '$scope', property: 'shoreline' } };

    expect(await readWidget(widget, holdingAShape)).toMatchObject({ type: 'kpi', worded: '—', value: shoreline, origin: [] });
  });
});

// The platform reads a name two kinds each declare as ambiguous and gives it no value, so a card that
// showed one kind's value, and said it came from that kind, would state what the platform will not.
describe('a figure two kinds of its Thing each declare, read out for a reader with no screen', () => {
  const twoKinds: ResolveContext = {
    index: buildModelIndex(
      [
        thing('is', 'is'),
        thing('has', 'has'),
        thing('zone', 'Zone', { IsArchetype: true, Properties: { householdSize: 2 } }),
        thing('field', 'Åker', { IsArchetype: true, Properties: { householdSize: 3 } }),
        thing('census', 'Census 2021'),
        thing('lot', 'Lot 4'),
      ],
      [edge('lot', 'is', 'zone'), edge('lot', 'is', 'field'), edge('lot', 'has', 'census')],
    ),
    scopeId: 'lot',
    reads: {} as ModelReads,
    language: 'en',
  };

  it('shows no value, and says so in the model\'s words without naming either kind or a source', async () => {
    const widget: Widget = {
      type: 'kpi',
      title: 'Household size',
      value: { kind: 'property', thing: '$scope', property: 'householdSize' },
      origin: {
        kind: 'origin',
        thing: '$scope',
        property: 'householdSize',
        reads: { assumed: 'assumed from {source}', stated: 'stated by {source}', ambiguous: 'two of its kinds each give a value {source}' },
        source: { via: [{ predicate: 'has' }] },
      },
    };

    expect(await readWidget(widget, twoKinds)).toEqual({
      type: 'kpi',
      title: 'Household size',
      value: NaN,
      worded: '—',
      origin: [{ origin: 'ambiguous', sentence: 'two of its kinds each give a value' }],
    });
  });
});
