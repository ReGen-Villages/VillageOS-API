export function textOf(properties: Record<string, unknown>, name: string): string | undefined {
  const value = properties[name];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}
