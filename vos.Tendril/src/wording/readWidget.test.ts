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
