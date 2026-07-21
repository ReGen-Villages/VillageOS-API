import type { Resources } from './en';

type DeepPartial<T> = {
  [Key in keyof T]?: T[Key] extends object ? DeepPartial<T[Key]> : T[Key];
};

/**
 * A non-base locale. Every key is optional: a draft translation may leave keys
 * untranslated, and any absent key falls back to the English base locale.
 */
export type PartialResources = DeepPartial<Resources>;
