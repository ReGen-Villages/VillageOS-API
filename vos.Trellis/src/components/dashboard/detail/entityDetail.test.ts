import { describe, it, expect } from 'vitest';
import { buildModelIndex } from '../../../api/dashboardApi';
import type { VosThing, VosRelationship } from '../../../types/vos';
import { collectInvolved, mergeTimeline, type MovementInput } from './entityDetail';

function thing(id: string, name: string): VosThing {
  return { Id: id, Name: name, Properties: {} };
}
function rel(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: id, SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

// root -has-> child -references-> grandchild, plus root -has-> peer -owns-> far.
// Two predicates reachable from the root, each leading one hop further on.
function fixture() {
  const things = [
    thing('root', 'ROOT-1'),
    thing('child', 'CHILD-1'),
    thing('grandchild', 'GRANDCHILD-1'),
    thing('peer', 'PEER-1'),
    thing('far', 'FAR-1'),
    thing('has', 'has'),
    thing('references', 'references'),
    thing('owns', 'owns'),
  ];
  const relationships = [
    rel('r1', 'root', 'has', 'child'),
    rel('r2', 'child', 'references', 'grandchild'),
    rel('r3', 'root', 'has', 'peer'),
    rel('r4', 'peer', 'owns', 'far'),
  ];
  return buildModelIndex(things, relationships);
}

describe('collectInvolved', () => {
  it('follows all predicates outbound to the given depth', () => {
    const idx = fixture();
    const involved = collectInvolved('root', idx, { direction: 'out', depth: 2 });
    // Two hops via any predicate: root→child→grandchild and root→peer→far.
    expect(new Set(involved)).toEqual(new Set(['child', 'peer', 'grandchild', 'far']));
  });

  it('honours a depth limit', () => {
    const idx = fixture();
    const involved = collectInvolved('root', idx, { direction: 'out', depth: 1 });
    expect(new Set(involved)).toEqual(new Set(['child', 'peer']));
    expect(involved).not.toContain('grandchild'); // two hops away
  });

  it('restricts traversal to the configured predicates', () => {
    const idx = fixture();
    const involved = collectInvolved('root', idx, { predicates: ['has', 'references'], depth: 3 });
    // 'owns' is excluded, so 'far' is never reached even though peer owns it.
    expect(involved).not.toContain('far');
    expect(new Set(involved)).toEqual(new Set(['child', 'peer', 'grandchild']));
  });

  it('excludes the root and never revisits (cycle-guarded)', () => {
    const things = [thing('A', 'A'), thing('B', 'B'), thing('link', 'link')];
    const idx = buildModelIndex(things, [rel('r1', 'A', 'link', 'B'), rel('r2', 'B', 'link', 'A')]);
    const involved = collectInvolved('A', idx, { direction: 'both', depth: 5 });
    expect(involved).toEqual(['B']);
  });
});

describe('mergeTimeline', () => {
  const mutations = [
    {
      ObjectId: 'root',
      ObjectName: 'ROOT-1',
      StartTime: '',
      EndTime: '',
      Mutations: [
        { Timestamp: '2026-07-14T09:14:00Z', PropertyName: 'status', OldValue: 'first', NewValue: 'second' },
        { Timestamp: '2026-07-14T09:02:00Z', PropertyName: 'status', OldValue: '', NewValue: 'first' },
      ],
    },
  ];

  it('orders events by time ascending', () => {
    const timeline = mergeTimeline({ mutations, movements: [] });
    expect(timeline.map((e) => e.time)).toEqual(['2026-07-14T09:02:00Z', '2026-07-14T09:14:00Z']);
    expect(timeline[1].label).toBe('status: first → second');
  });

  it('attributes a change to the Fact author whose value matches', () => {
    const factsByKey = new Map([
      [
        'root::status',
        [
          { kind: 'asserted' as const, sequenceNumber: 5, committedAt: '2026-07-14T09:14:00Z', author: 'WriterService', value: 'second' },
        ],
      ],
    ]);
    const timeline = mergeTimeline({ mutations, movements: [], factsByKey });
    const changed = timeline.find((e) => e.label.includes('second'));
    expect(changed?.author).toBe('WriterService');
  });

  it('interleaves movements and sorts untimed events last', () => {
    const movements: MovementInput[] = [
      { predicate: 'moves', subjectId: 'child', subjectName: 'CHILD-1', targetId: 'root', targetName: 'ROOT-1', time: '2026-07-14T09:10:00Z' },
      { predicate: 'has', subjectId: 'root', subjectName: 'ROOT-1', targetId: 'child', targetName: 'CHILD-1', time: null },
    ];
    const timeline = mergeTimeline({ mutations, movements });
    expect(timeline[timeline.length - 1].time).toBeNull();
    const times = timeline.filter((e) => e.time).map((e) => e.time);
    expect(times).toEqual([...times].sort());
    expect(timeline.some((e) => e.kind === 'movement' && e.label === 'moves → ROOT-1')).toBe(true);
  });
});
