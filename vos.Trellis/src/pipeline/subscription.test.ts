/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { NAVIGATION_AND_SETTINGS } from '../api/dashboardSubscription';
import { ROLE } from './model';
import { FOLLOWED_ONE_STEP, KINDS_READ, PIPELINE_PAGE } from './subscription';

const PIPELINE_DIRECTORY = dirname(fileURLToPath(import.meta.url));

/** The roles whose members the page's reads walk: `thingsOf(ROLE.X)` and `isOf(…, ROLE.X)` in the code
 *  that reads the model for the page. A role read that way and not asked for is a page that quietly
 *  shows nothing of that kind, so the list is read off the sources rather than kept by hand. */
function rolesReadAsKinds(): Set<string> {
  const roles = new Set<string>();
  for (const file of ['model.ts', 'catalysts.ts', 'serialize.ts', 'stop.ts', 'handOver.ts']) {
    const source = readFileSync(join(PIPELINE_DIRECTORY, file), 'utf8');
    for (const [, role] of source.matchAll(/\b(?:thingsOf|isOf)\((?:[^,()]+,\s*)?ROLE\.(\w+)\)/g)) roles.add(role);
  }
  return roles;
}

describe('the Pipelines page subscription', () => {
  it('keeps asking for what the navigation and the display settings are read from', () => {
    expect(PIPELINE_PAGE.ids).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.ids!));
    expect(PIPELINE_PAGE.boundTypes).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.boundTypes!));
    expect(PIPELINE_PAGE.boundArchetypes).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.boundArchetypes!));
    expect(PIPELINE_PAGE.names).toEqual(expect.arrayContaining(NAVIGATION_AND_SETTINGS.names!));
  });

  it('asks for every role Thing by identifier, and for what each binds', () => {
    for (const role of Object.values(ROLE)) {
      expect(PIPELINE_PAGE.ids).toContain(role);
      expect([...PIPELINE_PAGE.boundTypes!, ...PIPELINE_PAGE.boundArchetypes!]).toContain(role);
    }
  });

  it('asks for the members of every kind the page reads, and for no other role\'s members', () => {
    const read = rolesReadAsKinds();
    expect(read.size).toBeGreaterThan(0);
    for (const role of read) expect(KINDS_READ).toContain(ROLE[role as keyof typeof ROLE]);
    for (const role of PIPELINE_PAGE.boundArchetypes!) expect(KINDS_READ).not.toContain(role);
  });

  it('asks for the node runs, which the orchestrator writes and no read names by role', () => {
    expect(KINDS_READ).toContain(ROLE.NodeRun);
  });

  it('follows one step along the predicates that reach Things no role does, by role, and keeps following', () => {
    expect(PIPELINE_PAGE.traverse).toEqual(FOLLOWED_ONE_STEP.map((role) => ({ predicateRole: role })));
    expect(FOLLOWED_ONE_STEP).toEqual(expect.arrayContaining([ROLE.StandsFor, ROLE.Choice, ROLE.ComparesWith]));
    expect(PIPELINE_PAGE.includeLaterMatches).toBe(true);
  });

  it('asks for the built-in predicates the page matches by name', () => {
    expect(PIPELINE_PAGE.names).toEqual(expect.arrayContaining(['is', 'has', 'of', 'binds']));
  });

  it('asks for neither the whole model nor any kind by name', () => {
    expect(PIPELINE_PAGE.all).toBeUndefined();
    expect(PIPELINE_PAGE.types).toBeUndefined();
  });
});
