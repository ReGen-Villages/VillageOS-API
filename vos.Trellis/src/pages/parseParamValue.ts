/** JSON where it is valid JSON, else the text as typed, so `["a","b","c"]` runs once per item and `42` is a number. */
export function parseParameterValue(raw: string): unknown {
  const trimmed = raw.trim();
  if (trimmed === '') return '';
  try {
    return JSON.parse(trimmed);
  } catch {
    return raw;
  }
}
