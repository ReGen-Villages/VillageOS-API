import { describe, it, expect } from 'vitest';
import { locationFromMapLink } from './mapLink';

const located = (latitude: number, longitude: number) => ({ kind: 'located', latitude, longitude });

describe('reading a position out of a pasted map link', () => {
  it('reads the viewport centre a map link carries after an at sign', () => {
    expect(locationFromMapLink('https://www.google.com/maps/@39.5012,-8.4137,15z')).toEqual(
      located(39.5012, -8.4137),
    );
  });

  it('prefers the pinned place over the viewport centre, which is a different point', () => {
    const link =
      'https://www.google.com/maps/place/Willow+Bend/@39.6,-8.5,15z/data=!4m5!3m4!1s0x0:0x0!8m2!3d39.5012!4d-8.4137';

    expect(locationFromMapLink(link)).toEqual(located(39.5012, -8.4137));
  });

  it('reads a query parameter pair', () => {
    expect(locationFromMapLink('https://maps.example.com/?q=39.5012,-8.4137')).toEqual(
      located(39.5012, -8.4137),
    );
  });

  it('reads a query parameter pair whose comma arrived encoded', () => {
    expect(locationFromMapLink('https://maps.example.com/?ll=39.5012%2C-8.4137&z=15')).toEqual(
      located(39.5012, -8.4137),
    );
  });

  it('reads the marker an OpenStreetMap link names', () => {
    expect(locationFromMapLink('https://www.openstreetmap.org/?mlat=39.5012&mlon=-8.4137#map=15/39.6/-8.5')).toEqual(
      located(39.5012, -8.4137),
    );
  });

  it('reads the fragment an OpenStreetMap link ends with when it names no marker', () => {
    expect(locationFromMapLink('https://www.openstreetmap.org/#map=15/39.5012/-8.4137')).toEqual(
      located(39.5012, -8.4137),
    );
  });

  it('reads a bare pair a planner typed rather than pasted', () => {
    expect(locationFromMapLink(' 39.5012 , -8.4137 ')).toEqual(located(39.5012, -8.4137));
  });

  it('reads a pair given as whole numbers', () => {
    expect(locationFromMapLink('40,-8')).toEqual(located(40, -8));
  });
});

describe('a link that cannot be read', () => {
  it.each(['https://maps.app.goo.gl/AbCdEf123', 'https://goo.gl/maps/AbCdEf', 'https://bit.ly/3xYz'])(
    'tells the planner to open %s themselves rather than failing silently',
    (link) => {
      expect(locationFromMapLink(link)).toEqual({ kind: 'shortened' });
    },
  );

  it('reports no match for a link holding no coordinates', () => {
    expect(locationFromMapLink('https://www.example.com/about-us')).toEqual({ kind: 'unrecognised' });
  });

  it('reports no match for empty text', () => {
    expect(locationFromMapLink('   ')).toEqual({ kind: 'unrecognised' });
  });

  it('refuses a pair no point on Earth could be, rather than placing the site there', () => {
    expect(locationFromMapLink('120.5,-8.4137')).toEqual({ kind: 'unrecognised' });
    expect(locationFromMapLink('39.5012,-200.1')).toEqual({ kind: 'unrecognised' });
  });

  it('refuses a comma-separated pair that is not a position', () => {
    expect(locationFromMapLink('https://shop.example.com/item?size=10,5')).toEqual({ kind: 'unrecognised' });
  });
});
