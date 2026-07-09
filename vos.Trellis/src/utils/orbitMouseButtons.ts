import { MOUSE } from 'three';

/**
 * OrbitControls mouseButtons map. Default layout:
 *   left = rotate, middle = dolly (zoom), right = pan.
 *
 * Feature #5385 — when the user holds a pan modifier (Shift, Cmd, or Ctrl),
 * left-drag becomes pan while right-drag falls back to rotate. The middle
 * mouse button always dollies. Picking the modifier this way lets touchpad
 * users pan with a single-finger drag (the only gesture they have available
 * without an external mouse) — right-click is awkward on macOS trackpads.
 *
 * Pure function; the BimFragmentsViewer wires window keydown/keyup state into
 * the React render so OrbitControls re-receives this prop while a modifier
 * is held.
 */
export interface KeyboardModifiers {
  shift: boolean;
  meta: boolean;
  ctrl: boolean;
}

export interface OrbitMouseButtons {
  LEFT: number;
  MIDDLE: number;
  RIGHT: number;
}

export function isPanModifier(mods: KeyboardModifiers): boolean {
  return mods.shift || mods.meta || mods.ctrl;
}

export function orbitMouseButtonsFor(mods: KeyboardModifiers): OrbitMouseButtons {
  return isPanModifier(mods)
    ? { LEFT: MOUSE.PAN, MIDDLE: MOUSE.DOLLY, RIGHT: MOUSE.ROTATE }
    : { LEFT: MOUSE.ROTATE, MIDDLE: MOUSE.DOLLY, RIGHT: MOUSE.PAN };
}
