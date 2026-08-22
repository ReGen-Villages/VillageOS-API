/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Every `submissions` subcommand the command line answers to has a row in the CLI Command Parity
 * table, naming the page element that does the same thing — or saying it has none, and why.
 *
 * A command added on one branch and a table edited on another merge cleanly, because they touch
 * different lines. That is how `dispose` came to be missing from a table headed as complete
 * (Bug #6661). The table is read from the handler rather than remembered.
 */

const REPOSITORY = join(dirname(fileURLToPath(import.meta.url)), '..', '..', '..');

function subcommandsTheHandlerAnswers(): string[] {
  const handler = readFileSync(join(REPOSITORY, 'vos.Taproot', 'SubmissionsCommandHandler.cs'), 'utf-8');
  const dispatch = handler.slice(handler.indexOf('switch (subcommand'));
  return [...dispatch.matchAll(/case "([a-z]+)":/g)].map((match) => match[1]);
}

describe('the parity table lists every submissions command', () => {
  const parityTable = readFileSync(join(REPOSITORY, 'docs', 'TRELLIS.md'), 'utf-8');
  const subcommands = subcommandsTheHandlerAnswers();

  it('reads the subcommands off the handler rather than holding a list of them', () => {
    expect(subcommands.length).toBeGreaterThan(1);
  });

  for (const subcommand of subcommands) {
    it(`\`submissions ${subcommand}\` has a row`, () => {
      expect(parityTable).toContain(`| \`submissions ${subcommand}`);
    });
  }
});
