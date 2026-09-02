/** How long a test body that renders the whole multi-step submission form and then drives it is given.
 *
 *  Vitest's default of five seconds is a bound on how busy the build agent is rather than on anything the
 *  form does. The agent is shared, and under contention it has been measured creating each test file's
 *  environment in three to four and a half seconds against about a third of a second on a developer
 *  machine — ten to fifteen times slower. The heaviest of these bodies costs about four tenths of a second
 *  locally, so that much load carries it past five seconds with nothing wrong.
 *
 *  Set well clear of the worst load measured, so a busier agent than that still passes. Raising the
 *  suite-wide `testTimeout` instead would hand the same room to every test in the client, and a test that
 *  genuinely hangs would take this long to say so wherever it was.
 */
export const MULTI_STEP_FORM_TEST_TIMEOUT_MILLISECONDS = 30_000;
