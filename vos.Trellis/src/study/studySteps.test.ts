import { describe, expect, it } from 'vitest';
import { canOpen, editionChosen, chooseEdition, stepsFor } from './studySteps';

function storage(initial: Record<string, string> = {}): Storage {
  const held = new Map(Object.entries(initial));
  return {
    get length() {
      return held.size;
    },
    clear: () => held.clear(),
    getItem: (key) => held.get(key) ?? null,
    key: (index) => [...held.keys()][index] ?? null,
    removeItem: (key) => void held.delete(key),
    setItem: (key, value) => void held.set(key, value),
  };
}

describe('stepsFor', () => {
  it('walks Pro through Location, Data, Village, Layout and Timeline', () => {
    expect(stepsFor('pro')).toEqual(['location', 'data', 'village', 'layout', 'timeline']);
  });

  it('walks Lite through Location, Data, the village layout and Timeline', () => {
    expect(stepsFor('lite')).toEqual(['location', 'data', 'villageLayout', 'timeline']);
  });
});

describe('canOpen', () => {
  it('opens Location whether or not a location is confirmed', () => {
    expect(canOpen('location', false)).toBe(true);
    expect(canOpen('location', true)).toBe(true);
  });

  it('opens no later step before a location is confirmed', () => {
    expect(canOpen('data', false)).toBe('locationNeeded');
  });

  // Only Location is built so far, so every later step is shown as one still to come.
  it('opens no step that has no page yet, even with a location confirmed', () => {
    expect(canOpen('data', true)).toBe('notBuilt');
    expect(canOpen('timeline', true)).toBe('notBuilt');
  });
});

describe('the edition kept per browser', () => {
  it('is Lite until Pro is chosen', () => {
    expect(editionChosen(storage())).toBe('lite');
  });

  it('is the edition chosen last', () => {
    const kept = storage();

    chooseEdition(kept, 'pro');
    expect(editionChosen(kept)).toBe('pro');

    chooseEdition(kept, 'lite');
    expect(editionChosen(kept)).toBe('lite');
  });

  it('reads anything other than Pro as Lite', () => {
    expect(editionChosen(storage({ studyEdition: 'gold' }))).toBe('lite');
  });

  it('is Lite where the browser keeps nothing', () => {
    const refusing = { ...storage(), getItem: () => { throw new Error('storage is off'); } } as Storage;

    expect(editionChosen(refusing)).toBe('lite');
  });
});
