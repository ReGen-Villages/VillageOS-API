// Undo history + optimistic-save baseline for the pipeline editor.
//
// Two jobs, one small structure:
//  - Undo: `record` the editor state *before* each mutation; `undo` pops the last one back.
//  - Optimistic rollback: `commit` the state after a successful save/load as the baseline;
//    `rollbackTarget` returns it so a rejected save can restore the last server-confirmed state.
//
// Generic over the snapshot type and free of React Flow, so it is trivially unit-tested. The caller
// supplies immutable snapshots (e.g. structuredClone of { nodes, edges }); this module never mutates them.

export const MAX_HISTORY = 50;

export class EditorHistory<T> {
  private past: T[] = [];
  private baseline: T | null;

  constructor(baseline: T | null = null) {
    this.baseline = baseline;
  }

  /** Record the state *before* a mutation so `undo` can restore it. Bounded to MAX_HISTORY (oldest dropped). */
  record(previous: T): void {
    this.past.push(previous);
    if (this.past.length > MAX_HISTORY) this.past.shift();
  }

  canUndo(): boolean {
    return this.past.length > 0;
  }

  undo(): T | null {
    return this.past.pop() ?? null;
  }

  commit(state: T): void {
    this.baseline = state;
    this.past = [];
  }

  /** The last server-confirmed state to roll back to when a save is rejected (null before any save/load). */
  rollbackTarget(): T | null {
    return this.baseline;
  }

  get size(): number {
    return this.past.length;
  }
}
