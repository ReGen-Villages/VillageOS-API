'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const {
  convert,
  convertMermaid,
  buildSidebar,
  flatName,
  pageSlug,
  decodeName,
  mirror,
  REPO_BRANCH,
} = require('./sync-wiki');

const WIKI = 'https://github.com/ReGen-Villages/VillageOS-API/wiki';
const BLOB = 'https://github.com/ReGen-Villages/VillageOS-API/blob/develop';
const RAW_WIKI = 'https://raw.githubusercontent.com/wiki/ReGen-Villages/VillageOS-API';

// A file link on the public wiki answers 404 unless it names the branch the repository mirror pushes,
// and nothing but the pipeline says which branch that is — so the pipeline is read rather than remembered.
test('blob links name the branch the pipeline mirrors to GitHub', () => {
  const pipeline = fs.readFileSync(path.join(__dirname, '..', '..', 'azure-pipelines.yml'), 'utf8');
  const pushed = pipeline.match(/git push github "HEAD:refs\/heads\/([^"]+)"/);
  assert.ok(pushed, 'the pipeline no longer pushes the repository to GitHub by a fully-qualified ref');
  assert.equal(REPO_BRANCH, pushed[1]);
});

test('internal wiki links become absolute GitHub wiki URLs', () => {
  assert.equal(convert('[vos.Trellis](/GUI)'), `[vos.Trellis](${WIKI}/GUI)`);
});

test('nested wiki links are flattened to the last segment', () => {
  assert.equal(convert('[Delta](/Services/Delta)'), `[Delta](${WIKI}/Delta)`);
});

test('DevOps repo file links become GitHub blob links', () => {
  const input =
    '[docs](https://dev.azure.com/ReGenVillages/VillageOS-API/_git/VillageOS-API?path=/docs/SERVICES.md)';
  assert.equal(convert(input), `[docs](${BLOB}/docs/SERVICES.md)`);
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

// The wiki generator attaches every image and links it by an absolute wiki path, which GitHub would
// resolve against github.com itself. A diagram is fetched raw from the wiki repository the mirror pushes.
test('an attachment image link points at the raw file on the GitHub wiki repository', () => {
  assert.equal(
    convert('![A kind and a member](/.attachments/field-guide-three-ideas.svg)'),
    `![A kind and a member](${RAW_WIKI}/.attachments/field-guide-three-ideas.svg)`
  );
});

test('a screenshot attachment is rewritten the same way as a diagram', () => {
  assert.equal(
    convert('![The Dashboard](/.attachments/trellis-dashboard.png)'),
    `![The Dashboard](${RAW_WIKI}/.attachments/trellis-dashboard.png)`
  );
});

test('an attachment link is not mistaken for a wiki page', () => {
  const output = convert('![x](/.attachments/a.svg) and [GUI](/GUI)');
  assert.equal(output, `![x](${RAW_WIKI}/.attachments/a.svg) and [GUI](${WIKI}/GUI)`);
});

test('the mirror copies the attachments folder beside the pages', () => {
  const source = fs.mkdtempSync(path.join(os.tmpdir(), 'devops-wiki-'));
  const output = fs.mkdtempSync(path.join(os.tmpdir(), 'github-wiki-'));
  fs.writeFileSync(path.join(source, 'Field Guide.md'), '![x](/.attachments/a.svg)');
  fs.mkdirSync(path.join(source, '.attachments'));
  fs.writeFileSync(path.join(source, '.attachments', 'a.svg'), '<svg/>');
  fs.mkdirSync(path.join(source, '.git'));
  fs.writeFileSync(path.join(source, '.git', 'HEAD'), 'ref: refs/heads/wikiMaster');

  mirror(source, output);

  assert.equal(fs.readFileSync(path.join(output, '.attachments', 'a.svg'), 'utf8'), '<svg/>');
  assert.equal(
    fs.readFileSync(path.join(output, 'Field Guide.md'), 'utf8'),
    `![x](${RAW_WIKI}/.attachments/a.svg)`
  );
  assert.ok(!fs.existsSync(path.join(output, '.git')));
});

// The pipeline step, not this script, carries the output into the GitHub wiki clone. It once copied
// the pages alone, and the attachments the script wrote went nowhere.
test('the pipeline carries the whole mirror output into the GitHub wiki clone', () => {
  const pipeline = fs.readFileSync(path.join(__dirname, '..', '..', 'azure-pipelines.yml'), 'utf8');
  const step = pipeline.slice(pipeline.indexOf('node tools/wiki-mirror/sync-wiki.js'));
  assert.ok(step.includes('cp -R "$OUT"/. "$DEST"/'), 'the mirror step copies only part of the output');
  assert.ok(step.includes('rm -rf "$DEST"/.attachments'), 'a removed or renamed attachment would linger');
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
