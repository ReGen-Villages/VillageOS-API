# vos.Pages.Tests

**Tests of the library that reads a model's pages inside a C# process.**

## The models the tests read

**A hand-built model.** `HandBuiltModel.cs` builds a small model in the shape Mycelium answers a selection with: a few reservoirs, the page that compares them, and a page whose description cannot be read. `ReadsAnsweredFromMemory.cs` stands in for Mycelium. It serves the tests of the library's own behaviour: one fetch for several scopes, nothing kept, reads started together.

**A recorded model.** Values Mycelium computes, such as which way a derived figure moves with each input and the states a Thing holds, cannot be written by hand correctly. `Recorded/catchments.recorded.json` holds what a running Mycelium answered for the shared test seed `CatchmentsWithAnUnreadablePage.seed.json` (`Tests/vos.Tests.Shared/Fixtures`):

- the Things each selector reached,
- the answer of every read a page made, and the reads that were refused,
- each page as the reader resolved it in Node, for no scope and for each Thing the page compares.

`APageReadFromTheRecordedModelTests` reads every page through the library and expects exactly what Node resolved.

**What answered the recording now in the repository.** The seed as merged in develop at `e3746bd0`, read by Tendril's reader as built into `vos.Pages/reader.bundle.js`, and answered on 2026-11-18 by a Mycelium built from the platform's develop of 2026-11-17 (source at or after `154544e4`), not by a current one. It is recorded again when a current engine is next built on the recording machine.

To write the recording, with `Recorded/record.mjs`:

- **After the seed, the selector or Mycelium changed:** start a Mycelium holding the seed, on scratch directories and a port of its own, and run `RECORDING_ADMIN_PASSWORD=<password> node record.mjs <reader bundle> catchments.recorded.json <Mycelium address> CatchmentsWithAnUnreadablePage.seed.json`. No service needs to run: the page's one service binding is recorded as a read that was refused, which is what a reader meets when a service is down.
- **After only the reader changed:** `node record.mjs <reader bundle> catchments.recorded.json` resolves the pages again from the recording itself, with no server. It stops and says so if the reader now asks something the recording holds no answer for.

Read the diff of the recording either way. A changed answer is a change in what a page says.

## A machine that is not English

`AMachineThatIsNotEnglish.cs` runs every test as a machine set to Swedish would. The engine takes its default language from the machine, and Swedish orders Å and Ö after Z, so a test that the machine's language is never used would pass on an English machine whether or not that is true.

## Wording

`Wording/worded-by-the-pinned-engine.json` holds the text the pinned engine version produces for every number format a page uses, the month and day labels, relative time, name order and page addresses, in every page language. `Wording/cases.js` produces it.

**The rule: a page's text equals what the pinned engine version produces.** Two engines carrying different versions of the Unicode locale data word a few things differently, as two browsers of different ages do.

Known differences from Node 25, which carries newer locale data, all Italian compact numbers:

| Value | Pinned engine | Node 25 |
| --- | --- | --- |
| 999.95 | `1000` | `1K` |
| 1234.5 | `1234,5` | `1,2K` |
| -9876.543 | `-9876,5` | `-9,9K` |

To change the engine version:

1. Change the version in `vos.Pages/vos.Pages.csproj` and run the tests. The wording test fails and writes `Wording/produced-by-this-engine.json` beside the built tests.
2. Copy that file over `Wording/worded-by-the-pinned-engine.json`.
3. Read the diff. Each changed line is a change in what a page says.
