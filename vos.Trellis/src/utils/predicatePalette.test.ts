import { describe, it, expect } from 'vitest';
import { CURATED_PREDICATE_COLORS } from './predicatePalette';
import { resolvePredicateColor, PREDICATE_PALETTE, hashStringToIndex } from './colors';

describe('CURATED_PREDICATE_COLORS (Feature #5340)', () => {
  it('every entry is a valid 7-char hex color', () => {
    for (const [name, hex] of Object.entries(CURATED_PREDICATE_COLORS)) {
      expect(hex, `${name} -> ${hex}`).toMatch(/^#[0-9a-f]{6}$/i);
    }
  });

  it('covers every predicate the IFC pipeline emits', () => {
    // Source of truth: vos.Tools.ModelIngest/Pipeline/*RelationshipExtractor.cs
    // grep result (Bug #5340 audit). If the pipeline grows, add the new
    // predicate to predicatePalette.ts AND extend this list — the resolver
    // hash-fallback would otherwise silently pick a random color.
    const pipelinePredicates = [
      'is', 'has', 'contains', 'isContainedIn', 'aggregates', 'isAggregatedBy',
      'nests', 'hasMember', 'hasMaterial', 'hasLayer', 'hasLayerSet',
      'hasConstituent', 'hasProfile', 'hasProfileSet', 'hasPort', 'connectsTo',
      'connectsElement', 'connectsPath', 'connectsThrough', 'services',
      'voids', 'fills', 'hasBoundary', 'hasClassification', 'typeInfo',
      'hasDocument', 'hasConstraint', 'hasActor', 'hasProcess', 'referencedIn',
      'covers', 'interferesWith', 'node', 'value',
    ];
    for (const p of pipelinePredicates) {
      expect(CURATED_PREDICATE_COLORS[p], `missing curated color for ${p}`).toBeDefined();
    }
  });

  it('groups material-chain predicates into one color family', () => {
    // hasLayer, hasLayerSet, hasConstituent, hasProfile, hasProfileSet should
    // all be the same tan so the material composite chain reads as a group.
    const tan = CURATED_PREDICATE_COLORS.hasLayer;
    expect(CURATED_PREDICATE_COLORS.hasLayerSet).toBe(tan);
    expect(CURATED_PREDICATE_COLORS.hasConstituent).toBe(tan);
    expect(CURATED_PREDICATE_COLORS.hasProfile).toBe(tan);
    expect(CURATED_PREDICATE_COLORS.hasProfileSet).toBe(tan);
  });

  it('groups MEP connectivity predicates into one color family', () => {
    const cyan = CURATED_PREDICATE_COLORS.connectsTo;
    expect(CURATED_PREDICATE_COLORS.connectsElement).toBe(cyan);
    expect(CURATED_PREDICATE_COLORS.connectsPath).toBe(cyan);
    expect(CURATED_PREDICATE_COLORS.connectsThrough).toBe(cyan);
  });
});

describe('resolvePredicateColor priority chain (Feature #5340)', () => {
  it('1. user override beats curated default and hash', () => {
    // is is curated; user override should still win.
    expect(resolvePredicateColor('is', { is: '#ff00ff' })).toBe('#ff00ff');
    // hasMaterial is curated; override wins.
    expect(resolvePredicateColor('hasMaterial', { hasMaterial: '#000000' })).toBe('#000000');
  });

  it('2. curated default beats hash fallback', () => {
    // No user override — should land on curated, NOT on the hash palette.
    expect(resolvePredicateColor('is', {})).toBe(CURATED_PREDICATE_COLORS.is);
    expect(resolvePredicateColor('hasPort', {})).toBe(CURATED_PREDICATE_COLORS.hasPort);
    expect(resolvePredicateColor('interferesWith', {})).toBe(CURATED_PREDICATE_COLORS.interferesWith);
  });

  it('3. hash fallback for predicates not in the curated map', () => {
    // Long-tail predicates from non-IFC sources. Must still get a stable color.
    const hashed = resolvePredicateColor('someCustomDomainPredicate', {});
    expect(hashed).toBe(PREDICATE_PALETTE[
      hashStringToIndex('someCustomDomainPredicate', PREDICATE_PALETTE.length)
    ]);
  });

  it('hash fallback is deterministic (same name -> same color)', () => {
    expect(resolvePredicateColor('xyz', {})).toBe(resolvePredicateColor('xyz', {}));
  });

  it('override only applies to the matching predicate', () => {
    // Overriding "is" must not touch "has".
    const overrides = { is: '#ff00ff' };
    expect(resolvePredicateColor('has', overrides)).toBe(CURATED_PREDICATE_COLORS.has);
  });
});
