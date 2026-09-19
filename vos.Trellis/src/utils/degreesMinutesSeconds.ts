import type { BoundaryPoint } from './parcelGeometry';

const HUNDREDTHS_OF_A_SECOND_PER_DEGREE = 360_000;
const HUNDREDTHS_OF_A_SECOND_PER_MINUTE = 6_000;

/** A position as a surveyor reads it: `25°51′57.02″ S 28°01′22.07″ E`. Rounded to a hundredth of a
 *  second before it is split into parts, so a second that rounds up to sixty carries into the minute
 *  rather than reading as sixty. */
export function degreesMinutesSeconds(position: BoundaryPoint): string {
  return `${sexagesimal(position.latitude, 'N', 'S')} ${sexagesimal(position.longitude, 'E', 'W')}`;
}

function sexagesimal(value: number, positive: string, negative: string): string {
  const hundredths = Math.round(Math.abs(value) * HUNDREDTHS_OF_A_SECOND_PER_DEGREE);
  const degrees = Math.floor(hundredths / HUNDREDTHS_OF_A_SECOND_PER_DEGREE);
  const withinDegree = hundredths % HUNDREDTHS_OF_A_SECOND_PER_DEGREE;
  const minutes = Math.floor(withinDegree / HUNDREDTHS_OF_A_SECOND_PER_MINUTE);
  const seconds = (withinDegree % HUNDREDTHS_OF_A_SECOND_PER_MINUTE) / 100;
  const hemisphere = value < 0 ? negative : positive;
  return `${degrees}°${String(minutes).padStart(2, '0')}′${seconds.toFixed(2).padStart(5, '0')}″ ${hemisphere}`;
}
