import { describe, it, expect } from 'vitest';
import { stateColor, rangeBindingColor, findRange } from './rangeHelpers';
import type { RangeDto, ThingRangesResponse, PropertyBindingDto } from '../types/vos';

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function makeBinding(overrides: Partial<PropertyBindingDto> = {}): PropertyBindingDto {
  return {
    PropertyName: 'temperature',
    BoundsDescription: '20..30',
    BoundsType: 'numeric',
    IsActive: true,
    IsInBounds: true,
    ...overrides,
  };
}

function makeRange(overrides: Partial<RangeDto> = {}): RangeDto {
  return {
    Name: 'hot',
    Criteria: 'temperature > 30',
    IsInherited: false,
    ActiveBindings: 0,
    Bindings: [],
    ...overrides,
  };
}

function makeRangesResponse(overrides: Partial<ThingRangesResponse> = {}): ThingRangesResponse {
  return {
    ThingId: 't1',
    ThingName: 'Thing',
    OwnRanges: [],
    InheritedRanges: [],
    ...overrides,
  };
}

// ---------------------------------------------------------------------------
// rangeBindingColor
// ---------------------------------------------------------------------------

describe('rangeBindingColor', () => {
  it('returns green for undefined range', () => {
    expect(rangeBindingColor(undefined)).toBe('green');
  });

  it('returns green for range with no bindings', () => {
    expect(rangeBindingColor(makeRange({ Bindings: [] }))).toBe('green');
  });

  it('returns green when all bindings are in bounds', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ IsActive: true, IsInBounds: true }),
        makeBinding({ PropertyName: 'humidity', IsActive: true, IsInBounds: true }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('green');
  });

  it('returns green when out-of-bounds bindings are inactive', () => {
    const range = makeRange({
      Bindings: [makeBinding({ IsActive: false, IsInBounds: false })],
    });
    expect(rangeBindingColor(range)).toBe('green');
  });

  it('returns red for high severity (>= 0.5) deviation', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: 0.8 }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('red');
  });

  it('returns red for exactly 0.5 severity', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: 0.5 }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('red');
  });

  it('returns yellow for low severity (< 0.5) deviation', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: 0.3 }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('yellow');
  });

  it('returns red for null severity (categorical — treated as 1.0)', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: undefined }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('red');
  });

  it('uses max severity across multiple bindings', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ PropertyName: 'a', IsActive: true, IsInBounds: false, DeviationSeverity: 0.1 }),
        makeBinding({ PropertyName: 'b', IsActive: true, IsInBounds: false, DeviationSeverity: 0.9 }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('red');
  });

  it('ignores in-bounds bindings when computing max severity', () => {
    const range = makeRange({
      Bindings: [
        makeBinding({ PropertyName: 'a', IsActive: true, IsInBounds: true, DeviationSeverity: 1.0 }),
        makeBinding({ PropertyName: 'b', IsActive: true, IsInBounds: false, DeviationSeverity: 0.2 }),
      ],
    });
    expect(rangeBindingColor(range)).toBe('yellow');
  });
});

// ---------------------------------------------------------------------------
// findRange
// ---------------------------------------------------------------------------

describe('findRange', () => {
  it('finds a range in own ranges', () => {
    const r = makeRange({ Name: 'cold' });
    const data = makeRangesResponse({ OwnRanges: [r] });
    expect(findRange('cold', data)).toBe(r);
  });

  it('returns undefined when not found', () => {
    const data = makeRangesResponse({ OwnRanges: [makeRange({ Name: 'hot' })] });
    expect(findRange('cold', data)).toBeUndefined();
  });

  it('finds a range in top-level inherited ranges', () => {
    const r = makeRange({ Name: 'warm' });
    const data = makeRangesResponse({
      InheritedRanges: [{
        SourceId: 's1',
        SourceName: 'Source',
        InheritedAt: '2024-01-01',
        Ranges: [r],
        Inherited: [],
      }],
    });
    expect(findRange('warm', data)).toBe(r);
  });

  it('finds a range in nested inherited ranges', () => {
    const r = makeRange({ Name: 'deep' });
    const data = makeRangesResponse({
      InheritedRanges: [{
        SourceId: 's1',
        SourceName: 'L1',
        InheritedAt: '2024-01-01',
        Ranges: [],
        Inherited: [{
          SourceId: 's2',
          SourceName: 'L2',
          InheritedAt: '2024-01-01',
          Ranges: [r],
          Inherited: [],
        }],
      }],
    });
    expect(findRange('deep', data)).toBe(r);
  });

  it('prefers own range over inherited', () => {
    const own = makeRange({ Name: 'dup', Criteria: 'own' });
    const inherited = makeRange({ Name: 'dup', Criteria: 'inherited' });
    const data = makeRangesResponse({
      OwnRanges: [own],
      InheritedRanges: [{
        SourceId: 's1',
        SourceName: 'Source',
        InheritedAt: '2024-01-01',
        Ranges: [inherited],
        Inherited: [],
      }],
    });
    expect(findRange('dup', data)).toBe(own);
  });
});

// ---------------------------------------------------------------------------
// stateColor
// ---------------------------------------------------------------------------

describe('stateColor', () => {
  it('returns green when rangesData is null', () => {
    expect(stateColor('anything', null)).toBe('green');
  });

  it('returns green when state name has no matching range', () => {
    const data = makeRangesResponse({ OwnRanges: [makeRange({ Name: 'other' })] });
    expect(stateColor('missing', data)).toBe('green');
  });

  it('returns green when matching range has no deviations', () => {
    const data = makeRangesResponse({
      OwnRanges: [makeRange({
        Name: 'safe',
        Bindings: [makeBinding({ IsActive: true, IsInBounds: true })],
      })],
    });
    expect(stateColor('safe', data)).toBe('green');
  });

  it('returns red when matching range has high-severity deviation', () => {
    const data = makeRangesResponse({
      OwnRanges: [makeRange({
        Name: 'critical',
        Bindings: [makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: 0.7 })],
      })],
    });
    expect(stateColor('critical', data)).toBe('red');
  });

  it('returns yellow when matching range has low-severity deviation', () => {
    const data = makeRangesResponse({
      OwnRanges: [makeRange({
        Name: 'warning',
        Bindings: [makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: 0.2 })],
      })],
    });
    expect(stateColor('warning', data)).toBe('yellow');
  });

  it('resolves state color from inherited range', () => {
    const data = makeRangesResponse({
      InheritedRanges: [{
        SourceId: 's1',
        SourceName: 'Parent',
        InheritedAt: '2024-01-01',
        Ranges: [makeRange({
          Name: 'inherited-critical',
          Bindings: [makeBinding({ IsActive: true, IsInBounds: false, DeviationSeverity: 1.0 })],
        })],
        Inherited: [],
      }],
    });
    expect(stateColor('inherited-critical', data)).toBe('red');
  });
});
