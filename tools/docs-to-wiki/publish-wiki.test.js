'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pagePathOf, parentsFirst, pagesToRemove, flattenPages, attachmentBody, isAlreadyAttached } = require('./publish-wiki');
const { pageFileName } = require('./docs-to-wiki');

test('a generated file name maps back to its wiki page path', () => {
  assert.equal(pagePathOf('Home.md'), '/Home');
  assert.equal(pagePathOf('Project-Guide.md'), '/Project Guide');
  assert.equal(pagePathOf('Services/Relationship-Services.md'), '/Services/Relationship Services');
  assert.equal(pagePathOf('API%2DReference.md'), '/API-Reference');
});

// The generator names files from page paths and the publisher reads them back; a disagreement
// would publish to the wrong page or orphan the old one.
test('the mapping is the exact inverse of the generator naming', () => {
  for (const page of ['/Home', '/Project Guide', '/Services/Relationship Services', '/API-Reference', '/Land Intake']) {
    assert.equal(pagePathOf(pageFileName(page)), page);
  }
});

test('parents are written before their children', () => {
  const order = parentsFirst(['/Services/Delta', '/Home', '/Services', '/Services/Relationship Services']);
  assert.ok(order.indexOf('/Services') < order.indexOf('/Services/Delta'));
  assert.ok(order.indexOf('/Services') < order.indexOf('/Services/Relationship Services'));
});

test('pages no document produces are removed, deepest first', () => {
  const removals = pagesToRemove(
    ['/', '/Home', '/Services', '/Services/Python', '/Services/Delta', '/Old'],
    ['/Home', '/Services', '/Services/Delta'],
    true,
  );
  assert.deepEqual(removals, ['/Services/Python', '/Old']);
});

test('the wiki root is never a removal candidate', () => {
  assert.deepEqual(pagesToRemove(['/'], [], true), []);
});

// A wiki that also holds pages written on it and nowhere else: an unlisted page there is somebody's
// work, and removing it would be the publish quietly deleting documentation nobody backed up.
test('a wiki whose pages are not all generated keeps the ones the manifest does not produce', () => {
  assert.deepEqual(pagesToRemove(['/', '/Home', '/Guide', '/Written by hand'], ['/Guide'], false), []);
});

// Bug #6148: raw bytes were sent and every publish stopped at the first image with
// "The input is not a valid Base-64 string".
test('an attachment body is base64, and decodes back to the original bytes', () => {
  const bytes = Buffer.from(Array.from({ length: 256 }, (_, value) => value));
  const body = attachmentBody(bytes);

  assert.match(body, /^[A-Za-z0-9+/]+=*$/);
  assert.deepEqual(Buffer.from(body, 'base64'), bytes);
});

test('a page tree flattens to every path it contains', () => {
  const tree = {
    path: '/',
    subPages: [
      { path: '/Home' },
      { path: '/Services', subPages: [{ path: '/Services/Delta' }] },
    ],
  };
  assert.deepEqual(flattenPages(tree), ['/', '/Home', '/Services', '/Services/Delta']);
});

// Bug #6151 — every develop build failed here. The step expected an attachment that is already on
// the wiki to answer 409; the wiki answers 500 with this body, so the one case the step was written
// to tolerate was the one it died on. Verbatim from the failing run.
const ALREADY_THERE = JSON.stringify({
  $id: '1',
  innerException: null,
  message: "The wiki attachment creation failed with message : The path '/.attachments/land-intake-analysis-pipeline.png' specified in the add operation already exists. Please specify a new path.",
  typeName: 'Microsoft.TeamFoundation.Wiki.Server.WikiCreateAttachmentFailedException, Microsoft.TeamFoundation.Wiki.Server',
  typeKey: 'WikiCreateAttachmentFailedException',
  errorCode: 0,
  eventId: 3000,
});

test('an attachment already on the wiki is the desired state, however the wiki words it', () => {
  assert.equal(isAlreadyAttached(500, ALREADY_THERE), true);
  assert.equal(isAlreadyAttached(409, ''), true);
});

test('a real attachment failure still fails the run', () => {
  assert.equal(isAlreadyAttached(401, 'unauthorised'), false);
  assert.equal(isAlreadyAttached(400, 'The input is not a valid Base-64 string'), false);
  assert.equal(
    isAlreadyAttached(500, JSON.stringify({ typeKey: 'WikiCreateAttachmentFailedException', message: 'storage unavailable' })),
    false,
    'a create that failed for another reason must not be read as already there',
  );
  assert.equal(isAlreadyAttached(500, 'already exists'), false, 'the message alone is not enough — the wiki has to name the failure');
});
