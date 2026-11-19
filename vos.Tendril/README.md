# vos.Tendril — the console's page reader and writing widgets, for any application

**Tendril reads the bindings a page description names and draws the widgets that write, so an
application other than the console draws a model's pages the same way the console does, without a
copy of its own.**

## What it does

- **Reads bindings.** A page description says where each widget's value comes from with a binding
  ([docs/FIELD_GUIDE.md §90](../docs/FIELD_GUIDE.md)). Tendril resolves a binding against the loaded
  model, and against the reads the loaded model cannot answer itself, into a number, a word or rows.
- **Draws the widgets that write** ([docs/FIELD_GUIDE.md §93](../docs/FIELD_GUIDE.md)):
  - the **action list**: a row for each Thing its binding lists, and a button for each choice. A
    press records a decision about that row. The choices may come from the model, a choice may ask
    its own questions and post to its own route, and decided rows can sit behind a switch.
  - the **form**: records something no row on the page lists yet.
  - the **fields** either widget asks a person to fill before a press.
- **Builds what a press posts**: the body, and the route the widget's `via` names. `pressOf` composes
  the writes a press makes and sends nothing; `sendPress` sends them and stops at a refusal.
- **Words a figure**: a number in the format and the language asked for, a verdict's sentence, the
  line saying where a value came from, and what a figure was worked out from. The console's KPI
  card, verdict list and working list take their text from these, and `readWidget` reads a whole
  widget out for a program with no screen.
- **Says which Things a page needs**: `subscriptionForSpecification` turns a page description into
  the selector the platform reads.
- **Names a figure's inputs**: `inputsOf` walks a Thing's derived figures to the inputs at the
  bottom, says which way each moves the figure, and names the ones it leaves out with the reason.

## Two entries

| Import | Carries | For |
| --- | --- | --- |
| `@tendril-ui/tendril` | Everything, the widgets and hooks included | An application that draws pages with React |
| `@tendril-ui/tendril/reader` | The reader, the wording, the selector, the inputs and the press composition | A program with no browser |

Nothing reachable from the reader entry imports React or needs a browser's types. `src/reader.test.ts`
fails if React becomes reachable, and `npm run typecheck` checks the entry with no browser types.

**A change to any file the reader entry reaches needs the page reader's bundle rebuilt.** `vos.Pages`
runs this entry inside a C# process from one built file, `vos.Pages/reader.bundle.js`, which is
committed. The files the entry reaches are the ones `src/reader.ts` lists and what they import: the
`api`, `i18n`, `types`, `utils` and `wording` folders and `widgets/writeRequest.ts`. After changing
one, from the repository root:

```sh
node vos.Pages/bundle/build.mjs
```

and commit the changed bundle with the change. The pipeline builds the bundle again and fails when
the committed file differs, so a change that leaves it behind cannot merge. A change to a widget, a
hook or a test does not reach the entry and needs no rebuild.

## Order and language

- **Names a reader sees listed follow the language of the context**: the pages, the Things a page
  compares (as a list to choose from and as the rows of a comparison), the rows of a list of Things,
  and several names joined in one cell. English where the
  context gives no language. A list with a limit keeps the first rows in that language's order.
- **Which of two sibling kinds a Thing takes a value from never follows a language.** The kind whose
  name is last wins, compared code unit by code unit, so every reader resolves the same value.

## What the application that embeds it supplies

- **Who is pressing.** Every press goes out through `press(endpoint, body)` on the `ModelReads` the
  application supplies, optional as `relate` is. The application adds its own credential and session
  there, and may wait for its sign-in before posting. A page that supplies none cannot press: the
  widget says "This page cannot send changes." and sends nothing. A `service` binding never reads
  through `press`. Tendril holds no sign-in of its own.
- **The loaded model and the reads it cannot answer**, through ports the application fills.
- **The words for a refusal.** The endpoint's whole answer goes to `wordRefusal(answer)` on the
  context, beside `wrote`. Without one, the endpoint's own words are shown.
- **The language**, as `language` on the context, and **where a warning goes**, as `warn`. The
  reader warns where a page description asks a question that cannot be asked, and resolves that
  binding to nothing.
- **Colours.** Tendril reads them from CSS custom properties, one per role: `--tendril-text`,
  `--tendril-muted`, `--tendril-refusal`, `--tendril-action`, `--tendril-action-hover`,
  `--tendril-action-text`, `--tendril-field-border` and `--tendril-field`. Each class falls back to
  the console's colour in light and in dark mode. A property is the same in both modes, so an
  application sets it again under its own dark-mode rule where the dark colour differs.
- **Where each widget sits on the page.**

## Why it exists

Another application draws a model's pages besides the console, and it kept its own copy of the
binding reader and the writing widgets. The copies drift apart: every kind of binding added to the
console has to be copied by hand, or the other application cannot draw a page that uses it, and a
fix made in one copy never reaches the other. Installed by version from one place, a binding kind
or a widget feature is built once, and each application takes it when it moves to the version that
carries it. The console uses Tendril itself, so what it draws is what every other application draws.

Tendril holds platform vocabulary only. Every noun a reader sees comes from the page description.

## Translations

Tendril carries its own words under the i18next namespace `tendril`, in every language the console
offers. An application registers the namespace at start-up and can override any word in it.

## Versions and where it is published

Versions are semantic. While the version starts with `0.`, a minor version may still change a
declaration. Tendril has a version of its own rather than the platform's, because the feed refuses a
version it already holds and each fix needs a version to be published under.

Tendril is published as `@tendril-ui/tendril` to `packages`, the ReGenVillages organization's private
package feed. It reaches no public registry.

### Releasing a version

A release is a merged version change.

1. Change `version` in `package.json`, and run `npm install` here and in `vos.Trellis` so both lock
   files agree.
2. Merge the change into develop through a pull request.
3. Develop's build runs every check the repository has. When they pass, it asks the feed for that
   version and publishes it if the feed does not hold it yet. A build that finds the version already
   there publishes nothing, so building develop again never publishes twice.

### Installing it

An application's project `.npmrc` sends the `@tendril-ui` scope to the feed:

```ini
@tendril-ui:registry=https://pkgs.dev.azure.com/ReGenVillages/_packaging/packages/npm/registry/
```

Each machine signs in with an Azure DevOps personal access token that has the Packaging (Read)
scope, written base64-encoded into the user-level `~/.npmrc`, never into a tracked file:

```ini
//pkgs.dev.azure.com/ReGenVillages/_packaging/packages/npm/registry/:username=ReGenVillages
//pkgs.dev.azure.com/ReGenVillages/_packaging/packages/npm/registry/:_password=<the token, base64-encoded>
//pkgs.dev.azure.com/ReGenVillages/_packaging/packages/npm/registry/:email=npm requires an email but the feed does not use it
```

A pipeline signs in with the `npmAuthenticate` task. One in another Azure DevOps organization gives
that task an npm service connection holding the token.

An application using Tailwind adds `@source "…/node_modules/@tendril-ui/tendril/src";` to its
stylesheet, or Tendril's classes have no rules. The console does this with
`@source "../../vos.Tendril/src";` in `vos.Trellis/src/index.css`.

## Licence

The same as this repository: the GNU Affero General Public License v3 ([LICENSE](../LICENSE)), or
the commercial licence ([LICENSE-COMMERCIAL.md](../LICENSE-COMMERCIAL.md)).
