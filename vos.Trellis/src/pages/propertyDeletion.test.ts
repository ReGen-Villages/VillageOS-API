import { afterEach, describe, expect, it, vi } from 'vitest';

vi.mock('../api/thingApi', () => ({ thingApi: { deleteProperty: vi.fn() } }));
vi.mock('../api/relationshipApi', () => ({ relationshipApi: { deleteProperty: vi.fn() } }));
vi.mock('../components/common/toastStore', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { toast } from '../components/common/toastStore';
import { deletePropertyOf } from './propertyDeletion';

describe('deletePropertyOf', () => {
  afterEach(() => vi.clearAllMocks());

  it("deletes a Thing's property through the Thing's route and says so", async () => {
    vi.mocked(thingApi.deleteProperty).mockResolvedValue(undefined as never);

    expect(await deletePropertyOf('thing', 't1', 'height')).toBe(true);
    expect(thingApi.deleteProperty).toHaveBeenCalledWith('t1', 'height');
    expect(relationshipApi.deleteProperty).not.toHaveBeenCalled();
    expect(toast.success).toHaveBeenCalled();
  });

  it("deletes a relationship's property through the relationship's route", async () => {
    vi.mocked(relationshipApi.deleteProperty).mockResolvedValue(undefined as never);

    expect(await deletePropertyOf('relationship', 'r1', 'weight')).toBe(true);
    expect(relationshipApi.deleteProperty).toHaveBeenCalledWith('r1', 'weight');
  });

  it('says why when the delete is refused, and answers false', async () => {
    vi.mocked(thingApi.deleteProperty).mockRejectedValue(new Error('No such property'));

    expect(await deletePropertyOf('thing', 't1', 'height')).toBe(false);
    expect(toast.error).toHaveBeenCalledWith('No such property');
    expect(toast.success).not.toHaveBeenCalled();
  });
});
