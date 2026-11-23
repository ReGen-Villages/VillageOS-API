const collators = new Map<string, Intl.Collator>();

/** Compares two names the way a language orders them, for what a reader sees listed. Which of two
 *  sources a value is taken from is never decided by this: see `inPrecedenceOrder`. */
export function compareNamesIn(language: string): (a: string, b: string) => number {
  let collator = collators.get(language);
  if (!collator) {
    collator = new Intl.Collator(language);
    collators.set(language, collator);
  }
  return collator.compare;
}
