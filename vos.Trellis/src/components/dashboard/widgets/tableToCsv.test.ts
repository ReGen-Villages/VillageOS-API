import { describe, it, expect } from 'vitest';
import type { TableColumn } from '../../../types/dashboard';
import { csvFileName, tableToCsv } from './tableToCsv';

const columns: TableColumn[] = [
  { key: 'name', label: 'Catchment' },
  { key: 'storedLitres', label: 'Stored', numeric: true, format: 'percent' },
];

describe('tableToCsv', () => {
  it('writes the labels first, then only the drawn columns in their order', () => {
    const text = tableToCsv(columns, [{ id: 'c1', name: 'North ridge', storedLitres: 1234, hidden: 'x' }]);

    expect(text).toBe('Catchment,Stored\r\nNorth ridge,1234\r\n');
  });

  it('writes a number as a number, never as the text the table draws it as', () => {
    const text = tableToCsv(columns, [{ name: 'A', storedLitres: 0.456 }]);

    expect(text.split('\r\n')[1]).toBe('A,0.456');
  });

  it.each([
    ['a comma', 'North, ridge', '"North, ridge"'],
    ['a quote', 'the "north" ridge', '"the ""north"" ridge"'],
    ['a line break', 'North\nridge', '"North\nridge"'],
  ])('quotes a value holding %s, doubling its quotes', (_what, value, written) => {
    const text = tableToCsv(columns, [{ name: value, storedLitres: 1 }]);

    expect(text.split('\r\n')[1]).toBe(`${written},1`);
  });

  it('writes nothing for a value the row has no answer for', () => {
    const text = tableToCsv(columns, [{ name: 'A', storedLitres: null }]);

    expect(text.split('\r\n')[1]).toBe('A,');
  });
});

describe('csvFileName', () => {
  it('names the file after the table in words a file system takes', () => {
    expect(csvFileName('Catchments: stored / lost (2026)')).toBe('catchments-stored-lost-2026.csv');
  });

  it('falls back to a plain name when the table has none', () => {
    expect(csvFileName(undefined)).toBe('table.csv');
  });
});
