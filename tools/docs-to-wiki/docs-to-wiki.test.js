'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { execFileSync, spawnSync } = require('child_process');
const {
  repositoryDocuments,
  unaccountedDocuments,
  generate,
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
    wiki: manifest.wiki,
    pageOf: (file) => pages[file],
    anchorsOf: (file) => anchors[file],
    imagesSeen: new Set(),
  });
}

/** A repository with one document, one exclusion and a manifest describing both, plus somewhere
 *  outside it to generate into. */
function repositoryWithManifest({ extraDocument } = {}) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'docs-to-wiki-'));
  execFileSync('git', ['init', '--quiet'], { cwd: root });
  fs.mkdirSync(path.join(root, 'docs', 'assets'), { recursive: true });
  fs.writeFileSync(path.join(root, 'docs', 'GUIDE.md'), '# Guide\n\n![a diagram](assets/diagram.svg)\n');
  fs.writeFileSync(path.join(root, 'docs', 'assets', 'diagram.svg'), '<svg/>\n');
  fs.writeFileSync(path.join(root, 'docs', 'NOTES.md'), '# Notes\n');
  if (extraDocument) fs.writeFileSync(path.join(root, 'docs', extraDocument), '# Extra\n');
  fs.writeFileSync(
    path.join(root, 'wiki-map.json'),
    JSON.stringify({
      wiki: {
        organisation: 'https://dev.azure.com/Somewhere',
        project: 'A Project',
        repository: 'A Repository',
        name: 'A-Wiki',
        removeUnlistedPages: false,
      },
      pages: [{ doc: 'docs/GUIDE.md', page: '/Guide' }],
      excluded: [{ doc: 'docs/NOTES.md', why: 'A working note.' }],
    }),
  );
  return { root, manifest: path.join(root, 'wiki-map.json'), output: `${root}-out` };
}

function discard({ root, output }) {
  fs.rmSync(root, { recursive: true, force: true });
  fs.rmSync(output, { recursive: true, force: true });
}

// The guard that stops the wiki drifting again: a document that is neither published nor
// deliberately withheld is a decision nobody made.
test('every repository document is either mapped to a page or explicitly excluded', () => {
  const unaccounted = unaccountedDocuments(REPO_ROOT, manifest);
  assert.deepEqual(unaccounted, [], `add these to wiki-map.json as a page or an exclusion: ${unaccounted}`);
});

test('the generator refuses to run on a document the manifest does not account for', () => {
  const repository = repositoryWithManifest({ extraDocument: 'UNDECIDED.md' });
  try {
    assert.throws(
      () => generate(repository.root, repository.manifest, repository.output),
      /docs\/UNDECIDED\.md/,
    );
  } finally {
    discard(repository);
  }
});

test('the banner and the file links name the repository the manifest declares', () => {
  const repository = repositoryWithManifest();
  try {
    generate(repository.root, repository.manifest, repository.output);
    const page = fs.readFileSync(path.join(repository.output, 'Guide.md'), 'utf8');
    assert.match(page, /in the\n> A Repository repository/);
    assert.match(page, /https:\/\/dev\.azure\.com\/Somewhere\/A Project\/_git\/A Repository\?path=\/docs\/GUIDE\.md/);
    assert.ok(fs.existsSync(path.join(repository.output, '.attachments', 'diagram.svg')), 'the image was not copied');
  } finally {
    discard(repository);
  }
});

// Left unchecked this publishes pages whose banner names the "undefined" repository, which reads
// like a bad page rather than like a manifest that is missing a line.
test('a manifest with no wiki block is refused, by the field it is missing', () => {
  const repository = repositoryWithManifest();
  try {
    const manifest = JSON.parse(fs.readFileSync(repository.manifest, 'utf8'));
    delete manifest.wiki.repository;
    fs.writeFileSync(repository.manifest, JSON.stringify(manifest));

    assert.throws(
      () => generate(repository.root, repository.manifest, repository.output),
      /no wiki\.repository/,
    );
  } finally {
    discard(repository);
  }
});

// What makes the manifest a guard rather than a suggestion is the build going red, and that is the
// exit code rather than the thrown error — a script that printed the problem and exited 0 would
// leave every pipeline using it green.
test('run as a command, an unaccounted document exits non-zero', () => {
  const repository = repositoryWithManifest({ extraDocument: 'UNDECIDED.md' });
  try {
    const run = spawnSync(process.execPath,
      [path.join(__dirname, 'docs-to-wiki.js'), repository.root, repository.manifest, repository.output],
      { encoding: 'utf8' });

    assert.equal(run.status, 1);
    assert.match(run.stderr, /docs\/UNDECIDED\.md/);
  } finally {
    discard(repository);
  }
});

test('run as a command with an argument missing, it says what it wants', () => {
  const run = spawnSync(process.execPath, [path.join(__dirname, 'docs-to-wiki.js'), '.'], { encoding: 'utf8' });

  assert.equal(run.status, 2);
  assert.match(run.stderr, /<repo-root> <manifest> <output-dir>/);
});

// The output directory is emptied first, so a wrong argument here is a deleted document rather than
// a failed run.
test('the generator refuses an output directory inside the repository', () => {
  const repository = repositoryWithManifest();
  try {
    for (const inside of [repository.root, path.join(repository.root, 'docs')]) {
      assert.throws(() => generate(repository.root, repository.manifest, inside), /inside the repository/);
    }
    assert.ok(fs.existsSync(path.join(repository.root, 'docs', 'GUIDE.md')), 'the document was deleted');
  } finally {
    discard(repository);
  }
});

test('a document git ignores is a working note, not a document the repository carries', () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'docs-to-wiki-'));
  try {
    execFileSync('git', ['init', '--quiet'], { cwd: root });
    fs.writeFileSync(path.join(root, '.gitignore'), '*handoff*.md\n');
    fs.mkdirSync(path.join(root, 'docs'));
    fs.writeFileSync(path.join(root, 'docs', 'GUIDE.md'), '# Guide\n');
    fs.writeFileSync(path.join(root, 'docs', 'a-handoff.md'), '# Handoff\n');
    fs.writeFileSync(path.join(root, 'README.md'), '# Readme\n');

    assert.deepEqual(repositoryDocuments(root).sort(), ['README.md', 'docs/GUIDE.md']);
  } finally {
    fs.rmSync(root, { recursive: true, force: true });
  }
});

// A handoff may carry a downstream product's vocabulary, so the rule that keeps it out of the
// repository has to hold on a clone whose filesystem tells upper and lower case apart. gitignore
// matches those patterns literally, and a developer on macOS never sees the difference.
test('a handoff is ignored however it is spelled', () => {
  const ignored = (name) =>
    spawnSync('git', ['-c', 'core.ignorecase=false', 'check-ignore', '-q', '--no-index', name], {
      cwd: REPO_ROOT,
    }).status === 0;

  for (const name of ['handoff.md', 'HANDOFF.md', 'Handoff.md', 'HandOff.md',
                      'docs/trellis-performance-handoff.md', 'Trellis Performance Handoff.md']) {
    assert.ok(ignored(name), `a case-sensitive checkout would leave ${name} tracked`);
  }
  for (const name of ['README.md', 'docs/TRELLIS.md', 'handoff.txt']) {
    assert.ok(!ignored(name), `${name} is ignored, and is not a handoff`);
  }
});

// A handoff is a working note between sessions: it is never committed, so it is in nobody's clone,
// so a document pointing at one sends its reader nowhere — and these documents are published to the
// wiki, where the dead link goes with them. Documentation is what the rule is about, so documentation
// is what this reads: a test naming a filename in an assertion is not a reader being sent anywhere.
test('no handoff is carried, and no document points at one', () => {
  const tracked = execFileSync('git', ['ls-files'], { cwd: REPO_ROOT, encoding: 'utf8' })
    .split('\n')
    .filter(Boolean);

  const carried = tracked.filter((f) => /handoff/i.test(path.basename(f)));
  assert.deepEqual(carried, [], `a handoff is never committed: ${carried}`);

  const pointing = [];
  for (const file of tracked.filter((f) => f.toLowerCase().endsWith('.md'))) {
    const named = [
      ...new Set(fs.readFileSync(path.join(REPO_ROOT, file), 'utf8').match(/[\w.\-/]*handoff[\w.\-]*\.md/gi) || []),
    ];
    if (named.length) pointing.push(`${file} names ${named.join(', ')}`);
  }
  assert.deepEqual(pointing, [], `nothing may link to a handoff — say what the reader needs, or drop the link: ${pointing}`);
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
    wiki: manifest.wiki,
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
    wiki: manifest.wiki,
    pageOf: () => undefined,
    anchorsOf: () => ({}),
    imagesSeen: new Set(),
  });
  assert.match(page, /^> \*\*Generated page\.\*\*/);
  assert.match(page, /docs\/A\.md/);
  assert.ok(page.endsWith('# Title\n'));
});
