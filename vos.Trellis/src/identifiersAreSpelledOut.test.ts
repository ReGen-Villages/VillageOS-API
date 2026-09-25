/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Every name the console writes is spelled out, and this says which short forms are refused.
 *
 * The house rule is that an identifier is written in full — `quantity`, not `qty`. Other house
 * rules here are held by a test, so breaking them costs a red build; breaking this one cost
 * nothing, and a clipped name spread by being copied.
 *
 * SPELLED_OUT is the refusal: each clipped form beside the word it stands for, so the failure says
 * what to write rather than only that something is wrong. KEPT and KEPT_IN are the other half of
 * the record — every name that carries a short form on purpose, with the reason; the second holds
 * those allowed in one file rather than everywhere. A name is in one list or the other; nothing is
 * refused silently and nothing is allowed silently.
 *
 * The walk blanks comments and string literals before it reads anything, because neither is an
 * identifier: a scan over raw text counts a locale's sentences and a test fixture's contents.
 */

const SOURCE_DIRECTORY = dirname(fileURLToPath(import.meta.url));

/** Each clipped form and the word it stands for. `min` and `max` are absent on purpose: they are how
 *  a bound is written in arithmetic, not clipped words. */
const SPELLED_OUT: Record<string, string> = {
  'abbrev': 'abbreviated',
  'arch': 'archetype',
  'args': 'arguments',
  'attr': 'attribute',
  'attrs': 'attributes',
  'auth': 'authentication',
  'avg': 'average',
  'cb': 'callback',
  'cfg': 'configuration',
  'cls': 'class',
  'col': 'column',
  'cols': 'columns',
  'config': 'configuration',
  'cred': 'credential',
  'creds': 'credentials',
  'ctx': 'context',
  'desc': 'description',
  'dir': 'direction or directory',
  'dirs': 'directories',
  'ents': 'entities',
  'ep': 'endpoint',
  'exec': 'execute',
  'gw': 'gateway',
  'idx': 'index',
  'info': 'information',
  'init': 'initialise',
  'kv': 'key and value',
  'loc': 'location',
  'locs': 'locations',
  'ms': 'milliseconds',
  'msg': 'message',
  'msgs': 'messages',
  'nav': 'navigation',
  'nr': 'number',
  'num': 'number',
  'obj': 'object',
  'op': 'operation',
  'opts': 'options',
  'param': 'parameter',
  'params': 'parameters',
  'pct': 'percent',
  'pid': 'process id',
  'pos': 'position',
  'pred': 'predicate',
  'prev': 'previous',
  'prop': 'property',
  'props': 'properties',
  'qty': 'quantity',
  'rect': 'rectangle',
  'ref': 'reference',
  'refs': 'references',
  'rel': 'relationship',
  'rels': 'relationships',
  'req': 'request',
  'reqs': 'requests',
  'resp': 'response',
  'rng': 'random',
  'seq': 'sequence',
  'spec': 'specification',
  'specs': 'specifications',
  'src': 'source',
  'stats': 'statistics',
  'subj': 'subject',
  'svc': 'service',
  'targ': 'target',
  'tid': 'thing id',
  'tmp': 'temporary',
  'tok': 'token',
  'toks': 'tokens',
  'uid': 'unique id',
  'util': 'utility',
  'utils': 'utilities',
  'val': 'value',
  'vals': 'values',
};

/** Every name kept short on purpose, and why. A name here is one nobody here chose: a language's
 *  own word, a framework's, a markup tag, or a field that arrives over the wire already written
 *  that way. An entry says where the name is allowed as well as what it is: a plain one anywhere;
 *  one beginning with a dot only after a dot, where it belongs to whatever stands on the left; one
 *  ending in an equals sign only as a markup attribute; one beginning with `<` only as a tag. */
const KEPT = new Map<string, string>();
/** Names allowed in one file rather than everywhere; the key's first half is a path under src. */
const KEPT_IN = new Map<string, string>();

function kept(reason: string, ...names: string[]): void {
  for (const name of names) KEPT.set(name, reason);
}

function keptIn(where: string, reason: string, ...names: string[]): void {
  for (const name of names) KEPT_IN.set(`${where}::${name}`, reason);
}

/** Clipped words held whole under a work item, because the identifiers carrying them are JSON keys a
 *  page specification shares with the platform's templates and change together with those. */
const HELD_WORDS = new Set<string>();

function held(reason: string, words: string[], ...names: string[]): void {
  for (const word of words) HELD_WORDS.add(word);
  kept(reason, ...names);
}

kept("React's word for a component's inputs, and the type naming them; `properties` here are a Thing's",
  'props', 'Props', 'baseProps', 'orbitControlsProps', 'StepProps', 'EntityPickerProps', 'NodeProps',
  'BimFragmentsSceneProps', 'BimFragmentsViewerProps', 'BuildingDetail3DProps', 'ChangePasswordFormProps',
  'LoadingOverlayProps', 'LoginFormProps', 'MapViewProps', 'RegenLogoProps', 'ViewerToolbarProps');
kept("React's holder for a value that outlives a render, and the attribute that fills one", 'useRef', 'RefObject', 'MutableRefObject', 'ref=');
kept("React's report handed to an error boundary", 'ErrorInfo');
kept("react-router's hooks and link", 'useParams', 'useSearchParams', 'NavLink');
kept("react-three-fiber's constructor arguments on an element", 'args=');
kept("react-grid-layout's settings, each an attribute of the grid, and the column count inside one", 'gridConfig=', 'dragConfig=', 'resizeConfig=', 'dropConfig=', 'cols');
kept("Testing Library's first inputs to a rendered hook", 'initialProps');
kept("lucide's icon", 'Info');
kept("the browser's own names: the request shape, the query string, a bounding box, a canvas call, a table cell, a pattern match, a console level, a link relation",
  'RequestInit', 'HeadersInit', 'URLSearchParams', 'getBoundingClientRect', 'DOMRect', 'contentRect',
  'clearRect', 'fillRect', 'strokeRect', 'colSpan', '.exec', '.info', 'rel=');
kept('markup tags', '<rect', '<nav');
kept("an image element's own attributes: the file it shows, and the files a picture chooses between", 'src=', 'srcSet=');
kept("i18next's start-up", 'initReactI18next', '.init');
kept("vitest's runtime setting", '.setConfig');
kept("the platform's wire: the property envelope, a pipeline node's bindings, the service registry's figures",
  'typeInfo', 'paramBindings', 'Stats', 'AverageResponseMs', 'TotalResponseMs');
kept("property names the platform's templates declare; renaming them is a model change, listed on Task 7123",
  'pctOfConsumption', 'sharePct');
keptIn('components/map/MapView.tsx', "maplibre's export, aliased on import", 'config');
keptIn('components/map/MapView.test.tsx', "the mocked maplibre module exports maplibre's name", 'config');
kept("the wire's own name for a page's specification: the property a Dashboard Thing carries, the field the platform's declared pages and the intake service's findings answer with", 'spec');
held("JSON keys a page specification shares with the platform's templates and the seed generators; Task 7123 renames them together",
  [], 'op', 'avg', 'sortDir', 'setSortDir');

const IDENTIFIER = /[A-Za-z_$][A-Za-z0-9_$]*/g;

/** Own keys only: `constructor` is in every object's prototype and is not a clipped word. */
const isClipped = (word: string): boolean => Object.hasOwn(SPELLED_OUT, word.toLowerCase());

/** `text` with its comments and string literals blanked, keeping every line and column. Read one
 *  character at a time rather than by pattern, so the scan always knows where it stands: a pattern
 *  cannot, since an apostrophe inside a comment opens a string that swallows the code after it. */
function codeOnly(text: string): string {
  const kept: string[] = [];
  let at = 0;
  const blanked = (piece: string) => piece.replace(/[^\n]/g, ' ');
  while (at < text.length) {
    let end: number;
    if (text.startsWith('//', at)) {
      end = text.indexOf('\n', at);
      end = end < 0 ? text.length : end;
    } else if (text.startsWith('/*', at)) {
      end = text.indexOf('*/', at + 2);
      end = end < 0 ? text.length : end + 2;
    } else if (text[at] === '"' || text[at] === "'" || text[at] === '`') {
      const quote = text[at];
      end = at + 1;
      while (end < text.length) {
        if (text[end] === '\\') end += 2;
        else if (text[end] === quote) { end += 1; break; }
        else if (text[end] === '\n' && quote !== '`') break;
        else end += 1;
      }
      end = Math.min(end, text.length);
    } else {
      kept.push(text[at]);
      at += 1;
      continue;
    }
    kept.push(blanked(text.slice(at, end)));
    at = end;
  }
  return kept.join('');
}

/** The words `identifier` is made of, split on underscores and on where the case turns over. */
function wordsIn(identifier: string): string[] {
  return identifier.split('_').flatMap((chunk) => chunk.match(/[A-Z]+(?![a-z])|[A-Z][a-z0-9]*|[a-z0-9]+/g) ?? []);
}

function allowed(identifier: string, path: string, before: string, after: string): boolean {
  if (KEPT.has(identifier)) return true;
  if (before.trimEnd().endsWith('.') && KEPT.has(`.${identifier}`)) return true;
  if (after.startsWith('=') && !after.startsWith('==') && KEPT.has(`${identifier}=`)) return true;
  if ((before.trimEnd().endsWith('<') || before.trimEnd().endsWith('</')) && KEPT.has(`<${identifier}`)) return true;
  return KEPT_IN.has(`${extname(path)}::${identifier}`) || KEPT_IN.has(`${path}::${identifier}`);
}

function sourceFiles(directory: string): string[] {
  return readdirSync(directory).flatMap((entry) => {
    const path = join(directory, entry);
    if (statSync(path).isDirectory()) return sourceFiles(path);
    return /\.tsx?$/.test(entry) ? [path] : [];
  });
}

/** Every identifier written here that carries a clipped word, and where it is written. */
function clippedNames(): { path: string; line: number; identifier: string; word: string }[] {
  const found: { path: string; line: number; identifier: string; word: string }[] = [];
  for (const full of sourceFiles(SOURCE_DIRECTORY)) {
    const path = full.slice(SOURCE_DIRECTORY.length + 1);
    const lines = codeOnly(readFileSync(full, 'utf8')).split('\n');
    lines.forEach((line, index) => {
      for (const written of line.matchAll(IDENTIFIER)) {
        const identifier = written[0];
        if (allowed(identifier, path, line.slice(0, written.index), line.slice(written.index! + identifier.length))) continue;
        const clipped = wordsIn(identifier).find((word) => isClipped(word) && !HELD_WORDS.has(word.toLowerCase()));
        if (clipped) found.push({ path, line: index + 1, identifier, word: clipped.toLowerCase() });
      }
    });
  }
  return found;
}

describe('the names the console writes', () => {
  it('carry no clipped word, or are kept with a reason', () => {
    const found = clippedNames();
    const shown = found.slice(0, 60).map(
      ({ path, line, identifier, word }) => `${path}:${line}  ${identifier} — write '${SPELLED_OUT[word]}' for '${word}'`);

    expect(shown, `${found.length} names carry a clipped word`).toEqual([]);
  });

  it('every name kept short says why', () => {
    const silent = [...KEPT, ...KEPT_IN].filter(([, reason]) => !reason.trim()).map(([name]) => name);
    expect(silent).toEqual([]);
  });

  it('a kept name carries a clipped word, or the entry is idle', () => {
    const every = [...KEPT.keys(), ...[...KEPT_IN.keys()].map((key) => key.split('::')[1])];
    const idle = every.filter((name) => !wordsIn(name.replace(/^[.<]|=$/g, '')).some(isClipped));
    expect(idle).toEqual([]);
  });
});
