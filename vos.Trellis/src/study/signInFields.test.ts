import { describe, expect, it } from 'vitest';
import { signInProblems } from './signInFields';

describe('signInProblems', () => {
  it('finds nothing wrong with an address and a project name', () => {
    expect(signInProblems({ emailAddress: ' ana.ferreira@example.pt ', projectName: 'Willow Bend' })).toEqual({
      emailAddress: null,
      projectName: null,
    });
  });

  it('asks for an address that is missing', () => {
    expect(signInProblems({ emailAddress: '  ', projectName: 'Willow Bend' }).emailAddress).toBe('missing');
  });

  it.each(['ana.ferreira', 'ana@', '@example.pt', 'ana ferreira@example.pt', 'ana@exa mple.pt'])(
    'refuses %s as an address',
    (emailAddress) => {
      expect(signInProblems({ emailAddress, projectName: 'Willow Bend' }).emailAddress).toBe('invalid');
    },
  );

  it('asks for a project name that is missing', () => {
    expect(signInProblems({ emailAddress: 'ana.ferreira@example.pt', projectName: '   ' }).projectName).toBe('missing');
  });
});
