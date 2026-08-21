import type { VosThing } from '../types/vos';

export interface Position {
  latitude: number;
  longitude: number;
}

/**
 * Where the model sits, averaged over every Thing that knows its own position.
 *
 * The average is taken on the raw degrees. A model imported from one building file spans metres,
 * so it cannot straddle the antimeridian, where averaging degrees would put the centre half a
 * world away.
 */
export function modelCentre(things: VosThing[]): Position | null {
  let latitudeTotal = 0;
  let longitudeTotal = 0;
  let counted = 0;
  for (const thing of things) {
    const latitude = thing.Properties?.latitude;
    const longitude = thing.Properties?.longitude;
    if (typeof latitude !== 'number' || typeof longitude !== 'number') continue;
    latitudeTotal += latitude;
    longitudeTotal += longitude;
    counted += 1;
  }
  if (counted === 0) return null;
  return { latitude: latitudeTotal / counted, longitude: longitudeTotal / counted };
}
