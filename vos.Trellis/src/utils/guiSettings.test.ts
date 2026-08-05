import { describe, it, expect, beforeEach } from 'vitest';
import {
  extractFlashSettings,
  extractLayoutSettings,
  extractPredicateColors,
  findGuiSettingsProperties,
  LAYOUT_DEFAULTS,
  FLASH_DEFAULTS,
  extractNumberDisplaySettings,
  NUMBER_DISPLAY_DEFAULTS,
} from './guiSettings';
import type { VosThing, VosRelationship } from '../types/vos';

// ── Test helpers ──────────────────────────────────────────────────────

let idCounter = 0;
function uid() {
  return `test-${++idCounter}`;
}

function makeThing(
  name: string,
  props: Record<string, unknown> = {},
): VosThing {
  return { Id: uid(), Name: name, Properties: props };
}

function makeRel(
  subjectId: string,
  predicateId: string,
  targetId: string,
): VosRelationship {
  return {
    Id: uid(),
    Name: '',
    SubjectId: subjectId,
    PredicateId: predicateId,
    TargetId: targetId,
    Properties: {},
  };
}

/** Build the standard GUI_Settings type + GUI instance + is relationship. */
function buildGuiFixture(
  typeProps: Record<string, unknown>,
  instanceProps: Record<string, unknown> = {},
) {
  const isPred = makeThing('is');
  const guiType = makeThing('GUI_Settings', typeProps);
  const gui = makeThing('GUI', instanceProps);
  const rel = makeRel(gui.Id, isPred.Id, guiType.Id);
  return {
    things: [isPred, guiType, gui],
    relationships: [rel],
  };
}

beforeEach(() => {
  idCounter = 0;
});

// ── findGuiSettingsProperties ─────────────────────────────────────────

describe('findGuiSettingsProperties', () => {
  it('returns null when no GUI_Settings type exists', () => {
    const things = [makeThing('Foo'), makeThing('Bar')];
    expect(findGuiSettingsProperties(things, [])).toBeNull();
  });

  it('returns type properties when no instance is linked', () => {
    const guiType = makeThing('GUI_Settings', { FlashEdgeSize: 2.0 });
    const result = findGuiSettingsProperties([guiType], []);
    expect(result).toEqual({ FlashEdgeSize: 2.0 });
  });

  it('merges instance overrides over type defaults', () => {
    const { things, relationships } = buildGuiFixture(
      { FlashEdgeSize: 1.5, FlashNodeBrighten: 0.5 },
      { FlashNodeBrighten: 0.8 },
    );
    const result = findGuiSettingsProperties(things, relationships);
    expect(result).toEqual({ FlashEdgeSize: 1.5, FlashNodeBrighten: 0.8 });
  });
});

// ── extractFlashSettings ──────────────────────────────────────────────

describe('extractFlashSettings', () => {
  it('returns defaults when no GUI_Settings type exists', () => {
    const things = [makeThing('SomeOtherThing')];
    expect(extractFlashSettings(things, [])).toEqual(FLASH_DEFAULTS);
  });

  it('returns defaults for empty arrays', () => {
    expect(extractFlashSettings([], [])).toEqual(FLASH_DEFAULTS);
  });

  it('extracts settings from GUI_Settings type', () => {
    const { things, relationships } = buildGuiFixture({
      FlashEdgeSize: 3.0,
      FlashNodeSizeFactor: 2.0,
      FlashNodeBrighten: 0.8,
    });
    expect(extractFlashSettings(things, relationships)).toEqual({
      flashEdgeSize: 3.0,
      flashNodeSizeFactor: 2.0,
      flashNodeBrighten: 0.8,
    });
  });

  it('falls back to defaults for missing properties', () => {
    const { things, relationships } = buildGuiFixture({ FlashEdgeSize: 2.0 });
    const result = extractFlashSettings(things, relationships);
    expect(result.flashEdgeSize).toBe(2.0);
    expect(result.flashNodeSizeFactor).toBe(FLASH_DEFAULTS.flashNodeSizeFactor);
    expect(result.flashNodeBrighten).toBe(FLASH_DEFAULTS.flashNodeBrighten);
  });

  it('instance overrides type defaults', () => {
    const { things, relationships } = buildGuiFixture(
      { FlashEdgeSize: 1.5 },
      { FlashEdgeSize: 5.0 },
    );
    const result = extractFlashSettings(things, relationships);
    expect(result.flashEdgeSize).toBe(5.0);
  });

  it('falls back to defaults for invalid types', () => {
    const { things, relationships } = buildGuiFixture({
      FlashEdgeSize: 'not-a-number',
      FlashNodeSizeFactor: null,
      FlashNodeBrighten: undefined,
    });
    expect(extractFlashSettings(things, relationships)).toEqual(FLASH_DEFAULTS);
  });

  it('ignores NaN values', () => {
    const { things, relationships } = buildGuiFixture({ FlashEdgeSize: NaN });
    expect(extractFlashSettings(things, relationships).flashEdgeSize).toBe(
      FLASH_DEFAULTS.flashEdgeSize,
    );
  });
});

// ── extractLayoutSettings ─────────────────────────────────────────────

describe('extractLayoutSettings', () => {
  it('returns defaults when no GUI_Settings type exists', () => {
    const things = [makeThing('SomeOtherThing')];
    expect(extractLayoutSettings(things, [])).toEqual(LAYOUT_DEFAULTS);
  });

  it('returns defaults for empty arrays', () => {
    expect(extractLayoutSettings([], [])).toEqual(LAYOUT_DEFAULTS);
  });

  it('extracts all settings from GUI_Settings type', () => {
    const { things, relationships } = buildGuiFixture({
      LayoutRepulsion: 0.5,
      LayoutGravity: 0.01,
      ClusterRepulsion: 1.5,
    });
    expect(extractLayoutSettings(things, relationships)).toEqual({
      repulsion: 0.5,
      gravity: 0.01,
      clusterRepulsion: 1.5,
      // Bug #5361 — perf knobs not present in this fixture, so defaults apply.
      scalingRatioMultiplier: LAYOUT_DEFAULTS.scalingRatioMultiplier,
      gravityMultiplier: LAYOUT_DEFAULTS.gravityMultiplier,
      barnesHutTheta: LAYOUT_DEFAULTS.barnesHutTheta,
      slowDown: LAYOUT_DEFAULTS.slowDown,
      strongGravityMode: LAYOUT_DEFAULTS.strongGravityMode,
      nodeSizeMin: LAYOUT_DEFAULTS.nodeSizeMin,
      nodeSizeMax: LAYOUT_DEFAULTS.nodeSizeMax,
      nodeSizeSlope: LAYOUT_DEFAULTS.nodeSizeSlope,
      edgeSize: LAYOUT_DEFAULTS.edgeSize,
      // Feature #5362 cleanup — classifyingProperty defaults when unset
      classifyingProperty: LAYOUT_DEFAULTS.classifyingProperty,
    });
  });

  it('extracts the Bug #5361 perf knobs from GUI_Settings overrides', () => {
    const { things, relationships } = buildGuiFixture({
      LayoutScalingRatioMultiplier: 200,
      LayoutGravityMultiplier: 5000,
      LayoutBarnesHutTheta: 0.8,
      LayoutSlowDown: 7,
      LayoutStrongGravityMode: false,
      NodeSizeMin: 2,
      NodeSizeMax: 10,
      NodeSizeSlope: 0.6,
      EdgeSize: 0.5,
    });
    const r = extractLayoutSettings(things, relationships);
    expect(r.scalingRatioMultiplier).toBe(200);
    expect(r.gravityMultiplier).toBe(5000);
    expect(r.barnesHutTheta).toBe(0.8);
    expect(r.slowDown).toBe(7);
    expect(r.strongGravityMode).toBe(false);
    expect(r.nodeSizeMin).toBe(2);
    expect(r.nodeSizeMax).toBe(10);
    expect(r.nodeSizeSlope).toBe(0.6);
    expect(r.edgeSize).toBe(0.5);
  });

  it('extracts classifyingProperty when GUI_Settings overrides it', () => {
    const { things, relationships } = buildGuiFixture({
      ClassifyingProperty: 'kind',
    });
    expect(extractLayoutSettings(things, relationships).classifyingProperty).toBe('kind');
  });

  it('falls back to default classifyingProperty on empty/non-string', () => {
    const a = buildGuiFixture({ ClassifyingProperty: '' });
    expect(extractLayoutSettings(a.things, a.relationships).classifyingProperty).toBe(LAYOUT_DEFAULTS.classifyingProperty);
    const b = buildGuiFixture({ ClassifyingProperty: 42 });
    expect(extractLayoutSettings(b.things, b.relationships).classifyingProperty).toBe(LAYOUT_DEFAULTS.classifyingProperty);
  });

  it('falls back per-property for missing values', () => {
    const { things, relationships } = buildGuiFixture({
      LayoutRepulsion: 0.5,
    });
    const result = extractLayoutSettings(things, relationships);
    expect(result.repulsion).toBe(0.5);
    expect(result.gravity).toBe(LAYOUT_DEFAULTS.gravity);
    expect(result.clusterRepulsion).toBe(LAYOUT_DEFAULTS.clusterRepulsion);
  });

  it('falls back to defaults for invalid types', () => {
    const { things, relationships } = buildGuiFixture({
      LayoutRepulsion: null,
      LayoutGravity: undefined,
      ClusterRepulsion: false,
    });
    expect(extractLayoutSettings(things, relationships)).toEqual(
      LAYOUT_DEFAULTS,
    );
  });

  it('no longer exposes the retired force-engine knobs', () => {
    const result = extractLayoutSettings([], []) as unknown as Record<string, unknown>;
    expect('attraction' in result).toBe(false);
    expect('inertia' in result).toBe(false);
    expect('maxMove' in result).toBe(false);
  });
});

// ── extractPredicateColors ───────────────────────────────────────────

describe('extractPredicateColors', () => {
  it('returns empty object when no GUI_Settings type exists', () => {
    expect(extractPredicateColors([], [])).toEqual({});
  });

  it('returns empty object when PredicateColors property is missing', () => {
    const { things, relationships } = buildGuiFixture({ FlashEdgeSize: 1.5 });
    expect(extractPredicateColors(things, relationships)).toEqual({});
  });

  it('parses valid JSON color map', () => {
    const { things, relationships } = buildGuiFixture({
      PredicateColors: '{"consumes":"#fb7185","produces":"#22d3ee"}',
    });
    expect(extractPredicateColors(things, relationships)).toEqual({
      consumes: '#fb7185',
      produces: '#22d3ee',
    });
  });

  it('returns empty object for invalid JSON', () => {
    const { things, relationships } = buildGuiFixture({
      PredicateColors: 'not-json',
    });
    expect(extractPredicateColors(things, relationships)).toEqual({});
  });

  it('returns empty object for non-string property', () => {
    const { things, relationships } = buildGuiFixture({
      PredicateColors: 42,
    });
    expect(extractPredicateColors(things, relationships)).toEqual({});
  });

  it('returns empty object for JSON array', () => {
    const { things, relationships } = buildGuiFixture({
      PredicateColors: '["#ff0000"]',
    });
    expect(extractPredicateColors(things, relationships)).toEqual({});
  });

  it('skips non-hex values in the map', () => {
    const { things, relationships } = buildGuiFixture({
      PredicateColors: '{"consumes":"#fb7185","bad":"not-a-color","num":42}',
    });
    expect(extractPredicateColors(things, relationships)).toEqual({
      consumes: '#fb7185',
    });
  });

  it('returns empty object for empty string', () => {
    const { things, relationships } = buildGuiFixture({
      PredicateColors: '',
    });
    expect(extractPredicateColors(things, relationships)).toEqual({});
  });

  it('instance can override type PredicateColors', () => {
    const { things, relationships } = buildGuiFixture(
      { PredicateColors: '{"consumes":"#fb7185"}' },
      { PredicateColors: '{"consumes":"#ff0000","produces":"#00ff00"}' },
    );
    expect(extractPredicateColors(things, relationships)).toEqual({
      consumes: '#ff0000',
      produces: '#00ff00',
    });
  });
});

// How many decimal places a number shows belongs to the model being looked at, not to the client
// looking at it — a model of geometry, a model of money and a model of readings each want a
// different one, and only the model knows which it is (#6163).
describe('extractNumberDisplaySettings', () => {
  it('shows five places for both when no GUI_Settings type exists', () => {
    expect(extractNumberDisplaySettings([makeThing('SomeOtherThing')], [])).toEqual(NUMBER_DISPLAY_DEFAULTS);
    expect(NUMBER_DISPLAY_DEFAULTS).toEqual({ floatingPointPrecision: 5, decimalPrecision: 5 });
  });

  it('takes each setting from the GUI_Settings type', () => {
    const { things, relationships } = buildGuiFixture({
      FloatingPointDisplayPrecision: 3,
      DecimalDisplayPrecision: 2,
    });
    expect(extractNumberDisplaySettings(things, relationships)).toEqual({
      floatingPointPrecision: 3,
      decimalPrecision: 2,
    });
  });

  it('lets an instance override the type', () => {
    const { things, relationships } = buildGuiFixture(
      { FloatingPointDisplayPrecision: 3 },
      { FloatingPointDisplayPrecision: 8 },
    );
    expect(extractNumberDisplaySettings(things, relationships).floatingPointPrecision).toBe(8);
  });

  it('sets each independently, so one stated value does not move the other', () => {
    const { things, relationships } = buildGuiFixture({ DecimalDisplayPrecision: 2 });
    expect(extractNumberDisplaySettings(things, relationships)).toEqual({
      floatingPointPrecision: 5,
      decimalPrecision: 2,
    });
  });

  // A count of places has to be a whole number no smaller than zero, and toFixed rejects anything
  // past twenty. A setting outside that says nothing usable, so the default stands rather than the
  // panel throwing on a number somebody typed into the model.
  it('falls back on a value that cannot be a count of places', () => {
    for (const stated of [-1, 2.5, 21, NaN, '3', null, true]) {
      const { things, relationships } = buildGuiFixture({ FloatingPointDisplayPrecision: stated });
      expect(extractNumberDisplaySettings(things, relationships).floatingPointPrecision).toBe(5);
    }
  });

  it('accepts nought places, which is a real answer and not a missing one', () => {
    const { things, relationships } = buildGuiFixture({ FloatingPointDisplayPrecision: 0 });
    expect(extractNumberDisplaySettings(things, relationships).floatingPointPrecision).toBe(0);
  });
});
