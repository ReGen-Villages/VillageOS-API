import type { InheritedPropertySetAtAnInstant, ValuesAtAnInstant } from '../types/vos';

/**
 * Every value an object held at an instant, flat, keyed the way the resolved-properties read keys
 * them: an own property by its name, an override by the path of source names that reaches it
 * (`Reservoir.capacity`). An own property wins over an override of the same name, as it does live.
 */
export function valuesAtAnInstant(held: ValuesAtAnInstant): Record<string, unknown> {
  const values: Record<string, unknown> = {};
  collect(held.InheritedOverrides, '', values);
  Object.assign(values, held.Properties);
  return values;
}

function collect(
  sets: Record<string, InheritedPropertySetAtAnInstant>,
  pathPrefix: string,
  values: Record<string, unknown>,
): void {
  for (const set of Object.values(sets)) {
    const path = pathPrefix ? `${pathPrefix}.${set.SourceName}` : set.SourceName;
    for (const [name, value] of Object.entries(set.Properties)) values[`${path}.${name}`] = value;
    collect(set.Inherited, path, values);
  }
}
