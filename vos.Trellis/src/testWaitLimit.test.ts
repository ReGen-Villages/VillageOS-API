import { describe, it, expect } from 'vitest';
import { getConfig } from '@testing-library/react';
import { waitLimitWithin } from './testWaitLimit';

describe('how long a wait inside a test may last', () => {
  it('is one second under the five seconds a test has on a developer\'s machine', () => {
    expect(waitLimitWithin(5_000)).toBe(1_000);
  });

  it('lengthens with the limit the build agent gives a test', () => {
    expect(waitLimitWithin(30_000)).toBe(6_000);
  });

  it('is set for each test from that test\'s own limit', { timeout: 20_000 }, ({ task }) => {
    expect(task.timeout).toBe(20_000);
    expect(getConfig().asyncUtilTimeout).toBe(4_000);
  });
});
