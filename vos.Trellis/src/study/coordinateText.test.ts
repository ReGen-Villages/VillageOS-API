import { describe, expect, it } from 'vitest';
import { positionFromText } from './coordinateText';

describe('positionFromText', () => {
  it.each([
    ['25.1505, 55.5515', 25.1505, 55.5515],
    ['25.1505;55.5515', 25.1505, 55.5515],
    ['25.1505 55.5515', 25.1505, 55.5515],
    ['-25.15 -55.55', -25.15, -55.55],
    ['25.1505N 55.5515E', 25.1505, 55.5515],
    ['N25.1505 E55.5515', 25.1505, 55.5515],
    ['25.1505S, 55.5515W', -25.1505, -55.5515],
    ['55.5515E 25.1505N', 25.1505, 55.5515],
    ['25°09\'01.68"N 55°33\'05.52"E', 25.1504667, 55.5515333],
    ['25°09′01.68″N 55°33′05.52″E', 25.1504667, 55.5515333],
    ['25º09’01.68”N, 55˚33’05.52”E', 25.1504667, 55.5515333],
    ['25°09\'N 55°33\'E', 25.15, 55.55],
  ])('reads %s as a position', (text, latitude, longitude) => {
    const read = positionFromText(text);

    expect(read.kind).toBe('position');
    if (read.kind !== 'position') return;
    expect(read.latitude).toBeCloseTo(latitude, 6);
    expect(read.longitude).toBeCloseTo(longitude, 6);
  });

  it.each([
    ['25.15N 26.15N', 'two latitudes'],
    ['55.55E 56.55W', 'two longitudes'],
    ['91, 55.55', 'a latitude beyond 90'],
    ['25.15, 181', 'a longitude beyond 180'],
    ['25.15', 'one number'],
    ['25.15, 55.55, 3', 'three numbers'],
    ['Harbour Street', 'a place name'],
  ])('refuses %s, which is %s', (text) => {
    expect(positionFromText(text)).toEqual({ kind: 'notCoordinates' });
  });

  it('refuses an empty box', () => {
    expect(positionFromText('   ')).toEqual({ kind: 'notCoordinates' });
  });
});
