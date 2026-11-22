import { describe, expect, it } from 'vitest';
import { searchOnSubmit } from './placeSearch';

const now = 1_000_000;

describe('searchOnSubmit', () => {
  it('sends nothing for an empty box and says to type something', () => {
    expect(searchOnSubmit({ text: '   ', now, lastSearchAt: null, currentAddress: null })).toEqual({ kind: 'empty' });
  });

  it('sends nothing for the address the site already has', () => {
    expect(searchOnSubmit({ text: ' 12 Harbour Street ', now, lastSearchAt: null, currentAddress: '12 Harbour Street' })).toEqual({
      kind: 'unchanged',
    });
  });

  it('places coordinates without a search', () => {
    expect(searchOnSubmit({ text: '25.1505, 55.5515', now, lastSearchAt: now - 10, currentAddress: null })).toEqual({
      kind: 'position',
      latitude: 25.1505,
      longitude: 55.5515,
    });
  });

  it('searches at once when the last search was 1.1 s ago or longer', () => {
    expect(searchOnSubmit({ text: 'Harbour Street', now, lastSearchAt: now - 1100, currentAddress: null })).toEqual({
      kind: 'search',
      query: 'Harbour Street',
      afterMilliseconds: 0,
    });
  });

  it('searches at once when nothing has been searched yet', () => {
    expect(searchOnSubmit({ text: 'Harbour Street', now, lastSearchAt: null, currentAddress: null })).toEqual({
      kind: 'search',
      query: 'Harbour Street',
      afterMilliseconds: 0,
    });
  });

  it('waits out the rest of 1.1 s after the last search', () => {
    expect(searchOnSubmit({ text: 'Harbour Street', now, lastSearchAt: now - 400, currentAddress: null })).toEqual({
      kind: 'search',
      query: 'Harbour Street',
      afterMilliseconds: 700,
    });
  });
});
