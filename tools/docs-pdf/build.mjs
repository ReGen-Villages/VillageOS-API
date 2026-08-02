import { marked } from 'marked';
import { readFileSync, writeFileSync } from 'node:fs';
import { createRequire } from 'node:module';

const src = readFileSync(process.argv[2], 'utf8');
const out = process.argv[3];

const title = (src.match(/^#\s+(.+)$/m)?.[1] ?? out)
  .replace(/[`*_]/g, '')
  .trim();

// Mermaid fences become <pre class="mermaid"> for the browser to draw; everything else
// goes through the normal code path.
marked.use({
  renderer: {
    code({ text, lang }) {
      if (lang !== 'mermaid') return false;
      const escaped = text.replace(/[&<>]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;' })[c]);
      return `<pre class="mermaid">${escaped}</pre>`;
    },
  },
});
marked.setOptions({ gfm: true, breaks: false });
const body = marked.parse(src);

const hasDiagrams = body.includes('<pre class="mermaid">');
const require = createRequire(import.meta.url);
const mermaidScript = hasDiagrams
  ? `<script>${readFileSync(require.resolve('mermaid/dist/mermaid.min.js'), 'utf8')}</script>
<script>
  mermaid.initialize({ startOnLoad: true, theme: 'neutral', securityLevel: 'loose',
                       flowchart: { useMaxWidth: true }, sequence: { useMaxWidth: true } });
</script>`
  : '';

const css = `
@page { size: A4; margin: 18mm 20mm 20mm 20mm; }
:root { --ink:#1b1a18; --soft:#5b5750; --line:#ddd8d0; --accent:#8E2B22; }
* { box-sizing: border-box; }
body {
  font-family: "Charter", "Georgia", serif;
  font-size: 10.5pt; line-height: 1.5; color: var(--ink);
  margin: 0; -webkit-print-color-adjust: exact; print-color-adjust: exact;
}
h1 { font-size: 24pt; line-height: 1.15; margin: 0 0 4mm; letter-spacing: -0.01em; }
h2 {
  font-size: 15pt; margin: 10mm 0 3mm; padding-bottom: 1.5mm;
  border-bottom: 1.5px solid var(--accent); break-after: avoid;
}
h3 { font-size: 12pt; margin: 6mm 0 2mm; break-after: avoid; }
h4 { font-size: 10.5pt; margin: 5mm 0 2mm; break-after: avoid; }
p { margin: 0 0 3mm; orphans: 3; widows: 3; }
ul, ol { margin: 0 0 3mm; padding-left: 6mm; }
li { margin-bottom: 1mm; }
strong { font-weight: 700; }

blockquote {
  margin: 0 0 5mm; padding: 3mm 4mm; background: #faf8f5;
  border-left: 3px solid var(--accent); font-size: 9.5pt;
}
blockquote p:last-child { margin-bottom: 0; }

table {
  width: 100%; border-collapse: collapse; margin: 0 0 5mm;
  font-size: 9pt; break-inside: avoid;
}
th, td {
  border: 1px solid var(--line); padding: 1.8mm 2.5mm;
  text-align: left; vertical-align: top;
}
th { background: #f4f1ec; font-weight: 700; }

code {
  font-family: "SF Mono", Menlo, monospace; font-size: 8.5pt;
  background: #f4f1ec; padding: 0.3mm 1mm; border-radius: 1mm;
}
pre {
  background: #1b1a18; color: #ece8e1; padding: 3.5mm 4mm; border-radius: 2mm;
  overflow-x: hidden; margin: 0 0 5mm; break-inside: avoid;
}
pre code {
  background: none; color: inherit; padding: 0;
  font-size: 8pt; line-height: 1.45; white-space: pre-wrap; word-break: break-word;
}

/* The whole point of the exercise: diagrams fill the text column. */
img {
  display: block; width: 100%; height: auto;
  max-height: 225mm; object-fit: contain;
  margin: 4mm auto 5mm; break-inside: avoid;
}

hr { border: none; border-top: 1px solid var(--line); margin: 7mm 0; }
a { color: var(--accent); text-decoration: none; }
h2 + p > a { word-break: break-word; }

/* Mermaid draws into these; keep a diagram whole and inside the text column. */
pre.mermaid {
  background: none; color: inherit; padding: 0;
  margin: 4mm auto 5mm; text-align: center; break-inside: avoid;
}
pre.mermaid svg { max-width: 100%; max-height: 200mm; height: auto; }
`;

const html = `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>${title}</title>
<style>${css}</style>
</head><body>${body}${mermaidScript}</body></html>`;

writeFileSync(out, html);
console.log(`wrote ${out} (${(html.length / 1024).toFixed(0)} KB)`);
