/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { en } from './locales/en';
import { AREA_UNITS, STEPS } from '../intake/submissionDraft';
import { TEMPORAL_TABS } from '../pages/temporalTabs';
import { DELETABLE_ENTITIES } from '../pages/graphDeletions';
import { STATISTICS } from '../components/dashboard/widgets/RangeBar';
import { SIDES } from '../components/dashboard/widgets/DivergingBar';
import { WIDGET_KINDS } from '../utils/gridLayout';
import { BINDING_FIELD_KEYS, BINDING_KINDS, WIDGET_FIELD_KEYS } from '../utils/widgetSchema';
import { DESIGN_FINDING_CODES } from '../utils/designFindings';
import { LOADING_STAGES } from '../components/model/LoadingOverlay';
import { REFUSAL_CODES } from '../api/refusals';
import { PIPELINE_DECLARATIONS } from '../pipeline/serialize';

/**
 * Whether a key the console assembles at run time has anything to say.
 *
 * Most keys are written out where the compiler can see them and where `parity.test.ts` can see
 * them. A few are built from a value — ``t(`intake.step.${step}`)`` and the like — and neither
 * check reaches those. The compiler cannot read a template literal, and parity compares the other
 * locales against English rather than English against anything, so a value with no English
 * sentence is a key every locale agrees is absent: parity passes and the screen shows the key.
 *
 * English is the only locale asked here, because parity already carries every other locale to it.
 */

const SOURCE_DIRECTORY = join(dirname(fileURLToPath(import.meta.url)), '..');

/** Every family of key the console builds from a value, and the values it builds them from. */
const BUILT_FROM = {
  'intake.step': [...STEPS],
  'intake.stepHint': [...STEPS],
  'intake.areaEquivalent': [...AREA_UNITS],
  intake: [...AREA_UNITS],
  'temporal.tabs': [...TEMPORAL_TABS],
  'graph.entity': [...DELETABLE_ENTITIES],
  'widgets.rangeBar': [...STATISTICS],
  'widgets.divergingBar': SIDES.map((side) => side.word),
  'design.palette.kind': [...WIDGET_KINDS],
  'design.bindingKind': [...BINDING_KINDS],
  'design.bindingCost': [...BINDING_KINDS],
  'design.widgetField': [...WIDGET_FIELD_KEYS],
  'design.bindingField': [...BINDING_FIELD_KEYS],
  'design.finding': [...DESIGN_FINDING_CODES],
  'modelPage.stage': [...LOADING_STAGES],
  refusal: [...REFUSAL_CODES],
  'pipeline.declarations': [...PIPELINE_DECLARATIONS],
} as Record<string, string[]>;

/** A `t()` call whose key is assembled rather than written out, e.g. t(`intake.step.${step}`). */
const ASSEMBLED_KEY = /\bt\(\s*`([^`$]*)\$\{/g;

function sourceFiles(directory: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(directory)) {
    const full = join(directory, entry);
    if (statSync(full).isDirectory()) out.push(...sourceFiles(full));
    else if (/\.tsx?$/.test(entry) && !entry.includes('.test.')) out.push(full);
  }
  return out;
}

/** The key families the source actually assembles, as `intake.step` rather than `intake.step.`. */
function familiesAssembledInSource(): Set<string> {
  const found = new Set<string>();
  for (const file of sourceFiles(SOURCE_DIRECTORY)) {
    for (const [, prefix] of readFileSync(file, 'utf8').matchAll(ASSEMBLED_KEY)) {
      found.add(prefix.replace(/\.$/, ''));
    }
  }
  return found;
}

/** What English says under that dotted path, or undefined where it says nothing. */
function sentenceAt(path: string): unknown {
  return path.split('.').reduce<unknown>(
    (found, key) => (found && typeof found === 'object'
      ? (found as Record<string, unknown>)[key]
      : undefined),
    en,
  );
}

describe('a key the console builds at run time has a sentence to show', () => {
  for (const [family, values] of Object.entries(BUILT_FROM)) {
    it(`${family}.* answers every value it is built from`, () => {
      expect(values.length).toBeGreaterThan(0);
      const sayingNothing = values.filter(
        (value) => typeof sentenceAt(`${family}.${value}`) !== 'string');

      expect(sayingNothing).toEqual([]);
    });
  }

  it('is asked about every family the source assembles', () => {
    // Otherwise a new one is added, nothing here covers it, and the guard reads as complete.
    const assembled = [...familiesAssembledInSource()].sort();

    expect(assembled).toEqual(Object.keys(BUILT_FROM).sort());
  });
});
