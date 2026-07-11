import { describe, it, expect } from 'vitest';
import { EditorHistory, MAX_HISTORY } from './history';

// US #5872 (A): undo history + optimistic-save baseline for the pipeline editor.
// The module is pure and generic over the snapshot type, so it is unit-tested here without React Flow.
type Snap = { v: number };

describe('EditorHistory (US #5872)', () => {
  it('starts empty — nothing to undo', () => {
    const h = new EditorHistory<Snap>();
    expect(h.canUndo()).toBe(false);
    expect(h.size).toBe(0);
    expect(h.undo()).toBeNull();
  });

  it('undo returns the state recorded before the last change (TC #5876)', () => {
    const h = new EditorHistory<Snap>();
    h.record({ v: 1 }); // state before change 1
    expect(h.canUndo()).toBe(true);
    expect(h.undo()).toEqual({ v: 1 });
    expect(h.canUndo()).toBe(false);
  });

  it('undoes multiple changes in LIFO order', () => {
    const h = new EditorHistory<Snap>();
    h.record({ v: 1 });
    h.record({ v: 2 });
    h.record({ v: 3 });
    expect(h.undo()).toEqual({ v: 3 });
    expect(h.undo()).toEqual({ v: 2 });
    expect(h.undo()).toEqual({ v: 1 });
    expect(h.undo()).toBeNull();
  });

  it('caps the history at MAX_HISTORY, dropping the oldest', () => {
    const h = new EditorHistory<Snap>();
    for (let i = 0; i < MAX_HISTORY + 5; i++) h.record({ v: i });
    expect(h.size).toBe(MAX_HISTORY);
    // The 5 oldest were dropped; the newest is on top.
    expect(h.undo()).toEqual({ v: MAX_HISTORY + 4 });
  });

  it('commit sets the baseline and clears the undo stack (TC #5878)', () => {
    const h = new EditorHistory<Snap>();
    h.record({ v: 1 });
    h.record({ v: 2 });
    h.commit({ v: 99 }); // successful save/load
    expect(h.canUndo()).toBe(false);
    expect(h.size).toBe(0);
    expect(h.undo()).toBeNull(); // saved state is the floor
  });

  it('rollbackTarget returns the last committed baseline (TC #5877)', () => {
    const h = new EditorHistory<Snap>();
    expect(h.rollbackTarget()).toBeNull(); // nothing saved yet
    h.commit({ v: 10 });
    h.record({ v: 10 }); // an edit after save
    expect(h.rollbackTarget()).toEqual({ v: 10 }); // roll back to the saved state
  });

  it('accepts an initial baseline (loading an existing pipeline)', () => {
    const h = new EditorHistory<Snap>({ v: 7 });
    expect(h.rollbackTarget()).toEqual({ v: 7 });
    expect(h.canUndo()).toBe(false);
  });
});
