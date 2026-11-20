import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalysts.test.fixture';
import { catalystRail, type CatalystRow } from './catalysts';
import { handStateToOrchestrator, type BindingWrites } from './handOver';
import type { PipelineModel } from './model';

function recordingWrites(): BindingWrites & { calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    create: async (subjectId, predicateId, targetId) => { calls.push(`create ${subjectId} ${predicateId} ${targetId}`); },
    remove: async (relationshipId) => { calls.push(`remove ${relationshipId}`); },
  };
}

const rowFor = (model: PipelineModel, thingId: string): CatalystRow =>
  catalystRail(model).flatMap((group) => group.rows).find((each) => each.standsForId === thingId)!;

describe('handing a state to the orchestrator', () => {
  it('binds the connection to the orchestrator, then retracts the binding it had', async () => {
    const { model, id } = catalystFixture();
    const row = rowFor(model, id.runningLow);
    const writes = recordingWrites();

    await handStateToOrchestrator(model, row, writes);

    expect(writes.calls).toEqual([`create ${id.watchesRunningLow} ${id.has} ${id.conductor}`, `remove ${id.runningLowBinding}`]);
  });

  it('retracts nothing for a connection bound to no service', async () => {
    const { model, id } = catalystFixture();
    const row = rowFor(model, id.runningLow);
    const unbound: CatalystRow = { ...row, handling: { connectionId: id.watchesRunningLow, byOrchestrator: false } };
    const writes = recordingWrites();

    await handStateToOrchestrator(model, unbound, writes);

    expect(writes.calls).toEqual([`create ${id.watchesRunningLow} ${id.has} ${id.conductor}`]);
  });

  it('writes nothing for a row the orchestrator is not offered on', async () => {
    const { model, id } = catalystFixture();
    const writes = recordingWrites();

    for (const row of [rowFor(model, id.hourlyReading), rowFor(model, id.surplus)])
      await handStateToOrchestrator(model, row, writes);

    expect(writes.calls).toEqual([]);
  });
});
