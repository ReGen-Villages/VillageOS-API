import { describe, it, expect } from 'vitest';
import { thingsInStatePath } from './stateQuery';

describe('thingsInStatePath', () => {
  it('asks for a state by its name alone where nothing narrows it', () => {
    expect(thingsInStatePath('Running low')).toBe('/api/states/Running%20low/things');
    expect(thingsInStatePath('RunningLow', {})).toBe('/api/states/RunningLow/things');
  });

  it('carries every narrowing the endpoint accepts, in one fixed order', () => {
    expect(thingsInStatePath('RunningLow', {
      alsoIn: ['Fed', 'Open'],
      notIn: ['Closed'],
      type: 'Reservoir',
      within: 'c1',
      withinPredicate: 'drains into',
      includeArchetypes: true,
      limit: 20,
      properties: ['levelMetres', 'capacityCubicMetres'],
    })).toBe(
      '/api/states/RunningLow/things?alsoIn=Fed%2COpen&notIn=Closed&type=Reservoir&within=c1&withinPredicate=drains+into'
      + '&includeArchetypes=true&limit=20&properties=levelMetres%2CcapacityCubicMetres',
    );
  });

  it('writes a value as a form does, so the question reads the same wherever it is built', () => {
    expect(thingsInStatePath('RunningLow', { type: "Åsen's (north) ~spring* & co!" }))
      .toBe('/api/states/RunningLow/things?type=%C3%85sen%27s+%28north%29+%7Espring*+%26+co%21');
  });

  it('leaves out a container named without the predicate it is reached by', () => {
    expect(thingsInStatePath('RunningLow', { within: 'c1' })).toBe('/api/states/RunningLow/things');
  });

  it('asks for the count alone', () => {
    expect(thingsInStatePath('RunningLow', { countOnly: true })).toBe('/api/states/RunningLow/things?countOnly=true');
  });
});
