import { describe, it, expect } from 'vitest';
import type { ActionWidget, FormWidget } from '../../../types/dashboard';
import { actionRequest, complete, formRequest, nameOf } from './writeRequest';

const DECIDE: ActionWidget = {
  type: 'action',
  title: 'Springs to inspect',
  rows: { kind: 'thingList', archetype: 'Spring' },
  asks: [
    { key: 'inspector', label: 'Inspector', kind: 'choice', options: { kind: 'thingList', archetype: 'Person' } },
    { key: 'litres', label: 'Litres', kind: 'number', optional: true },
    { key: 'note', label: 'Note', optional: true },
  ],
  writes: {
    via: 'inspections',
    archetype: 'Inspection',
    predicate: 'about',
    choices: [
      { label: 'Cleared', target: 'Cleared', viaPredicate: 'found' },
      { label: 'Close it', act: 'close' },
    ],
  },
};

const BOOK: FormWidget = {
  type: 'form',
  title: 'Book a reading',
  fields: [
    { key: 'spring', label: 'Spring', kind: 'choice', options: { kind: 'thingList', archetype: 'Spring' } },
    { key: 'catchments', label: 'Catchments', kind: 'multichoice', options: { kind: 'thingList', archetype: 'Catchment' } },
    { key: 'litres', label: 'Litres', kind: 'number' },
    { key: 'note', label: 'Note', optional: true },
  ],
  submit: 'Book',
  preview: { act: 'cover', label: 'What this covers' },
  writes: { via: 'readings', act: 'book', archetype: 'Reading' },
};

describe('what a press on a row sends', () => {
  const row = { id: 's1', name: 'SPRING-1', flow: 12 };

  it('sends the row by name and a choice naming a Thing as the reason', () => {
    expect(actionRequest(DECIDE, row, DECIDE.writes.choices[0], { inspector: 'Ada' })).toEqual({
      record: 'SPRING-1',
      reason: 'Cleared',
      inspector: 'Ada',
    });
  });

  it('sends a choice naming an act as the view', () => {
    expect(actionRequest(DECIDE, row, DECIDE.writes.choices[1], { inspector: 'Ada' })).toEqual({
      view: 'close',
      record: 'SPRING-1',
      inspector: 'Ada',
    });
  });

  it('sends a number as a number and leaves an optional empty field out altogether', () => {
    const body = actionRequest(DECIDE, row, DECIDE.writes.choices[1], { inspector: 'Ada', litres: '40', note: '   ' });
    expect(body.litres).toBe(40);
    expect(body).not.toHaveProperty('note');
  });

  it('names nobody: no actor and no session travel in the body', () => {
    const body = actionRequest(DECIDE, row, DECIDE.writes.choices[0], { inspector: 'Ada' });
    expect(Object.keys(body).sort()).toEqual(['inspector', 'reason', 'record']);
  });

  it('reads the row by the label the widget names, or its id where it has no name', () => {
    expect(nameOf({ ...DECIDE, label: 'flow' }, row)).toBe('12');
    expect(nameOf(DECIDE, { id: 'nameless' })).toBe('nameless');
  });
});

describe('what a form sends', () => {
  it('sends the act as the view, every filled field, and the names chosen for a field taking several', () => {
    expect(formRequest(BOOK, { spring: 'SPRING-1', catchments: ['CATCH-1', 'CATCH-2'], litres: '12.5' })).toEqual({
      view: 'book',
      spring: 'SPRING-1',
      catchments: ['CATCH-1', 'CATCH-2'],
      litres: 12.5,
    });
  });

  it('posts the preview under its own act with what is filled so far', () => {
    expect(formRequest(BOOK, { spring: 'SPRING-1' }, 'cover')).toEqual({ view: 'cover', spring: 'SPRING-1' });
  });
});

describe('whether every value a person must supply has been', () => {
  it('is not complete while a required field is blank or nothing is chosen', () => {
    expect(complete(BOOK.fields, { spring: 'SPRING-1', catchments: [], litres: '3' })).toBe(false);
    expect(complete(BOOK.fields, { spring: ' ', catchments: ['CATCH-1'], litres: '3' })).toBe(false);
  });

  it('is complete once every required field holds something, whatever the optional ones hold', () => {
    expect(complete(BOOK.fields, { spring: 'SPRING-1', catchments: ['CATCH-1'], litres: '3' })).toBe(true);
    expect(complete(undefined, {})).toBe(true);
  });
});
