#!/usr/bin/env node
import { marked } from 'marked';
import { readFileSync, writeFileSync, unlinkSync, existsSync } from 'node:fs';
import { dirname, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import puppeteer from 'puppeteer-core';
import { mark, mermaidTheme, stylesheet, palette } from './brand.mjs';
import { inlineFigure } from './figures.mjs';

const require = createRequire(import.meta.url);

/* ── What the caller asked for ─────────────────────────────────────────────── */

function usage(message) {
  if (message) console.error(`docs-pdf: ${message}\n`);
  console.error(`Render a guide's Markdown as a ReGen-branded PDF.

  node build.mjs <guide.md> [options]

  --out <file.pdf>     where the PDF goes (default: the guide's name, beside it)
  --keep-html          leave the intermediate HTML beside the PDF, to look at in a browser
  --html-only          write the HTML and stop, without starting a browser
  --chrome <path>      the browser to render with (default: CHROME_PATH, else the usual places)
  --owner <name>       who the document belongs to (default: ReGen Villages BV)
  --notice <text>      the line at the foot of the cover (default: Proprietary and confidential)
`);
  process.exit(message ? 2 : 0);
}

function options(argv) {
  const o = { keepHtml: false, htmlOnly: false, owner: 'ReGen Villages BV', notice: 'Proprietary and confidential' };
  const rest = [];
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    if (a === '--help' || a === '-h') usage();
    else if (a === '--keep-html') o.keepHtml = true;
    else if (a === '--html-only') { o.htmlOnly = true; o.keepHtml = true; }
    else if (a === '--out') o.out = argv[++i];
    else if (a === '--chrome') o.chrome = argv[++i];
    else if (a === '--owner') o.owner = argv[++i];
    else if (a === '--notice') o.notice = argv[++i];
    else if (a.startsWith('--')) usage(`unknown option ${a}`);
    else rest.push(a);
  }
  if (rest.length !== 1) usage(rest.length ? 'name one Markdown file' : 'name the Markdown file to render');
  o.input = resolve(rest[0]);
  if (!existsSync(o.input)) usage(`no such file: ${o.input}`);
  o.out = resolve(o.out ?? o.input.replace(/\.md$/i, '.pdf'));
  o.html = o.out.replace(/\.pdf$/i, '.html');
  return o;
}

/* ── Markdown to a document ────────────────────────────────────────────────── */

export const PART = /^(Part\s+(?:[IVXLCDM]+|\d+)|Appendix\s+[A-Z\d]+|Annex\s+[A-Z\d]+)\s*[—–:-]\s*(.+)$/i;
const NUMBERED = /^(\d+)\.\s+(.+)$/;

const ROMAN = { I: 1, V: 5, X: 10, L: 50, C: 100, D: 500, M: 1000 };

/** The figure to set large on a divider: a part's ordinal, an appendix's letter. */
export function numeral(label) {
  const [kind, token = ''] = label.split(/\s+/);
  if (/^appendix$/i.test(kind)) return token;
  if (!/^[IVXLCDM]+$/.test(token)) return token;
  let total = 0;
  for (let i = 0; i < token.length; i++) {
    const here = ROMAN[token[i]], next = ROMAN[token[i + 1]];
    total += next && here < next ? -here : here;
  }
  return String(total);
}

const slug = (text) => text.toLowerCase()
  .replace(/[`*_]/g, '')
  .replace(/[^\w\s-]/g, '')
  .trim().replace(/\s+/g, '-') || 'section';

const escapeHtml = (s) => s.replace(/[&<>"]/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));

/**
 * Read the document's own shape rather than imposing one: a title, the standfirst under it, the
 * front matter, and the parts each holding numbered chapters. A guide written without parts still
 * has every one of those but the parts, and renders as prose with a contents page.
 */
export function readDocument(source) {
  const tokens = marked.lexer(source);
  const doc = { title: null, lede: null, note: null, front: [], parts: [], sections: [] };

  // A guide says at what depth its parts sit rather than being told: one writes them as the top
  // heading under the title, another as the heading below that, and chapters are always one deeper.
  const titleAt = tokens.findIndex((t) => t.type === 'heading' && t.depth === 1);
  const partDepth = tokens
    .filter((t, i) => t.type === 'heading' && i > titleAt && PART.test(t.text))
    .reduce((shallowest, t) => Math.min(shallowest, t.depth), Infinity);
  const chapterDepth = partDepth + 1;
  const topDepth = tokens
    .filter((t, i) => t.type === 'heading' && i > titleAt)
    .reduce((shallowest, t) => Math.min(shallowest, t.depth), Infinity);

  let part = null;
  let chapter = null;
  let seenHeading = false;

  const push = (token) => {
    if (chapter) chapter.tokens.push(token);
    else if (part) part.preamble.push(token);
    else doc.front.push(token);
  };

  for (const token of tokens) {
    if (token.type === 'heading' && token.depth === 1 && !doc.title) { doc.title = token.text; continue; }
    if (!seenHeading && token.type === 'blockquote' && !doc.lede) { doc.lede = token.raw; continue; }
    if (!seenHeading && token.type === 'html' && /<sub[\s>]/i.test(token.raw) && !doc.note) { doc.note = token.raw; continue; }

    if (token.type === 'heading') {
      if (token.depth <= partDepth) seenHeading = true;
      const asPart = token.depth === partDepth && PART.exec(token.text);
      if (asPart) {
        chapter = null;
        part = { label: asPart[1], title: asPart[2], id: slug(token.text), audience: null, preamble: [], chapters: [] };
        doc.parts.push(part);
        continue;
      }
      if (token.depth === chapterDepth && part) {
        const numbered = NUMBERED.exec(token.text);
        chapter = {
          number: numbered ? numbered[1] : null,
          title: numbered ? numbered[2] : token.text,
          id: slug(token.text),
          tokens: [],
        };
        part.chapters.push(chapter);
        continue;
      }
      // With no parts to divide it, a guide is still navigable by its own top-level headings.
      if (!part && token.depth === topDepth) doc.sections.push({ title: token.text, id: slug(token.text) });
      if (token.depth <= partDepth && !part) { doc.front.push(token); continue; }
    }

    // The line under a part heading, set in italics, says who the part is written for.
    if (part && !chapter && !part.audience && token.type === 'paragraph' && /^\*[^*].*\*$/s.test(token.raw.trim())) {
      part.audience = token.text.replace(/^\*|\*$/g, '');
      continue;
    }

    push(token);
  }

const trimRules = (tokens) => {
    // A rule at the end of a section separates it from the next in the source; in print the divider
    // that follows says the same thing, and the rule prints as a stray line at the foot of a page.
    while (tokens.length && ['hr', 'space'].includes(tokens[tokens.length - 1].type)) tokens.pop();
  };
  for (const p of doc.parts) {
    trimRules(p.preamble);
    for (const c of p.chapters) trimRules(c.tokens);
  }
  trimRules(doc.front);

  doc.front = keepEverythingBut(doc.front, (t) => t.type === 'heading' && t.depth === 2 && /^(table of )?contents$/i.test(t.text));
  return doc;
}

/**
 * Everything but the section the test names: the heading it starts at, and every token up to the
 * next heading of the same depth. Blank lines arrive as tokens of their own, so a section cannot be
 * recognised by what sits immediately before it.
 */
function keepEverythingBut(tokens, startsSection) {
  const kept = [];
  let dropping = false;
  for (const token of tokens) {
    if (startsSection(token)) { dropping = true; continue; }
    if (dropping && token.type === 'heading' && token.depth <= 2) dropping = false;
    if (!dropping) kept.push(token);
  }
  return kept;
}

/* ── Markdown to HTML ──────────────────────────────────────────────────────── */

function configureMarked(baseDir, report) {
  marked.use({
    gfm: true,
    breaks: false,
    renderer: {
      code({ text, lang }) {
        if (lang !== 'mermaid') return false;
        return `<pre class="mermaid">${escapeHtml(text)}</pre>`;
      },
      image({ href, title, text }) {
        if (/^(https?:|data:)/i.test(href)) return false;
        const path = resolve(baseDir, decodeURIComponent(href));
        if (!existsSync(path)) {
          report.missing.push(href);
          return `<figure class="figure-missing"><figcaption>Figure not found: ${escapeHtml(href)}</figcaption></figure>`;
        }
        const { uri, lightened } = inlineFigure(path);
        if (lightened) report.lightened.push(basename(path));
        const alt = escapeHtml(text ?? '');
        return `<img src="${uri}" alt="${alt}"${title ? ` title="${escapeHtml(title)}"` : ''}>`;
      },
      heading({ tokens, depth }) {
        const text = this.parser.parseInline(tokens);
        return `<h${depth} id="${slug(this.parser.parseInline(tokens, this.parser.textRenderer))}">${text}</h${depth}>`;
      },
      table(token) {
        const align = (a) => (a ? ` align="${a}"` : '');
        const head = token.header
          .map((cell, i) => `<th${align(token.align[i])}>${this.parser.parseInline(cell.tokens)}</th>`)
          .join('');
        const body = token.rows
          .map((row) => `<tr>${row.map((cell, i) => `<td${align(token.align[i])}>${this.parser.parseInline(cell.tokens)}</td>`).join('')}</tr>`)
          .join('');
        // Two columns is a glossary — a term and what it means — and is set with the term carrying
        // the weight. Anything wider is a table of data and is set evenly.
        const shape = token.header.length === 2 ? ' class="pairs"' : '';
        return `<table${shape}><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
      },
    },
  });
}

const render = (tokens) => (tokens.length ? marked.parse(tokens.map((t) => t.raw).join('')) : '');
const renderRaw = (raw) => (raw ? marked.parse(raw) : '');

/* ── The document as pages ─────────────────────────────────────────────────── */

function cover(doc, o) {
  const rendered = new Date().toLocaleDateString('en-GB', { day: 'numeric', month: 'long', year: 'numeric' });
  const lede = doc.lede
    ? renderRaw(doc.lede.replace(/^>\s?/gm, ''))
    : '';
  return `<section class="cover">
  <div class="cover-head">
    ${mark(palette.regen)}
    <div class="cover-wordmark">ReGen Villages<span>${escapeHtml(o.owner)}</span></div>
  </div>
  <div class="cover-body">
    <h1 class="cover-title">${escapeHtml(doc.title ?? basename(o.input))}</h1>
    <div class="cover-rule"></div>
    <div class="cover-lede">${lede}</div>
  </div>
  <div class="cover-foot">
    <div><b>Document</b>${escapeHtml(basename(o.input))}</div>
    <div><b>Rendered</b>${rendered}</div>
    <div><b>Notice</b>${escapeHtml(o.notice)}</div>
  </div>
</section>`;
}

function contents(doc) {
  if (!doc.parts.length) {
    if (doc.sections.length < 4) return '';
    const rows = doc.sections
      .map((s, i) => `<div class="contents-chapter"><span class="n">${i + 1}</span><a href="#${s.id}">${escapeHtml(s.title)}</a></div>`)
      .join('');
    return `<section class="contents">
  <div class="section-label">Contents</div>
  <h2 class="plain-heading">What is in this guide</h2>
  <div class="contents-grid"><div class="contents-part">${rows}</div></div>
</section>`;
  }
  const parts = doc.parts.map((part) => `
    <div class="contents-part">
      <div class="contents-part-label">${escapeHtml(part.label)}</div>
      <div class="contents-part-title"><a href="#${part.id}">${escapeHtml(part.title)}</a></div>
      ${part.chapters.map((c) => `<div class="contents-chapter"><span class="n">${c.number ?? '·'}</span><a href="#${c.id}">${escapeHtml(c.title)}</a></div>`).join('')}
    </div>`).join('');
  return `<section class="contents">
  <div class="section-label">Contents</div>
  <h2 class="plain-heading">What is in this guide</h2>
  <div class="contents-grid">${parts}</div>
</section>`;
}

function divider(part) {
  const figure = numeral(part.label);
  const chapters = part.chapters.length
    ? `<div class="divider-chapters">${part.chapters
        .map((c) => `<div class="divider-chapter"><span class="n">${c.number ?? '·'}</span><span>${escapeHtml(c.title)}</span></div>`)
        .join('')}</div>`
    : '';
  return `<section class="divider" id="${part.id}">
  <div class="divider-numeral">${escapeHtml(figure)}</div>
  ${mark(palette.regen700)}
  <div class="divider-label">${escapeHtml(part.label)}</div>
  <h2 class="divider-title">${escapeHtml(part.title)}</h2>
  <div class="divider-rule"></div>
  ${part.audience ? `<p class="divider-audience">${escapeHtml(part.audience)}</p>` : ''}
  ${chapters}
</section>`;
}

function chapter(c) {
  const badge = c.number ? `<span class="n">${c.number}</span>` : '';
  return `<section class="chapter">
  <h3 id="${c.id}">${badge}<span>${escapeHtml(c.title)}</span></h3>
  ${render(c.tokens)}
</section>`;
}

function document(doc, o) {
  const front = render(doc.front);
  const body = doc.parts
    .map((part) => divider(part) + render(part.preamble) + part.chapters.map(chapter).join(''))
    .join('');
  const plain = doc.parts.length ? '' : render(doc.front);
  return cover(doc, o)
    + (doc.parts.length ? `<section class="front">${front}${doc.note ? `<p class="front-note">${doc.note}</p>` : ''}</section>` : '')
    + contents(doc)
    + (doc.parts.length ? body : plain);
}

function page(doc, o) {
  const html = document(doc, o);
  const hasDiagrams = html.includes('<pre class="mermaid">');
  const diagrams = hasDiagrams
    ? `<script>${readFileSync(require.resolve('mermaid/dist/mermaid.min.js'), 'utf8')}</script>
<script>
  window.__figuresReady = (async () => {
    mermaid.initialize({ startOnLoad: false, securityLevel: 'loose', ...${JSON.stringify(mermaidTheme())} });
    await mermaid.run({ querySelector: 'pre.mermaid' });
  })();
</script>`
    : '<script>window.__figuresReady = Promise.resolve();</script>';

  return `<!doctype html>
<html lang="en"><head><meta charset="utf-8">
<title>${escapeHtml(doc.title ?? basename(o.input))}</title>
<style>${stylesheet()}
.front { break-after: page; }
.front-note { margin-top: 6mm; }
.figure-missing { border: 0.6pt dashed ${palette.amber}; border-radius: 2mm; padding: 6mm; text-align: center; color: ${palette.amber}; font-family: "ReGen Sans", sans-serif; font-size: 9pt; margin: 5mm 0; }
</style>
</head><body>${html}${diagrams}</body></html>`;
}

/* ── The browser that prints it ────────────────────────────────────────────── */

const CHROME_CANDIDATES = [
  '/Applications/Google Chrome.app/Contents/MacOS/Google Chrome',
  '/Applications/Chromium.app/Contents/MacOS/Chromium',
  '/usr/bin/google-chrome',
  '/usr/bin/google-chrome-stable',
  '/usr/bin/chromium',
  '/usr/bin/chromium-browser',
  '/snap/bin/chromium',
];

function findChrome(given) {
  const candidates = [given, process.env.CHROME_PATH, ...CHROME_CANDIDATES].filter(Boolean);
  const found = candidates.find((p) => existsSync(p));
  if (found) return found;
  console.error(`docs-pdf: no browser to render with. Name one with --chrome or CHROME_PATH.\nLooked in:\n  ${candidates.join('\n  ')}`);
  process.exit(3);
}

const footer = (title) => `<div style="width:100%;box-sizing:border-box;padding:0 19mm;display:flex;align-items:center;justify-content:space-between;font-family:system-ui,'Segoe UI',Helvetica,Arial,sans-serif;font-size:7pt;color:${palette.faint};-webkit-print-color-adjust:exact;">
  <span style="letter-spacing:.06em;">${escapeHtml(title)}</span>
  <span style="color:${palette.regen800};font-weight:600;font-size:8pt;"><span class="pageNumber"></span></span>
  <span style="letter-spacing:.06em;">ReGen Villages</span>
</div>`;

async function toPdf(htmlPath, o, title) {
  const browser = await puppeteer.launch({
    executablePath: findChrome(o.chrome),
    headless: true,
    args: ['--font-render-hinting=none', '--disable-lcd-text'],
  });
  try {
    const page = await browser.newPage();
    page.on('pageerror', (e) => console.error(`  browser: ${e.message}`));
    page.on('console', (m) => { if (m.type() === 'error') console.error(`  browser: ${m.text()}`); });
    await page.goto(`file://${htmlPath}`, { waitUntil: 'load' });

    // Both waits answer with a plain value: a promise for a font set, or for mermaid's own run,
    // cannot cross out of the browser, and the failure that produces reads as an empty object.
    const drawn = await page.evaluate(async () => {
      const expected = document.querySelectorAll('pre.mermaid').length;
      try { await window.__figuresReady; } catch (e) { return { error: String(e?.message ?? e), expected }; }
      await document.fonts.ready;
      return { expected, drawn: document.querySelectorAll('pre.mermaid svg').length };
    });
    if (drawn.error) throw new Error(`the diagrams did not draw: ${drawn.error}`);
    if (drawn.drawn < drawn.expected) throw new Error(`${drawn.expected - drawn.drawn} of ${drawn.expected} diagrams did not draw`);
    if (drawn.expected) console.log(`  drew ${drawn.drawn} diagrams`);
    await page.pdf({
      path: o.out,
      preferCSSPageSize: true,
      printBackground: true,
      displayHeaderFooter: true,
      headerTemplate: '<div></div>',
      footerTemplate: footer(title),
      tagged: true,
      outline: true,
      timeout: 180_000,
    });
  } finally {
    await browser.close();
  }
}

/* ── Run ───────────────────────────────────────────────────────────────────── */

export async function renderGuide(argv) {
  const o = options(argv);
  const source = readFileSync(o.input, 'utf8');
  const report = { missing: [], lightened: [] };
  configureMarked(dirname(o.input), report);

  const doc = readDocument(source);
  const html = page(doc, o);
  writeFileSync(o.html, html);

  const chapters = doc.parts.reduce((n, p) => n + p.chapters.length, 0);
  console.log(`${basename(o.input)}: ${doc.parts.length} parts, ${chapters} chapters, ${(html.length / 1024 / 1024).toFixed(1)} MB of HTML`);
  if (report.lightened.length) console.log(`  turned light for print: ${[...new Set(report.lightened)].join(', ')}`);
  if (report.missing.length) console.log(`  FIGURES NOT FOUND: ${report.missing.join(', ')}`);

  if (o.htmlOnly) {
    console.log(`  wrote ${o.html}`);
    return o;
  }

  await toPdf(o.html, o, doc.title ?? basename(o.input));
  if (!o.keepHtml) unlinkSync(o.html);
  console.log(`  wrote ${o.out}${o.keepHtml ? ` and ${o.html}` : ''}`);
  return o;
}

// Reading this file must not render anything, so its own tests can ask what it makes of a guide.
if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await renderGuide(process.argv.slice(2));
}
