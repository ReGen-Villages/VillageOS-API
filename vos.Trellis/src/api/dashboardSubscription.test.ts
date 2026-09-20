import { describe, it, expect } from 'vitest';
import { NAVIGATION_AND_SETTINGS, subscriptionForSpecification } from './dashboardSubscription';
import type { DashboardSpecification } from '../types/dashboard';

const SCOPE_ID = '11111111-2222-3333-4444-555555555555';

function specificationWith(over: Partial<DashboardSpecification>): DashboardSpecification {
  return { title: 'Ops', sections: [], ...over };
}

/** A spec whose one widget carries the binding under test. */
function specificationDrawing(value: DashboardSpecification['sections'][number]['widgets'][number]): DashboardSpecification {
  return specificationWith({ sections: [{ widgets: [value] }] });
}

describe('subscriptionForSpec', () => {
  it('keeps asking for what the navigation and the display settings are read from', () => {
    const selector = subscriptionForSpecification(specificationWith({}), null);

    expect(selector.types).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.types!));
    expect(selector.names).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.names!));
  });

  it('asks to keep following the types it names, as the navigation does', () => {
    expect(subscriptionForSpecification(specificationWith({}), null).includeLaterMatches).toBe(true);
  });

  // A reading is written as an observation, which the platform delivers only to a subscription
  // that asked; the navigation reads no readings and asks for none.
  it('asks for observations, so a figure bound to a reading moves without a reload', () => {
    expect(subscriptionForSpecification(specificationWith({}), null).includeObservations).toBe(true);
    expect(NAVIGATION_AND_SETTINGS.includeObservations).toBeUndefined();
    expect(NAVIGATION_AND_SETTINGS.includeLaterMatches).toBe(true);
  });

  it('asks for the entities the scope switcher offers', () => {
    const selector = subscriptionForSpecification(specificationWith({ compare: { label: 'site', archetype: 'Site' } }), null);

    expect(selector.types).toContain('Site');
  });

  it('names the selected entity as an identifier', () => {
    const selector = subscriptionForSpecification(specificationWith({ compare: { label: 'site', archetype: 'Site' } }), SCOPE_ID);

    expect(selector.ids).toEqual([SCOPE_ID]);
  });

  // The platform answers a count from the state endpoint and a series from its reduction, so their
  // members would arrive only to be counted again and thrown away — and a type with a member per
  // event is exactly where that is worst.
  it('leaves out the type of a binding the platform answers', () => {
    const selector = subscriptionForSpecification(
      specificationWith({
        sections: [{
          widgets: [
            { type: 'kpi', title: 'open', value: { kind: 'stateCount', state: 'open', archetype: 'Delivery' } },
            {
              type: 'kpi',
              title: 'flow',
              value: {
                kind: 'timeseries',
                archetype: 'Reading',
                happenedAt: 'happenedAt',
                op: 'count',
                bucketSeconds: 3600,
                buckets: 8,
              },
            },
          ],
        }],
      }),
      null,
    );

    expect(selector.types).not.toContain('Delivery');
    expect(selector.types).not.toContain('Reading');
  });

  // Its rows arrive complete now, which makes the type look droppable. It is not: dropping it
  // leaves every computed column on the table empty and every detail card opened from it titled
  // with a bare identifier — quietly, with nothing on screen to say why.
  it('keeps the type of a state-driven table, which its rows are still read against', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'table',
        columns: [{ key: 'reference', label: 'Reference' }],
        rows: {
          kind: 'stateList',
          state: 'open',
          archetype: 'Delivery',
          properties: ['reference'],
          computed: [{ key: 'via', value: { kind: 'related', via: [{ predicate: 'references' }] } }],
        },
      }),
      null,
    );

    expect(selector.types).toContain('Delivery');
  });

  it('asks for the type a roster draws', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({ type: 'table', columns: [], rows: { kind: 'thingList', archetype: 'Parcel' } }),
      null,
    );

    expect(selector.types).toContain('Parcel');
  });

  it('asks for the rows a writing widget lists and the rosters its fields are chosen from', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'action',
        rows: { kind: 'thingList', archetype: 'Spring' },
        asks: [{ key: 'reader', label: 'Reader', kind: 'choice', options: { kind: 'thingList', archetype: 'Person' } }],
        writes: { via: 'readings', choices: [{ label: 'Assign', act: 'assign' }] },
      }),
      null,
    );
    expect(selector.types).toEqual(expect.arrayContaining(['Spring', 'Person']));

    const form = subscriptionForSpecification(
      specificationDrawing({
        type: 'form',
        fields: [{ key: 'catchment', label: 'Catchment', kind: 'multichoice', options: { kind: 'thingList', archetype: 'Catchment' } }],
        submit: 'Book',
        writes: { via: 'readings', act: 'book', archetype: 'Reading' },
      }),
      null,
    );
    expect(form.types).toContain('Catchment');
  });

  // A binding narrowed to the selected entity reaches its rows along that entity's relationships, so asking
  // for its type as well would pull in every other entity's rows for a page showing one.
  it('leaves out a scoped binding\'s type once an entity is selected, and follows its edge instead', () => {
    const scoped = specificationDrawing({
      type: 'table',
      columns: [],
      rows: { kind: 'thingList', archetype: 'Parcel', scope: { viaPredicate: 'has' } },
    });

    expect(subscriptionForSpecification(scoped, SCOPE_ID).types).not.toContain('Parcel');
    expect(subscriptionForSpecification(scoped, SCOPE_ID).traverse)
      .toContainEqual({ predicate: 'has', direction: 'outgoing', depth: expect.any(Number) });
  });

  // With no entity selected the binding does run over every member of the type, so the page holds
  // them all — the same answer the resolver gives.
  it('asks for a scoped binding\'s type while every entity is being shown', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'table',
        columns: [],
        rows: { kind: 'thingList', archetype: 'Parcel', scope: { viaPredicate: 'has' } },
      }),
      null,
    );

    expect(selector.types).toContain('Parcel');
  });

  // A scope walk is transitive: the entity relates to Things that in turn relate to the rows.
  it('follows a scope predicate as far as it reaches, and a binding\'s step one hop', () => {
    const selector = subscriptionForSpecification(
      specificationWith({
        sections: [{
          widgets: [
            { type: 'kpi', title: 'a', value: { kind: 'aggregate', archetype: 'Parcel', op: 'count', scope: { viaPredicate: 'contains' } } },
            { type: 'kpi', title: 'b', value: { kind: 'related', via: [{ predicate: 'studies', direction: 'in' }] } },
          ],
        }],
      }),
      SCOPE_ID,
    );

    const contains = selector.traverse!.find((rule) => rule.predicate === 'contains')!;
    const studies = selector.traverse!.find((rule) => rule.predicate === 'studies')!;
    expect(contains.depth).toBeGreaterThan(1);
    expect(studies).toEqual({ predicate: 'studies', direction: 'incoming', depth: 1 });
  });

  // The platform applies each rule to everything selected before it, and applies it once. A walk of
  // two steps is only reproduced if its first relationship is asked for before its second.
  it('asks for a walk\'s edges in the order the walk takes them', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'verdict',
        rows: [{
          label: 'Water',
          verdicts: { kind: 'verdict', states: [], via: [{ predicate: 'studies', direction: 'in' }, { predicate: 'has' }] },
        }],
      }),
      SCOPE_ID,
    );

    const followed = selector.traverse!.map((rule) => rule.predicate);
    expect(followed.indexOf('studies')).toBeLessThan(followed.indexOf('has'));
  });

  // A tile saying where its figure came from is useless if the Thing that says so was
  // never sent. The line then drops the source it names, which reads exactly like a figure whose
  // source nobody recorded — the one reading the origin vocabulary exists to refuse.
  it('follows an origin binding to the Thing holding the value and on to what says so', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'kpi',
        title: 'Drawn area',
        value: { kind: 'related', via: [{ predicate: 'has', archetype: 'Parcel' }], property: 'measuredAreaHectares' },
        origin: {
          kind: 'origin',
          property: 'measuredAreaHectares',
          reads: { stated: 'from a boundary {source}' },
          via: [{ predicate: 'has', archetype: 'Parcel' }],
          source: { via: [{ predicate: 'obtainedBy' }] },
        },
      }),
      SCOPE_ID,
    );

    expect(selector.traverse!.map((rule) => rule.predicate)).toContain('obtainedBy');
  });

  // The source walk starts from what `via` reached, not from the scope. Asked for first it would be
  // applied to the scope entity, reach nothing, and select nothing.
  it('asks for the source edge after the edge that reaches the Thing holding the value', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'kpi',
        title: 'Rainfall',
        value: { kind: 'property', thing: '$scope', property: 'rainfallMillimetresPerYear' },
        origin: {
          kind: 'origin',
          property: 'rainfallMillimetresPerYear',
          reads: { measured: 'resolved {resolvedAt} from {source}' },
          via: [{ predicate: 'studies', direction: 'in' }],
          source: { via: [{ predicate: 'has' }], resolvedAt: 'lastResolvedAt' },
        },
      }),
      SCOPE_ID,
    );

    const followed = selector.traverse!.map((rule) => rule.predicate);
    expect(followed.indexOf('studies')).toBeLessThan(followed.indexOf('has'));
  });

  it('follows a source walk taken straight from the scope entity', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'kpi',
        title: 'Rainfall',
        value: { kind: 'property', thing: '$scope', property: 'rainfallMillimetresPerYear' },
        origin: {
          kind: 'origin',
          property: 'rainfallMillimetresPerYear',
          reads: { measured: 'resolved {resolvedAt} from {source}' },
          source: { via: [{ predicate: 'has', archetype: 'DataSource' }], resolvedAt: 'lastResolvedAt' },
        },
      }),
      SCOPE_ID,
    );

    // The relationship is what brings the sources in; the step's archetype narrows what it reached, the way
    // it does for every other walking binding, so it is not asked for as a type of its own.
    expect(selector.traverse!.map((rule) => rule.predicate)).toContain('has');
  });

  // A detail window walks the same way a binding does, and it opens on a row of a page that has
  // already narrowed — so what it reaches has to be in the page's own set.
  it('follows the edges a detail card walks, root first', () => {
    const selector = subscriptionForSpecification(
      specificationWith({
        detail: { relations: [{ predicate: 'has', relations: [{ predicate: 'categorizedAs' }] }] },
      }),
      SCOPE_ID,
    );

    const followed = selector.traverse!.map((rule) => rule.predicate);
    expect(followed).toEqual(['has', 'categorizedAs']);
  });

  it('finds the bindings nested inside a ratio and a computed column', () => {
    const selector = subscriptionForSpecification(
      specificationWith({
        sections: [{
          widgets: [
            {
              type: 'kpi',
              title: 'rate',
              value: {
                kind: 'ratio',
                numerator: { kind: 'aggregate', archetype: 'Harvest', op: 'sum', property: 'volume' },
                denominator: { kind: 'aggregate', archetype: 'Parcel', op: 'count' },
              },
            },
            {
              type: 'table',
              columns: [],
              rows: {
                kind: 'thingList',
                archetype: 'Site',
                computed: [{ key: 'reach', value: { kind: 'related', via: [{ predicate: 'supplies' }] } }],
              },
            },
          ],
        }],
      }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['Harvest', 'Parcel', 'Site']));
    expect(selector.traverse!.map((rule) => rule.predicate)).toContain('supplies');
  });

  // A spec's Thing reference is an id or a name, and the platform reads the two as different
  // questions: a name sent as an id is refused rather than looked up.
  it('tells a named Thing apart from one referred to by identifier', () => {
    const selector = subscriptionForSpecification(
      specificationWith({
        sections: [{
          widgets: [
            { type: 'kpi', title: 'a', value: { kind: 'property', thing: 'Reservoir', property: 'volume' } },
            { type: 'kpi', title: 'b', value: { kind: 'property', thing: SCOPE_ID, property: 'volume' } },
            { type: 'kpi', title: 'c', value: { kind: 'property', thing: '$scope', property: 'volume' } },
          ],
        }],
      }),
      null,
    );

    expect(selector.names).toContain('Reservoir');
    expect(selector.ids).toEqual([SCOPE_ID]);
    expect(selector.names).not.toContain('$scope');
  });
});

// A tile reading the newest point of a series holds that series as a nested binding, so what the
// series narrows by is only reachable through it.
describe('a tile reading the newest point of a series', () => {
  const series = {
    kind: 'timeseries',
    archetype: 'Reading',
    happenedAt: 'happenedAt',
    op: 'sum',
    property: 'volume',
    bucketSeconds: 900,
    buckets: 32,
    bucketsPerPoint: 4,
    scope: { viaPredicate: 'contains', direction: 'out' as const },
  } as const;

  it('follows the edge its nested series narrows by', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({ type: 'kpi', title: 'flow', value: { kind: 'latest', series } }),
      SCOPE_ID,
    );

    expect(selector.traverse!.map((rule) => rule.predicate)).toContain('contains');
  });

  // The platform answers the reduction, so the members would arrive only to be reduced again and
  // thrown away — the same reason the series itself is left out.
  it('leaves out the type its nested series reduces', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({ type: 'kpi', title: 'flow', value: { kind: 'latest', series } }),
      null,
    );

    expect(selector.types).not.toContain('Reading');
  });
});

// A step's parameter bound to the model — a setpoint the study declares, a bound a class Thing carries
// — is read like the series it shapes, so the Thing it names is in the subscription and a change to it
// re-asks the question.
describe('a history binding whose step parameter is bound', () => {
  it('follows the edge the bound parameter walks and names the Thing it reads', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'kpi', title: 'degree days',
        value: {
          kind: 'history', property: 'temperatureCelsius', windowSeconds: 31536000,
          steps: [{ fold: 'day', function: 'Average' }, {
            fold: 'month', function: 'SumAbove',
            threshold: { kind: 'related', via: [{ predicate: 'studies', direction: 'in' }], property: 'coolingSetpointCelsius' },
          }],
        },
      }),
      SCOPE_ID,
    );

    expect(selector.traverse!.map((rule) => rule.predicate)).toContain('studies');
  });

  it('names the class Thing a stacked share colours by', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
        type: 'stackedShares', title: 'stress',
        classes: [{
          label: 'no stress',
          colour: { kind: 'property', thing: 'No thermal stress', property: 'colour' },
          share: { kind: 'history', property: 'apparentTemperatureCelsius', windowSeconds: 31536000, steps: [{ fold: 'monthOfYear', function: 'ShareWithin', from: 9, to: 26 }] },
        }],
      }),
      SCOPE_ID,
    );

    expect(selector.names).toContain('No thermal stress');
  });
});

// A binding the widget holds and the subscription does not name is a figure that never arrives on a
// live page, so every one of the widget's bindings has to reach the selector.
describe('subscriptionForSpec over a rangeBar widget', () => {
  it('asks for every binding the widget holds', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
              type: 'rangeBar',
              title: 'Temperature',
              hint: 'by month',
              unit: 'degrees',
              months: {
                recordedHigh: { kind: 'aggregate', archetype: 'RecordedHigh', op: 'count' }, designHigh: { kind: 'aggregate', archetype: 'DesignHigh', op: 'count' },
                averageHigh: { kind: 'aggregate', archetype: 'AverageHigh', op: 'count' }, mean: { kind: 'aggregate', archetype: 'Mean', op: 'count' },
                averageLow: { kind: 'aggregate', archetype: 'AverageLow', op: 'count' }, designLow: { kind: 'aggregate', archetype: 'DesignLow', op: 'count' },
                recordedLow: { kind: 'aggregate', archetype: 'RecordedLow', op: 'count' },
              },
              bands: [{ label: 'Comfort', from: { kind: 'aggregate', archetype: 'BandFrom', op: 'count' }, to: { kind: 'aggregate', archetype: 'BandTo', op: 'count' }, colour: '#0f0' }],
            }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['RecordedHigh', 'DesignHigh', 'AverageHigh', 'Mean', 'AverageLow', 'DesignLow', 'RecordedLow', 'BandFrom', 'BandTo']));
  });
});

describe('subscriptionForSpec over a lineSeries widget', () => {
  it('asks for every binding the widget holds', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
              type: 'lineSeries',
              title: 'Rain',
              series: [{ label: 'This year', value: { kind: 'aggregate', archetype: 'ThisYear', op: 'count' } }, { label: 'Last year', value: { kind: 'aggregate', archetype: 'LastYear', op: 'count' } }],
            }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['ThisYear', 'LastYear']));
  });
});

describe('subscriptionForSpec over a heatmap widget', () => {
  it('asks for every binding the widget holds', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
              type: 'heatmap',
              title: 'Sun',
              value: { kind: 'aggregate', archetype: 'Reading', op: 'count' },
              sun: { latitude: { kind: 'aggregate', archetype: 'Latitude', op: 'count' }, longitude: { kind: 'aggregate', archetype: 'Longitude', op: 'count' }, utcOffsetSeconds: { kind: 'aggregate', archetype: 'Offset', op: 'count' } },
            }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['Reading', 'Latitude', 'Longitude', 'Offset']));
  });
});

describe('subscriptionForSpec over a stackedShares widget', () => {
  it('asks for every binding the widget holds', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
              type: 'stackedShares',
              title: 'Cover',
              classes: [
                { label: 'Trees', colour: '#080', share: { kind: 'aggregate', archetype: 'Trees', op: 'count' } },
                { label: 'Grass', colour: { kind: 'aggregate', archetype: 'GrassColour', op: 'count' }, share: { kind: 'aggregate', archetype: 'Grass', op: 'count' } },
              ],
            }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['Trees', 'GrassColour', 'Grass']));
  });
});

describe('subscriptionForSpec over a divergingBar widget', () => {
  it('asks for every binding the widget holds', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
              type: 'divergingBar',
              title: 'Balance',
              up: { label: 'Gain', value: { kind: 'aggregate', archetype: 'Gain', op: 'count' }, threshold: { kind: 'aggregate', archetype: 'GainLine', op: 'count' } },
              down: { label: 'Loss', value: { kind: 'aggregate', archetype: 'Loss', op: 'count' }, threshold: { kind: 'aggregate', archetype: 'LossLine', op: 'count' } },
            }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['Gain', 'GainLine', 'Loss', 'LossLine']));
  });
});

describe('subscriptionForSpec over a smallMultiples widget', () => {
  it('asks for every binding the widget holds', () => {
    const selector = subscriptionForSpecification(
      specificationDrawing({
              type: 'smallMultiples',
              title: 'Months',
              bars: { label: 'Rain', value: { kind: 'aggregate', archetype: 'Rain', op: 'count' } },
              line: { label: 'Heat', value: { kind: 'aggregate', archetype: 'Heat', op: 'count' } },
              band: { label: 'Comfort', from: { kind: 'aggregate', archetype: 'BandFrom', op: 'count' }, to: { kind: 'aggregate', archetype: 'BandTo', op: 'count' }, colour: '#0f0' },
            }),
      null,
    );

    expect(selector.types).toEqual(expect.arrayContaining(['Rain', 'Heat', 'BandFrom', 'BandTo']));
  });
});
