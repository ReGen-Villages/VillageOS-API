import { describe, it, expect, beforeAll } from 'vitest';
import { subscriptionForSpecification } from '../../src/api/dashboardSubscription';
import { DASHBOARD_SPECIFICATION_PROPERTY, type DashboardSpecification, type VosThing } from '@tendril-ui/tendril';
import type { SubscriptionSelector } from '../../src/types/subscription';
import { BASE_URL, login } from './signIn';

// Only a running platform can say whether it reads the selector the client writes. Each dashboard must be
// sent fewer Things than the model holds, because a subscription that covers everything narrows nothing.
//
// Needs a broker serving a model that publishes a dashboard; VOS_INTEGRATION_MODEL names that model where
// there are several (see signIn.ts). Run with `npm run test:integration`.

interface OpenedSubscription {
  subscriptionId: string;
  watermark: number;
  snapshot: { things: VosThing[]; relationships: { Id: string }[] };
}

async function open(selector: SubscriptionSelector, token: string): Promise<OpenedSubscription> {
  const response = await fetch(`${BASE_URL}/api/subscriptions`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}` },
    body: JSON.stringify(selector),
  });
  if (!response.ok) {
    throw new Error(`The platform refused ${JSON.stringify(selector)}: ${response.status} ${await response.text()}`);
  }
  return (await response.json()) as OpenedSubscription;
}

function release(subscriptionId: string, token: string) {
  return fetch(`${BASE_URL}/api/subscriptions/${subscriptionId}`, {
    method: 'DELETE',
    headers: { Authorization: `Bearer ${token}` },
  });
}

/** JSON in a property, as the client reads it. */
function specificationsIn(things: VosThing[]): { name: string; specification: DashboardSpecification }[] {
  const found: { name: string; specification: DashboardSpecification }[] = [];
  for (const thing of things) {
    const raw = (thing.Properties ?? {})[DASHBOARD_SPECIFICATION_PROPERTY];
    const value = raw && typeof raw === 'object' && 'value' in raw ? (raw as { value: unknown }).value : raw;
    if (typeof value !== 'string') continue;
    try {
      const specification = JSON.parse(value) as DashboardSpecification;
      if (Array.isArray(specification.sections)) found.push({ name: thing.Name, specification });
    } catch { /* a property called `specification` that is not one */ }
  }
  return found;
}

describe('the subscription a dashboard opens', () => {
  let token: string;
  let published: { name: string; specification: DashboardSpecification }[];
  let modelSize: number;

  beforeAll(async () => {
    token = await login();
    const response = await fetch(`${BASE_URL}/api/things`, { headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error(`Could not read the model: ${response.status}`);
    const things = (await response.json()) as VosThing[];
    modelSize = things.length;
    published = specificationsIn(things);
    if (!published.length) {
      throw new Error('The signed-in model publishes no dashboard, so there is no subscription to derive.');
    }
  });

  it('is one the platform reads, and answers with less than the whole model', async () => {
    for (const { name, specification } of published) {
      const opened = await open(subscriptionForSpecification(specification, null), token);
      try {
        expect(opened.snapshot.things.length, `${name} was sent nothing`).toBeGreaterThan(0);
        expect(opened.snapshot.things.length, `${name} was sent the whole model`).toBeLessThan(modelSize);
      } finally {
        await release(opened.subscriptionId, token);
      }
    }
  });

  // The page reads a Thing that a binding names outright only from what it was sent.
  it('carries every Thing the specifications name', async () => {
    for (const { name, specification } of published) {
      const selector = subscriptionForSpecification(specification, null);
      const opened = await open(selector, token);
      try {
        const sent = new Set(opened.snapshot.things.flatMap((thing) => [thing.Id, thing.Name]));
        const named = [...(selector.ids ?? []), ...(selector.names ?? [])];
        expect(named.filter((ref) => !sent.has(ref)), `${name} names these and was not sent them`)
          .toEqual([]);
      } finally {
        await release(opened.subscriptionId, token);
      }
    }
  });
});
