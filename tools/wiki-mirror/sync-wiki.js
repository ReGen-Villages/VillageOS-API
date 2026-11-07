#!/usr/bin/env node
'use strict';

// Convert the pages tools/docs-to-wiki generates, which are written for an Azure DevOps project
// wiki, into the flat, GitHub-flavoured form the GitHub wiki repository
// (ReGen-Villages/VillageOS-API.wiki) expects, rewriting every DevOps-specific link to its GitHub
// target so visitors stay on GitHub.
//
// Usage: node sync-wiki.js <generated-dir> <manifest> <output-dir>
//
// The transforms (convert, buildSidebar, flatName) and the file walk (mirror) are exported for unit
// tests; the CLI runs only when invoked directly.

const fs = require('fs');
const path = require('path');

const REPO = 'https://github.com/ReGen-Villages/VillageOS-API';
const WIKI = `${REPO}/wiki`;
// Where a file committed to the GitHub wiki repository is served raw, with its own content type.
const RAW_WIKI = 'https://raw.githubusercontent.com/wiki/ReGen-Villages/VillageOS-API';
// The folder the wiki generator attaches every image to.
const ATTACHMENTS = '.attachments';
// The branch GitHub file links point at: the one the build publishes the wiki from, which the build
// file names in its "Publish the wiki to GitHub" step. A link against any other branch answers 404,
// and the test beside this reads the build file so the two cannot drift apart again.
const REPO_BRANCH = 'develop';

// The generator percent-encodes a literal hyphen in a page name, as an Azure DevOps wiki stores
// it (API-Reference is written as API%2DReference.md). GitHub addresses pages by the decoded
// name, so decode before writing.
function decodeName(name) {
  return decodeURIComponent(name);
}

// The GitHub page slug for a DevOps wiki path. Folders are flattened, so only the
// last segment matters; the slug is the decoded segment with spaces as hyphens
// (GitHub's own wiki slug convention).
function pageSlug(wikiPath) {
  const last = wikiPath.replace(/^\/+/, '').split('/').pop();
  return decodeName(last).replace(/ /g, '-');
}

// Flattened output filename for a source path relative to the wiki root.
function flatName(relPath) {
  const base = path.basename(relPath).replace(/\.md$/i, '');
  return `${decodeName(base)}.md`;
}

function convert(content) {
  let out = content;

  // 1. Strip links to private-only DevOps resources (Boards): keep the link text,
  //    drop the hyperlink. No public GitHub equivalent worth pointing visitors at.
  out = out.replace(
    /\[([^\]]+)\]\(https?:\/\/dev\.azure\.com\/[^)]*_boards[^)]*\)/g,
    '$1'
  );

  // 2. DevOps wiki root (VillageOS%20API or VillageOS-API) -> GitHub wiki root.
  out = out.replace(
    /https?:\/\/dev\.azure\.com\/ReGenVillages\/VillageOS(?:%20|-)API\/_wiki(?:\/[^)\s]*)?/g,
    WIKI
  );

  // 3. Attached images: [text](/.attachments/name) -> the raw file on the GitHub wiki repository.
  //    Left alone, GitHub resolves the absolute path against github.com itself.
  out = out.replace(
    /\]\((\/\.attachments\/[^)\s]+)\)/g,
    (_m, attachment) => `](${RAW_WIKI}${attachment})`
  );

  // 4. Internal wiki links: [text](/Page) or [text](/Folder/Page) -> absolute
  //    GitHub wiki URL. Folders are flattened, so only the last segment is used.
  out = out.replace(
    /\]\((\/[A-Za-z0-9%][^)\s]*)\)/g,
    (_m, p) => `](${WIKI}/${pageSlug(p)})`
  );

  // 5. Stray relative repo-doc links: [text](Foo.md) or [text](docs/Foo.md)
  //    -> GitHub blob link. A bare name is assumed to live under docs/.
  out = out.replace(
    /\]\((?!https?:\/\/|\/|#)([^)\s]+\.md)\)/g,
    (_m, p) => {
      const clean = p.replace(/^\.\//, '');
      const target = clean.includes('/') ? clean : `docs/${clean}`;
      return `](${REPO}/blob/${REPO_BRANCH}/${target})`;
    }
  );

  // 6. Mermaid: DevOps ":::​ mermaid ... :::" -> GitHub "```mermaid ... ```".
  out = convertMermaid(out);

  return out;
}

// Convert DevOps container-block mermaid fences to GitHub fenced code blocks.
// Opening "::: mermaid" becomes "```mermaid"; the matching closing ":::" becomes
// "```". Non-mermaid ":::" containers (none in this wiki) are left untouched.
function convertMermaid(content) {
  const lines = content.split('\n');
  const result = [];
  let inMermaid = false;
  for (const line of lines) {
    if (!inMermaid && /^:::\s*mermaid\s*$/.test(line)) {
      result.push('```mermaid');
      inMermaid = true;
    } else if (inMermaid && /^:::\s*$/.test(line)) {
      result.push('```');
      inMermaid = false;
    } else {
      result.push(line);
    }
  }
  return result.join('\n');
}

// A GitHub wiki _Sidebar.md listing the manifest's pages in the order they are declared, a page
// under a parent indented beneath it. Folders are flattened, so a page is linked by its last segment.
function buildSidebar(pagePaths) {
  const lines = pagePaths.map((pagePath) => {
    const segments = pagePath.replace(/^\/+/, '').split('/');
    const slug = pageSlug(pagePath);
    return `${'  '.repeat(segments.length - 1)}- [${slug}](${WIKI}/${slug})`;
  });
  return lines.join('\n') + '\n';
}

function pagePathsOf(manifestPath) {
  return JSON.parse(fs.readFileSync(manifestPath, 'utf8')).pages.map((entry) => entry.page);
}

function listMarkdown(dir, base = dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      out.push(...listMarkdown(full, base));
    } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) {
      out.push(path.relative(base, full));
    }
  }
  return out;
}

function mirror(srcDir, outDir, pagePaths) {
  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });

  const attachments = path.join(srcDir, ATTACHMENTS);
  if (fs.existsSync(attachments)) {
    fs.cpSync(attachments, path.join(outDir, ATTACHMENTS), { recursive: true });
  }

  const seen = new Map();
  for (const rel of listMarkdown(srcDir)) {
    const name = flatName(rel);
    if (seen.has(name)) {
      throw new Error(
        `Flatten collision: "${rel}" and "${seen.get(name)}" both map to "${name}". ` +
          'Rename one page in the manifest.'
      );
    }
    seen.set(name, rel);
    const content = fs.readFileSync(path.join(srcDir, rel), 'utf8');
    fs.writeFileSync(path.join(outDir, name), convert(content));
  }

  fs.writeFileSync(path.join(outDir, '_Sidebar.md'), buildSidebar(pagePaths));

  return seen.size;
}

function main(argv) {
  const [srcDir, manifestPath, outDir] = argv;
  if (!srcDir || !manifestPath || !outDir) {
    console.error('Usage: node sync-wiki.js <generated-dir> <manifest> <output-dir>');
    process.exit(2);
  }

  const pages = mirror(srcDir, outDir, pagePathsOf(manifestPath));
  console.log(`Wrote ${pages} page(s) + _Sidebar.md to ${outDir}`);
}

if (require.main === module) {
  main(process.argv.slice(2));
}

module.exports = { convert, convertMermaid, buildSidebar, flatName, pageSlug, decodeName, mirror, pagePathsOf, REPO_BRANCH };
