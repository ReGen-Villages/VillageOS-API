import { create } from 'zustand';
import type { DashboardDescriptor } from '@tendril-ui/tendril';

/** Read once per account rather than per model load, because what the platform declares depends on who asks,
 *  not on which model is open; held here so the navigation and its page read one list. The read lives in
 *  `api/platformPages.ts`, because the public pages share the hooks that read this store and reach no broker. */
interface PlatformPagesState {
  pages: DashboardDescriptor[];
  /** The account the pages were read for, or null while none were. */
  loadedFor: string | null;
}

export const usePlatformPagesStore = create<PlatformPagesState>(() => ({
  pages: [],
  loadedFor: null,
}));
