import { describe, it, expect } from 'vitest';
import { pathLabel, wireDrawing, withoutChoices } from './wireDrawing';

const choiceNames = new Map([['c1', 'Large'], ['c2', 'Small']]);

describe('wireDrawing', () => {
  it('draws a wire taken on fail dashed', () => {
    expect(wireDrawing({}, ['fail'], 'fail', choiceNames).style).toEqual({ strokeDasharray: '6 4' });
    expect(wireDrawing({}, ['success'], 'fail', choiceNames).style).toBeUndefined();
  });

  it('labels a wire with the choices it is taken on, ahead of its field mapping', () => {
    expect(wireDrawing({ fromPath: 'a', toPath: 'b' }, ['c1', 'c2'], 'fail', choiceNames).label).toBe('Large, Small · a → b');
    expect(wireDrawing({}, ['c2'], 'fail', choiceNames).label).toBe('Small');
    expect(wireDrawing({}, ['success'], 'fail', choiceNames).label).toBeUndefined();
  });

  it('says what a wire maps without naming a choice it is not taken on', () => {
    expect(pathLabel('a', undefined, 'x')).toBe('a → · ƒ');
    expect(wireDrawing({ transform: 'x' }, ['success'], undefined, choiceNames).label).toBe('ƒ');
  });
});

describe('withoutChoices', () => {
  const edges = [
    { id: 'w1', source: 'sort', data: { takenOn: ['c1', 'fail'] } },
    { id: 'w2', source: 'sort', data: { takenOn: ['c2'] } },
    { id: 'w3', source: 'other', data: { takenOn: ['c1'] } },
  ];

  it('takes a removed choice off the wires leaving its node and leaves every other wire as it was', () => {
    const kept = withoutChoices(edges, 'sort', new Set(['c1']));

    expect(kept.map((edge) => edge.data.takenOn)).toEqual([['fail'], ['c2'], ['c1']]);
    expect(kept[1]).toBe(edges[1]);
    expect(kept[2]).toBe(edges[2]);
  });
});
