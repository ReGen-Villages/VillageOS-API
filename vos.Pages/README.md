# vos.Pages

**A C# library that lists and reads a model's pages with the binding reader the console uses, so a program that is not a browser answers a page exactly as Trellis draws it.**

Tendril's reader is TypeScript. This library bundles it into one JavaScript file, embeds the file, and runs it in the V8 JavaScript engine inside the calling process. There is one reader, in Tendril, and no second one in C#.

## What it offers

| Call | Answers |
| --- | --- |
| `PageReader.ListPagesAsync` | Each page the model publishes: its identifier, name, the address the console gives it, and whether its description could be read. A page whose description cannot be read is still listed |
| `PageReader.ScopesOfAsync` | The Things a page compares, each of which it can be read for |
| `PageReader.ReadPageAsync` | One page for each scope asked for, as JSON: its sections, each widget's type and title, and every binding's result as data. The page's Things are fetched once for all the scopes |

A null scope reads the page across every Thing it compares.

## What it asks of its caller

The library speaks no HTTP. It asks everything of `IPageReads`, which the caller fills:

- `SelectAsync` takes a selector and answers the Things it reaches and the relationships between them. On Mycelium that is `POST /api/snapshots`.
- The other members are the reads a page makes that its own Things cannot answer: the Things in a state, one Thing's ranges, a reduction over Things by time bucket, a reduction over one property's history, and a service call.

Questions and answers are the JSON Mycelium's routes take and give. The library forwards them and interprets nothing.

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
