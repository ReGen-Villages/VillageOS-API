#!/usr/bin/env node
/**
 * Generates an Azure DevOps project wiki from a repository's own markdown.
 *
 * The repository is the source of truth: every page is produced from a file listed in the
 * manifest, so a page cannot fall behind the document it documents.
 *
 * The manifest names the wiki it publishes to, so a repository that keeps its own documentation
 * drives this tool with its own manifest rather than carrying a copy of it.
 *
 * Usage: node docs-to-wiki.js <repo-root> <manifest> <output-dir>
 */
const fs = require('fs');
const path = require('path');
const { spawnSync } = require('child_process');

const ATTACHMENTS = '.attachments';

/** The markdown a clone of the repository would contain. A file git ignores is a working note
 *  somebody keeps locally, so the wiki manifest is never expected to account for it. */
function repositoryDocuments(root) {
  const documents = [
    ...fs.readdirSync(path.join(root, 'docs')).filter((f) => f.endsWith('.md')).map((f) => `docs/${f}`),
    ...fs.readdirSync(root).filter((f) => f.endsWith('.md')),
  ];
  const check = spawnSync('git', ['check-ignore', '--stdin'], {
    cwd: root,
    input: documents.join('\n'),
    encoding: 'utf8',
  });
  // Nothing ignored is the safe answer when git cannot be asked: the manifest then has to account
  // for every file, which fails loudly, where dropping them all would pass in silence.
  const ignored = new Set((check.stdout || '').split('\n').filter(Boolean));
  return documents.filter((doc) => !ignored.has(doc));
}

/** DevOps renders diagrams from a `::: mermaid` block; the repository uses a fenced block. */
function convertMermaid(markdown) {
  return markdown.replace(/^```mermaid[ \t]*\r?\n([\s\S]*?)^```[ \t]*$/gm, (_all, body) =>
    `::: mermaid\n${body}:::`,
  );
}

function stripLintDirectives(markdown) {
  return markdown.replace(/^<!--\s*markdownlint-[\s\S]*?-->\r?\n/gm, '');
}

/** Heading text as the repository's own tables of contents slug it (GitHub rules). Each space
 *  becomes its own hyphen, so a removed character such as an em dash leaves a double hyphen. */
function githubSlug(heading) {
  return headingText(heading)
    .toLowerCase()
    .replace(/[^\w\s-]/g, '')
    .trim()
    .replace(/ /g, '-');
}

/** Heading text as the DevOps wiki slugs it: punctuation such as `.` and `—` survives,
 *  brackets do not. Verified against the anchors the hand-written wiki pages used. */
function devopsSlug(heading) {
  return headingText(heading)
    .toLowerCase()
    .replace(/[()[\]{}<>|\\/"'`*_!?,:;]/g, '')
    .trim()
    .replace(/ /g, '-');
}

/** Strip the markdown a heading may carry so both slugs see the same words. */
function headingText(heading) {
  return heading
    .replace(/`/g, '')
    .replace(/\*\*?/g, '')
    .replace(/\[([^\]]*)\]\([^)]*\)/g, '$1')
    .trim();
}

/** Every heading in a document, as { githubAnchor: devopsAnchor }. Fenced code is skipped so a
 *  comment inside a shell example never registers as a heading. */
function anchorMap(markdown) {
  const map = {};
  let inFence = false;
  for (const line of markdown.split(/\r?\n/)) {
    if (/^\s*```/.test(line)) inFence = !inFence;
    if (inFence) continue;
    const heading = /^#{1,6}\s+(.*?)\s*$/.exec(line);
    if (heading) map[githubSlug(heading[1])] = devopsSlug(heading[1]);
  }
  return map;
}

/** The file a wiki page path lives in: spaces become hyphens, and a hyphen the page name
 *  really contains is percent-encoded so the two cannot be confused. */
function pageFileName(pagePath) {
  return `${pagePath
    .replace(/^\//, '')
    .split('/')
    .map((segment) => segment.replace(/-/g, '%2D').replace(/ /g, '-'))
    .join('/')}.md`;
}

function repoFileUrl(wiki, repoRelativePath) {
  return `${wiki.organisation}/${wiki.project}/_git/${wiki.repository}?path=/${repoRelativePath}`;
}

/** A document nobody decided about is how a wiki starts falling behind, so the generator refuses to
 *  run on one — the guard for a caller with no test suite of its own. */
function unaccountedDocuments(repoRoot, manifest) {
  const accounted = new Set([
    ...manifest.pages.map((entry) => entry.doc),
    ...manifest.excluded.map((entry) => entry.doc),
  ]);
  return repositoryDocuments(repoRoot).filter((doc) => !accounted.has(doc));
}

/**
 * Rewrites the links of one document for the wiki.
 * - a link to a mapped document becomes that document's wiki page (anchor translated)
 * - a link to any other repository file becomes a link to the file in the repository
 * - an in-page anchor is translated to the wiki's own heading slug
 * - an image becomes a wiki attachment
 */
function rewriteLinks(markdown, { docPath, wiki, pageOf, anchorsOf, imagesSeen }) {
  const docDirectory = path.posix.dirname(docPath);
  const ownAnchors = anchorsOf(docPath) ?? {};

  return markdown.replace(/(!?)\[([^\]]*)\]\(([^)\s]+)(\s+"[^"]*")?\)/g, (all, bang, text, target, title) => {
    if (/^(https?:|mailto:|#)/.test(target) === false && bang === '!') {
      const file = path.posix.normalize(path.posix.join(docDirectory, target));
      imagesSeen.add(file);
      return `![${text}](/${ATTACHMENTS}/${path.posix.basename(file)})`;
    }
    if (target.startsWith('#')) {
      const translated = ownAnchors[target.slice(1)];
      return translated ? `[${text}](#${translated})` : all;
    }
    if (/^(https?:|mailto:)/.test(target)) return all;

    const [targetPath, anchor] = target.split('#');
    const file = path.posix.normalize(path.posix.join(docDirectory, targetPath));
    const page = pageOf(file);
    if (page) {
      const translated = anchor ? (anchorsOf(file) ?? {})[anchor] : undefined;
      // A doc index labels its links with file names; on the wiki those read as page names.
      const label = text === path.posix.basename(targetPath) || text === targetPath
        ? page.split('/').pop()
        : text;
      return `[${label}](${page}${translated ? `#${translated}` : ''})`;
    }
    return `[${text}](${repoFileUrl(wiki, file)}${title ?? ''})`;
  });
}

function banner(wiki, docPath) {
  return `> **Generated page.** This page is built from [\`${docPath}\`](${repoFileUrl(wiki, docPath)}) in the\n> ${wiki.repository} repository. Edit that file — changes made here are overwritten by the next build.\n\n`;
}

function convertPage(markdown, options) {
  const body = rewriteLinks(convertMermaid(stripLintDirectives(markdown)), options);
  return banner(options.wiki, options.docPath) + body.replace(/\s*$/, '\n');
}

function generate(repoRoot, manifestPath, outputDirectory) {
  // The output directory is emptied before anything is written, so naming one inside the
  // repository deletes source files. A caller that passed its arguments in the wrong order once
  // named a tracked manifest as the output and the run removed it.
  const output = path.resolve(outputDirectory);
  const root = path.resolve(repoRoot);
  if (output === root || output.startsWith(root + path.sep)) {
    throw new Error(`the output directory is inside the repository and would be emptied: ${output}`);
  }

  const manifest = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
  // Without this the run succeeds and publishes pages whose banner names the "undefined"
  // repository, which is harder to recognise than a manifest that would not load.
  for (const field of ['organisation', 'project', 'repository', 'name']) {
    if (!manifest.wiki?.[field]) throw new Error(`${manifestPath} has no wiki.${field}`);
  }

  const unaccounted = unaccountedDocuments(repoRoot, manifest);
  if (unaccounted.length) {
    throw new Error(`add to ${manifestPath} as a page or an exclusion: ${unaccounted.join(', ')}`);
  }
  const byDoc = new Map(manifest.pages.map((entry) => [entry.doc, entry.page]));
  const anchors = new Map();
  const imagesSeen = new Set();

  const read = (doc) => fs.readFileSync(path.join(repoRoot, doc), 'utf8');
  const anchorsOf = (doc) => {
    if (!byDoc.has(doc)) return undefined;
    if (!anchors.has(doc)) anchors.set(doc, anchorMap(read(doc)));
    return anchors.get(doc);
  };

  fs.rmSync(output, { recursive: true, force: true });
  for (const { doc, page } of manifest.pages) {
    const converted = convertPage(read(doc), {
      docPath: doc,
      wiki: manifest.wiki,
      pageOf: (file) => byDoc.get(file),
      anchorsOf,
      imagesSeen,
    });
    const destination = path.join(output, pageFileName(page));
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, converted);
    console.log(`${doc} -> ${page}`);
  }

  for (const image of imagesSeen) {
    const destination = path.join(output, ATTACHMENTS, path.basename(image));
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.copyFileSync(path.join(repoRoot, image), destination);
  }
  console.log(`${manifest.pages.length} pages, ${imagesSeen.size} attachments -> ${output}`);
}

module.exports = {
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
  banner,
};

if (require.main === module) {
  const [repoRoot, manifestPath, outputDirectory] = process.argv.slice(2);
  if (!repoRoot || !manifestPath || !outputDirectory) {
    console.error('Usage: node docs-to-wiki.js <repo-root> <manifest> <output-dir>');
    process.exit(2);
  }
  try {
    generate(repoRoot, manifestPath, outputDirectory);
  } catch (error) {
    console.error(error.message);
    process.exit(1);
  }
}
