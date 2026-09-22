import type { NumberFormat } from '../../../types/dashboard';
import { formatNumber } from './format';

/**
 * The sentence a verdict reads as: the wording the model supplied, with the judged figure and the
 * target it was tested against put into it.
 *
 * A placeholder the verdict has no figure for is dropped along with the space beside it, so one
 * wording serves a balance that was assessed and a balance nobody assessed. It is dropped rather
 * than filled with a zero or a dash: a measurement of zero and no measurement at all are the
 * distinction the withheld verdicts exist to keep.
 */
export function verdictSentence(
  reads: string,
  figures: { value: number | null; target: number | null },
  format?: NumberFormat,
  unit?: string,
): string {
  const substituted = reads.replace(/\{(value|target)\}/g, (_, placeholder) => {
    const figure = placeholder === 'value' ? figures.value : figures.target;
    return figure === null ? '' : `${formatNumber(figure, format)}${unit ? ` ${unit}` : ''}`;
  });
  return substituted.replace(/\s{2,}/g, ' ').trim();
}
