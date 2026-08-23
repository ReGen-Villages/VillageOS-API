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
 * Usage: node publish-wiki.js <manifest> <generated-dir>   (AZURE_DEVOPS_PAT in the environment)
 */
const fs = require('fs');
const path = require('path');

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
 *  while a child still hangs off it.
 *
 *  A wiki that is entirely generated removes them. One that also holds pages written on the wiki
 *  and nowhere else removes nothing: there, an unlisted page is somebody's work rather than a
 *  leftover, and the manifest cannot tell the two apart. */
function pagesToRemove(existing, generated, removeUnlistedPages) {
  if (!removeUnlistedPages) return [];
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

/** The attachments API takes the content base64-encoded. A raw body is rejected with "The input
 *  is not a valid Base-64 string", which reads like a problem with the file rather than with how
 *  it was sent — so this stays a named step rather than an inline call nobody questions. */
function attachmentBody(bytes) {
  return bytes.toString('base64');
}

/**
 * Whether an attachment upload was refused only because that name is already on the wiki.
 *
 * Every run uploads every attachment, so this is the ordinary answer once the first run has been.
 * The wiki does not report it as a conflict: it answers 500 with a WikiCreateAttachmentFailedException
 * whose message says the path already exists. Both are read here, so whichever the wiki chooses,
 * the run treats an attachment that is already there as the desired state.
 *
 * The API creates, it does not replace — re-running with different bytes under a name that exists
 * cannot update it. Changing an image means giving it a new file name.
 */
function isAlreadyAttached(status, body) {
  if (status === 409) return true;
  return status === 500 && /already exists/i.test(body) && /WikiCreateAttachmentFailedException/.test(body);
}

function flattenPages(page, into = []) {
  into.push(page.path || '/');
  for (const child of page.subPages ?? []) flattenPages(child, into);
  return into;
}

async function main() {
  const [manifestPath, generatedDirectory] = process.argv.slice(2);
  const token = process.env.AZURE_DEVOPS_PAT;
  if (!manifestPath || !generatedDirectory || !token) {
    console.error('Usage: node publish-wiki.js <manifest> <generated-dir> (needs AZURE_DEVOPS_PAT)');
    process.exit(2);
  }

  const wiki = JSON.parse(fs.readFileSync(manifestPath, 'utf8')).wiki;
  const authorization = `Basic ${Buffer.from(`:${token}`).toString('base64')}`;
  const wikiUrl = `${wiki.organisation}/${wiki.project}/_apis/wiki/wikis/${wiki.name}`;
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
      const response = await fetch(`${wikiUrl}/attachments?name=${encodeURIComponent(name)}&${API_VERSION}`, {
        method: 'PUT',
        headers: { Authorization: authorization, 'Content-Type': 'application/octet-stream' },
        body: attachmentBody(fs.readFileSync(path.join(attachmentDirectory, name))),
      });
      const failure = response.ok ? '' : await response.text();
      const alreadyThere = !response.ok && isAlreadyAttached(response.status, failure);
      if (!response.ok && !alreadyThere) {
        throw new Error(`attachment ${name} -> ${response.status} ${failure}`);
      }
      console.log(`attachment ${name}${alreadyThere ? ' (already there)' : ''}`);
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

  for (const page of pagesToRemove([...existing], [...generated.keys()], wiki.removeUnlistedPages)) {
    await send(pageUrl(page), { method: 'DELETE' });
    console.log(`removed ${page} (no document produces it)`);
  }

  console.log(`${written} written, ${unchanged} already current, ${generated.size} pages total`);
}

module.exports = { pagePathOf, parentsFirst, pagesToRemove, flattenPages, markdownFiles, attachmentBody, isAlreadyAttached };

if (require.main === module) {
  main().catch((error) => {
    console.error(error.message);
    process.exit(1);
  });
}
