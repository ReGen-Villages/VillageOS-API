# docs-pdf

**Renders a guide's Markdown as a ReGen-branded PDF.** The Markdown stays the source; the PDF is
built when somebody wants one and is never committed.

```bash
cd tools/docs-pdf
npm install                      # once
node build.mjs ../../docs/TRELLIS.md
```

That writes `docs/TRELLIS.pdf`. A guide in the other repository is rendered the same way, by naming
it:

```bash
node build.mjs ../../../VillageOS/docs/VILLAGEOS_FIELD_GUIDE.md
```

| Option | What it does |
| --- | --- |
| `--out <file.pdf>` | Where the PDF goes. Default: the guide's own name, beside the guide |
| `--keep-html` | Leave the intermediate HTML beside the PDF, to open in a browser |
| `--html-only` | Write the HTML and stop, without starting a browser |
| `--chrome <path>` | The browser to render with. Default: `CHROME_PATH`, else the usual places |
| `--owner <name>` | Who the document belongs to, under the mark on the cover |
| `--notice <text>` | The confidentiality line at the foot of the cover |

It needs Google Chrome or Chromium on the machine. It drives one itself through `puppeteer-core`,
so rendering is one command and a diagram that fails to draw fails the run rather than printing an
empty box.

## What it makes

- **A cover** carrying the ReGen mark, the guide's own title and the standfirst under it, and the
  document name, the date and the notice.
- **A contents page**, generated from the guide's headings rather than from any list the guide
  writes for itself — that list is dropped, so a reader never meets two.
- **A divider page per part**, with the part's ordinal set large, who the part is written for, and
  the chapters it holds.
- **A chapter badge** on every numbered heading, so a cross-reference to "chapter 42" is findable.
- **A running footer** with the guide's name, the page number and the owner.
- **A bookmark outline** in the PDF, so a reader can navigate from the sidebar, and tagged content
  for anything that reads a PDF aloud.

## What it decides for you

**The typography travels with the file.** Three variable faces are embedded, so a guide renders the
same on a machine that has none of them.

**A figure drawn dark is turned light.** The diagrams were drawn for a screen, on a near-black
ground; printing one costs a page of ink a reader did not ask for. A figure whose backdrop is dark
has every colour's lightness turned over — the ground becomes paper, light text becomes ink, and an
accent keeps its hue — and its backdrop is set to white. A figure that was already light is left
exactly as it is: inverting one would be the fault this exists to prevent. `figures.mjs` decides by
reading the backdrop rectangle, not by guessing.

**Every page is light**, for the same reason: the cover, the part dividers, the table headings and
the code blocks are all set in pale teal rather than in a solid fill, so a guide printed whole costs
what its words cost and little more.

**Images are inlined.** Every figure is read from disk and carried inside the file, so the HTML can
be written anywhere and a guide cannot lose a picture to a path that resolved somewhere else. A
figure a guide names and the disk does not hold is drawn as a marked gap and named in the run's
output, rather than going missing quietly.

**Pages are not bled.** The cover and the dividers are panels inside the page margins rather than
full-bleed, so the file prints on a machine that cannot bleed and the footer stays in the margin
where it belongs.

## What a guide has to look like

Nothing, but it gets more if it offers more.

- The first `#` heading is the title. The block quote under it is the standfirst on the cover.
- A heading of the form `Part IV — Something`, `Part 4 — Something` or `Appendix B — Something`
  starts a part. Parts may sit at either of the top two heading levels; whichever level a guide uses
  for them, the level below holds its chapters.
- A chapter heading beginning with `12.` is numbered 12.
- The italic line under a part heading says who the part is for.
- A list under a heading called `Contents` or `Table of contents` is dropped: the contents page is
  generated, and a reader should never meet two.
- A guide with no parts is rendered as prose, with a contents page built from its own top-level
  headings when it has at least four.

## The files

| File | What it holds |
| --- | --- |
| `build.mjs` | The command: Markdown to a document, a document to HTML, HTML to PDF |
| `brand.mjs` | The palette, the embedded faces, what mermaid draws with, and the stylesheet |
| `regen-mark.svg` | The ReGen mark, traced from the brand's own artwork. Replacing this file is how the mark is changed |
| `figures.mjs` | Reading a figure, deciding whether it was drawn dark, and turning it light |
| `docs-pdf.test.mjs` | `npm test` — what the renderer makes of a guide, and what it makes of a figure |
