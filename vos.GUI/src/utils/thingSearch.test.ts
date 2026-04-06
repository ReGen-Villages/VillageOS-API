import { describe, it, expect } from 'vitest';
import type { VosThing, VosRelationship } from '../types/vos';
import {
  buildThingSearchIndex,
  scoreThingMatch,
  searchThings,
  buildThingSearchMarkdown,
  THING_SEARCH_SKIP_KEYS,
  PREVIEW_PROPS,
} from './thingSearch';

// ── Helpers ────────────────────────────────────────────────────────────

function makeThing(id: string, name: string, props: Record<string, unknown> = {}): VosThing {
  return { Id: id, Name: name, Properties: props };
}

function makeRel(
  id: string,
  subjectId: string,
  predicateId: string,
  targetId: string,
): VosRelationship {
  return {
    Id: id,
    Name: `${subjectId}-${predicateId}-${targetId}`,
    SubjectId: subjectId,
    PredicateId: predicateId,
    TargetId: targetId,
    Properties: {},
  };
}

// ── Fixtures ────────────────────────────────────────────────────────────

const isPred = makeThing('p-is', 'is');
const hasPred = makeThing('p-has', 'has');

const building = makeThing('b1', 'Building-A', { height: 20, floors: 5 });
const sensor = makeThing('s1', 'Sensor-01', { temperature: 22.5, unit: 'C' });
const buildingType = makeThing('bt1', 'BuildingType');
const zone = makeThing('z1', 'Zone-North');

// building --[is]--> buildingType
const relIsBuilding = makeRel('r1', 'b1', 'p-is', 'bt1');
// sensor --[has]--> zone
const relHasSensor = makeRel('r2', 's1', 'p-has', 'z1');

const allThings = [building, sensor, buildingType, zone, isPred, hasPred];
const allRels = [relIsBuilding, relHasSensor];

// ── scoreThingMatch ────────────────────────────────────────────────────

describe('scoreThingMatch', () => {
  describe('exact match (score 0)', () => {
    it('scores exact name match as 0', () => {
      expect(scoreThingMatch(building, 'Building-A')).toBe(0);
    });

    it('is case-insensitive for exact match', () => {
      expect(scoreThingMatch(building, 'building-a')).toBe(0);
      expect(scoreThingMatch(building, 'BUILDING-A')).toBe(0);
    });
  });

  describe('prefix match (score 1)', () => {
    it('scores prefix match as 1', () => {
      expect(scoreThingMatch(building, 'Building')).toBe(1);
      expect(scoreThingMatch(building, 'Build')).toBe(1);
    });

    it('is case-insensitive for prefix match', () => {
      expect(scoreThingMatch(building, 'build')).toBe(1);
      expect(scoreThingMatch(building, 'BUILD')).toBe(1);
    });
  });

  describe('substring match (score 2)', () => {
    it('scores substring match as 2', () => {
      expect(scoreThingMatch(building, 'ing-A')).toBe(2);
      expect(scoreThingMatch(building, '-A')).toBe(2);
    });

    it('is case-insensitive for substring match', () => {
      expect(scoreThingMatch(building, 'ING-A')).toBe(2);
    });

    it('does not score a prefix as substring when prefix matches first', () => {
      // "Build" is a prefix → score 1, not 2
      expect(scoreThingMatch(building, 'Build')).toBe(1);
    });
  });

  describe('ID match (score 3)', () => {
    it('scores ID substring match as 3 when name does not match', () => {
      const thing = makeThing('abc-123-def', 'Unrelated');
      expect(scoreThingMatch(thing, 'abc-123')).toBe(3);
    });

    it('is case-insensitive for ID match', () => {
      const thing = makeThing('ABC-123', 'Unrelated');
      expect(scoreThingMatch(thing, 'abc-123')).toBe(3);
    });

    it('prefers name match over ID match', () => {
      // thing whose name contains "abc" and ID also contains "abc" → score 2 (name substring)
      const thing = makeThing('abc-id', 'contains-abc-name');
      expect(scoreThingMatch(thing, 'abc')).toBe(2);
    });
  });

  describe('no match', () => {
    it('returns null when neither name nor ID matches', () => {
      expect(scoreThingMatch(building, 'Sensor')).toBeNull();
      expect(scoreThingMatch(building, 'zzz')).toBeNull();
    });

    it('matches everything with empty query via prefix (caller must guard)', () => {
      // '' startsWith '' → true → score 1; searchThings guards against empty query
      expect(scoreThingMatch(building, '')).toBe(1);
    });
  });
});

// ── buildThingSearchIndex ──────────────────────────────────────────────

describe('buildThingSearchIndex', () => {
  describe('isSubjectToTypeName', () => {
    it('maps subject ID to target name for "is" relationships', () => {
      const index = buildThingSearchIndex(allThings, allRels);
      expect(index.isSubjectToTypeName.get('b1')).toBe('BuildingType');
    });

    it('returns undefined for things with no "is" relationship', () => {
      const index = buildThingSearchIndex(allThings, allRels);
      expect(index.isSubjectToTypeName.get('s1')).toBeUndefined();
      expect(index.isSubjectToTypeName.get('z1')).toBeUndefined();
    });

    it('ignores non-"is" predicates', () => {
      const index = buildThingSearchIndex(allThings, allRels);
      // sensor --[has]--> zone should NOT appear in isSubjectToTypeName
      expect(index.isSubjectToTypeName.get('z1')).toBeUndefined();
    });

    it('uses first "is" relationship when multiple exist', () => {
      const typeA = makeThing('ta', 'TypeA');
      const typeB = makeThing('tb', 'TypeB');
      const instance = makeThing('inst', 'Instance');
      const relA = makeRel('rA', 'inst', 'p-is', 'ta');
      const relB = makeRel('rB', 'inst', 'p-is', 'tb');
      const index = buildThingSearchIndex(
        [isPred, instance, typeA, typeB],
        [relA, relB],
      );
      // First encountered wins
      expect(index.isSubjectToTypeName.get('inst')).toBe('TypeA');
    });

    it('is case-insensitive when looking up "is" predicate', () => {
      const IS = makeThing('p-IS', 'IS');
      const inst = makeThing('i1', 'Instance');
      const type = makeThing('t1', 'TheType');
      const rel = makeRel('rx', 'i1', 'p-IS', 't1');
      const index = buildThingSearchIndex([IS, inst, type], [rel]);
      expect(index.isSubjectToTypeName.get('i1')).toBe('TheType');
    });
  });

  describe('relCountByThing', () => {
    it('counts outgoing relationships for subject', () => {
      const index = buildThingSearchIndex(allThings, allRels);
      // building is subject of relIsBuilding → count 1
      expect(index.relCountByThing.get('b1')).toBe(1);
    });

    it('counts incoming relationships for target', () => {
      const index = buildThingSearchIndex(allThings, allRels);
      // buildingType is target of relIsBuilding → count 1
      expect(index.relCountByThing.get('bt1')).toBe(1);
    });

    it('counts both outgoing and incoming for a thing with multiple relationships', () => {
      // sensor is subject of relHasSensor (outgoing to zone)
      // Add another rel where sensor is target
      const other = makeThing('o1', 'Other');
      const relToSensor = makeRel('r3', 'o1', 'p-has', 's1');
      const index = buildThingSearchIndex(
        [...allThings, other],
        [...allRels, relToSensor],
      );
      // sensor: 1 outgoing (relHasSensor) + 1 incoming (relToSensor) = 2
      expect(index.relCountByThing.get('s1')).toBe(2);
    });

    it('returns 0 for things with no relationships', () => {
      const isolated = makeThing('iso', 'Isolated');
      const index = buildThingSearchIndex([isolated], []);
      expect(index.relCountByThing.get('iso') ?? 0).toBe(0);
    });
  });
});

// ── searchThings ───────────────────────────────────────────────────────

describe('searchThings', () => {
  const index = buildThingSearchIndex(allThings, allRels);

  describe('empty query', () => {
    it('returns empty array for empty query', () => {
      expect(searchThings('', allThings, index)).toHaveLength(0);
    });

    it('returns empty array for whitespace-only query', () => {
      expect(searchThings('   ', allThings, index)).toHaveLength(0);
    });
  });

  describe('matching', () => {
    it('finds things by exact name', () => {
      const results = searchThings('Building-A', allThings, index);
      expect(results.map((r) => r.id)).toContain('b1');
    });

    it('finds things by name prefix', () => {
      const results = searchThings('Sensor', allThings, index);
      expect(results.map((r) => r.id)).toContain('s1');
    });

    it('finds things by name substring', () => {
      const results = searchThings('Zone', allThings, index);
      expect(results.map((r) => r.id)).toContain('z1');
    });

    it('is case-insensitive', () => {
      const results = searchThings('building-a', allThings, index);
      expect(results.map((r) => r.id)).toContain('b1');
    });

    it('returns no results when nothing matches', () => {
      const results = searchThings('zzz-no-match', allThings, index);
      expect(results).toHaveLength(0);
    });

    it('can match multiple things', () => {
      // "Zone" matches Zone-North; "Building" matches Building-A and BuildingType
      const results = searchThings('Building', allThings, index);
      const ids = results.map((r) => r.id);
      expect(ids).toContain('b1');
      expect(ids).toContain('bt1');
    });
  });

  describe('sorting', () => {
    it('sorts exact match before prefix before substring', () => {
      // "Building-A" exact → score 0
      // "BuildingType" prefix → score 1 (starts with "Building")
      const results = searchThings('Building-A', allThings, index);
      expect(results[0].id).toBe('b1'); // exact match first
    });

    it('sorts same-score results alphabetically', () => {
      // "B" matches Building-A (prefix, score 1) and BuildingType (prefix, score 1)
      const results = searchThings('B', allThings, index);
      const names = results.map((r) => r.name);
      const buildingAIdx = names.indexOf('Building-A');
      const buildingTypeIdx = names.indexOf('BuildingType');
      // "Building-A" < "BuildingType" alphabetically
      expect(buildingAIdx).toBeLessThan(buildingTypeIdx);
    });
  });

  describe('ThingMatch fields', () => {
    it('attaches typeName from "is" relationship', () => {
      const results = searchThings('Building-A', allThings, index);
      const match = results.find((r) => r.id === 'b1')!;
      expect(match.typeName).toBe('BuildingType');
    });

    it('leaves typeName undefined when no "is" relationship', () => {
      const results = searchThings('Zone', allThings, index);
      const match = results.find((r) => r.id === 'z1')!;
      expect(match.typeName).toBeUndefined();
    });

    it('sets relationshipCount for subjects and targets', () => {
      const results = searchThings('Building-A', allThings, index);
      const match = results.find((r) => r.id === 'b1')!;
      expect(match.relationshipCount).toBe(1);
    });

    it('sets relationshipCount to 0 for unconnected things', () => {
      const alone = makeThing('lone', 'Loner');
      const localIndex = buildThingSearchIndex([alone], []);
      const results = searchThings('Loner', [alone], localIndex);
      expect(results[0].relationshipCount).toBe(0);
    });

    it('counts ownPropertyCount excluding skip keys', () => {
      const thing = makeThing('t1', 'TestThing', {
        name: 'foo',
        geometry: 'big-blob',
        footprint: 'big-blob',
        __geometry_envelope: 'big-blob',
        __IsSurface: true,
        value: 42,
      });
      const localIndex = buildThingSearchIndex([thing], []);
      const results = searchThings('TestThing', [thing], localIndex);
      // Only 'name' and 'value' survive the filter
      expect(results[0].ownPropertyCount).toBe(2);
    });

    it('includes up to PREVIEW_PROPS properties in previewProps', () => {
      const props: Record<string, unknown> = {};
      for (let i = 0; i < PREVIEW_PROPS + 3; i++) props[`prop${i}`] = i;
      const thing = makeThing('t1', 'PropRich', props);
      const localIndex = buildThingSearchIndex([thing], []);
      const results = searchThings('PropRich', [thing], localIndex);
      expect(results[0].previewProps).toHaveLength(PREVIEW_PROPS);
    });

    it('does not include skip keys in previewProps', () => {
      const thing = makeThing('t1', 'GeoThing', {
        geometry: '{}',
        footprint: '{}',
        __geometry_envelope: '{}',
        __IsSurface: true,
        realProp: 'visible',
      });
      const localIndex = buildThingSearchIndex([thing], []);
      const results = searchThings('GeoThing', [thing], localIndex);
      const keys = results[0].previewProps.map((p) => p.key);
      expect(keys).not.toContain('geometry');
      expect(keys).not.toContain('footprint');
      expect(keys).not.toContain('__geometry_envelope');
      expect(keys).not.toContain('__IsSurface');
      expect(keys).toContain('realProp');
    });

    it('includes formatted string for each previewProp', () => {
      const thing = makeThing('t1', 'Thing', { count: 42 });
      const localIndex = buildThingSearchIndex([thing], []);
      const results = searchThings('Thing', [thing], localIndex);
      expect(typeof results[0].previewProps[0].formatted).toBe('string');
    });
  });
});

// ── THING_SEARCH_SKIP_KEYS ─────────────────────────────────────────────

describe('THING_SEARCH_SKIP_KEYS', () => {
  it('contains expected skip keys', () => {
    expect(THING_SEARCH_SKIP_KEYS.has('geometry')).toBe(true);
    expect(THING_SEARCH_SKIP_KEYS.has('footprint')).toBe(true);
    expect(THING_SEARCH_SKIP_KEYS.has('__geometry_envelope')).toBe(true);
    expect(THING_SEARCH_SKIP_KEYS.has('__IsSurface')).toBe(true);
  });
});

// ── buildThingSearchMarkdown ───────────────────────────────────────────

describe('buildThingSearchMarkdown', () => {
  const sampleResults = [
    {
      id: 'b1',
      name: 'Building-A',
      score: 0,
      typeName: 'BuildingType',
      ownPropertyCount: 2,
      relationshipCount: 1,
      previewProps: [],
    },
    {
      id: 's1',
      name: 'Sensor-01',
      score: 2,
      typeName: undefined,
      ownPropertyCount: 2,
      relationshipCount: 0,
      previewProps: [],
    },
  ];

  it('starts with a heading containing the query', () => {
    const md = buildThingSearchMarkdown('Building', sampleResults);
    expect(md).toContain('# Thing Search: "Building"');
  });

  it('includes a markdown table header', () => {
    const md = buildThingSearchMarkdown('B', sampleResults);
    expect(md).toContain('| Name | Type | Properties | Relationships |');
    expect(md).toContain('|------|------|------------|---------------|');
  });

  it('includes a row for each result', () => {
    const md = buildThingSearchMarkdown('B', sampleResults);
    expect(md).toContain('| Building-A |');
    expect(md).toContain('| Sensor-01 |');
  });

  it('uses typeName when present', () => {
    const md = buildThingSearchMarkdown('B', sampleResults);
    expect(md).toContain('| BuildingType |');
  });

  it('uses em-dash when typeName is absent', () => {
    const md = buildThingSearchMarkdown('B', sampleResults);
    expect(md).toContain('| — |');
  });

  it('includes property and relationship counts', () => {
    const md = buildThingSearchMarkdown('B', sampleResults);
    expect(md).toContain('| 2 | 1 |');
  });

  it('returns empty table for empty results', () => {
    const md = buildThingSearchMarkdown('xyz', []);
    expect(md).toContain('# Thing Search: "xyz"');
    expect(md).toContain('| Name |');
    // No data rows
    const lines = md.split('\n').filter((l) => l.startsWith('|') && !l.startsWith('|---') && !l.includes('Name'));
    expect(lines).toHaveLength(0);
  });
});
