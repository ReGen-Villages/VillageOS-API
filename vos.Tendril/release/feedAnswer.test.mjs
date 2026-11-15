import { describe, expect, it } from 'vitest';
import { feedHolds } from './feedAnswer.mjs';

const notFound = (summary) => JSON.stringify({ error: { code: 'E404', summary } });

describe("the feed's answer to whether it holds a version", () => {
  it('holds a version it names back', () => {
    expect(feedHolds({ status: 0, stdout: '"0.1.0"' }, '0.1.0')).toBe(true);
  });

  it('does not hold a version of a package it holds other versions of', () => {
    expect(feedHolds({ status: 1, stdout: notFound('No match found for version 0.1.1') }, '0.1.1')).toBe(false);
  });

  it('does not hold any version of a package it has never seen', () => {
    expect(feedHolds({ status: 1, stdout: notFound('Not Found') }, '0.1.0')).toBe(false);
  });

  it('is not taken as not holding the version when it could not be asked', () => {
    const refused = JSON.stringify({ error: { code: 'E401', summary: 'Unable to authenticate' } });

    expect(() => feedHolds({ status: 1, stdout: refused }, '0.1.0')).toThrow('E401');
  });

  it('is not taken as not holding the version when it answered nothing readable', () => {
    expect(() => feedHolds({ status: 1, stdout: '' }, '0.1.0')).toThrow();
  });
});
