// A wait inside a test (findBy…, waitFor) gets a fifth of the test's own limit: one second of the
// five a test has by default, which is the testing library's own default. The build agent gives a
// test a longer limit because it is shared and often busy, and a wait that still gave up after one
// second would fail there for the same reason.
const WAITS_THAT_FIT_IN_ONE_TEST = 5;

export function waitLimitWithin(testLimitMilliseconds: number): number {
  return testLimitMilliseconds / WAITS_THAT_FIT_IN_ONE_TEST;
}
