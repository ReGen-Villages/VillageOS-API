import { create } from 'zustand';
import type { DashboardDescriptor } from '../types/dashboard';

/**
 * The pages the platform declares for the signed-in account, beside the ones the loaded model
 * publishes. What the platform declares depends on who is asking and not on which model is open,
 * so they are read once per account rather than once per model load, and held here so the
 * navigation and the page it points at read one list.
 *
 * State alone: the read that fills it lives with the signed-in application (`api/platformPages.ts`),
 * because the public pages share the hooks that read this store and must reach no broker route.
 */
interface PlatformPagesState {
  pages: DashboardDescriptor[];
  /** The account the pages were read for, or null while none were. */
  loadedFor: string | null;
}

export const usePlatformPagesStore = create<PlatformPagesState>(() => ({
  pages: [],
  loadedFor: null,
}));
