import { beforeEach, describe, expect, it, vi } from 'vitest';

const mocks = vi.hoisted(() => ({
  constructedWith: [] as unknown[],
  getWorker: vi.fn(async () => 'blob:downloaded-from-elsewhere'),
}));

vi.mock('@thatopen/fragments', () => ({
  FragmentsModels: class {
    static getWorker = mocks.getWorker;
    constructor(workerUrl: unknown) {
      mocks.constructedWith.push(workerUrl);
    }
  },
}));

vi.mock('@thatopen/fragments/worker?worker&url', () => ({
  default: '/assets/fragments-worker.mjs',
}));

import { startBimFragments } from './bimFragmentsWorker';

describe('starting the building-model viewer', () => {
  beforeEach(() => {
    mocks.constructedWith.length = 0;
    mocks.getWorker.mockClear();
  });

  it('starts its worker from the file the bundler emitted beside the page', () => {
    startBimFragments();

    expect(mocks.constructedWith).toEqual(['/assets/fragments-worker.mjs']);
  });

  it('never asks the library to fetch the worker, which downloads it from a public package site', () => {
    startBimFragments();

    expect(mocks.getWorker).not.toHaveBeenCalled();
  });
});
