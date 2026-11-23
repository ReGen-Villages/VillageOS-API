import type { PipelineModel } from './model';
import type { CatalystRow } from './catalysts';

export interface BindingWrites {
  create(subjectId: string, predicateId: string, targetId: string): Promise<unknown>;
  remove(relationshipId: string): Promise<unknown>;
}

/** The new binding is written first, so a failure between the two writes leaves the state sent to a service
 *  rather than to none. A row the orchestrator is not offered on writes nothing. */
export async function handStateToOrchestrator(model: PipelineModel, row: CatalystRow, writes: BindingWrites): Promise<void> {
  const orchestrator = model.orchestratorService();
  const has = model.predicateIdByName('has');
  if (!row.offersOrchestrator || !row.handling || !orchestrator || !has) return;
  await writes.create(row.handling.connectionId, has, orchestrator.Id);
  if (row.handling.bindingId) await writes.remove(row.handling.bindingId);
}
