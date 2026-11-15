import { describe, expect, it } from 'vitest';
import { releaseRefusal } from './releaseTag.mjs';

describe('a release tag', () => {
  it('publishes when it names the package version and sits on develop', () => {
    expect(releaseRefusal({ tag: 'tendril-v0.1.0', version: '0.1.0', onDevelop: true })).toBeNull();
  });

  it('is refused when it names another version', () => {
    expect(releaseRefusal({ tag: 'tendril-v0.1.1', version: '0.1.0', onDevelop: true })).toBe(
      "The tag tendril-v0.1.1 does not name Tendril's version 0.1.0. Tag the commit tendril-v0.1.0, or change the version first.",
    );
  });

  it('is refused when it names the version without the prefix', () => {
    expect(releaseRefusal({ tag: '0.1.0', version: '0.1.0', onDevelop: true })).not.toBeNull();
  });

  it('is refused when its commit is not on develop', () => {
    expect(releaseRefusal({ tag: 'tendril-v0.1.0', version: '0.1.0', onDevelop: false })).toBe(
      'The tag tendril-v0.1.0 is on a commit develop does not hold. Tag a commit on develop.',
    );
  });
});
