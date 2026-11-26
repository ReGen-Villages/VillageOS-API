# vos.Hypha.Tests

**Tests of the MCP server, Hypha.**

## The recorded reads

`Recorded/show-calls.catchments.recorded.json` holds the reads `show_calls` makes for the catchment
Ålvik south slope of `Catchments.seed.json` (`Tests/vos.Tests.Shared/Fixtures`): the first
`POST api/snapshots` and its answer, the read for what the first named and did not carry, and each of
the Thing's relationships as `GET api/relationships/{id}` answered it. Before the reads, one
relationship was written from the catchment to a reservoir along `echo`.

It was answered on 2026-11-18 by a Mycelium built from the platform's develop of 2026-11-17. The seed
does not say where the echo service's program is, so the call a state started stays `Pending` and the
call a relationship started is `Failed` with the platform's own reason.
`ShowCallsAgainstTheRecordingTests` reads the recording and expects what the tool says about it.

Record it again after the seed, the tool's selectors or Mycelium change in a way that alters what the
tool says, and read the diff: a changed answer is a change in what the tool tells a person.
