#!/usr/bin/env node
'use strict';

// Convert an Azure DevOps project-wiki git checkout into GitHub-wiki content.
//
// The canonical wiki lives in the Azure DevOps project wiki. This tool transforms
// a clone of that wiki into the flat, GitHub-flavoured form expected by the GitHub
// wiki repo (ReGen-Villages/VillageOS-API.wiki), rewriting every DevOps-specific link to
// its GitHub target so visitors stay on GitHub.
//
// Usage: node sync-wiki.js <devops-wiki-dir> <output-dir>
//
// The pure transforms (convert, buildSidebar, flatName) are exported for unit tests;
// the file-walking CLI runs only when invoked directly.

const fs = require('fs');
const path = require('path');

const REPO = 'https://github.com/ReGen-Villages/VillageOS-API';
const WIKI = `${REPO}/wiki`;
// Branch that GitHub blob links point at. The mirror pushes main only, which is
// also the branch a documentation deep-link should be stable against.
const REPO_BRANCH = 'main';

// --- pure helpers ----------------------------------------------------------

// Azure DevOps project wikis percent-encode reserved characters in file names
// (e.g. a literal hyphen in "API-Reference" is stored as API%2DReference.md).
// GitHub addresses pages by the decoded name, so decode before writing.
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

  // 2. DevOps repo file links (?path=/...) -> GitHub blob links.
  out = out.replace(
    /https?:\/\/dev\.azure\.com\/ReGenVillages\/VillageOS-API\/_git\/VillageOS-API\?path=(\/[^)\s]+)/g,
    (_m, p) => `${REPO}/blob/${REPO_BRANCH}${p}`
  );

  // 3. DevOps wiki root (VillageOS%20API or VillageOS-API) -> GitHub wiki root.
  out = out.replace(
    /https?:\/\/dev\.azure\.com\/ReGenVillages\/VillageOS(?:%20|-)API\/_wiki(?:\/[^)\s]*)?/g,
    WIKI
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

// Build a GitHub wiki _Sidebar.md from the DevOps .order files. `readOrder` is a
// function (dir) => string[] | null returning the ordered base names in a folder
// (null if no .order). Nested folders are rendered as indented sub-items.
function buildSidebar(readOrder) {
  const lines = [];
  const top = readOrder('') || [];
  for (const entry of top) {
    const slug = decodeName(entry).replace(/ /g, '-');
    lines.push(`- [${slug}](${WIKI}/${slug})`);
    const children = readOrder(entry);
    if (children) {
      for (const child of children) {
        const childSlug = decodeName(child).replace(/ /g, '-');
        lines.push(`  - [${childSlug}](${WIKI}/${childSlug})`);
      }
    }
  }
  return lines.join('\n') + '\n';
}

// --- CLI -------------------------------------------------------------------

function listMarkdown(dir, base = dir) {
  const out = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === '.git') continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      out.push(...listMarkdown(full, base));
    } else if (entry.isFile() && entry.name.toLowerCase().endsWith('.md')) {
      out.push(path.relative(base, full));
    }
  }
  return out;
}

function main(argv) {
  const [srcDir, outDir] = argv;
  if (!srcDir || !outDir) {
    console.error('Usage: node sync-wiki.js <devops-wiki-dir> <output-dir>');
    process.exit(2);
  }

  fs.rmSync(outDir, { recursive: true, force: true });
  fs.mkdirSync(outDir, { recursive: true });

  const seen = new Map();
  for (const rel of listMarkdown(srcDir)) {
    const name = flatName(rel);
    if (seen.has(name)) {
      throw new Error(
        `Flatten collision: "${rel}" and "${seen.get(name)}" both map to "${name}". ` +
          'Rename one page in the Azure DevOps wiki.'
      );
    }
    seen.set(name, rel);
    const content = fs.readFileSync(path.join(srcDir, rel), 'utf8');
    fs.writeFileSync(path.join(outDir, name), convert(content));
  }

  const readOrder = (entry) => {
    const orderPath = path.join(srcDir, entry, '.order');
    if (!fs.existsSync(orderPath)) return null;
    return fs
      .readFileSync(orderPath, 'utf8')
      .split('\n')
      .map((l) => l.trim())
      .filter(Boolean);
  };
  fs.writeFileSync(path.join(outDir, '_Sidebar.md'), buildSidebar(readOrder));

  console.log(`Wrote ${seen.size} page(s) + _Sidebar.md to ${outDir}`);
}

if (require.main === module) {
  main(process.argv.slice(2));
}

module.exports = { convert, convertMermaid, buildSidebar, flatName, pageSlug, decodeName };
