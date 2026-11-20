import { myceliumApi } from './myceliumApi';
import { declaredPageDescriptor } from '@tendril-ui/tendril';
import { usePlatformPagesStore } from '../stores/platformPagesStore';

/** Once per account. Claimed before the read answers, so two pages mounting together ask once, and the
 *  last account's pages are dropped with the claim, because they were declared for somebody else. */
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
