import { describe, it, expect, vi, beforeEach } from 'vitest';

vi.mock('./myceliumApi', () => ({ myceliumApi: { getPages: vi.fn() } }));

import { myceliumApi } from './myceliumApi';
import { usePlatformPagesStore } from '../stores/platformPagesStore';
import { loadPlatformPages } from './platformPages';

const ACCOUNTS = { title: 'Accounts', sections: [] };

describe('loadPlatformPages', () => {
  beforeEach(() => {
    usePlatformPagesStore.setState({ pages: [], loadedFor: null });
    vi.mocked(myceliumApi.getPages).mockReset();
  });

  it('keeps each page the platform declares as a descriptor with its own address, in the order declared', async () => {
    vi.mocked(myceliumApi.getPages).mockResolvedValue([
      { name: 'Accounts', spec: ACCOUNTS },
      { name: 'Signing keys', spec: { title: 'Signing keys', sections: [] } },
    ]);

    await loadPlatformPages('user-1');

    const pages = usePlatformPagesStore.getState().pages;
    expect(pages.map((page) => page.routeKey)).toEqual(['accounts', 'signing-keys']);
    expect(pages[0].name).toBe('Accounts');
    expect(pages[0].spec?.title).toBe('Accounts');
    expect(pages[0].id).not.toBe(pages[1].id);
  });

  it('reads once per signed-in account, and again for another', async () => {
    vi.mocked(myceliumApi.getPages).mockResolvedValue([{ name: 'Accounts', spec: ACCOUNTS }]);

    await loadPlatformPages('user-1');
    await loadPlatformPages('user-1');
    expect(myceliumApi.getPages).toHaveBeenCalledTimes(1);

    await loadPlatformPages('user-2');
    expect(myceliumApi.getPages).toHaveBeenCalledTimes(2);
  });

  it('lists a declared page whose spec could not be read, so the fault is seen rather than the page missing', async () => {
    vi.mocked(myceliumApi.getPages).mockResolvedValue([{ name: 'Accounts', spec: { title: 'Accounts' } }]);

    await loadPlatformPages('user-1');

    expect(usePlatformPagesStore.getState().pages).toHaveLength(1);
    expect(usePlatformPagesStore.getState().pages[0].spec).toBeNull();
  });

  it('holds no page when the platform could not be asked, and asks again next time', async () => {
    vi.mocked(myceliumApi.getPages).mockRejectedValue(new Error('offline'));

    await loadPlatformPages('user-1');
    expect(usePlatformPagesStore.getState().pages).toEqual([]);

    vi.mocked(myceliumApi.getPages).mockResolvedValue([{ name: 'Accounts', spec: ACCOUNTS }]);
    await loadPlatformPages('user-1');
    expect(usePlatformPagesStore.getState().pages).toHaveLength(1);
  });
});
