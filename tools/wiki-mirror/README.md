# wiki-mirror

Converts the pages [`docs-to-wiki`](../docs-to-wiki/) generates into the content the
**GitHub wiki** (`ReGen-Villages/VillageOS-API.wiki`) expects, rewriting every
DevOps-specific link to its GitHub target so visitors who land on GitHub stay on
GitHub.

The repository is canonical. The GitHub wiki is generated from it — **do not edit
it directly**; edits are overwritten on the next `develop` build.

## What it does

`sync-wiki.js <generated-dir> <manifest> <output-dir>` walks the generated pages and,
for each page:

- **Decodes percent-encoded filenames** — `API%2DReference.md` → `API-Reference.md`.
- **Flattens subfolders** — `Services/Delta.md` → `Delta.md`. The build fails on
  a name collision rather than silently overwriting.
- **Rewrites internal links** — `[Delta](/Services/Delta)` →
  `[Delta](https://github.com/ReGen-Villages/VillageOS-API/wiki/Delta)`.
- **Rewrites bare relative links** — `X.md` → `…/blob/develop/docs/X.md`. The branch is the one
  the build's *Publish the wiki to GitHub* step runs on; a test reads the build file to hold the two
  together.
- **Rewrites the DevOps `_wiki` root** → the GitHub wiki root.
- **Strips private-only `_boards` links** to plain text (no public equivalent).
- **Converts mermaid** — DevOps `::: mermaid … :::` → GitHub ` ```mermaid … ``` `.
- **Carries the images** — copies the wiki's `.attachments` folder into the output and rewrites each
  `/.attachments/<name>` image link to the raw file on the GitHub wiki repository, so the diagrams
  render on GitHub.
- **Generates `_Sidebar.md`** from the manifest's page order, a child indented under its parent.

## Tests

```bash
cd tools/wiki-mirror
node --test
```

The pure transforms (`convert`, `convertMermaid`, `buildSidebar`, `flatName`,
`pageSlug`) are unit-tested in `sync-wiki.test.js`. CI runs this on every build.

## CI integration

Every build runs *Generate and convert the wiki pages, and prove the publishing token* in
[`azure-pipelines.yml`](../../azure-pipelines.yml): it runs `docs-to-wiki` and this converter,
then proves `GITHUB_PAT` with a push that sends nothing. On `develop`, *Publish the wiki to GitHub*
pushes the converted pages, replacing the GitHub wiki's whole content.

One-time prerequisites:

- **GitHub** — enable *Wikis* in repo Settings → Features (creates `.wiki.git`).
- **`GITHUB_PAT`** — a secret pipeline variable holding a classic GitHub token with repo scope,
  which pushes to the GitHub wiki.
