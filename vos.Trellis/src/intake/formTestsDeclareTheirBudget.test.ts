import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync } from 'node:fs';
import { join, relative, resolve } from 'node:path';
import { importsOf } from '../sourceImports';

/**
 * A test body that renders the whole multi-step submission form and then drives it needs more room than
 * vitest's default five seconds, which bounds how busy the build agent is rather than anything the form
 * does. `testTimeouts.ts` holds what that room is sized from.
 *
 * The pages are found by importing the form and their tests by importing a page, rather than listed here,
 * so a second page built on the form or a second test file written for one is held to this without anyone
 * adding it. This replaces annotating one body at a time as each first goes red.
 */
const SOURCE = resolve(__dirname, '..');
const FORM = resolve(__dirname, 'IntakeWizard.tsx');

const DECLARES_THE_BUDGET =
  /vi\.setConfig\(\s*\{\s*testTimeout:\s*MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS\s*,?\s*\}\s*\)/;

const everySource = readdirSync(SOURCE, { recursive: true })
  .map((entry) => join(SOURCE, entry as string))
  .filter((file) => file.endsWith('.ts') || file.endsWith('.tsx'));

const isTest = (file: string): boolean => file.endsWith('.test.ts') || file.endsWith('.test.tsx');

const formPages = everySource.filter((file) => !isTest(file) && importsOf(file).includes(FORM));
const formTests = everySource.filter(
  (file) => isTest(file) && importsOf(file).some((imported) => formPages.includes(imported)),
);

describe('a test that renders the whole submission form', () => {
  it('is found by what it imports, so an empty walk cannot pass this file', () => {
    expect(formPages).not.toEqual([]);
    expect(formTests).not.toEqual([]);
  });

  it.each(formTests.map((file) => relative(SOURCE, file)))(
    '%s gives its bodies the budget a loaded agent needs',
    (named) => {
      expect(readFileSync(resolve(SOURCE, named), 'utf8')).toMatch(DECLARES_THE_BUDGET);
    },
  );
});
