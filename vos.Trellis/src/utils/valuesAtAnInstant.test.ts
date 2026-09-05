import { describe, it, expect } from 'vitest';
import { valuesAtAnInstant } from './valuesAtAnInstant';

describe('valuesAtAnInstant', () => {
  it('keys an override by the path of source names that reaches it, an own property by its name', () => {
    const values = valuesAtAnInstant({
      Properties: { note: 'dredged' },
      InheritedOverrides: {
        'pond-id': {
          SourceId: 'pond-id',
          SourceName: 'Pond',
          Properties: {},
          Inherited: {
            'reservoir-id': {
              SourceId: 'reservoir-id',
              SourceName: 'Reservoir',
              Properties: { capacity: 100 },
              Inherited: {},
            },
          },
        },
      },
    });

    expect(values).toEqual({ note: 'dredged', 'Pond.Reservoir.capacity': 100 });
  });

  it('answers nothing for an object that held nothing at the instant', () => {
    expect(valuesAtAnInstant({ Properties: {}, InheritedOverrides: {} })).toEqual({});
  });
});
