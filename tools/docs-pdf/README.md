# docs-pdf

Renders a documentation Markdown file to a print-ready PDF, so diagrams and tables land at a
sensible size in a page-oriented reader.

Word processors do not reliably size images imported from Markdown — LibreOffice ignores the
intrinsic dimensions and places them as thumbnails. Producing the PDF here removes the reader's
sizing heuristics from the path entirely.

## Use

```bash
npm install                       # once — pulls in the Markdown parser
node build.mjs ../../docs/LAND_INTAKE.md /tmp/out.html
"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" \
  --headless --disable-gpu --no-pdf-header-footer \
  --print-to-pdf=../../docs/LAND_INTAKE.pdf "file:///tmp/out.html"
```

Run the HTML step from the directory holding the Markdown file, so its relative image paths resolve.

The stylesheet lives in `build.mjs`. Images are set to the full text-column width with a height cap
just under the printable page height, so a tall diagram scales down rather than spilling.
