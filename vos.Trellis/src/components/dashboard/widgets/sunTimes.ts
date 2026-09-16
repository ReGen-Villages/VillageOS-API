/**
 * When the sun rises and sets on a day of the year at a place on Earth, in the clock the hours are
 * read in — the two curves a heatmap of the hours draws over itself. The low-precision series
 * expansion for declination and the equation of time, good to a few minutes, which is well inside
 * an hour-tall cell.
 */

const DEGREES = Math.PI / 180;
/** The sun's altitude at which it is said to rise or set: its radius plus refraction at the horizon. */
const HORIZON_ALTITUDE = -0.833;

export function sunTimes(
  dayOfYear: number, latitude: number, longitude: number, utcOffsetHours: number,
): { sunrise: number; sunset: number } {
  const gamma = ((2 * Math.PI) / 365) * (dayOfYear - 1);
  const declination =
    0.006918 - 0.399912 * Math.cos(gamma) + 0.070257 * Math.sin(gamma)
    - 0.006758 * Math.cos(2 * gamma) + 0.000907 * Math.sin(2 * gamma)
    - 0.002697 * Math.cos(3 * gamma) + 0.014615 * Math.sin(3 * gamma);
  const equationOfTimeMinutes = 229.18 * (
    0.000075 + 0.001868 * Math.cos(gamma) - 0.032077 * Math.sin(gamma)
    - 0.014615 * Math.cos(2 * gamma) - 0.040849 * Math.sin(2 * gamma));

  const phi = latitude * DEGREES;
  const cosHourAngle =
    (Math.sin(HORIZON_ALTITUDE * DEGREES) - Math.sin(phi) * Math.sin(declination))
    / (Math.cos(phi) * Math.cos(declination));
  const halfDayHours = Math.acos(Math.max(-1, Math.min(1, cosHourAngle))) / DEGREES / 15;

  const standardMeridian = utcOffsetHours * 15;
  const noon = 12 - (longitude - standardMeridian) / 15 - equationOfTimeMinutes / 60;
  return { sunrise: noon - halfDayHours, sunset: noon + halfDayHours };
}
