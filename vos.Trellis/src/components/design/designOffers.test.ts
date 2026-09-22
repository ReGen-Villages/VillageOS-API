import { describe, it, expect } from 'vitest';
import { kindReachedBy } from './designOffers';
import { catchmentOffers } from './testOffers';

describe('kindReachedBy', () => {
  const offers = catchmentOffers();

  it('follows each hop through the links the model holds from the kind reached so far', () => {
    expect(kindReachedBy('Spring', [{ predicate: 'feeds' }], offers)).toBe('Reservoir');
    expect(kindReachedBy('Spring', [{ predicate: 'feeds' }, { predicate: 'supplies' }], offers)).toBe('Village');
  });

  it('takes the kind a step states over what the links say, and reaches nothing where a hop is unknown', () => {
    expect(kindReachedBy('Spring', [{ predicate: 'anything', archetype: 'Village' }], offers)).toBe('Village');
    expect(kindReachedBy('Spring', [{ predicate: 'drains' }], offers)).toBeUndefined();
    expect(kindReachedBy(undefined, [{ predicate: 'feeds' }], offers)).toBeUndefined();
  });

  it('walks a link backwards when the step says so', () => {
    expect(kindReachedBy('Reservoir', [{ predicate: 'feeds', direction: 'in' }], offers)).toBe('Spring');
    expect(kindReachedBy('Reservoir', [{ predicate: 'feeds' }], offers)).toBeUndefined();
  });
});
