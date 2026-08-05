/** Parse a Params-bar value as JSON when it is valid JSON (lists, numbers, booleans, objects); otherwise keep
 * it as the raw string. Lets a user type `["a","b","c"]` to drive a fan-out, or `42` for a number param. */
export function parseParamValue(raw: string): unknown {
  const trimmed = raw.trim();
  if (trimmed === '') return '';
  try {
    return JSON.parse(trimmed);
  } catch {
    return raw;
  }
}
