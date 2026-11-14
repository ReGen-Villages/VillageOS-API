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
    press records a decision about that row.
  - the **form**: records something no row on the page lists yet.
  - the **fields** either widget asks a person to fill before a press.
- **Builds what a press posts**: the body, and the route the widget's `via` names.

## What the application that embeds it supplies

- **Who is pressing.** Every press goes out through a send function the application supplies. The
  application adds its own credential and session there, and may wait for its sign-in before
  posting. Tendril holds no sign-in of its own.
- **The loaded model and the reads it cannot answer**, through ports the application fills.
- **The words for a refusal.** The endpoint's whole answer goes to a function the application
  supplies. Without one, the endpoint's own words are shown.
- **Colours.** Tendril reads them from CSS custom properties. Unset, they are the console's.
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
declaration. Tendril is published to the ReGenVillages organization's private package feed;
installing it needs a read-only token for that feed.

## Licence

The same as this repository: the GNU Affero General Public License v3 ([LICENSE](../LICENSE)), or
the commercial licence ([LICENSE-COMMERCIAL.md](../LICENSE-COMMERCIAL.md)).
