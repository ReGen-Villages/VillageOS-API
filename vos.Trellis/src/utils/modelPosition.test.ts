import { describe, it, expect } from 'vitest';
import type { VosThing } from '../types/vos';
import { modelCentre } from './modelPosition';

function thing(Id: string, Properties: Record<string, unknown>): VosThing {
  return { Id, Name: Id, Properties };
}

describe('modelCentre', () => {
  it('averages the Things that carry coordinates', () => {
    const centre = modelCentre([
      thing('a', { latitude: 41.3, longitude: -70.6 }),
      thing('b', { latitude: 41.5, longitude: -70.8 }),
    ])!;
    expect(centre.latitude).toBeCloseTo(41.4, 10);
    expect(centre.longitude).toBeCloseTo(-70.7, 10);
  });

  it('ignores Things that carry neither', () => {
    expect(
      modelCentre([thing('a', { latitude: 41.3, longitude: -70.6 }), thing('b', { name: 'no position' })]),
    ).toEqual({ latitude: 41.3, longitude: -70.6 });
  });

  it('ignores a Thing carrying only one of the pair, which would place it in the wrong ocean', () => {
    expect(
      modelCentre([thing('a', { latitude: 41.3, longitude: -70.6 }), thing('b', { latitude: 51.5 })]),
    ).toEqual({ latitude: 41.3, longitude: -70.6 });
  });

  it('ignores coordinates that arrived as text rather than numbers', () => {
    expect(modelCentre([thing('a', { latitude: '41.3', longitude: '-70.6' })])).toBeNull();
  });

  it('returns nothing when no Thing in the model has a position', () => {
    expect(modelCentre([thing('a', {}), thing('b', {})])).toBeNull();
  });
});
