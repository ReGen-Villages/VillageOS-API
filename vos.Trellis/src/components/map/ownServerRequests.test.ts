import { describe, it, expect } from 'vitest';
import { carryingOwnServerHeaders } from './ownServerRequests';

const PAGE = 'https://mycelium.example.org';
const SIGNED_IN = () => ({ Authorization: 'Bearer the-token' });

describe('carryingOwnServerHeaders', () => {
  it('adds the headers to a tile the page’s own server serves, named by a path', () => {
    const transform = carryingOwnServerHeaders(PAGE, SIGNED_IN);

    expect(transform('/basemaps/satellite-tiles/3/4/5')).toEqual({
      url: '/basemaps/satellite-tiles/3/4/5',
      headers: { Authorization: 'Bearer the-token' },
    });
  });

  it('adds them to a full address on the page’s own server too', () => {
    const transform = carryingOwnServerHeaders(PAGE, SIGNED_IN);

    expect(transform(`${PAGE}/basemaps/satellite-tiles/3/4/5`)?.headers).toEqual({ Authorization: 'Bearer the-token' });
  });

  it('never sends them to another server, where the token would be handed to a map provider', () => {
    const transform = carryingOwnServerHeaders(PAGE, SIGNED_IN);

    expect(transform('https://tiles.openfreemap.org/styles/liberty')).toBeUndefined();
    expect(transform('https://s3.amazonaws.com/elevation-tiles-prod/terrarium/1/2/3.png')).toBeUndefined();
  });

  it('leaves every request alone on a page nobody signed in to', () => {
    expect(carryingOwnServerHeaders(PAGE, null)('/basemaps/satellite-tiles/3/4/5')).toBeUndefined();
  });

  it('reads the headers at each request, so a token refreshed after the map was built is the one sent', () => {
    let token = 'first';
    const transform = carryingOwnServerHeaders(PAGE, () => ({ Authorization: `Bearer ${token}` }));
    token = 'refreshed';

    expect(transform('/basemaps/satellite-tiles/3/4/5')?.headers).toEqual({ Authorization: 'Bearer refreshed' });
  });
});
