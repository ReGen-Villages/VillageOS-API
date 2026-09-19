import { describe, it, expect } from 'vitest';
import {
  MAXIMUM_SURVEY_BYTES,
  asRefused,
  asSending,
  asShared,
  readyToSend,
  withChosen,
  withDescription,
  withListed,
  withoutQueued,
  type QueuedSurvey,
  type SharedSurvey,
} from './sharedSurveys';

function aFile(name: string, bytes: number): File {
  const file = new File(['x'], name, { type: 'application/pdf' });
  Object.defineProperty(file, 'size', { value: bytes });
  return file;
}

let minted = 0;
const mint = () => `key-${(minted += 1)}`;

describe('choosing survey files', () => {
  it('queues each chosen file waiting, with no description yet', () => {
    const queue = withChosen([], [aFile('soil.pdf', 1024), aFile('water.csv', 2048)], mint, 'too large');

    expect(queue.map((queued) => [queued.file.name, queued.state, queued.description, queued.progress]))
      .toEqual([['soil.pdf', 'waiting', '', 0], ['water.csv', 'waiting', '', 0]]);
    expect(new Set(queue.map((queued) => queued.key)).size).toBe(2);
  });

  it('queues a file over the limit already refused, saying why, beside the others', () => {
    const queue = withChosen([], [aFile('atlas.tif', MAXIMUM_SURVEY_BYTES + 1), aFile('soil.pdf', 1024)], mint, 'too large');

    expect(queue[0]).toMatchObject({ state: 'refused', reason: 'too large' });
    expect(queue[1]).toMatchObject({ state: 'waiting', reason: null });
    expect(readyToSend(queue).map((queued) => queued.file.name)).toEqual(['soil.pdf']);
  });

  it('keeps a file exactly at the limit', () => {
    const queue = withChosen([], [aFile('atlas.tif', MAXIMUM_SURVEY_BYTES)], mint, 'too large');

    expect(queue[0].state).toBe('waiting');
  });

  it('keeps two chosen files of one name apart by their keys', () => {
    const queue = withChosen([], [aFile('survey.pdf', 1), aFile('survey.pdf', 2)], mint, 'too large');
    const described = withDescription(queue, queue[1].key, 'The second one');

    expect(described.map((queued) => queued.description)).toEqual(['', 'The second one']);
  });
});

describe('a queued file on its way', () => {
  const queue: QueuedSurvey[] = withChosen([], [aFile('soil.pdf', 1024)], mint, 'too large');
  const key = queue[0].key;

  it('reports the share of bytes sent, held to the unit interval', () => {
    expect(asSending(queue, key, 0.4)[0]).toMatchObject({ state: 'sending', progress: 0.4 });
    expect(asSending(queue, key, 1.7)[0].progress).toBe(1);
    expect(asSending(queue, key, -1)[0].progress).toBe(0);
  });

  it('is shared whole, or refused with the reason and nothing sent counted', () => {
    expect(asShared(asSending(queue, key, 0.4), key)[0]).toMatchObject({ state: 'shared', progress: 1, reason: null });
    expect(asRefused(asSending(queue, key, 0.4), key, 'Files are not taken here.')[0])
      .toMatchObject({ state: 'refused', progress: 0, reason: 'Files are not taken here.' });
  });

  it('can be taken out of the queue', () => {
    expect(withoutQueued(queue, key)).toEqual([]);
  });
});

describe('the list of what the model holds', () => {
  const survey = (id: string): SharedSurvey =>
    ({ id, fileName: `${id}.pdf`, description: null, contentType: null, sizeBytes: null, sharedAt: null });

  it('joins a listing to what was shared without repeating a document already shown', () => {
    const joined = withListed([survey('a'), survey('b')], [survey('b'), survey('c')]);

    expect(joined.map((listed) => listed.id)).toEqual(['a', 'b', 'c']);
  });
});
