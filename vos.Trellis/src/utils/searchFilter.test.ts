import { describe, it, expect } from 'vitest';
import { filterGraph } from './searchFilter';
import type { VosThing, VosRelationship } from '../types/vos';
import type { SearchOptions } from './searchFilter';

// ── Helpers ────────────────────────────────────────────────────────────

function makeThing(id: string, name: string): VosThing {
  return { Id: id, Name: name, Properties: {} };
}

function makeRel(id: string, subjectId: string, predicateId: string, targetId: string): VosRelationship {
  return { Id: id, Name: `${subjectId}-${predicateId}-${targetId}`, SubjectId: subjectId, PredicateId: predicateId, TargetId: targetId, Properties: {} };
}

const defaults: SearchOptions = { caseSensitive: false, exactMatch: false, useRegex: false };

// ── Test data ──────────────────────────────────────────────────────────

const alice = makeThing('1', 'Alice');
const bob = makeThing('2', 'Bob');
const charlie = makeThing('3', 'Charlie');
const likes = makeThing('4', 'likes');
const things = [alice, bob, charlie, likes];

// Alice -likes-> Bob,  Bob -likes-> Charlie
const rel1 = makeRel('r1', '1', '4', '2'); // Alice likes Bob
const rel2 = makeRel('r2', '2', '4', '3'); // Bob likes Charlie
const relationships = [rel1, rel2];

// ── Tests ──────────────────────────────────────────────────────────────

describe('filterGraph', () => {
  describe('empty query', () => {
    it('returns all things and relationships with matchCount 0', () => {
      const result = filterGraph('', things, relationships, defaults);
      expect(result.filteredThings).toBe(things);
      expect(result.filteredRelationships).toBe(relationships);
      expect(result.matchCount).toBe(0);
    });
  });

  describe('case-insensitive substring (default)', () => {
    it('matches a name by substring', () => {
      const result = filterGraph('ali', things, relationships, defaults);
      expect(result.matchCount).toBe(1);
      expect(result.filteredThings.map((t) => t.Name)).toContain('Alice');
    });

    it('is case-insensitive by default', () => {
      const result = filterGraph('ALICE', things, relationships, defaults);
      expect(result.matchCount).toBe(1);
      expect(result.filteredThings.map((t) => t.Name)).toContain('Alice');
    });

    it('returns no matches for non-matching query', () => {
      const result = filterGraph('zzz', things, relationships, defaults);
      expect(result.matchCount).toBe(0);
      expect(result.filteredThings).toHaveLength(0);
      expect(result.filteredRelationships).toHaveLength(0);
    });
  });

  describe('case-sensitive', () => {
    it('matches when case matches', () => {
      const result = filterGraph('Alice', things, relationships, { caseSensitive: true, exactMatch: false, useRegex: false });
      expect(result.matchCount).toBe(1);
    });

    it('does not match when case differs', () => {
      const result = filterGraph('alice', things, relationships, { caseSensitive: true, exactMatch: false, useRegex: false });
      expect(result.matchCount).toBe(0);
    });
  });

  describe('exact match', () => {
    it('matches only exact name (case-insensitive)', () => {
      const result = filterGraph('alice', things, relationships, { caseSensitive: false, exactMatch: true, useRegex: false });
      expect(result.matchCount).toBe(1);
      expect(result.filteredThings.map((t) => t.Name)).toContain('Alice');
    });

    it('does not match substrings', () => {
      const result = filterGraph('Ali', things, relationships, { caseSensitive: false, exactMatch: true, useRegex: false });
      expect(result.matchCount).toBe(0);
    });
  });

  describe('case-sensitive + exact match', () => {
    it('matches only exact case-sensitive name', () => {
      const result = filterGraph('Alice', things, relationships, { caseSensitive: true, exactMatch: true, useRegex: false });
      expect(result.matchCount).toBe(1);
    });

    it('rejects case mismatch', () => {
      const result = filterGraph('alice', things, relationships, { caseSensitive: true, exactMatch: true, useRegex: false });
      expect(result.matchCount).toBe(0);
    });
  });

  describe('neighbor expansion', () => {
    it('includes direct neighbors of matched nodes', () => {
      // Alice matches, and Alice-likes->Bob, so Bob and likes should be included
      const result = filterGraph('Alice', things, relationships, defaults);
      const names = result.filteredThings.map((t) => t.Name);
      expect(names).toContain('Alice');
      expect(names).toContain('Bob');
      expect(names).toContain('likes');
    });

    it('includes relationships that touch matched nodes', () => {
      const result = filterGraph('Alice', things, relationships, defaults);
      expect(result.filteredRelationships).toHaveLength(1);
      expect(result.filteredRelationships[0].Id).toBe('r1');
    });

    it('does not include unrelated nodes', () => {
      // Searching for Charlie — only Bob-likes->Charlie touches Charlie
      // So result should include Charlie, Bob, likes, and rel2
      // Alice should NOT be included (no direct edge to Charlie)
      const result = filterGraph('Charlie', things, relationships, defaults);
      const names = result.filteredThings.map((t) => t.Name);
      expect(names).toContain('Charlie');
      expect(names).toContain('Bob');
      expect(names).toContain('likes');
      expect(names).not.toContain('Alice');
    });
  });

  describe('edge filtering', () => {
    it('only includes edges where both endpoints are present', () => {
      // Search for "Bob" — Bob is subject of r2 and target of r1
      // Both r1 and r2 touch Bob, so neighbors expand to Alice, Charlie, likes
      // All endpoints present → both edges included
      const result = filterGraph('Bob', things, relationships, defaults);
      expect(result.filteredRelationships).toHaveLength(2);
    });
  });

  describe('predicate things included for edge labels', () => {
    it('includes predicate things even if they are not direct neighbors', () => {
      // Create a scenario where the predicate thing isn't a neighbor of the match
      const sensor = makeThing('s1', 'Sensor');
      const zone = makeThing('z1', 'Zone');
      const monitors = makeThing('p1', 'monitors');
      const isolated = makeThing('x1', 'Isolated');
      const rel = makeRel('rx', 's1', 'p1', 'z1');
      const allThings = [sensor, zone, monitors, isolated];

      const result = filterGraph('Sensor', allThings, [rel], defaults);
      const ids = result.filteredThings.map((t) => t.Id);
      expect(ids).toContain('p1'); // predicate included
      expect(ids).not.toContain('x1'); // isolated node excluded
    });
  });

  describe('multiple matches', () => {
    it('returns correct matchCount for multiple hits', () => {
      // "li" matches Alice, Charlie, and likes (all contain "li")
      const result = filterGraph('li', things, relationships, defaults);
      expect(result.matchCount).toBe(3);
    });
  });

  // ── Comma-separated list ────────────────────────────────────────────

  describe('comma-separated list', () => {
    it('matches multiple names separated by commas', () => {
      const result = filterGraph('Alice, Charlie', things, relationships, defaults);
      expect(result.matchCount).toBe(2);
      const names = result.filteredThings.map((t) => t.Name);
      expect(names).toContain('Alice');
      expect(names).toContain('Charlie');
    });

    it('matches by thing ID in comma list', () => {
      // ID "2" is Bob
      const result = filterGraph('Alice, 2', things, relationships, defaults);
      expect(result.matchCount).toBe(2);
      const names = result.filteredThings.map((t) => t.Name);
      expect(names).toContain('Alice');
      expect(names).toContain('Bob');
    });

    it('handles trailing commas gracefully', () => {
      const result = filterGraph('Alice,', things, relationships, defaults);
      expect(result.matchCount).toBe(1);
      expect(result.filteredThings.map((t) => t.Name)).toContain('Alice');
    });

    it('handles whitespace around terms', () => {
      const result = filterGraph('  Alice , Bob  ', things, relationships, defaults);
      expect(result.matchCount).toBe(2);
    });

    it('supports substring matching per term', () => {
      // "li" matches Alice, Charlie, likes; "ob" matches Bob
      const result = filterGraph('li, ob', things, relationships, defaults);
      expect(result.matchCount).toBe(4); // Alice, Charlie, likes, Bob
    });

    it('respects exact match with comma list', () => {
      const result = filterGraph('Alice, Bob', things, relationships, { caseSensitive: false, exactMatch: true, useRegex: false });
      expect(result.matchCount).toBe(2);

      // Substring should NOT match in exact mode
      const result2 = filterGraph('Ali, Bo', things, relationships, { caseSensitive: false, exactMatch: true, useRegex: false });
      expect(result2.matchCount).toBe(0);
    });

    it('respects case sensitivity with comma list', () => {
      const result = filterGraph('alice, bob', things, relationships, { caseSensitive: true, exactMatch: false, useRegex: false });
      expect(result.matchCount).toBe(0);

      const result2 = filterGraph('Alice, Bob', things, relationships, { caseSensitive: true, exactMatch: false, useRegex: false });
      expect(result2.matchCount).toBe(2);
    });

    it('returns empty when all terms are empty', () => {
      const result = filterGraph(', , ', things, relationships, defaults);
      expect(result.matchCount).toBe(0);
    });
  });

  // ── Regex search ────────────────────────────────────────────────────

  describe('regex search', () => {
    it('matches names by regex pattern', () => {
      const result = filterGraph('^A', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(1);
      expect(result.filteredThings.map((t) => t.Name)).toContain('Alice');
    });

    it('supports regex alternation', () => {
      const result = filterGraph('Alice|Charlie', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(2);
      const names = result.filteredThings.map((t) => t.Name);
      expect(names).toContain('Alice');
      expect(names).toContain('Charlie');
    });

    it('matches by thing ID with regex', () => {
      // ID "2" is Bob
      const result = filterGraph('^2$', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(1);
      const names = result.filteredThings.map((t) => t.Name);
      expect(names).toContain('Bob');
    });

    it('is case-insensitive by default', () => {
      const result = filterGraph('^alice$', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(1);
    });

    it('respects case-sensitive flag', () => {
      const result = filterGraph('^alice$', things, relationships, { caseSensitive: true, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(0);

      const result2 = filterGraph('^Alice$', things, relationships, { caseSensitive: true, exactMatch: false, useRegex: true });
      expect(result2.matchCount).toBe(1);
    });

    it('supports character classes', () => {
      // Match names ending in "e"
      const result = filterGraph('e$', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(2); // Alice, Charlie
    });

    it('supports quantifiers', () => {
      // Match names with one or more "l" characters
      const result = filterGraph('l+', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      // Alice, Charlie, and likes all contain "l"
      expect(result.matchCount).toBe(3);
    });

    it('falls back to plain text on invalid regex', () => {
      // "[" is invalid regex — should fall through to substring match
      const result = filterGraph('[', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      // No names contain "[", so 0 matches
      expect(result.matchCount).toBe(0);
    });

    it('handles special regex chars when regex is off', () => {
      // "." would match everything in regex mode, but in plain text mode it matches nothing
      const result = filterGraph('.', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: false });
      expect(result.matchCount).toBe(0); // No names contain literal "."
    });

    it('matches everything with .* in regex mode', () => {
      const result = filterGraph('.*', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(4); // All things match
    });

    it('supports word boundary matching', () => {
      const result = filterGraph('\\bBob\\b', things, relationships, { caseSensitive: false, exactMatch: false, useRegex: true });
      expect(result.matchCount).toBe(1);
      expect(result.filteredThings.map((t) => t.Name)).toContain('Bob');
    });
  });
});
