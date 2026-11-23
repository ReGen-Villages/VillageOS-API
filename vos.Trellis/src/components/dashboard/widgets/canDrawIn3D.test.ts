import { describe, it, expect, vi, afterEach } from 'vitest';
import { canDrawIn3D } from './canDrawIn3D';

const answering = (context: unknown) =>
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockReturnValue(context as never);

afterEach(() => vi.restoreAllMocks());

describe('whether this browser draws in 3D', () => {
  it('says no where the browser gives no WebGL 2 context', () => {
    answering(null);

    expect(canDrawIn3D()).toBe(false);
  });

  it('says yes where it gives one, and releases it at once', () => {
    const loseContext = vi.fn();
    const asked = answering({ getExtension: (name: string) => (name === 'WEBGL_lose_context' ? { loseContext } : null) });

    expect(canDrawIn3D()).toBe(true);
    expect(asked).toHaveBeenCalledWith('webgl2');
    expect(loseContext).toHaveBeenCalledOnce();
  });

  it('says yes where the browser cannot release the context early', () => {
    answering({ getExtension: () => null });

    expect(canDrawIn3D()).toBe(true);
  });
});
