'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('fs');
const path = require('path');
const { pagePathOf, parentsFirst, pagesToRemove, flattenPages, attachmentBody } = require('./publish-wiki');
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
  );
  assert.deepEqual(removals, ['/Services/Python', '/Old']);
});

test('the wiki root is never a removal candidate', () => {
  assert.deepEqual(pagesToRemove(['/'], []), []);
});

// Bug #6148: raw bytes were sent and every publish stopped at the first image with
// "The input is not a valid Base-64 string".
test('an attachment body is base64, and decodes back to the original bytes', () => {
  const bytes = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0xff]);
  const body = attachmentBody(bytes);

  assert.match(body, /^[A-Za-z0-9+/]+=*$/);
  assert.deepEqual(Buffer.from(body, 'base64'), bytes);
});

test('a real image survives the encoding unchanged', () => {
  const image = fs.readFileSync(path.join(__dirname, '..', '..', 'docs', 'assets', 'land-intake-parts.png'));
  assert.deepEqual(Buffer.from(attachmentBody(image), 'base64'), image);
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
