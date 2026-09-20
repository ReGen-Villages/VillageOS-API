import { myceliumApi } from './myceliumApi';
import { declaredPageDescriptor } from './dashboardApi';
import { usePlatformPagesStore } from '../stores/platformPagesStore';

/** Reads the pages the platform declares for the signed-in account into the store, once per
 *  account. Claimed before the read answers, so two pages mounting together ask once between them;
 *  the last account's pages go with the claim, since they were declared for somebody else. */
export async function loadPlatformPages(accountId: string): Promise<void> {
  const store = usePlatformPagesStore;
  if (store.getState().loadedFor === accountId) return;
  store.setState({ loadedFor: accountId, pages: [] });
  try {
    store.setState({ pages: (await myceliumApi.getPages()).map(declaredPageDescriptor) });
  } catch {
    // Left unclaimed, so the next sign-in asks again rather than a session holding no declared page
    // for as long as it lasts.
    store.setState({ pages: [], loadedFor: null });
  }
}
