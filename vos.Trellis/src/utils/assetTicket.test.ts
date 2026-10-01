import { describe, it, expect } from 'vitest';
import { assetTicketOf } from './assetTicket';

// A ticket is the exact shape Tributary writes when an endpoint keeps what it fetched:
// "sha256:" and the 64 hex digits of the content. Only the whole string counts — a sentence
// that mentions a ticket is prose, not a reference the store can resolve.
describe('assetTicketOf', () => {
  const ticket = 'sha256:' + 'ab'.repeat(32);

  it('recognizes the exact ticket shape', () => {
    expect(assetTicketOf(ticket)).toBe(ticket);
  });

  it('accepts upper-case hex, since the store does', () => {
    const upperCased = 'sha256:' + 'AB'.repeat(32);
    expect(assetTicketOf(upperCased)).toBe(upperCased);
  });

  it.each([
    ['bare hex with no algorithm', 'ab'.repeat(32)],
    ['too few digits', 'sha256:' + 'ab'.repeat(31)],
    ['too many digits', 'sha256:' + 'ab'.repeat(32) + 'a'],
    ['another algorithm', 'sha512:' + 'ab'.repeat(32)],
    ['a sentence mentioning a ticket', `the ticket is sha256:${'ab'.repeat(32)}`],
    ['surrounding whitespace', ` sha256:${'ab'.repeat(32)} `],
    ['digits outside hex', 'sha256:' + 'zz'.repeat(32)],
  ])('rejects %s', (_, value) => {
    expect(assetTicketOf(value)).toBeNull();
  });

  it.each([
    ['a number', 42],
    ['null', null],
    ['an object', { hash: 'sha256:' + 'ab'.repeat(32) }],
  ])('rejects %s, because only a scalar string is a ticket', (_, value) => {
    expect(assetTicketOf(value)).toBeNull();
  });
});
