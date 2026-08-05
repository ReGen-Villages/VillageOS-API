'use strict';

const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pagePathOf, parentsFirst, pagesToRemove, flattenPages } = require('./publish-wiki');
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
