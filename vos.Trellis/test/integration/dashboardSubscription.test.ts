import { describe, it, expect, beforeAll } from 'vitest';
import { subscriptionForSpec } from '../../src/api/dashboardSubscription';
import { DASHBOARD_SPEC_PROPERTY, type DashboardSpec } from '../../src/types/dashboard';
import type { SubscriptionSelector } from '../../src/types/subscription';
import type { VosThing } from '../../src/types/vos';

// A dashboard now says what it is about and the platform answers with that and no more, which is
// only true if the selector the client writes is one the platform reads. Nothing in the offline
// suite can check that: it asserts the shape of a request nobody sends.
//
// This signs in to a running platform, derives each of its dashboards' subscriptions from the spec
// the model itself carries, and checks the platform answers each with the Things that dashboard
// reads — and with fewer than the model holds, since a subscription that covers everything has
// narrowed nothing.
//
// Needs a running Mycelium serving a model that publishes at least one dashboard.
// Run with `npm run test:integration`.
//   VOS_INTEGRATION_URL       default https://localhost:7243
//   VOS_INTEGRATION_USERNAME  default admin
//   VOS_INTEGRATION_PASSWORD  default admin
//   VOS_INTEGRATION_MODEL     the model to sign in to, when the platform serves several

const BASE_URL = process.env.VOS_INTEGRATION_URL || 'https://localhost:7243';
const USERNAME = process.env.VOS_INTEGRATION_USERNAME || 'admin';
const PASSWORD = process.env.VOS_INTEGRATION_PASSWORD || 'admin';
const MODEL = process.env.VOS_INTEGRATION_MODEL;

interface ModelChoiceResponse {
  models?: { Id: string; Name: string }[];
}

async function requestToken(modelId?: string) {
  const body: Record<string, string> = { Username: USERNAME, Password: PASSWORD };
  if (modelId) body.ModelId = modelId;
  return fetch(`${BASE_URL}/api/auth/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  });
}

/** A platform serving several models wants one named. Which one matters here — it has to be one
 *  that publishes a dashboard — so the caller may name it, and otherwise the first offered is
 *  tried and the run says what it found if that one publishes none. */
async function login(): Promise<string> {
  let response = await requestToken(MODEL);
  if (response.status === 400 && !MODEL) {
    const choice = (await response.clone().json()) as ModelChoiceResponse;
    const offered = choice.models?.[0]?.Id;
    if (offered) response = await requestToken(offered);
  }
  if (!response.ok) {
    throw new Error(`Could not sign in to ${BASE_URL}: ${response.status} ${await response.text()}`);
  }
  return ((await response.json()) as { token: string }).token;
}

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

/** The specs the model publishes, read the way the client reads them: JSON in a property. */
function specsIn(things: VosThing[]): { name: string; spec: DashboardSpec }[] {
  const found: { name: string; spec: DashboardSpec }[] = [];
  for (const thing of things) {
    const raw = (thing.Properties ?? {})[DASHBOARD_SPEC_PROPERTY];
    const value = raw && typeof raw === 'object' && 'value' in raw ? (raw as { value: unknown }).value : raw;
    if (typeof value !== 'string') continue;
    try {
      const spec = JSON.parse(value) as DashboardSpec;
      if (Array.isArray(spec.sections)) found.push({ name: thing.Name, spec });
    } catch { /* a property called `spec` that is not one */ }
  }
  return found;
}

describe('the subscription a dashboard opens', () => {
  let token: string;
  let published: { name: string; spec: DashboardSpec }[];
  let modelSize: number;

  beforeAll(async () => {
    token = await login();
    const response = await fetch(`${BASE_URL}/api/things`, { headers: { Authorization: `Bearer ${token}` } });
    if (!response.ok) throw new Error(`Could not read the model: ${response.status}`);
    const things = (await response.json()) as VosThing[];
    modelSize = things.length;
    published = specsIn(things);
    if (!published.length) {
      throw new Error('The signed-in model publishes no dashboard, so there is no subscription to derive.');
    }
  });

  it('is one the platform reads, and answers with less than the whole model', async () => {
    for (const { name, spec } of published) {
      const opened = await open(subscriptionForSpec(spec, null), token);
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
  it('carries every Thing the specs name', async () => {
    for (const { name, spec } of published) {
      const selector = subscriptionForSpec(spec, null);
      const opened = await open(selector, token);
      try {
        const sent = new Set(opened.snapshot.things.flatMap((thing) => [thing.Id, thing.Name]));
        const named = [...(selector.ids ?? []), ...(selector.names ?? [])];
        // The two names every page asks for are the client's own, not the spec's, and a model need
        // not carry either — a model with no dashboards to list is the case.
        const fromTheSpec = named.filter((ref) => !['Dashboard', 'GUI_Settings'].includes(ref));
        expect(fromTheSpec.filter((ref) => !sent.has(ref)), `${name} names these and was not sent them`)
          .toEqual([]);
      } finally {
        await release(opened.subscriptionId, token);
      }
    }
  });
});
