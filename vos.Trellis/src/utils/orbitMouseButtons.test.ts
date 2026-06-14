import { describe, it, expect } from 'vitest';
import { MOUSE } from 'three';
import { isPanModifier, orbitMouseButtonsFor } from './orbitMouseButtons';

describe('isPanModifier', () => {
  it('returns false when no modifier is held', () => {
    expect(isPanModifier({ shift: false, meta: false, ctrl: false })).toBe(false);
  });
  it('returns true when shift is held', () => {
    expect(isPanModifier({ shift: true, meta: false, ctrl: false })).toBe(true);
  });
  it('returns true when meta (Cmd) is held', () => {
    expect(isPanModifier({ shift: false, meta: true, ctrl: false })).toBe(true);
  });
  it('returns true when ctrl is held', () => {
    expect(isPanModifier({ shift: false, meta: false, ctrl: true })).toBe(true);
  });
});

describe('orbitMouseButtonsFor', () => {
  it('default mapping: left=rotate, middle=dolly, right=pan', () => {
    expect(orbitMouseButtonsFor({ shift: false, meta: false, ctrl: false })).toEqual({
      LEFT: MOUSE.ROTATE,
      MIDDLE: MOUSE.DOLLY,
      RIGHT: MOUSE.PAN,
    });
  });

  it('modifier held: left=pan, middle=dolly, right=rotate', () => {
    expect(orbitMouseButtonsFor({ shift: true, meta: false, ctrl: false })).toEqual({
      LEFT: MOUSE.PAN,
      MIDDLE: MOUSE.DOLLY,
      RIGHT: MOUSE.ROTATE,
    });
  });

  it('any of shift/meta/ctrl toggles to pan mode', () => {
    const pan = orbitMouseButtonsFor({ shift: false, meta: true, ctrl: false });
    expect(pan.LEFT).toBe(MOUSE.PAN);
    const pan2 = orbitMouseButtonsFor({ shift: false, meta: false, ctrl: true });
    expect(pan2.LEFT).toBe(MOUSE.PAN);
  });
});
