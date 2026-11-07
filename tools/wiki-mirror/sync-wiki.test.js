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
  pagePathsOf,
  REPO_BRANCH,
} = require('./sync-wiki');

const WIKI = 'https://github.com/ReGen-Villages/VillageOS-API/wiki';
const BLOB = 'https://github.com/ReGen-Villages/VillageOS-API/blob/develop';
const RAW_WIKI = 'https://raw.githubusercontent.com/wiki/ReGen-Villages/VillageOS-API';

const PIPELINE = fs.readFileSync(path.join(__dirname, '..', '..', 'azure-pipelines.yml'), 'utf8');
const PUBLISH_STEP = "displayName: 'Publish the wiki to GitHub'";
const CONVERSION_STEP = "displayName: 'Generate and convert the wiki pages, and prove the publishing token'";

// A step's display name sits near its end, so the step's text is found backwards from it.
function stepNamed(displayName) {
  const named = PIPELINE.indexOf(displayName);
  assert.ok(named > -1, `the build no longer has the step ${displayName}`);
  const start = PIPELINE.lastIndexOf('\n  - ', named);
  const next = PIPELINE.indexOf('\n  - ', named);
  return PIPELINE.slice(start, next > -1 ? next : undefined);
}

// The public wiki describes the branch the build publishes it from, and a file link naming any other
// branch can answer 404 — so the pipeline is read rather than remembered.
test('blob links name the branch the build publishes the wiki from', () => {
  const published = stepNamed(PUBLISH_STEP).match(/'refs\/heads\/([^']+)'/);
  assert.ok(published, 'the build no longer names the branch the wiki is published from');
  assert.equal(REPO_BRANCH, published[1]);
});

test('no build step reads or writes the Azure DevOps copy of the wiki', () => {
  assert.ok(!PIPELINE.includes('publish-wiki.js'), 'the build still publishes to the Azure DevOps wiki');
  assert.ok(!PIPELINE.includes('/_git/'), 'the build still clones a wiki from Azure DevOps');
  assert.ok(!PIPELINE.includes('AZURE_DEVOPS_PAT'), 'the build still carries the Azure DevOps token');
});

test('every build generates and converts the wiki before the publish step, which does neither', () => {
  const generated = PIPELINE.indexOf('node tools/docs-to-wiki/docs-to-wiki.js');
  const converted = PIPELINE.indexOf('node tools/wiki-mirror/sync-wiki.js');
  const published = PIPELINE.indexOf(PUBLISH_STEP);
  assert.ok(generated > -1 && converted > generated && published > converted);
  const publishStep = stepNamed(PUBLISH_STEP);
  assert.ok(!publishStep.includes('docs-to-wiki.js') && !publishStep.includes('sync-wiki.js'));
  const conversionStep = stepNamed(CONVERSION_STEP);
  assert.ok(conversionStep.includes('sync-wiki.js'));
  assert.ok(!conversionStep.includes("'refs/heads/"), 'the conversion runs on one branch only');
  assert.ok(!/ push(?! --dry-run)/.test(conversionStep), 'a validation build must not publish');
});

test('the page order comes from the manifest the build passes', () => {
  const pages = pagePathsOf(path.join(__dirname, '..', 'docs-to-wiki', 'wiki-map.json'));
  assert.equal(pages[0], '/Home');
  assert.ok(pages.every((page) => page.startsWith('/')));
});

test('internal wiki links become absolute GitHub wiki URLs', () => {
  assert.equal(convert('[vos.Trellis](/GUI)'), `[vos.Trellis](${WIKI}/GUI)`);
});

test('nested wiki links are flattened to the last segment', () => {
  assert.equal(convert('[Delta](/Services/Delta)'), `[Delta](${WIKI}/Delta)`);
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

test('the mirror copies the attachments folder beside the pages and writes the sidebar from the page order', () => {
  const source = fs.mkdtempSync(path.join(os.tmpdir(), 'wiki-generated-'));
  const output = fs.mkdtempSync(path.join(os.tmpdir(), 'github-wiki-'));
  fs.writeFileSync(path.join(source, 'Field Guide.md'), '![x](/.attachments/a.svg)');
  fs.mkdirSync(path.join(source, 'Services'));
  fs.writeFileSync(path.join(source, 'Services', 'Delta.md'), 'Delta');
  fs.mkdirSync(path.join(source, '.attachments'));
  fs.writeFileSync(path.join(source, '.attachments', 'a.svg'), '<svg/>');

  mirror(source, output, ['/Field Guide', '/Services/Delta']);

  assert.equal(fs.readFileSync(path.join(output, '.attachments', 'a.svg'), 'utf8'), '<svg/>');
  assert.equal(
    fs.readFileSync(path.join(output, 'Field Guide.md'), 'utf8'),
    `![x](${RAW_WIKI}/.attachments/a.svg)`
  );
  assert.equal(fs.readFileSync(path.join(output, 'Delta.md'), 'utf8'), 'Delta');
  assert.equal(
    fs.readFileSync(path.join(output, '_Sidebar.md'), 'utf8'),
    `- [Field-Guide](${WIKI}/Field-Guide)\n  - [Delta](${WIKI}/Delta)\n`
  );
});

// The pipeline step, not this script, carries the output into the GitHub wiki clone.
test('the pipeline carries the whole converted output into the GitHub wiki clone', () => {
  const step = stepNamed(PUBLISH_STEP);
  assert.ok(step.includes('cp -R "$OUT"/. "$DEST"/'), 'the publish step copies only part of the output');
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

test('buildSidebar renders the manifest pages in order, a child indented under its parent', () => {
  const pages = ['/Home', '/API-Reference', '/Services', '/Services/Delta', '/Services/Tributary', '/GUI'];
  const expected =
    `- [Home](${WIKI}/Home)\n` +
    `- [API-Reference](${WIKI}/API-Reference)\n` +
    `- [Services](${WIKI}/Services)\n` +
    `  - [Delta](${WIKI}/Delta)\n` +
    `  - [Tributary](${WIKI}/Tributary)\n` +
    `- [GUI](${WIKI}/GUI)\n`;
  assert.equal(buildSidebar(pages), expected);
});

test('run as a command with an argument missing, it says what it wants', () => {
  const run = require('node:child_process').spawnSync('node', [path.join(__dirname, 'sync-wiki.js'), 'only-one'], {
    encoding: 'utf8',
  });
  assert.equal(run.status, 2);
  assert.match(run.stderr, /Usage: node sync-wiki\.js <generated-dir> <manifest> <output-dir>/);
});
