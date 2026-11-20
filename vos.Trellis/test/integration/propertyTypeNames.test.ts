import { describe, it, expect, beforeAll } from 'vitest';
import { VOS_TYPES } from '../../src/utils/constants';
import { BASE_URL, login } from './signIn';

// The client's compiled list of property type names cannot notice the platform's list changing, so this
// compares the two on a running broker; any model will do.

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

  // The worse direction: a type a person can pick and then cannot save.
  it('names nothing the platform would reject', () => {
    const unknownToPlatform = clientNames.filter((name) => !served.includes(name));
    expect(unknownToPlatform, 'the client names these types and the platform rejects them').toEqual([]);
  });
});
