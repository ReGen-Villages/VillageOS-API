import { describe, expect, it } from 'vitest';
import { siteAfterImport } from './siteMove';
import type { BoundaryPoint } from '../utils/parcelGeometry';

// On the mean sphere, so a latitude offset puts the border's centre a known distance north of the site.
const metresPerDegreeOfLatitude = 111194.9;

function squareCentredAt(latitude: number, longitude: number): BoundaryPoint[] {
  const half = 0.001;
  return [
    { latitude: latitude - half, longitude: longitude - half },
    { latitude: latitude - half, longitude: longitude + half },
    { latitude: latitude + half, longitude: longitude + half },
    { latitude: latitude + half, longitude: longitude - half },
  ];
}

const site = { latitude: 25.15, longitude: 55.55 };

describe('siteAfterImport', () => {
  it('keeps the site where an imported border is centred 300 m away or less', () => {
    const border = squareCentredAt(site.latitude + 290 / metresPerDegreeOfLatitude, site.longitude);

    expect(siteAfterImport(site, border)).toEqual({ position: site, moved: false });
  });

  it('moves the site to the centre of an imported border more than 300 m away', () => {
    const centre = { latitude: site.latitude + 310 / metresPerDegreeOfLatitude, longitude: site.longitude };

    const after = siteAfterImport(site, squareCentredAt(centre.latitude, centre.longitude));

    expect(after.moved).toBe(true);
    expect(after.position.latitude).toBeCloseTo(centre.latitude, 9);
    expect(after.position.longitude).toBeCloseTo(centre.longitude, 9);
  });

  it('starts the site at the border centre when there is no site yet', () => {
    const after = siteAfterImport(null, squareCentredAt(24.5, 54.4));

    expect(after.moved).toBe(true);
    expect(after.position.latitude).toBeCloseTo(24.5, 9);
    expect(after.position.longitude).toBeCloseTo(54.4, 9);
  });

  // The middle of the corners' extent, as the demo takes it, so a border with many corners on one side is not pulled.
  it('takes the centre as the middle of the border extent, not the mean of its corners', () => {
    const lopsided: BoundaryPoint[] = [
      { latitude: 24.0, longitude: 54.0 },
      { latitude: 24.0, longitude: 54.01 },
      { latitude: 24.002, longitude: 54.01 },
      { latitude: 24.009, longitude: 54.01 },
      { latitude: 24.01, longitude: 54.01 },
      { latitude: 24.01, longitude: 54.0 },
    ];

    const after = siteAfterImport(null, lopsided);

    expect(after.position.latitude).toBeCloseTo(24.005, 9);
    expect(after.position.longitude).toBeCloseTo(54.005, 9);
  });
});
