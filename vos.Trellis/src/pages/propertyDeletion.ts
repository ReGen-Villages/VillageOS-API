import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { toast } from '../components/common/toastStore';
import i18n from '../i18n';
import type { DeletableEntity } from './graphDeletions';

/** True when the property was deleted; either way the person is told. */
export async function deletePropertyOf(owner: DeletableEntity, ownerId: string, propertyName: string): Promise<boolean> {
  try {
    await (owner === 'thing' ? thingApi : relationshipApi).deleteProperty(ownerId, propertyName);
    toast.success(i18n.t('graph.toast.propertyDeleted', { name: propertyName }));
    return true;
  } catch (err) {
    toast.error(err instanceof Error ? err.message : i18n.t('graph.toast.deleteFailed'));
    return false;
  }
}
