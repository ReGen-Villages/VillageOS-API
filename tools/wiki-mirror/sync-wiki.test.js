'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const {
  convert,
  convertMermaid,
  buildSidebar,
  flatName,
  pageSlug,
  decodeName,
} = require('./sync-wiki');

const WIKI = 'https://github.com/regenrob/VillageOS-API/wiki';
const BLOB = 'https://github.com/regenrob/VillageOS-API/blob/main';

test('internal wiki links become absolute GitHub wiki URLs', () => {
  assert.equal(convert('[vos.GUI](/GUI)'), `[vos.GUI](${WIKI}/GUI)`);
});

test('nested wiki links are flattened to the last segment', () => {
  assert.equal(convert('[Delta](/Services/Delta)'), `[Delta](${WIKI}/Delta)`);
});

test('DevOps repo file links become GitHub blob links', () => {
  const input =
    '[docs](https://dev.azure.com/ReGenVillages/VillageOS-API/_git/VillageOS-API?path=/docs/MICROSERVICES.md)';
  assert.equal(convert(input), `[docs](${BLOB}/docs/MICROSERVICES.md)`);
});

test('DevOps wiki root (space-encoded) becomes the GitHub wiki root', () => {
  const input = '[Wiki](https://dev.azure.com/ReGenVillages/VillageOS%20API/_wiki)';
  assert.equal(convert(input), `[Wiki](${WIKI})`);
});

test('DevOps wiki root (hyphenated) becomes the GitHub wiki root', () => {
  const input = '[Wiki](https://dev.azure.com/ReGenVillages/VillageOS-API/_wiki)';
  assert.equal(convert(input), `[Wiki](${WIKI})`);
});

test('private Boards links are stripped to plain text', () => {
  const input =
    'Create a Bug in [Azure DevOps Boards](https://dev.azure.com/ReGenVillages/VillageOS%20API/_boards) or open an issue.';
  assert.equal(
    convert(input),
    'Create a Bug in Azure DevOps Boards or open an issue.'
  );
});

test('Boards stripping does not consume an adjacent wiki link', () => {
  const input =
    '[Boards](https://dev.azure.com/ReGenVillages/VillageOS%20API/_boards) and [GUI](/GUI)';
  assert.equal(convert(input), `Boards and [GUI](${WIKI}/GUI)`);
});

test('stray relative .md link (bare name) points under docs/', () => {
  assert.equal(
    convert('see [IFC_IMPORT_GUIDE.md](IFC_IMPORT_GUIDE.md).'),
    `see [IFC_IMPORT_GUIDE.md](${BLOB}/docs/IFC_IMPORT_GUIDE.md).`
  );
});

test('stray relative .md link with a path is kept as-is under blob', () => {
  assert.equal(
    convert('[x](docs/sub/Y.md)'),
    `[x](${BLOB}/docs/sub/Y.md)`
  );
});

test('already-absolute https .md links are left untouched', () => {
  const url = `[x](${BLOB}/docs/Z.md)`;
  assert.equal(convert(url), url);
});

test('GitHub wiki URLs produced earlier are not double-rewritten', () => {
  // /GUI -> wiki URL; that URL must survive the later relative-.md pass etc.
  assert.equal(convert('[a](/GUI) [b](/CLI)'), `[a](${WIKI}/GUI) [b](${WIKI}/CLI)`);
});

test('mermaid container blocks become fenced code blocks', () => {
  const input = '::: mermaid\ngraph TB\n  A --> B\n:::';
  assert.equal(convertMermaid(input), '```mermaid\ngraph TB\n  A --> B\n```');
});

test('only the closing ::: of a mermaid block is converted', () => {
  const input = 'before\n::: mermaid\nX\n:::\nafter';
  assert.equal(convert(input), 'before\n```mermaid\nX\n```\nafter');
});

test('multiple mermaid blocks all convert', () => {
  const input = '::: mermaid\nA\n:::\ntext\n::: mermaid\nB\n:::';
  assert.equal(
    convertMermaid(input),
    '```mermaid\nA\n```\ntext\n```mermaid\nB\n```'
  );
});

test('decodeName decodes percent-encoded hyphen', () => {
  assert.equal(decodeName('API%2DReference'), 'API-Reference');
});

test('flatName decodes and strips folders and extension', () => {
  assert.equal(flatName('Services/Delta.md'), 'Delta.md');
  assert.equal(flatName('API%2DReference.md'), 'API-Reference.md');
});

test('pageSlug uses the decoded last segment with spaces as hyphens', () => {
  assert.equal(pageSlug('/Services/Delta'), 'Delta');
  assert.equal(pageSlug('/API%2DReference'), 'API-Reference');
});

test('buildSidebar renders top-level and nested entries', () => {
  const order = {
    '': ['Home', 'API%2DReference', 'Services', 'GUI'],
    Services: ['Delta', 'Tributary'],
  };
  const readOrder = (entry) => order[entry] || null;
  const expected =
    `- [Home](${WIKI}/Home)\n` +
    `- [API-Reference](${WIKI}/API-Reference)\n` +
    `- [Services](${WIKI}/Services)\n` +
    `  - [Delta](${WIKI}/Delta)\n` +
    `  - [Tributary](${WIKI}/Tributary)\n` +
    `- [GUI](${WIKI}/GUI)\n`;
  assert.equal(buildSidebar(readOrder), expected);
});
