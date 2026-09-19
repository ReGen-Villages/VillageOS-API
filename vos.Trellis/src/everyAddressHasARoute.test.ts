/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Guards that every address the console sends somebody to is an address the router answers.
 *
 * The router ends with a catch-all that redirects anything it does not recognise to the dashboard,
 * so an address with no route behind it does not fail: the page is redrawn and the person is told
 * nothing. It reads addresses written as literals — a navigate() call, a `to=` on a link, and the
 * sidebar's own table. One assembled entirely from a value is beyond it, which is why an address
 * that does not resolve to an absolute path is reported rather than skipped.
 */

const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));

/** The addresses the routes answer, less the catch-all — which hides a missing route rather than
 *  answering for it. */
function routePatterns(): string[] {
  const app = readFileSync(join(SOURCE_DIRECTORY, 'App.tsx'), 'utf8');
  return [...app.matchAll(/<Route\s+path="([^"]+)"/g)]
    .map((match) => match[1])
    .filter((path) => path !== '*');
}

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(entry) && !entry.includes('.test.') ? [path] : [];
  });
}

const NAVIGATION_CALL = /navigate\(\s*['"`]([^'"`]*)['"`]/g;
const LINK_TARGET = /\bto=(?:\{\s*)?['"`]([^'"`]*)['"`]/g;
const NAMED_VALUE = /\$\{(\w+)\}/g;
const ANY_VALUE = /\$\{[^}]*\}/g;
const EXPORTED_PATH = /(?:export )?const (\w+) = ['"](\/[^'"]*)['"]/g;

/** Stands in for a segment filled by a value, which any route segment may answer. */
const FILLED_BY_A_VALUE = '<a value>';

/** What a constant holding an address is worth, so an address built on one reads as an address
 *  rather than as a name. */
function knownPaths(sources: Map<string, string>): Map<string, string> {
  const paths = new Map<string, string>();
  for (const source of sources.values())
    for (const [, name, value] of source.matchAll(EXPORTED_PATH)) paths.set(name, value);
  return paths;
}

/** The address as far as the route table cares: constants resolved, a value standing in for the
 *  segment it fills, and the query and fragment cut off. */
function addressOf(written: string, paths: Map<string, string>): string {
  const resolved = written.replace(NAMED_VALUE, (whole, name: string) => paths.get(name) ?? whole);
  return resolved.replace(ANY_VALUE, FILLED_BY_A_VALUE).split(/[?#]/)[0];
}

function isAnswered(address: string, patterns: string[]): boolean {
  const wanted = address.split('/');
  return patterns.some((pattern) => {
    const offered = pattern.split('/');
    return (
      offered.length === wanted.length &&
      offered.every(
        (segment, index) =>
          segment.startsWith(':') ||
          wanted[index] === FILLED_BY_A_VALUE ||
          segment === wanted[index],
      )
    );
  });
}

describe('the addresses the console opens', () => {
  it('are every one of them answered by a route', () => {
    const sources = new Map(sourceFiles(SOURCE_DIRECTORY).map((path) => [path, readFileSync(path, 'utf8')]));
    const paths = knownPaths(sources);
    const patterns = routePatterns();

    const opened = [...sources].flatMap(([file, source]) =>
      [...source.matchAll(NAVIGATION_CALL), ...source.matchAll(LINK_TARGET)].map((match) => ({
        where: file.slice(SOURCE_DIRECTORY.length + 1),
        address: addressOf(match[1], paths),
      })),
    );

    const said = (one: { where: string; address: string }) => `${one.where} :: ${one.address}`;
    const unresolved = opened.filter((one) => !one.address.startsWith('/')).map(said);
    const unanswered = opened
      .filter((one) => one.address.startsWith('/') && !isAnswered(one.address, patterns))
      .map(said);

    expect({ unanswered, unresolved }).toEqual({ unanswered: [], unresolved: [] });
  });
});
