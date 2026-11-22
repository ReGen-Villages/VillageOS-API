export type ReadPosition =
  | { readonly kind: 'position'; readonly latitude: number; readonly longitude: number }
  | { readonly kind: 'notCoordinates' };

type Hemisphere = '' | 'N' | 'S' | 'E' | 'W';

interface ReadAngle {
  readonly degrees: number;
  readonly hemisphere: Hemisphere;
}

const NOT_COORDINATES: ReadPosition = { kind: 'notCoordinates' };

const ANGLE =
  /^([NSEW])?\s*(-?\d+(?:\.\d+)?)\s*°?\s*(?:(\d+(?:\.\d+)?)\s*'\s*)?(?:(\d+(?:\.\d+)?)\s*"?\s*)?([NSEW])?$/i;

// Tried in turn, so a pair written with a comma is not split again at its spaces.
const WAYS_TO_SPLIT: readonly RegExp[] = [/\s*[,;]\s*/, /(?<=[NSEW])\s+(?=[\d-])/i, /\s+(?=[NSEW]\s*\d)/i, /\s+/];

export function positionFromText(text: string): ReadPosition {
  const plain = text
    .trim()
    .replace(/[′’‘]/g, "'")
    .replace(/[″”“]|''/g, '"')
    .replace(/[º˚]/g, '°');
  for (const splitter of WAYS_TO_SPLIT) {
    const parts = plain.split(splitter);
    if (parts.length !== 2) continue;
    const first = angleFrom(parts[0]);
    const second = angleFrom(parts[1]);
    if (!first || !second) continue;
    const [latitude, longitude] = isLongitude(first) || isLatitude(second) ? [second, first] : [first, second];
    if (isLongitude(latitude) || isLatitude(longitude)) continue;
    if (Math.abs(latitude.degrees) <= 90 && Math.abs(longitude.degrees) <= 180) {
      return { kind: 'position', latitude: latitude.degrees, longitude: longitude.degrees };
    }
  }
  return NOT_COORDINATES;
}

function angleFrom(part: string): ReadAngle | null {
  const match = part.trim().match(ANGLE);
  if (!match) return null;
  const hemisphere = (match[1] ?? match[5] ?? '').toUpperCase() as Hemisphere;
  const whole = Number(match[2]);
  const size = Math.abs(whole) + Number(match[3] ?? 0) / 60 + Number(match[4] ?? 0) / 3600;
  return { degrees: whole < 0 || hemisphere === 'S' || hemisphere === 'W' ? -size : size, hemisphere };
}

function isLatitude(angle: ReadAngle): boolean {
  return angle.hemisphere === 'N' || angle.hemisphere === 'S';
}

function isLongitude(angle: ReadAngle): boolean {
  return angle.hemisphere === 'E' || angle.hemisphere === 'W';
}
