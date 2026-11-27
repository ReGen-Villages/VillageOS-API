# vos.Pages

**A C# library that lists and reads a model's pages with the binding reader the console uses, so a program that is not a browser answers a page exactly as Trellis draws it.**

Tendril's reader is TypeScript. This library bundles it into one JavaScript file, embeds the file, and runs it in the V8 JavaScript engine inside the calling process. There is one reader, in Tendril, and no second one in C#.

## What it offers

| Call | Answers |
| --- | --- |
| `PageReader.ListPagesAsync` | Each page the model publishes: its identifier, name, the address the console gives it, and whether its description could be read. A page whose description cannot be read is still listed |
| `PageReader.ScopesOfAsync` | The Things a page compares, each of which it can be read for |
| `PageReader.ReadPageAsync` | One page for each scope asked for, worded as the console words it. A figure comes with its value, its worded value, its unit and the lines saying where it came from; a verdict as its sentences and the inputs it offers; a worked figure with its formula and inputs. A widget that draws a chart or a table comes back as what each of its bindings resolved to, with the columns a table declares, and no sentence. The page's Things are fetched once for all the scopes |
| `PageReader.InputsOfAsync` | The inputs at the bottom of a Thing's derived figures: where each value is held, which way the figure moves with it, and the way out of each state that judges it; the inputs with no direction are listed with the reason |
| `PageReader.OriginOfAsync` | Where one value came from: stated, measured, assumed from a kind, or unknown |
| `PageReader.ExplainAsync` | One figure from the model alone: its value, the ranges that judge it, what it was worked out from, and its inputs |
| `PageReader.OfferedChoicesAsync` | The choices an action widget offers on one row. A widget is named by its title, and a row by its identifier or a name only one row has. A listed choice is keyed `listed:<n>`, which holds only while the description is unchanged, and a drawn choice by its Thing |
| `PageReader.ComposePressAsync` | The writes a press makes, sending nothing. It is refused for a choice that asks a secret, before the answers are read, and for an answer missing, not asked for, or naming a Thing its field does not offer |
| `PageReader.SendPressAsync` | Sends the composed writes through `IPageWrites`, stopping at the first one refused |

A null scope reads the page across every Thing it compares. `InputsOfAsync`, `OriginOfAsync` and
`ExplainAsync` need no page, and they answer data, not sentences.

## What it asks of its caller

The library speaks no HTTP. It asks every read of `IPageReads`, which the caller fills:

- `SelectAsync` takes a selector and answers the Things it reaches and the relationships between them. On Mycelium that is `POST /api/snapshots`.
- `GetAsync` takes a path on Mycelium, and `PostAsync` a path and a JSON body. The reader names each path. A page description is the model's data and a service binding names a route, so the caller sends only these five and refuses every other path: `GET /api/states/{state}/things` with a query, `GET /api/things/{identifier}/ranges`, `POST /api/temporal/aggregate`, `POST /api/temporal/reduce` and `POST /api/endpoints/{name}`.

A press is sent through `IPageWrites`. `PressAsync` posts to the route the choice or widget names, `RelateAsync` relates the row to a drawn Thing, `StampAsync` sets the stamped time, and `RecordAction` is told of each post. Its route and identifiers come out of the page description, so the caller decides whether to send each write.

## What it loads and keeps

- A page is read from only the Things its description implies, chosen by Tendril's selector, the rule the console uses for the page's subscription. The roles the console's detail card reads are not asked for.
- Nothing is kept between calls. The Things are indexed for one call and let go at its end.
- One engine serves every call. It lets one caller in at a time; a read that is waiting on `IPageReads` lets the others run.
- The engine's memory is capped by the constant `EngineMemoryCeilingMebibytes` in `PageReader`.
- Where a page description asks a question that cannot be asked, the reader resolves that binding to nothing and says why. In a browser that goes to the console; here it goes to `PageReaderOptions.Warned`.

## Language

- Every call takes a language. Names a page lists are ordered as that language orders them, and never by the machine's own language.
- Results are data.

## The engine

- The engine is the `Microsoft.ClearScript.V8` package, pinned to one exact version in the project file.
- The text an engine produces for a number or a date depends on the locale data that version carries. `Tests/vos.Pages.Tests/Wording` holds the text the pinned version produces; see the README there before changing the version.
- The engine is a native library, one package per operating system and processor.

| Platform | Native package referenced | Proven |
| --- | --- | --- |
| macOS arm64 | Yes | Yes: the library's tests, and every page of a full-size model against a running Mycelium |
| Linux x64 | Yes | Yes: on 2026-11-18 every test of the library, the wording cases and the recorded pages included, passed on the public host (Ubuntu on x86-64, C library 2.43, .NET 10.0.15). The native library needs C library 2.27 or later |
| Windows x64 | No | Not required yet |

The pipeline's agents are macOS, so no build proves Linux x64 again. Run the tests on a Linux x64 machine after changing the engine version.

## The bundle

`reader.bundle.js` is built from `bundle/readerHost.ts` and committed, so building the solution needs no Node.

```sh
cd vos.Tendril && npm ci && cd ..
node vos.Pages/bundle/build.mjs          # rebuild after changing the reader
node vos.Pages/bundle/build.mjs --check  # what the pipeline runs
```

- The pipeline rebuilds the bundle and fails when the committed file differs, so a change to the reader that is not rebuilt cannot merge.
- The bundle is not minified, so its diff can be read.
- The bundle is built from Tendril's reader entry, `@tendril-ui/tendril/reader`, which loads no React. The pipeline type-checks `bundle/readerHost.ts` with no browser types, because the engine has none.
