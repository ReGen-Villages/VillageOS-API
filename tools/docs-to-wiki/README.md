# docs-to-wiki

Generates the **Azure DevOps project wiki** from this repository's own markdown.

The repository is the source of truth. Every wiki page is produced from a file listed in
[`wiki-map.json`](wiki-map.json), so a page cannot quietly fall behind the document it is
built from — which is exactly what happened before this existed.

**Do not edit pages in the wiki browser.** Edits are overwritten on the next develop build,
and each generated page says so at the top. Edit the document in `docs/` instead.

## What it does

`docs-to-wiki.js <repo-root> <output-dir>` writes a complete wiki tree and, for each page:

- **Adds a banner** naming the source document, so a reader knows where to make a change.
- **Converts diagrams** — a fenced ` ```mermaid ` block becomes the `::: mermaid` block the
  DevOps wiki renders. Ordinary code fences are untouched.
- **Rewrites cross-document links** — `[Tributary](TRIBUTARY.md)` becomes
  `[Tributary](/Services/Tributary)` when the target is a mapped document, and a link to the
  file in the repository when it is not (a roadmap, a licence).
- **Relabels file-name links** — a documentation index that writes `[TRELLIS.md](TRELLIS.md)`
  reads as `[Trellis](/Trellis)` on the wiki.
- **Translates heading anchors** — the two systems slug headings differently: `1. Getting
  Started` is `#1-getting-started` in the repository and `#1.-getting-started` on the wiki.
  Anchors are translated from the target document's real headings, and an anchor that matches
  no heading is left alone rather than guessed at.
- **Copies images** into the wiki's `.attachments` folder and repoints them.

## The manifest

`wiki-map.json` has two lists: `pages` (document → wiki page, in the order they should appear)
and `excluded` (documents that deliberately have no page, each with a reason).

A test fails when a markdown file is in neither list. That is the guard: a new document cannot
be added without someone deciding whether it belongs on the wiki.

Only material that originates in this repository is published. A page with no document behind
it is not carried forward.

## Tests

```bash
cd tools/docs-to-wiki
node --test
```

The transforms are pure functions and are unit-tested; CI runs this on every build.

## How it is published

`publish-wiki.js <generated-dir>` writes the tree to the wiki through its REST API: it uploads
the attachments, writes each page (parents before children), and removes pages no document
produces (deepest first, so a parent never goes while a child hangs off it). A page whose
content already matches is left alone, so a build that changes no documentation adds no
revisions.

The `Publish Docs to Wiki` step in [`azure-pipelines.yml`](../../azure-pipelines.yml) runs both
scripts on `develop`, **before** the `Mirror Wiki to GitHub` step, so one build carries a
documentation change from a merge all the way to the public GitHub wiki.

### Why the API and not git

A project wiki *is* a git repository, so pushing to it needs a token with the **Code** scope —
which on a classic personal access token means write access to every repository in the
organisation, in order to publish one wiki. The REST API needs only **Wiki: Read & Write**,
which grants exactly what its name says. `AZURE_DEVOPS_PAT` therefore needs the Wiki scope;
the mirror step's clone is covered by its existing Code: Read.

The trade is page ordering: the wiki keeps that in `.order` files that only the git path can
write, so the wiki orders pages itself and this tool does not generate them. Content, images
and removals are unaffected.

## Related

[`tools/wiki-mirror`](../wiki-mirror/) does the next hop: DevOps wiki → GitHub wiki.
