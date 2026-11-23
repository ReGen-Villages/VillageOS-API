// Undo keeps the editor's state from before each change, and the state the last save or load confirmed is what
// a refused save returns to. The caller passes copies it will not change, and nothing here changes them.

export const MAX_HISTORY = 50;

export class EditorHistory<T> {
  private past: T[] = [];
  private baseline: T | null;

  constructor(baseline: T | null = null) {
    this.baseline = baseline;
  }

  /** The oldest is dropped past MAX_HISTORY. */
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

  /** Null before any save or load. */
  rollbackTarget(): T | null {
    return this.baseline;
  }

  get size(): number {
    return this.past.length;
  }
}
