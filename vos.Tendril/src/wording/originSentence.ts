/**
 * The line an origin reads as: the wording the model supplied, with what says so and when it last
 * did put into it.
 *
 * A placeholder with nothing to put in it is dropped along with the space beside it, the way a
 * verdict's is, so one wording serves a figure whose source the model names and one whose it does
 * not. Dropped rather than filled with a dash: a dash in place of a source reads as a source nobody
 * recorded, which is a different claim from a figure that names none.
 *
 * The wording has to survive that, so put a placeholder where the sentence still reads without it —
 * "resolved {resolvedAt} from {source}" reads whichever of the two the model holds, while
 * "from {source}, resolved {resolvedAt}" leaves a comma and a verb hanging.
 */
export function originSentence(
  reads: string,
  saidBy: { source: string | null; resolvedAt: string | null },
): string {
  const substituted = reads.replace(/\{(source|resolvedAt)\}/g, (_, placeholder) =>
    (placeholder === 'source' ? saidBy.source : saidBy.resolvedAt) ?? '');
  return substituted.replace(/\s{2,}/g, ' ').trim();
}
