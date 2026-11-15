const RELEASE_TAG_PREFIX = 'tendril-v';

export function releaseRefusal({ tag, version, onDevelop }) {
  const expected = `${RELEASE_TAG_PREFIX}${version}`;
  if (tag !== expected) {
    return `The tag ${tag} does not name Tendril's version ${version}. Tag the commit ${expected}, or change the version first.`;
  }
  if (!onDevelop) {
    return `The tag ${tag} is on a commit develop does not hold. Tag a commit on develop.`;
  }
  return null;
}
