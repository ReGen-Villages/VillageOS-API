import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { numeral, readDocument, renderGuide } from './build.mjs';
import { lightenForPrint, inlineFigure } from './figures.mjs';
import { mark, stylesheet } from './brand.mjs';

const GUIDE = `# A Guide

> **The standfirst.** What this guide is.

<sub>A note in small print.</sub>

## How to read this

Find yourself below.

## Contents

- [Part I — The first part](#part-i--the-first-part)
- [Appendix A — A list](#appendix-a--a-list)

## Part I — The first part

*For anyone.*

### 1. The first chapter

Some prose.

### 2. The second chapter

More prose.

---

## Appendix A — A list

### 3. The only appendix chapter

A word.
`;

test('a part is numbered by its ordinal and an appendix by its letter', () => {
  assert.equal(numeral('Part I'), '1');
  assert.equal(numeral('Part IV'), '4');
  assert.equal(numeral('Part VI'), '6');
  assert.equal(numeral('Part 4'), '4');
  assert.equal(numeral('Appendix C'), 'C');
});

test('a guide is read as its title, its standfirst, its parts and their chapters', () => {
  const doc = readDocument(GUIDE);
  assert.equal(doc.title, 'A Guide');
  assert.match(doc.lede, /The standfirst/);
  assert.deepEqual(doc.parts.map((p) => p.label), ['Part I', 'Appendix A']);
  assert.equal(doc.parts[0].title, 'The first part');
  assert.equal(doc.parts[0].audience, 'For anyone.');
  assert.deepEqual(doc.parts[0].chapters.map((c) => [c.number, c.title]),
    [['1', 'The first chapter'], ['2', 'The second chapter']]);
  assert.equal(doc.parts[1].chapters.length, 1);
});

test("the guide's own contents list is dropped, so a reader never meets two", () => {
  const doc = readDocument(GUIDE);
  const front = doc.front.map((t) => t.raw).join('');
  assert.match(front, /How to read this/);
  assert.doesNotMatch(front, /Contents/);
  assert.doesNotMatch(front, /#part-i--the-first-part/);
});

test('a rule between two parts does not print at the foot of a page', () => {
  const doc = readDocument(GUIDE);
  const last = doc.parts[0].chapters.at(-1);
  assert.ok(!last.tokens.some((t) => t.type === 'hr'));
});

test('a guide with no parts is read as prose, and its own headings become the contents', () => {
  const doc = readDocument('# Plain\n\n## One\n\ntext\n\n## Two\n\ntext\n');
  assert.equal(doc.parts.length, 0);
  assert.deepEqual(doc.sections.map((s) => s.title), ['One', 'Two']);
});

const DARK = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50">
  <rect width="100" height="50" fill="#0f1c17"/>
  <text fill="#ecfdf5">light on dark</text>
  <rect x="4" y="4" width="20" height="10" fill="#13301e" stroke="#22c55e"/>
</svg>`;

const LIGHT = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 50">
  <rect width="100" height="50" fill="#ffffff"/>
  <text fill="#111111">dark on light</text>
</svg>`;

test('a figure drawn on a dark ground is turned light, and its ground becomes paper', () => {
  const { svg, lightened } = lightenForPrint(DARK);
  assert.equal(lightened, true);
  assert.match(svg, /<rect width="100" height="50" fill="#ffffff"/);
  assert.doesNotMatch(svg, /#ecfdf5/);
  assert.doesNotMatch(svg, /#13301e/);
});

test('a figure that was already light is left exactly as it is', () => {
  const { svg, lightened } = lightenForPrint(LIGHT);
  assert.equal(lightened, false);
  assert.equal(svg, LIGHT);
});

test('a figure with no ground of its own is left alone', () => {
  const bare = '<svg viewBox="0 0 10 10"><circle r="4" fill="#000000"/></svg>';
  assert.equal(lightenForPrint(bare).lightened, false);
});

test('a figure is carried inside the file rather than pointed at', () => {
  const dir = mkdtempSync(join(tmpdir(), 'docs-pdf-'));
  const path = join(dir, 'figure.svg');
  writeFileSync(path, DARK);
  const { uri, lightened } = inlineFigure(path);
  assert.equal(lightened, true);
  assert.match(uri, /^data:image\/svg\+xml;base64,/);
  assert.match(Buffer.from(uri.split(',')[1], 'base64').toString('utf8'), /fill="#ffffff"/);
});

test('the mark is the artwork beside this file, in the colour it is asked for', () => {
  const svg = mark('#37c2aa');
  assert.match(svg, /color:#37c2aa/);
  assert.match(svg, /<path/);
  assert.match(svg, /currentColor/);
});

test('the typography travels with the file rather than being left to the machine', () => {
  const css = stylesheet();
  assert.equal((css.match(/@font-face/g) ?? []).length, 3);
  assert.match(css, /src:url\(data:font\/woff2;base64,/);
});

test('a guide renders to HTML with a cover, a contents page and a divider per part', async (t) => {
  const dir = mkdtempSync(join(tmpdir(), 'docs-pdf-'));
  const input = join(dir, 'guide.md');
  writeFileSync(input, GUIDE);
  const out = join(dir, 'guide.pdf');
  const o = await renderGuide([input, '--out', out, '--html-only']);
  const html = readFileSync(o.html, 'utf8');
  assert.equal((html.match(/class="divider"/g) ?? []).length, 2);
  assert.match(html, /class="cover"/);
  assert.match(html, /class="contents"/);
  assert.match(html, /class="chapter"/);
  assert.doesNotMatch(html, /page: cover/);
});
