# wiki-mirror

Converts a clone of the **Azure DevOps project wiki** into the content the
**GitHub wiki** (`ReGen-Villages/VillageOS-API.wiki`) expects, rewriting every
DevOps-specific link to its GitHub target so visitors who land on GitHub stay on
GitHub.

The Azure DevOps project wiki is canonical. The GitHub wiki is a generated
mirror — **do not edit it directly**; edits are overwritten on the next
main build.

## What it does

`sync-wiki.js <devops-wiki-dir> <output-dir>` walks the source wiki and, for each
page:

- **Decodes percent-encoded filenames** — `API%2DReference.md` → `API-Reference.md`.
- **Flattens subfolders** — `Services/Delta.md` → `Delta.md`. The build fails on
  a name collision rather than silently overwriting.
- **Rewrites internal links** — `[Delta](/Services/Delta)` →
  `[Delta](https://github.com/ReGen-Villages/VillageOS-API/wiki/Delta)`.
- **Rewrites DevOps repo links** — `…/_git/VillageOS-API?path=/docs/X.md` →
  `…/blob/main/docs/X.md`; bare relative `X.md` links → `…/blob/main/docs/X.md`.
- **Rewrites the DevOps `_wiki` root** → the GitHub wiki root.
- **Strips private-only `_boards` links** to plain text (no public equivalent).
- **Converts mermaid** — DevOps `::: mermaid … :::` → GitHub ` ```mermaid … ``` `.
- **Generates `_Sidebar.md`** from the DevOps `.order` files (nested one level).

## Tests

```bash
cd tools/wiki-mirror
node --test
```

The pure transforms (`convert`, `convertMermaid`, `buildSidebar`, `flatName`,
`pageSlug`) are unit-tested in `sync-wiki.test.js`. CI runs this on every build.

## CI integration

The `Mirror Wiki to GitHub` step in [`azure-pipelines.yml`](../../azure-pipelines.yml)
runs on `main`: it clones the DevOps wiki, runs this converter, then force-pushes
the result to the GitHub wiki. `main` is the released branch, and `Publish Docs to
Wiki` runs on it first, so what is cloned here is `main`'s own documentation.

One-time prerequisites:

- **GitHub** — enable *Wikis* in repo Settings → Features (creates `.wiki.git`).
- **`AZURE_DEVOPS_PAT`** — a secret pipeline variable holding a PAT with *Code: Read*
  on the project wiki repo. `System.AccessToken` cannot be used here (it is scoped to
  checked-out repos only, so cloning the wiki with it fails TF401019).
- **`GITHUB_PAT`** — the same secret pipeline variable used by the repo mirror.
