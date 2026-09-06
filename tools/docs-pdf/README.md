# docs-pdf

Renders a documentation Markdown file to a print-ready PDF, so diagrams and tables land at a
sensible size in a page-oriented reader.

Word processors do not reliably size images imported from Markdown — LibreOffice ignores the
intrinsic dimensions and places them as thumbnails. Producing the PDF here removes the reader's
sizing heuristics from the path entirely.

## Use

```bash
npm install                       # once — Markdown parser and diagram renderer
cd ../../docs                     # the directory holding the Markdown file
node ../tools/docs-pdf/build.mjs VILLAGEOS_FIELD_GUIDE.md ./_pdfbuild.html
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
  --headless --disable-gpu --no-pdf-header-footer --virtual-time-budget=60000 \
  --print-to-pdf="$PWD/VILLAGEOS_FIELD_GUIDE.pdf" "file://$PWD/_pdfbuild.html"
rm _pdfbuild.html
```

**Write the intermediate HTML beside the Markdown file, not to a temporary directory.** Relative
image paths resolve against the HTML, so a file in `/tmp` silently drops every diagram and you get a
PDF that looks fine until you notice the pictures are missing.

`--virtual-time-budget` gives the page time to draw its diagrams before the PDF is captured. Without
it a document with mermaid diagrams prints while they are still blank.

## What it handles

- **Images** — set to the full text-column width with a height cap just under the printable page
  height, so a tall diagram scales down rather than spilling onto the next page.
- **Mermaid diagrams** — ```` ```mermaid ```` fences are drawn by [mermaid](https://mermaid.js.org/)
  in the browser and land in the PDF as vector graphics with selectable text, not as a block of
  source code. The library is inlined into the HTML, so rendering needs no network access.
- **Title** — taken from the document's first `#` heading, so the PDF reports its own name rather
  than inheriting whichever document the tool was last used on.

The stylesheet lives in `build.mjs`.
