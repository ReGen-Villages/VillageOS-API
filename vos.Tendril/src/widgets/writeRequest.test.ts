import { describe, it, expect } from 'vitest';
import type { ActionChoice, ActionWidget, FormWidget } from '../types/dashboard';
import type { ModelReads } from '../api/modelReads';
import { choiceLabelOf, complete, formRequest, nameOf, offeredChoices, pressOf, sendPress, type Entered, type OfferedChoice, type PressWrite } from './writeRequest';

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
  const ON_THE_PLATFORM: ActionWidget = { ...DECIDE, writes: { ...DECIDE.writes, via: '/api/springs/inspect' } };

  function posted(widget: ActionWidget, choice: ActionChoice, entered: Entered): Record<string, unknown> {
    const pressed = pressOf(widget, row, { key: 'pressed', choice }, entered, () => undefined);
    const post = 'writes' in pressed ? pressed.writes[0] : undefined;
    if (post?.write !== 'post') throw new Error('the press posted nothing');
    return post.body;
  }

  it('sends the row by name and a choice naming a Thing as the reason', () => {
    expect(posted(DECIDE, DECIDE.writes.choices[0], { inspector: 'Ada' })).toEqual({
      record: 'SPRING-1',
      reason: 'Cleared',
      inspector: 'Ada',
    });
  });

  it('sends a route on the platform the row\'s id as well, and a forwarded endpoint names only', () => {
    expect(posted(ON_THE_PLATFORM, DECIDE.writes.choices[0], { inspector: 'Ada' }).recordId).toBe('s1');
    expect(posted(DECIDE, DECIDE.writes.choices[0], { inspector: 'Ada' })).not.toHaveProperty('recordId');
  });

  it('sends a choice naming an act as the view', () => {
    expect(posted(DECIDE, DECIDE.writes.choices[1], { inspector: 'Ada' })).toEqual({
      view: 'close',
      record: 'SPRING-1',
      inspector: 'Ada',
    });
  });

  it('sends a number as a number and leaves an optional empty field out altogether', () => {
    const body = posted(DECIDE, DECIDE.writes.choices[1], { inspector: 'Ada', litres: '40', note: '   ' });
    expect(body.litres).toBe(40);
    expect(body).not.toHaveProperty('note');
  });

  it('names nobody: no actor and no session travel in the body', () => {
    const body = posted(ON_THE_PLATFORM, DECIDE.writes.choices[0], { inspector: 'Ada' });
    expect(Object.keys(body).sort()).toEqual(['inspector', 'reason', 'record', 'recordId']);
  });

  it('reads the row by the label the widget names, or its id where it has no name', () => {
    expect(nameOf({ ...DECIDE, label: 'flow' }, row)).toBe('12');
    expect(nameOf(DECIDE, { id: 'nameless' })).toBe('nameless');
  });
});

describe('what a form sends', () => {
  it('sends the act as the view, every filled field, and the names chosen for a field taking several', () => {
    expect(formRequest(BOOK, { spring: 'SPRING-1', catchments: ['CATCH-1', 'CATCH-2'], litres: '12.5' }, BOOK.writes.act)).toEqual({
      view: 'book',
      spring: 'SPRING-1',
      catchments: ['CATCH-1', 'CATCH-2'],
      litres: 12.5,
    });
  });

  it('leaves a blank name out of the names chosen, and counts a field holding only blanks as nothing chosen', () => {
    expect(formRequest(BOOK, { spring: 'SPRING-1', catchments: ['CATCH-1', '  ', ''], litres: '3' }, BOOK.writes.act).catchments).toEqual(['CATCH-1']);
    expect(formRequest(BOOK, { spring: 'SPRING-1', catchments: [' '], litres: '3' }, BOOK.writes.act)).not.toHaveProperty('catchments');
    expect(complete(BOOK.fields, { spring: 'SPRING-1', catchments: [' ', ''], litres: '3' })).toBe(false);
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

describe('the choices a row is offered', () => {
  const drawn = [{ id: 'c1', name: 'Cleared' }, { id: 'c2', name: 'Fenced' }];
  const offered = offeredChoices({ ...DECIDE, writes: { ...DECIDE.writes, choicesFrom: { from: { kind: 'thingList', archetype: 'Finding' }, predicate: 'found' } } }, drawn, (row) => String(row.name));

  it('keys a choice the model offers by the Thing it stands for', () => {
    expect(offered.find((choice) => choice.drawnId === 'c2')?.key).toBe('c2');
  });

  it('keys a listed choice by its place in the page description, whatever its label reads in a language', () => {
    expect(offered.map((choice) => [choice.choice.label, choice.key])).toEqual([
      ['Cleared', 'listed:0'],
      ['Fenced', 'c2'],
      ['Close it', 'listed:1'],
    ]);
  });
});

describe('the label of a choice the model offers', () => {
  const worded = { id: 'c1', name: 'Cleared', wording: JSON.stringify({ en: 'Cleared of debris', de: 'Geräumt' }) };

  it('is the drawn Thing’s wording in the language asked', () => {
    expect(choiceLabelOf(worded, 'de')).toBe('Geräumt');
  });

  it('falls back to the wording of the language’s primary subtag, then English', () => {
    expect(choiceLabelOf(worded, 'de-AT')).toBe('Geräumt');
    expect(choiceLabelOf(worded, 'fr')).toBe('Cleared of debris');
  });

  it('is the Thing’s name where the model words it in no language', () => {
    expect(choiceLabelOf({ id: 'c2', name: 'Fenced' }, 'de')).toBe('Fenced');
    expect(choiceLabelOf({ id: 'c3', name: 'Dug', wording: 'not words by language' }, 'en')).toBe('Dug');
  });

  it('is nothing where the Thing has neither wording nor a name, as the console showed it', () => {
    expect(choiceLabelOf({ id: 'c4' }, 'en')).toBe('');
  });
});

describe('the writes a press makes, composed and not sent', () => {
  const row = { id: 's1', name: 'SPRING-1' };
  const predicates = (predicate: string) => (predicate === 'found' ? 'p-found' : undefined);
  const listed = (position: number): OfferedChoice => ({ key: `listed:${position}`, choice: DECIDE.writes.choices[position] });

  it('posts to the route the widget names, with the body the row and the choice make', () => {
    expect(pressOf(DECIDE, row, listed(0), { inspector: 'Ada' }, predicates)).toEqual({
      writes: [{ write: 'post', via: 'inspections', body: { record: 'SPRING-1', reason: 'Cleared', inspector: 'Ada' } }],
    });
  });

  it('posts to the route a choice names for itself', () => {
    const toAPlatformRoute: OfferedChoice = { key: 'listed:1', choice: { ...DECIDE.writes.choices[1], via: '/api/things/close' } };

    expect(pressOf(DECIDE, row, toAPlatformRoute, { inspector: 'Ada' }, predicates)).toEqual({
      writes: [{ write: 'post', via: '/api/things/close', body: { view: 'close', record: 'SPRING-1', recordId: 's1', inspector: 'Ada' } }],
    });
  });

  it('relates the row to the Thing a choice the model offered stands for', () => {
    const drawing: ActionWidget = { ...DECIDE, writes: { ...DECIDE.writes, choicesFrom: { from: { kind: 'thingList', archetype: 'Finding' }, predicate: 'found' } } };

    expect(pressOf(drawing, row, { key: 'c2', choice: { label: 'Fenced' }, drawnId: 'c2' }, {}, predicates)).toEqual({
      writes: [{ write: 'relate', predicate: 'found', subjectId: 's1', predicateId: 'p-found', targetId: 'c2' }],
    });
  });

  it('stamps the row after the write where the widget stamps', () => {
    const stamping: ActionWidget = { ...DECIDE, writes: { ...DECIDE.writes, stamps: 'inspectedAt' } };

    expect(pressOf(stamping, row, listed(0), { inspector: 'Ada' }, predicates)).toEqual({
      writes: [
        { write: 'post', via: 'inspections', body: { record: 'SPRING-1', reason: 'Cleared', inspector: 'Ada' } },
        { write: 'stamp', thingId: 's1', property: 'inspectedAt' },
      ],
    });
  });

  it('composes a press on a row the binding gave no identifier, naming the row by its name alone', () => {
    const toAPlatformRoute: OfferedChoice = { key: 'listed:1', choice: { ...DECIDE.writes.choices[1], via: '/api/things/close' } };

    expect(pressOf(DECIDE, { name: 'SPRING-1' }, toAPlatformRoute, {}, predicates)).toEqual({
      writes: [{ write: 'post', via: '/api/things/close', body: { view: 'close', record: 'SPRING-1' } }],
    });
  });

  it('refuses to relate where the widget draws no choices from the model', () => {
    expect(pressOf({ ...DECIDE, writes: { ...DECIDE.writes, via: undefined as unknown as string } }, row, { key: 'c2', choice: { label: 'Fenced' }, drawnId: 'c2' }, {}, predicates))
      .toEqual({ refusal: 'This page cannot relate a row along “”.' });
  });

  it('refuses to relate along a predicate the model does not hold, and composes nothing', () => {
    const drawing: ActionWidget = { ...DECIDE, writes: { ...DECIDE.writes, choicesFrom: { from: { kind: 'thingList', archetype: 'Finding' }, predicate: 'judgedAs' } } };

    expect(pressOf(drawing, row, { key: 'c2', choice: { label: 'Fenced' }, drawnId: 'c2' }, {}, predicates))
      .toEqual({ refusal: 'This page cannot relate a row along “judgedAs”.' });
  });
});

describe('sending the writes a press composed', () => {
  const post: PressWrite = { write: 'post', via: 'inspections', body: { record: 'SPRING-1' } };
  const relate: PressWrite = { write: 'relate', predicate: 'found', subjectId: 's1', predicateId: 'p-found', targetId: 'c2' };
  const stamp: PressWrite = { write: 'stamp', thingId: 's1', property: 'inspectedAt' };

  function recordingReads(answers: Partial<ModelReads> = {}) {
    const sent: unknown[][] = [];
    const reads = {
      press: async (endpoint: string, body: Record<string, unknown>) => { sent.push(['press', endpoint, body]); return { said: 'Recorded' }; },
      relate: async (subjectId: string, predicateId: string, targetId: string) => { sent.push(['relate', subjectId, predicateId, targetId]); },
      stampTime: async (thingId: string, property: string) => { sent.push(['stamp', thingId, property]); },
      ...answers,
    } as unknown as ModelReads;
    return { reads, sent };
  }

  it('posts, then stamps, and answers what the endpoint said', async () => {
    const { reads, sent } = recordingReads();

    expect(await sendPress([post, stamp], reads)).toEqual({ said: 'Recorded' });
    expect(sent).toEqual([['press', '/api/endpoints/inspections', { record: 'SPRING-1' }], ['stamp', 's1', 'inspectedAt']]);
  });

  it('relates, then stamps', async () => {
    const { reads, sent } = recordingReads();

    expect(await sendPress([relate, stamp], reads)).toEqual({});
    expect(sent).toEqual([['relate', 's1', 'p-found', 'c2'], ['stamp', 's1', 'inspectedAt']]);
  });

  it('stops at a refusal, so a refused press is never stamped', async () => {
    const { reads, sent } = recordingReads({ press: async () => ({ error: 'Not yours to decide' }) });

    expect(await sendPress([post, stamp], reads)).toEqual({ error: 'Not yours to decide' });
    expect(sent).toEqual([]);
  });

  it('answers a relate the platform refused in the platform’s words, and does not stamp', async () => {
    const { reads, sent } = recordingReads({ relate: async () => { throw new Error('The model takes no such relationship'); } });

    expect(await sendPress([relate, stamp], reads)).toEqual({ error: 'The model takes no such relationship' });
    expect(sent).toEqual([]);
  });

  it('answers a refusal that is not an error object in the words it came in', async () => {
    const { reads } = recordingReads({ relate: async () => { throw 'refused by the model'; } });

    expect(await sendPress([relate], reads)).toEqual({ error: 'refused by the model' });
  });

  it('refuses to relate on a page that cannot write, and sends nothing', async () => {
    const { reads, sent } = recordingReads({ relate: undefined });

    expect(await sendPress([relate, stamp], reads)).toEqual({ error: 'This page cannot relate a row along “found”.' });
    expect(sent).toEqual([]);
  });

  it('leaves the stamp out on a page that cannot stamp', async () => {
    const { reads, sent } = recordingReads({ stampTime: undefined });

    expect(await sendPress([post, stamp], reads)).toEqual({ said: 'Recorded' });
    expect(sent).toHaveLength(1);
  });
});
