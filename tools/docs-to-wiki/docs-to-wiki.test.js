'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const {
  convertMermaid,
  stripLintDirectives,
  githubSlug,
  devopsSlug,
  anchorMap,
  pageFileName,
  rewriteLinks,
  convertPage,
} = require('./docs-to-wiki');

const REPO_ROOT = path.join(__dirname, '..', '..');
const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, 'wiki-map.json'), 'utf8'));

function rewrite(markdown, docPath = 'docs/A.md') {
  const pages = { 'docs/A.md': '/A', 'docs/B.md': '/Services/B', 'README.md': '/Project Guide' };
  const anchors = {
    'docs/A.md': { 'part-1--user-guide': 'part-1-—-user-guide' },
    'docs/B.md': { '74-pipelines-dag-editor': '7.4-pipelines-dag-editor' },
  };
  return rewriteLinks(markdown, {
    docPath,
    pageOf: (file) => pages[file],
    anchorsOf: (file) => anchors[file],
    imagesSeen: new Set(),
  });
}

// The guard that stops the wiki drifting again: a document that is neither published nor
// deliberately withheld is a decision nobody made.
test('every repository document is either mapped to a page or explicitly excluded', () => {
  const accounted = new Set([
    ...manifest.pages.map((entry) => entry.doc),
    ...manifest.excluded.map((entry) => entry.doc),
  ]);
  const documents = [
    ...fs.readdirSync(path.join(REPO_ROOT, 'docs')).filter((f) => f.endsWith('.md')).map((f) => `docs/${f}`),
    ...fs.readdirSync(REPO_ROOT).filter((f) => f.endsWith('.md')),
  ];
  const unaccounted = documents.filter((doc) => !accounted.has(doc));
  assert.deepEqual(unaccounted, [], `add these to wiki-map.json as a page or an exclusion: ${unaccounted}`);
});

test('every mapped document exists and every page path is unique', () => {
  const seen = new Set();
  for (const { doc, page } of manifest.pages) {
    assert.ok(fs.existsSync(path.join(REPO_ROOT, doc)), `${doc} is mapped but missing`);
    assert.ok(!seen.has(page), `${page} is claimed twice`);
    seen.add(page);
  }
});

test('a diagram fence becomes the wiki diagram block', () => {
  assert.equal(convertMermaid('```mermaid\ngraph TD;\nA-->B;\n```'), '::: mermaid\ngraph TD;\nA-->B;\n:::');
});

test('an ordinary code fence is left alone', () => {
  const code = '```bash\nnpm run build\n```';
  assert.equal(convertMermaid(code), code);
});

test('markdownlint directives are stripped', () => {
  assert.equal(stripLintDirectives('<!-- markdownlint-disable-file MD025 -->\n# Title'), '# Title');
});

test('heading slugs differ between the repository and the wiki', () => {
  assert.equal(githubSlug('1. Getting Started'), '1-getting-started');
  assert.equal(devopsSlug('1. Getting Started'), '1.-getting-started');
  assert.equal(githubSlug('Part 1 — User Guide'), 'part-1--user-guide');
  assert.equal(devopsSlug('Part 1 — User Guide'), 'part-1-—-user-guide');
  assert.equal(devopsSlug('7.4 Pipelines (DAG editor)'), '7.4-pipelines-dag-editor');
});

test('heading formatting does not leak into an anchor', () => {
  assert.equal(devopsSlug('The `is` predicate'), 'the-is-predicate');
  assert.equal(devopsSlug('**Bold** heading'), 'bold-heading');
});

test('anchors are collected from headings but not from fenced code', () => {
  const map = anchorMap('# Real Heading\n\n```bash\n# not a heading\n```\n\n## 1. Getting Started\n');
  assert.deepEqual(map, { 'real-heading': 'real-heading', '1-getting-started': '1.-getting-started' });
});

test('a link to a mapped document becomes a wiki page link', () => {
  assert.equal(rewrite('[B](B.md)'), '[B](/Services/B)');
  assert.equal(rewrite('[readme](../README.md)'), '[readme](/Project Guide)');
});

// A documentation index labels its links with file names, which read as noise on a wiki.
test('a link labelled with the file name is relabelled with the page name', () => {
  assert.equal(rewrite('[B.md](B.md)'), '[B](/Services/B)');
  assert.equal(rewrite('[../README.md](../README.md)'), '[Project Guide](/Project Guide)');
});

test('a link with real wording keeps its own text', () => {
  assert.equal(rewrite('[the B service](B.md)'), '[the B service](/Services/B)');
});

test('a link carrying an anchor is translated to the target page anchor', () => {
  assert.equal(rewrite('[Pipelines](B.md#74-pipelines-dag-editor)'), '[Pipelines](/Services/B#7.4-pipelines-dag-editor)');
});

test('an in-page anchor is translated to the wiki heading slug', () => {
  assert.equal(rewrite('[Part 1](#part-1--user-guide)'), '[Part 1](#part-1-—-user-guide)');
});

test('an anchor with no matching heading is left untouched rather than guessed at', () => {
  assert.equal(rewrite('[gone](#no-such-heading)'), '[gone](#no-such-heading)');
});

test('a link to an unmapped repository file points at the file in the repository', () => {
  assert.match(rewrite('[roadmap](SERVICE_HOST_ROADMAP.md)'), /_git\/VillageOS-API\?path=\/docs\/SERVICE_HOST_ROADMAP\.md\)$/);
});

test('external links are left alone', () => {
  const link = '[spec](https://example.com/a.md)';
  assert.equal(rewrite(link), link);
});

test('an image becomes a wiki attachment and is collected for copying', () => {
  const imagesSeen = new Set();
  const out = rewriteLinks('![a diagram](assets/a-diagram.png)', {
    docPath: 'docs/A_DOCUMENT.md',
    pageOf: () => undefined,
    anchorsOf: () => ({}),
    imagesSeen,
  });
  assert.equal(out, '![a diagram](/.attachments/a-diagram.png)');
  assert.deepEqual([...imagesSeen], ['docs/assets/a-diagram.png']);
});

test('a page path becomes the file name the wiki expects', () => {
  assert.equal(pageFileName('/Home'), 'Home.md');
  assert.equal(pageFileName('/Project Guide'), 'Project-Guide.md');
  assert.equal(pageFileName('/Services/Relationship Services'), 'Services/Relationship-Services.md');
  // A hyphen the name really contains is encoded, so it survives the round trip to a page name.
  assert.equal(pageFileName('/API-Reference'), 'API%2DReference.md');
});

test('a generated page names its source so an editor knows edits are overwritten', () => {
  const page = convertPage('# Title\n', {
    docPath: 'docs/A.md',
    pageOf: () => undefined,
    anchorsOf: () => ({}),
    imagesSeen: new Set(),
  });
  assert.match(page, /^> \*\*Generated page\.\*\*/);
  assert.match(page, /docs\/A\.md/);
  assert.ok(page.endsWith('# Title\n'));
});
