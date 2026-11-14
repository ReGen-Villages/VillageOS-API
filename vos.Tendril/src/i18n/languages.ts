export const BASE_LANGUAGE = 'en';

/** Primary subtag of a BCP 47 tag, lower-cased: `ar-SA` and `AR` both give `ar`. */
export function primarySubtag(tag: string): string {
  return tag.split('-')[0]?.toLowerCase() ?? '';
}
