import { MOUSE } from 'three';

/** Holding Shift, Command or Control makes a left drag pan and a right drag rotate, because a trackpad offers
 *  only a one-finger drag and right-clicking on a Mac trackpad is awkward. The viewer passes the held keys in,
 *  so OrbitControls is given the new map while a key is held. */
interface KeyboardModifiers {
  shift: boolean;
  meta: boolean;
  ctrl: boolean;
}

interface OrbitMouseButtons {
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
