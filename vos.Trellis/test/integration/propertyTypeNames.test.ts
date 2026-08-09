import { describe, it, expect, beforeAll } from 'vitest';
import { VOS_TYPES } from '../../src/utils/constants';

// The client holds the platform's property type names as a compiled set (#6147), which checks the
// names it writes but cannot notice when the platform's own list changes. This reads the list from
// a running platform and compares it, so a type added on one side and not the other fails a build
// rather than surfacing later as a property the GUI mishandles for no visible reason.
//
// Needs a running Mycelium. Run with `npm run test:integration`.
//   VOS_INTEGRATION_URL       default https://localhost:7243
//   VOS_INTEGRATION_USERNAME  default admin
//   VOS_INTEGRATION_PASSWORD  default admin

const BASE_URL = process.env.VOS_INTEGRATION_URL || 'https://localhost:7243';
const USERNAME = process.env.VOS_INTEGRATION_USERNAME || 'admin';
const PASSWORD = process.env.VOS_INTEGRATION_PASSWORD || 'admin';

interface ModelChoiceResponse {
  models?: { Id: string; Name: string }[];
}

interface TokenResponse {
  token: string;
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

// Signing in wants a model named whenever more than one is loaded. Which one makes no difference
// here — the route under test describes the build, not a model — so the first offered will do.
async function login(): Promise<string> {
  let response = await requestToken();
  if (response.status === 400) {
    const choice = (await response.clone().json()) as ModelChoiceResponse;
    const offered = choice.models?.[0]?.Id;
    if (offered) response = await requestToken(offered);
  }
  if (!response.ok) {
    throw new Error(`Could not sign in to ${BASE_URL}: ${response.status} ${await response.text()}`);
  }
  return ((await response.json()) as TokenResponse).token;
}

// Widened from the literal tuple so both comparisons below read the same way round.
const clientNames: readonly string[] = VOS_TYPES;

describe('the client set and the names the platform accepts', () => {
  let served: string[];

  beforeAll(async () => {
    const token = await login();
    const response = await fetch(`${BASE_URL}/api/properties/types`, {
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!response.ok) {
      throw new Error(`Could not read the property type names: ${response.status} ${await response.text()}`);
    }
    served = (await response.json()) as string[];
  });

  it('has every name the platform accepts', () => {
    const missingFromClient = served.filter((name) => !clientNames.includes(name));
    expect(missingFromClient, 'the platform accepts these types and the client does not name them').toEqual([]);
  });

  // The worse direction of the two: a name the client offers that no write route accepts reaches a
  // user as a type they can pick and then cannot save.
  it('names nothing the platform would reject', () => {
    const unknownToPlatform = clientNames.filter((name) => !served.includes(name));
    expect(unknownToPlatform, 'the client names these types and the platform rejects them').toEqual([]);
  });
});
