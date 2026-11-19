import type { Binding } from '@tendril-ui/tendril';
import type { BindingResult } from '@tendril-ui/tendril';

export const values = new Map<string, BindingResult>();

/** vi.mock is hoisted above a test file's imports, so a test reaches this through an import inside its factory. */
export async function withAnsweredBindings(importOriginal: () => Promise<typeof import('@tendril-ui/tendril')>) {
  return {
    ...(await importOriginal()),
    useBindings: (bindings: (Binding | undefined)[]) =>
      bindings.map((binding) => ({
        loading: false,
        error: false,
        value: binding ? (values.get(JSON.stringify(binding)) ?? null) : null,
      })),
  };
}
