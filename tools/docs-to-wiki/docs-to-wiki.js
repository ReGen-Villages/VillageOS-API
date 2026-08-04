#!/usr/bin/env node
/**
 * Generates the Azure DevOps project wiki from the repository's own markdown.
 *
 * The repository is the source of truth: every page is produced from a file listed in
 * wiki-map.json, so a page cannot fall behind the document it documents. Pages written by
 * hand on the wiki are not preserved — see README.md for why that is the point.
 *
 * Usage: node docs-to-wiki.js <repo-root> <output-dir>
 */
const fs = require('fs');
const path = require('path');

const ATTACHMENTS = '.attachments';

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

/** The name a page contributes to its folder's .order file. */
function orderEntry(pagePath) {
  return pageFileName(pagePath).split('/').pop().replace(/\.md$/, '');
}

function repoFileUrl(repoRelativePath) {
  return `https://dev.azure.com/ReGenVillages/VillageOS-API/_git/VillageOS-API?path=/${repoRelativePath}`;
}

/**
 * Rewrites the links of one document for the wiki.
 * - a link to a mapped document becomes that document's wiki page (anchor translated)
 * - a link to any other repository file becomes a link to the file in the repository
 * - an in-page anchor is translated to the wiki's own heading slug
 * - an image becomes a wiki attachment
 */
function rewriteLinks(markdown, { docPath, pageOf, anchorsOf, imagesSeen }) {
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
    return `[${text}](${repoFileUrl(file)}${title ?? ''})`;
  });
}

function banner(docPath) {
  return `> **Generated page.** This page is built from [\`${docPath}\`](${repoFileUrl(docPath)}) in the\n> VillageOS-API repository. Edit that file — changes made here are overwritten by the next build.\n\n`;
}

function convertPage(markdown, options) {
  const body = rewriteLinks(convertMermaid(stripLintDirectives(markdown)), options);
  return banner(options.docPath) + body.replace(/\s*$/, '\n');
}

/** .order contents for every folder the pages occupy, parents before their children. */
function buildOrders(pages) {
  const orders = new Map();
  for (const { page } of pages) {
    const folder = path.posix.dirname(pageFileName(page));
    const key = folder === '.' ? '' : folder;
    if (!orders.has(key)) orders.set(key, []);
    orders.get(key).push(orderEntry(page));
  }
  return orders;
}

function generate(repoRoot, outputDirectory) {
  const manifest = JSON.parse(fs.readFileSync(path.join(__dirname, 'wiki-map.json'), 'utf8'));
  const byDoc = new Map(manifest.pages.map((entry) => [entry.doc, entry.page]));
  const anchors = new Map();
  const imagesSeen = new Set();

  const read = (doc) => fs.readFileSync(path.join(repoRoot, doc), 'utf8');
  const anchorsOf = (doc) => {
    if (!byDoc.has(doc)) return undefined;
    if (!anchors.has(doc)) anchors.set(doc, anchorMap(read(doc)));
    return anchors.get(doc);
  };

  fs.rmSync(outputDirectory, { recursive: true, force: true });
  for (const { doc, page } of manifest.pages) {
    const converted = convertPage(read(doc), {
      docPath: doc,
      pageOf: (file) => byDoc.get(file),
      anchorsOf,
      imagesSeen,
    });
    const destination = path.join(outputDirectory, pageFileName(page));
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.writeFileSync(destination, converted);
    console.log(`${doc} -> ${page}`);
  }

  for (const [folder, entries] of buildOrders(manifest.pages)) {
    fs.writeFileSync(path.join(outputDirectory, folder, '.order'), `${entries.join('\n')}\n`);
  }

  for (const image of imagesSeen) {
    const destination = path.join(outputDirectory, ATTACHMENTS, path.basename(image));
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.copyFileSync(path.join(repoRoot, image), destination);
  }
  console.log(`${manifest.pages.length} pages, ${imagesSeen.size} attachments -> ${outputDirectory}`);
}

module.exports = {
  convertMermaid,
  stripLintDirectives,
  githubSlug,
  devopsSlug,
  anchorMap,
  pageFileName,
  orderEntry,
  rewriteLinks,
  convertPage,
  buildOrders,
  banner,
};

if (require.main === module) {
  const [repoRoot, outputDirectory] = process.argv.slice(2);
  if (!repoRoot || !outputDirectory) {
    console.error('Usage: node docs-to-wiki.js <repo-root> <output-dir>');
    process.exit(2);
  }
  generate(repoRoot, outputDirectory);
}
