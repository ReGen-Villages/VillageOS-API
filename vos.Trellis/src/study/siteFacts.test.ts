import { describe, expect, it } from 'vitest';
import { coordinatesText, elevationText, hectaresText } from './siteFacts';

describe('hectaresText', () => {
  it.each([
    [62.484565, '62.5'],
    [62.347761, '62.3'],
    [0.04, '0.0'],
    [1230877.836, '1230877.8'],
  ])('writes %s hectares to one decimal as %s', (hectares, text) => {
    expect(hectaresText(hectares)).toBe(text);
  });
});

describe('coordinatesText', () => {
  it.each([
    [25.1505, 55.5515, '25.15050°N, 55.55150°E'],
    [-33.8688, 151.2093, '33.86880°S, 151.20930°E'],
    [40.7128, -74.006, '40.71280°N, 74.00600°W'],
    [0, 0, '0.00000°N, 0.00000°E'],
  ])('writes %s, %s to five decimals with hemisphere letters', (latitude, longitude, text) => {
    expect(coordinatesText({ latitude, longitude })).toBe(text);
  });
});

describe('elevationText', () => {
  it('gives the site elevation alone when the border has no range', () => {
    expect(elevationText({ siteMetres: 100.4, lowestMetres: 100, highestMetres: 100 })).toEqual({ site: '100', range: null });
  });

  it('gives the range within the border where its lowest and highest differ', () => {
    expect(elevationText({ siteMetres: 31.6, lowestMetres: 18.2, highestMetres: 44.9 })).toEqual({ site: '32', range: '18–45' });
  });

  it('gives the site elevation alone before any border is drawn', () => {
    expect(elevationText({ siteMetres: 7, lowestMetres: null, highestMetres: null })).toEqual({ site: '7', range: null });
  });
});
