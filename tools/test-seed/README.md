# test-seed

*Developer tool — reference for engineers writing tests that need a model.*

Builds the two seeds under [`Tests/vos.Tests.Shared/Fixtures`](../../Tests/vos.Tests.Shared/Fixtures): a small
model of sites, catchments, reservoirs and springs that the tests of the MCP server and of the pages
library read. Plain Python 3.10 or later, with no packages to install.

## Use it

```bash
# write both seeds where the tests read them
python3 build_seed.py

# write them somewhere else
python3 build_seed.py --out /path/to/a/folder

# the tests, which fail when a committed seed is not what the generator writes
python3 -m unittest discover
```

Change the model by editing `build_seed.py`, run it, and commit the script and both seeds together.

## What the seeds hold

| Part | What it is for |
| --- | --- |
| `Site`, `Catchment`, `Reservoir`, `Spring` and their members | Things to read, with sites named outside ASCII so name order is tested |
| Derived figures on `Catchment` | A formula, a total over members, a figure worked out from another figure, an input added and taken away in one formula, and an input reached along two routes that disagree |
| Ranges on `Catchment`, and three range Things | States a page counts, lists and words as a verdict |
| Property-name Things | The model's words for an input in three languages, some with `lowest`, `highest` and `stepSize` |
| One property of each origin on `Ålvik north slope` | Stated, measured, assumed and unknown |
| `surveysCatchment` | A connection a state starts, so a catchment awaiting a survey gets a call record |
| `echo` | The same echo service at a subdomain, which the page's form and one choice post to |
| The `Catchments` page | Every kind of binding the reader resolves, a form, and an action list whose choices come from the model plus three of its own: one states a value, one posts to `echo`, and one posts to `/api/operator-activity`, a platform route that refuses the body a page sends, so a press tool must refuse that choice before sending |
| The `Service` kind | What the platform starts a service from, described below |

The `Service` kind declares what the platform starts a service from: `ExecutablePathTemplate`, a port,
arguments, and `AutoStart` false, so a test host that loads a seed starts nothing. The template is
empty, because where a service is built depends on the machine. With it empty the platform builds no
path and starts no service, so a call record the seed's connection writes stays `Pending`. Whoever runs
the seed with a live service writes the template into its copy, as a deployment does, and the platform
fills `{service}` from the prototype's `ServiceAssembly`. The echo service states its own port nested
under its prototype and then the `Service` kind, the shape the platform reads for a name declared two
kinds up. Once the template is written, the first call to the echo service starts it. Every service a
connection binds is listed, so `GET api/mycelium/services` lists the echo service, reading `Unknown`
until a start is tried, and while the template is empty there is no program for a start to run.

`CatchmentsWithAnUnreadablePage.seed.json` is the same model plus a page whose description is not readable
JSON, for tests of how an unreadable page is listed. The seed validator refuses that page by design, so
only `Catchments.seed.json` has to pass it. `CatchmentsWithAnUnreadablePage.seed.refusals.txt` beside it
lists the one error the validator must answer, a line per error: the platform's check of these seeds
expects a seed with such a file to be refused with exactly those errors, and every other seed to pass.

## Identifiers and shared code

The role and identifier helpers are the playground generator's, imported from
[`../pipeline-playground/generate.py`](../pipeline-playground/generate.py): every identifier is the
version 5 identifier of a stable key, and every role Thing has the identifier of its word.

## Checking a seed

The seed validator lives in the platform repository. From a checkout of it:

```bash
dotnet run --project vos.SeedValidate -- /path/to/VillageOS-API/Tests/vos.Tests.Shared/Fixtures/Catchments.seed.json
```
