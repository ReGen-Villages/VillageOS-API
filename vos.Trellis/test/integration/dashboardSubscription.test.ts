import { describe, it, expect, beforeAll } from 'vitest';
import { subscriptionForSpecification } from '../../src/api/dashboardSubscription';
import { DASHBOARD_SPECIFICATION_PROPERTY, type DashboardSpecification, type VosThing } from '@tendril-ui/tendril';
import type { SubscriptionSelector } from '../../src/types/subscription';
import { BASE_URL, login } from './signIn';

// A dashboard now says what it is about and the platform answers with that and no more, which is
// only true if the selector the client writes is one the platform reads. Nothing in the offline
// suite can check that: it asserts the shape of a request nobody sends.
//
// This signs in to a running platform, derives each of its dashboards' subscriptions from the specification
// the model itself carries, and checks the platform answers each with the Things that dashboard
// reads — and with fewer than the model holds, since a subscription that covers everything has
// narrowed nothing.
//
// Needs a running Mycelium serving a model that publishes at least one dashboard; where the platform
// serves several, name that model in VOS_INTEGRATION_MODEL (see signIn.ts), or the run says what it
// found if the first offered publishes none. Run with `npm run test:integration`.

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

/** The specifications the model publishes, read the way the client reads them: JSON in a property. */
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

  // A binding that names a Thing outright is the one case a walk cannot rescue: the page reads that
  // Thing's properties out of what it was sent, and reads nothing if it was not sent.
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
