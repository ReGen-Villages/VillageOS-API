import { describe, it, expect } from 'vitest';
import { readModelLoadProperties } from './guiSettings';

describe('readModelLoadProperties', () => {
  it('reads the names a model declares its pages are drawn with', () => {
    expect(readModelLoadProperties({ ModelLoadProperties: 'ifcClass,ifcGlobalId' }))
      .toEqual(['ifcClass', 'ifcGlobalId']);
  });

  it('ignores spacing around the commas, which is the author being readable', () => {
    expect(readModelLoadProperties({ ModelLoadProperties: ' latitude , longitude ' }))
      .toEqual(['latitude', 'longitude']);
  });

  it('treats a model that declares nothing as asking for everything', () => {
    expect(readModelLoadProperties(null)).toEqual([]);
    expect(readModelLoadProperties({})).toEqual([]);
    expect(readModelLoadProperties({ ModelLoadProperties: '' })).toEqual([]);
  });

  it('treats a list of nothing but separators as asking for everything', () => {
    expect(readModelLoadProperties({ ModelLoadProperties: ' , , ' })).toEqual([]);
  });

  // Narrowing on a value that is not a list would strip properties a page needs; loading everything
  // is only slower.
  it('treats a value that is not text as asking for everything', () => {
    expect(readModelLoadProperties({ ModelLoadProperties: 42 })).toEqual([]);
    expect(readModelLoadProperties({ ModelLoadProperties: ['a', 'b'] })).toEqual([]);
  });
});
