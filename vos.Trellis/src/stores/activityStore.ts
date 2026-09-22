import { create } from 'zustand';
import type { ActivityEvent } from '../types/mycelium';

const MAX_EVENTS = 200;

interface ActivityState {
  events: ActivityEvent[];
  pushEvent: (event: ActivityEvent) => void;
  clear: () => void;
}

export const useActivityStore = create<ActivityState>((set) => ({
  events: [],
  pushEvent: (event) =>
    set((state) => ({
      events: [...state.events.slice(-(MAX_EVENTS - 1)), event],
    })),
  clear: () => set({ events: [] }),
}));
