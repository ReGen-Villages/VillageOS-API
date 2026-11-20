/**
 * The order that decides which of two same-distance sources a Thing takes a value from. It compares
 * names code unit by code unit, so every reader resolves the same value: an order that followed a
 * language would let two people reading one model in different languages see different values.
 */
export function inPrecedenceOrder(a: string, b: string): number {
  return a < b ? -1 : a > b ? 1 : 0;
}
