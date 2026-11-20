// Waits scale with the test's own limit, which the busy build agent raises; a fixed wait would still fail there.
const WAITS_THAT_FIT_IN_ONE_TEST = 5;

const WAIT_LIMIT_OF_A_TEST_WITH_NO_LIMIT_MILLISECONDS = 1_000;

export function waitLimitWithin(testLimitMilliseconds: number): number {
  const testHasALimit = Number.isFinite(testLimitMilliseconds) && testLimitMilliseconds > 0;
  return testHasALimit
    ? testLimitMilliseconds / WAITS_THAT_FIT_IN_ONE_TEST
    : WAIT_LIMIT_OF_A_TEST_WITH_NO_LIMIT_MILLISECONDS;
}
