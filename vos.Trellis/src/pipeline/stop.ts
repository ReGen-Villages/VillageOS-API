import { thingApi } from '../api/thingApi';
import { relationshipApi } from '../api/relationshipApi';
import { PipelineModel, ROLE } from './model';
import i18n from '../i18n';

/** The reason is written after the request is of its kind, which declares it, and before the relationship the
 *  orchestrator reads it on. */
export async function stopRun(runId: string, reason: string, model: PipelineModel): Promise<void> {
  const isId = model.predicateIdByName('is');
  const kind = model.boundTo(ROLE.StopRequest);
  const stops = model.boundTo(ROLE.RunStopping);
  if (!isId || !kind || !stops) throw new Error(i18n.t('pipeline.noStopRoles'));

  const request = await thingApi.create(i18n.t('pipeline.stopRequestName'), false);
  await relationshipApi.create(request.Id, isId, kind);
  await thingApi.setProperty(request.Id, 'reason', 'vos.String', reason);
  await relationshipApi.create(request.Id, stops, runId);
}
