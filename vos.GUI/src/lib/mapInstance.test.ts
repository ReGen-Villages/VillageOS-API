import { describe, it, expect, beforeEach } from 'vitest';
import { setMapInstance, getMapInstance } from './mapInstance';

describe('mapInstance module ref', () => {
  beforeEach(() => {
    setMapInstance(null);
  });

  it('returns null when no map is set', () => {
    expect(getMapInstance()).toBeNull();
  });

  it('returns the ref that was set', () => {
    // Lightweight stub — we only care that the same reference comes back out.
    const stub = { _kind: 'fake-map' } as unknown as Parameters<typeof setMapInstance>[0];
    setMapInstance(stub);
    expect(getMapInstance()).toBe(stub);
  });

  it('clears when set to null', () => {
    const stub = { _kind: 'fake-map' } as unknown as Parameters<typeof setMapInstance>[0];
    setMapInstance(stub);
    setMapInstance(null);
    expect(getMapInstance()).toBeNull();
  });

  it('replaces an existing ref on a second set', () => {
    const a = { _kind: 'a' } as unknown as Parameters<typeof setMapInstance>[0];
    const b = { _kind: 'b' } as unknown as Parameters<typeof setMapInstance>[0];
    setMapInstance(a);
    expect(getMapInstance()).toBe(a);
    setMapInstance(b);
    expect(getMapInstance()).toBe(b);
  });
});
