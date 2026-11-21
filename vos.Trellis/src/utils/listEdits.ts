export function replaceAt<T>(items: readonly T[], index: number, next: T): T[] {
  return items.map((held, at) => (at === index ? next : held));
}

export function dropAt<T>(items: readonly T[], index: number): T[] {
  return items.filter((_, at) => at !== index);
}
