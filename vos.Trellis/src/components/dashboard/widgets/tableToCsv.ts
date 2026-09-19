import type { TableColumn } from '../../../types/dashboard';
import type { Row } from '../../../api/dashboardApi';

/** The rows a table shows, as comma-separated text a spreadsheet opens.
 *
 *  Only the columns the table draws, in the order it draws them, with the column labels as the
 *  first line. A number is written as a number rather than as the text the table draws it as: a
 *  spreadsheet given "45%" or "1,234" cannot sum it. Lines end in CRLF and a value holding a comma,
 *  a quote or a line break is quoted with its quotes doubled, which is what every spreadsheet reads
 *  without being asked. */
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
