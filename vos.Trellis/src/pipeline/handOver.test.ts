import { describe, it, expect } from 'vitest';
import { catalystFixture } from './catalysts.test.fixture';
import { catalystRail, type CatalystRow } from './catalysts';
import { handStateToOrchestrator, type BindingWrites } from './handOver';

function recordingWrites(): BindingWrites & { calls: string[] } {
  const calls: string[] = [];
  return {
    calls,
    create: async (subjectId, predicateId, targetId) => { calls.push(`create ${subjectId} ${predicateId} ${targetId}`); },
    remove: async (relationshipId) => { calls.push(`remove ${relationshipId}`); },
  };
}

describe('handing a state to the orchestrator', () => {
  it('binds the connection to the orchestrator, then retracts the binding it had', async () => {
    const { model, id } = catalystFixture();
    const row = catalystRail(model)[1].rows.find((each) => each.standsForId === id.belowReorder)!;
    const writes = recordingWrites();

    await handStateToOrchestrator(model, row, writes);

    expect(writes.calls).toEqual([`create ${id.watchesReorder} ${id.has} ${id.conductor}`, `remove ${id.reorderBinding}`]);
  });

  it('retracts nothing for a connection bound to no service', async () => {
    const { model, id } = catalystFixture();
    const row = catalystRail(model)[1].rows.find((each) => each.standsForId === id.belowReorder)!;
    const unbound: CatalystRow = { ...row, handling: { connectionId: id.watchesReorder, byOrchestrator: false } };
    const writes = recordingWrites();

    await handStateToOrchestrator(model, unbound, writes);

    expect(writes.calls).toEqual([`create ${id.watchesReorder} ${id.has} ${id.conductor}`]);
  });

  it('writes nothing for a row the orchestrator is not offered on', async () => {
    const { model, id } = catalystFixture();
    const [external, internal] = catalystRail(model);
    const writes = recordingWrites();

    for (const row of [external.rows[0], internal.rows.find((each) => each.standsForId === id.surplus)!])
      await handStateToOrchestrator(model, row, writes);

    expect(writes.calls).toEqual([]);
  });
});
