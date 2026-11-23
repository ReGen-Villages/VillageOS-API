import type { TableColumn } from '@tendril-ui/tendril';
import type { Row } from '@tendril-ui/tendril';

/** Property columns a breakdown table draws beside the name and the measure. A Thing can hold
 *  dozens, and a table wide enough for all of them is a table nobody reads; the card behind a row
 *  holds the rest. */
const MAXIMUM_PROPERTY_COLUMNS = 6;

interface BreakdownTable {
  columns: TableColumn[];
  /** Properties the rows carry that no column shows, named so a reader knows the table is not the
   *  whole of what a row holds. */
  omitted: string[];
}

/** The name, then the measure a reader opened the figure for, then the properties that tell rows apart, because
 *  one reading the same on every row repeats the sentence above the table. Ties go to the property more rows
 *  carry, then its name, so the same rows draw the same table. Labels after the first are the model's keys,
 *  untranslated. */
export function breakdownTable(rows: Row[], measure: string | null, nameLabel: string): BreakdownTable {
  const rowsHolding = new Map<string, number>();
  const distinct = new Map<string, Set<unknown>>();
  for (const row of rows) {
    for (const [key, value] of Object.entries(row)) {
      if (key === 'id' || key === 'name' || key === measure) continue;
      if (value === null || value === undefined || value === '') continue;
      rowsHolding.set(key, (rowsHolding.get(key) ?? 0) + 1);
      const seen = distinct.get(key);
      if (seen) seen.add(value);
      else distinct.set(key, new Set([value]));
    }
  }
  const ranked = [...rowsHolding.keys()].sort(
    (a, b) =>
      distinct.get(b)!.size - distinct.get(a)!.size ||
      rowsHolding.get(b)! - rowsHolding.get(a)! ||
      a.localeCompare(b),
  );

  const columns: TableColumn[] = [{ key: 'name', label: nameLabel, render: 'id' }];
  if (measure) columns.push({ key: measure, label: measure, numeric: true });
  for (const key of ranked.slice(0, MAXIMUM_PROPERTY_COLUMNS)) {
    columns.push({ key, label: key, numeric: numericThroughout(rows, key) });
  }
  return { columns, omitted: ranked.slice(MAXIMUM_PROPERTY_COLUMNS) };
}

/** Whether every value a column holds is a number. One text value among them makes the whole column
 *  text: right-aligning and sorting it as a number would put the text wherever zero belongs. */
function numericThroughout(rows: Row[], key: string): boolean {
  let carried = false;
  for (const row of rows) {
    const value = row[key];
    if (value === null || value === undefined || value === '') continue;
    if (typeof value !== 'number') return false;
    carried = true;
  }
  return carried;
}
