import type { TableColumn } from '@tendril-ui/tendril';
import type { Row } from '@tendril-ui/tendril';

/** A number is written as a number, not as the table draws it, because a spreadsheet cannot sum "45%" or
 *  "1,234". Line endings and quoting follow what every spreadsheet reads without being asked. */
export function tableToCsv(columns: TableColumn[], rows: Row[]): string {
  const lines = [columns.map((column) => cell(column.label))];
  for (const row of rows) lines.push(columns.map((column) => cell(row[column.key])));
  return lines.map((line) => line.join(',') + '\r\n').join('');
}

function cell(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'number') return String(value);
  const text = String(value);
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** The file a table downloads as, named after the table in words a file system takes. */
export function csvFileName(title: string | undefined): string {
  const words = (title ?? '')
    .toLowerCase()
    .replace(/[^\p{L}\p{N}]+/gu, '-')
    .replace(/^-+|-+$/g, '');
  return `${words || 'table'}.csv`;
}
