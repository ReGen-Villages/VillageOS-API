import { describe, it, expect, beforeAll } from 'vitest';
import { VOS_TYPES } from '../../src/utils/constants';
import { BASE_URL, login } from './signIn';

// The client holds the platform's property type names as a compiled set, which checks the
// names it writes but cannot notice when the platform's own list changes. This reads the list from
// a running platform and compares it, so a type added on one side and not the other fails a build
// rather than surfacing later as a property the GUI mishandles for no visible reason.
//
// Needs a running Mycelium; any model it serves will do, since the route under test describes the
// build, not a model. Run with `npm run test:integration` (sign-in settings in signIn.ts).

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
