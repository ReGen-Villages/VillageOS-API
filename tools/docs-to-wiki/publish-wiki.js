#!/usr/bin/env node
/**
 * Publishes a generated wiki tree through the wiki REST API.
 *
 * The API rather than git on purpose. A project wiki is a git repository, so pushing to it needs
 * a token carrying the Code scope — which on a classic personal access token means write access
 * to every repository in the organisation, to publish one wiki. These calls need only the Wiki
 * scope, which grants exactly what its name says.
 *
 * The trade: page order lives in .order files that only the git path can write, so order is
 * whatever the service assigns. Content, images and removals are unaffected.
 *
 * Usage: node publish-wiki.js <generated-dir>   (AZURE_DEVOPS_PAT in the environment)
 */
const fs = require('fs');
const path = require('path');

const ORGANISATION = 'https://dev.azure.com/ReGenVillages';
const PROJECT = 'VillageOS-API';
const WIKI = 'VillageOS-API-Wiki';
const API_VERSION = 'api-version=7.1-preview.1';
const ATTACHMENTS = '.attachments';

/** File name back to page path — the inverse of the generator's pageFileName: a hyphen is a
 *  space, and %2D is a hyphen the page name really contains. */
function pagePathOf(relativeFile) {
  return '/' + relativeFile
    .replace(/\.md$/, '')
    .split(path.sep)
    .map((segment) => segment.replace(/-/g, ' ').replace(/%2D/g, '-'))
    .join('/');
}

/** Parents first, so a subpage is never written before the page above it exists. */
function parentsFirst(pagePaths) {
  return [...pagePaths].sort((a, b) => a.split('/').length - b.split('/').length || a.localeCompare(b));
}

/** Pages the wiki holds that no document produces, deepest first so a parent is never removed
 *  while a child still hangs off it. */
function pagesToRemove(existing, generated) {
  const keep = new Set(generated);
  return existing
    .filter((page) => page !== '/' && !keep.has(page))
    .sort((a, b) => b.split('/').length - a.split('/').length || a.localeCompare(b));
}

function markdownFiles(directory, base = directory) {
  const found = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      if (entry.name !== ATTACHMENTS) found.push(...markdownFiles(full, base));
    } else if (entry.name.endsWith('.md')) {
      found.push(path.relative(base, full));
    }
  }
  return found;
}

function flattenPages(page, into = []) {
  into.push(page.path || '/');
  for (const child of page.subPages ?? []) flattenPages(child, into);
  return into;
}

async function main() {
  const [generatedDirectory] = process.argv.slice(2);
  const token = process.env.AZURE_DEVOPS_PAT;
  if (!generatedDirectory || !token) {
    console.error('Usage: node publish-wiki.js <generated-dir> (needs AZURE_DEVOPS_PAT)');
    process.exit(2);
  }

  const authorization = `Basic ${Buffer.from(`:${token}`).toString('base64')}`;
  const wikiUrl = `${ORGANISATION}/${PROJECT}/_apis/wiki/wikis/${WIKI}`;
  const pageUrl = (page) => `${wikiUrl}/pages?path=${encodeURIComponent(page)}&${API_VERSION}`;

  const send = async (url, options = {}) => {
    const response = await fetch(url, { ...options, headers: { Authorization: authorization, ...options.headers } });
    if (!response.ok) {
      throw new Error(`${options.method ?? 'GET'} ${url.split('?')[0]} -> ${response.status} ${await response.text()}`);
    }
    return response;
  };

  const attachmentDirectory = path.join(generatedDirectory, ATTACHMENTS);
  if (fs.existsSync(attachmentDirectory)) {
    for (const name of fs.readdirSync(attachmentDirectory)) {
      // An attachment that is already there answers 409. That is the desired state, not a failure.
      const response = await fetch(`${wikiUrl}/attachments?name=${encodeURIComponent(name)}&${API_VERSION}`, {
        method: 'PUT',
        headers: { Authorization: authorization, 'Content-Type': 'application/octet-stream' },
        body: fs.readFileSync(path.join(attachmentDirectory, name)),
      });
      if (!response.ok && response.status !== 409) {
        throw new Error(`attachment ${name} -> ${response.status} ${await response.text()}`);
      }
      console.log(`attachment ${name}${response.status === 409 ? ' (already present)' : ''}`);
    }
  }

  const generated = new Map(
    markdownFiles(generatedDirectory).map((file) => [pagePathOf(file), path.join(generatedDirectory, file)]),
  );
  const existing = new Set(flattenPages(await (await send(`${wikiUrl}/pages?path=/&recursionLevel=full&${API_VERSION}`)).json()));

  let written = 0;
  let unchanged = 0;
  for (const page of parentsFirst([...generated.keys()])) {
    const content = fs.readFileSync(generated.get(page), 'utf8');
    const headers = { 'Content-Type': 'application/json' };
    if (existing.has(page)) {
      const current = await send(`${pageUrl(page)}&includeContent=true`);
      // Skip an unchanged page so a build that touches no documentation adds no revisions.
      if ((await current.json()).content === content) {
        unchanged++;
        continue;
      }
      headers['If-Match'] = current.headers.get('etag');
    }
    await send(pageUrl(page), { method: 'PUT', headers, body: JSON.stringify({ content }) });
    console.log(`${existing.has(page) ? 'updated' : 'created'} ${page}`);
    written++;
  }

  for (const page of pagesToRemove([...existing], [...generated.keys()])) {
    await send(pageUrl(page), { method: 'DELETE' });
    console.log(`removed ${page} (no document produces it)`);
  }

  console.log(`${written} written, ${unchanged} already current, ${generated.size} pages total`);
}

module.exports = { pagePathOf, parentsFirst, pagesToRemove, flattenPages, markdownFiles };

if (require.main === module) {
  main().catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
}
