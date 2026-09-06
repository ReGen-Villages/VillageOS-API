import { readFileSync } from 'node:fs';
import { extname } from 'node:path';

/**
 * Figures are prepared for paper here: every image is inlined, so a rendered guide carries its own
 * pictures and cannot lose one to a path that resolved somewhere else, and a figure drawn on a dark
 * ground is turned light, because a page of ink a reader did not ask for is a page they pay for.
 */

const HEX = /^#([0-9a-f]{3,8})$/i;

function toRgb(hex) {
  const m = HEX.exec(hex.trim());
  if (!m) return null;
  let h = m[1];
  if (h.length === 3 || h.length === 4) h = [...h].map((c) => c + c).join('');
  if (h.length !== 6 && h.length !== 8) return null;
  return {
    r: parseInt(h.slice(0, 2), 16),
    g: parseInt(h.slice(2, 4), 16),
    b: parseInt(h.slice(4, 6), 16),
    a: h.length === 8 ? h.slice(6, 8) : '',
  };
}

/** Relative luminance, the measure of how much light a colour actually throws. */
function luminance(hex) {
  const c = toRgb(hex);
  if (!c) return null;
  const lin = (v) => {
    const s = v / 255;
    return s <= 0.04045 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b);
}

function toHsl({ r, g, b }) {
  const [rr, gg, bb] = [r / 255, g / 255, b / 255];
  const max = Math.max(rr, gg, bb), min = Math.min(rr, gg, bb);
  const l = (max + min) / 2;
  if (max === min) return { h: 0, s: 0, l };
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h;
  if (max === rr) h = ((gg - bb) / d + (gg < bb ? 6 : 0)) / 6;
  else if (max === gg) h = ((bb - rr) / d + 2) / 6;
  else h = ((rr - gg) / d + 4) / 6;
  return { h, s, l };
}

function toHex({ h, s, l }, alpha = '') {
  const f = (n) => {
    const k = (n + h * 12) % 12;
    const a = s * Math.min(l, 1 - l);
    const v = l - a * Math.max(-1, Math.min(k - 3, 9 - k, 1));
    return Math.round(v * 255).toString(16).padStart(2, '0');
  };
  return `#${f(0)}${f(8)}${f(4)}${alpha}`;
}

/**
 * The same colour with its lightness turned over: a near-black ground becomes near-paper, light
 * text becomes ink, and an accent keeps its hue and only changes how dark it sits. Saturation is
 * lifted a little for what was very dark, because a colour that was carrying a glow on black has
 * nothing to carry on white.
 */
function flipLightness(hex) {
  const rgb = toRgb(hex);
  if (!rgb) return hex;
  const { h, s, l } = toHsl(rgb);
  const flipped = 1 - l;
  const saturation = l < 0.2 && s > 0.05 ? Math.min(1, s * 1.15) : s;
  return toHex({ h, s: saturation, l: flipped }, rgb.a);
}

const PAINT = /\b(fill|stroke|stop-color)="(#[0-9a-fA-F]{3,8})"/g;

/** The colour a backdrop rectangle actually paints, following a gradient reference to its stops. */
function backdropColour(svg) {
  const size = /viewBox="\s*[-\d.]+\s+[-\d.]+\s+([\d.]+)\s+([\d.]+)/.exec(svg);
  if (!size) return null;
  const [w, h] = [size[1], size[2]];
  const rects = svg.match(/<rect\b[^>]*>/g) ?? [];
  for (const rect of rects) {
    const width = /\bwidth="([^"]+)"/.exec(rect)?.[1];
    const height = /\bheight="([^"]+)"/.exec(rect)?.[1];
    const covers = (v, full) => v === full || v === '100%' || Number(v) >= Number(full);
    if (!width || !height || !covers(width, w) || !covers(height, h)) continue;
    const fill = /\bfill="([^"]+)"/.exec(rect)?.[1];
    if (!fill) continue;
    if (HEX.test(fill)) return { rect, colour: fill };
    const ref = /^url\(#([^)]+)\)$/.exec(fill);
    if (!ref) continue;
    const gradient = new RegExp(`<(?:linear|radial)Gradient[^>]*id="${ref[1]}"[\\s\\S]*?</(?:linear|radial)Gradient>`).exec(svg);
    const stop = gradient && /stop-color="(#[0-9a-fA-F]{3,8})"/.exec(gradient[0])?.[1];
    if (stop) return { rect, colour: stop };
  }
  return null;
}

/**
 * Turn a figure drawn for a screen into one drawn for paper. A figure that was already light is
 * returned untouched — inverting it would be the very fault this exists to prevent.
 */
export function lightenForPrint(svg) {
  const backdrop = backdropColour(svg);
  if (!backdrop) return { svg, lightened: false };
  const lum = luminance(backdrop.colour);
  if (lum === null || lum >= 0.35) return { svg, lightened: false };

  let out = svg.replace(PAINT, (_, attr, colour) => `${attr}="${flipLightness(colour)}"`);
  // The ground itself goes to paper rather than to the pale tint the flip would leave, so the
  // largest area on the page costs nothing to print.
  const flippedRect = backdrop.rect.replace(PAINT, (_, attr, colour) => `${attr}="${flipLightness(colour)}"`);
  out = out.replace(flippedRect, flippedRect.replace(/\bfill="[^"]+"/, 'fill="#ffffff"'));
  return { svg: out, lightened: true };
}

const MIME = {
  '.svg': 'image/svg+xml', '.png': 'image/png', '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg', '.gif': 'image/gif', '.webp': 'image/webp',
};

/** An image as a data address, lightened where it was drawn dark. */
export function inlineFigure(path) {
  const kind = extname(path).toLowerCase();
  const mime = MIME[kind];
  if (!mime) throw new Error(`unsupported figure type: ${path}`);
  if (kind !== '.svg') {
    return { uri: `data:${mime};base64,${readFileSync(path).toString('base64')}`, lightened: false };
  }
  const { svg, lightened } = lightenForPrint(readFileSync(path, 'utf8'));
  return { uri: `data:${mime};base64,${Buffer.from(svg, 'utf8').toString('base64')}`, lightened };
}
