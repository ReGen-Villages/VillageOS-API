/**
 * Every map service writes coordinates differently, and a link read halfway puts a site somewhere nobody
 * asked for, so this answers with a position only when it read one. A shortened link is told apart from an
 * unknown one, because the browser cannot follow it, and the planner must open it and copy the numbers.
 */

export type MapLinkReading =
  | { readonly kind: 'located'; readonly latitude: number; readonly longitude: number }
  | { readonly kind: 'shortened' }
  | { readonly kind: 'unrecognised' };

/** The browser blocks the redirect to another origin, so the coordinates are behind a request this page
 *  cannot make. */
const SHORTENERS = ['maps.app.goo.gl', 'goo.gl', 'bit.ly', 'tinyurl.com', 'osm.org/go/'];

const PAIR = String.raw`(-?\d+(?:\.\d+)?)`;

/** Ordered: the first that matches wins, and the place pin is read before the viewport centre because
 *  a place link carries both and they are not the same point — the pin is what was searched for. */
const FORMS: readonly RegExp[] = [
  new RegExp(String.raw`!3d${PAIR}!4d${PAIR}`),
  new RegExp(String.raw`[?&]mlat=${PAIR}&mlon=${PAIR}`),
  new RegExp(String.raw`#map=[\d.]+/${PAIR}/${PAIR}`),
  new RegExp(String.raw`@${PAIR},${PAIR}`),
  new RegExp(String.raw`[?&](?:q|ll|center|sll|daddr)=${PAIR}(?:,|%2C)${PAIR}`, 'i'),
  new RegExp(String.raw`^\s*${PAIR}\s*,\s*${PAIR}\s*$`),
];

export function locationFromMapLink(pasted: string): MapLinkReading {
  const text = pasted.trim();
  if (text.length === 0) return { kind: 'unrecognised' };
  if (SHORTENERS.some((host) => text.includes(host))) return { kind: 'shortened' };

  for (const form of FORMS) {
    const match = form.exec(text);
    if (!match) continue;
    const latitude = Number(match[1]);
    const longitude = Number(match[2]);
    if (onEarth(latitude, longitude)) return { kind: 'located', latitude, longitude };
  }
  return { kind: 'unrecognised' };
}

/** A pair outside these bounds is a pair of numbers that happened to sit beside a comma — a zoom level,
 *  a tile index, a price. Refusing it is what keeps a wrong answer from reading like a right one. */
export function onEarth(latitude: number, longitude: number): boolean {
  return (
    Number.isFinite(latitude) &&
    Number.isFinite(longitude) &&
    Math.abs(latitude) <= 90 &&
    Math.abs(longitude) <= 180
  );
}
