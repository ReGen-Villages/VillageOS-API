import { readFileSync } from 'node:fs';
import { createRequire } from 'node:module';
import { fileURLToPath } from 'node:url';
import { dirname, join } from 'node:path';

const require = createRequire(import.meta.url);
const here = dirname(fileURLToPath(import.meta.url));

/**
 * The ReGen palette. `regen` is the mark's own colour; everything else is mixed toward it, so a
 * page reads as one family rather than as a grey document with a teal accent dropped on it.
 */
export const palette = {
  regen: '#37c2aa',
  regen600: '#22a18a',
  regen700: '#177a6b',
  regen800: '#115a4f',
  forest: '#0b2823',
  forest2: '#134238',
  ink: '#16211e',
  soft: '#556660',
  faint: '#8b9a95',
  line: '#dfeae6',
  tint: '#f2f9f7',
  tint2: '#e6f4f0',
  sand: '#fbf9f4',
  deep: '#054e45',
  amber: '#bf8620',
};

/** A woff2 inlined, so a rendered guide carries its own typography and cannot be re-set by a
 *  machine that happens to lack a face. */
function face(family, file, weightRange) {
  const data = readFileSync(require.resolve(file)).toString('base64');
  return `@font-face{font-family:"${family}";font-style:normal;font-display:block;` +
    `font-weight:${weightRange};src:url(data:font/woff2;base64,${data}) format("woff2-variations");}`;
}

function fonts() {
  return [
    face('ReGen Sans', '@fontsource-variable/inter/files/inter-latin-wght-normal.woff2', '100 900'),
    face('ReGen Serif', '@fontsource-variable/source-serif-4/files/source-serif-4-latin-wght-normal.woff2', '200 900'),
    face('ReGen Mono', '@fontsource-variable/jetbrains-mono/files/jetbrains-mono-latin-wght-normal.woff2', '100 800'),
  ].join('');
}

/**
 * The ReGen mark, traced from the brand's own artwork: a circle quartered by two diameters, each
 * quarter holding one of the things a village is made of — shelter, wind and weather, the network
 * that connects them, and what grows. `regen-mark.svg` is that trace, and replacing the file is how
 * the mark is changed; nothing here draws it.
 */
const artwork = readFileSync(join(here, 'regen-mark.svg'), 'utf8').trim();

export function mark(colour = palette.regen, cls = '') {
  return artwork
    .replace('<svg ', `<svg style="color:${colour}" `)
    .replace('class="mark"', `class="mark ${cls}"`);
}

/** What mermaid draws with, so a diagram belongs to the same document as the prose around it. */
export function mermaidTheme() {
  return {
    theme: 'base',
    themeVariables: {
      fontFamily: '"ReGen Sans", system-ui, sans-serif',
      fontSize: '13px',
      primaryColor: palette.tint2,
      primaryTextColor: palette.ink,
      primaryBorderColor: palette.regen700,
      lineColor: palette.regen700,
      secondaryColor: palette.sand,
      secondaryBorderColor: palette.faint,
      tertiaryColor: '#ffffff',
      tertiaryBorderColor: palette.line,
      background: '#ffffff',
      mainBkg: palette.tint2,
      nodeBorder: palette.regen700,
      clusterBkg: '#f8fbfa',
      clusterBorder: palette.line,
      titleColor: palette.regen800,
      edgeLabelBackground: '#ffffff',
      textColor: palette.ink,
      actorBkg: palette.tint2,
      actorBorder: palette.regen700,
      actorTextColor: palette.ink,
      signalColor: palette.ink,
      signalTextColor: palette.ink,
      labelBoxBkgColor: palette.tint,
      labelBoxBorderColor: palette.regen700,
      noteBkgColor: palette.sand,
      noteBorderColor: palette.amber,
    },
    flowchart: { useMaxWidth: true, htmlLabels: true, curve: 'basis', padding: 10 },
    sequence: { useMaxWidth: true, mirrorActors: false, actorMargin: 60, boxMargin: 8 },
  };
}

export function stylesheet() {
  const p = palette;
  return `
${fonts()}

@page { size: A4; margin: 21mm 19mm 20mm 19mm; }

*, *::before, *::after { box-sizing: border-box; }

:root {
  --regen: ${p.regen};
  --regen-700: ${p.regen700};
  --regen-800: ${p.regen800};
  --forest: ${p.forest};
  --ink: ${p.ink};
  --soft: ${p.soft};
  --faint: ${p.faint};
  --line: ${p.line};
  --tint: ${p.tint};
  --tint-2: ${p.tint2};
  --sand: ${p.sand};
  --amber: ${p.amber};
}

html { -webkit-print-color-adjust: exact; print-color-adjust: exact; }

body {
  margin: 0;
  font-family: "ReGen Serif", Charter, Georgia, serif;
  font-size: 10.4pt;
  line-height: 1.58;
  color: var(--ink);
  font-variant-numeric: oldstyle-nums proportional-nums;
  text-rendering: geometricPrecision;
}

p { margin: 0 0 3.2mm; orphans: 3; widows: 3; }
strong { font-weight: 650; color: var(--ink); }
em { font-style: italic; }
a { color: var(--regen-800); text-decoration: none; border-bottom: 0.4pt solid ${p.tint2}; }

ul, ol { margin: 0 0 3.6mm; padding-left: 5.5mm; }
li { margin-bottom: 1.4mm; padding-left: 0.5mm; }
li::marker { color: var(--regen-700); }
li > p { margin-bottom: 1.4mm; }

hr {
  border: none; height: 0.5pt; background: var(--line);
  margin: 8mm auto; width: 100%;
}

/* ── The cover ───────────────────────────────────────────────────────────── */

.cover {
  break-after: page;
  position: relative;
  width: 100%; height: 250mm;
  padding: 16mm 2mm 6mm;
  display: flex; flex-direction: column;
  color: var(--ink);
}
.cover::before {
  content: ""; position: absolute; top: 0; left: 0; right: 0; height: 3.4mm;
  border-radius: 1.7mm;
  background: linear-gradient(90deg, ${p.deep} 0%, ${p.regen} 100%);
}
.cover-head { display: flex; align-items: center; gap: 6mm; }
.cover-head .mark { width: 26mm; height: 26mm; flex: none; }
.cover-wordmark {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 13pt; font-weight: 650; letter-spacing: 0.22em;
  text-transform: uppercase; color: ${p.deep};
}
.cover-wordmark span {
  display: block; font-size: 8pt; font-weight: 450; letter-spacing: 0.14em;
  color: var(--faint); margin-top: 1.8mm; text-transform: none;
}
.cover-body { margin-top: auto; }
.cover-title {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 40pt; font-weight: 600; line-height: 1.07;
  letter-spacing: -0.022em; margin: 0 0 7mm; color: ${p.deep};
  max-width: 150mm;
}
.cover-rule { width: 26mm; height: 1.2mm; background: ${p.regen}; border-radius: 1mm; margin-bottom: 7mm; }
.cover-lede {
  font-size: 12.5pt; line-height: 1.55; color: var(--soft);
  max-width: 140mm; margin: 0;
}
.cover-lede strong { color: var(--ink); font-weight: 650; }
.cover-foot {
  margin-top: 14mm; padding-top: 5mm;
  border-top: 0.5pt solid var(--line);
  display: flex; justify-content: space-between; gap: 8mm;
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 8pt; color: var(--soft);
}
.cover-foot b {
  display: block; color: var(--regen-700); font-weight: 650;
  margin-bottom: 1.2mm; letter-spacing: 0.12em; text-transform: uppercase; font-size: 7.2pt;
}

/* ── Front matter and contents ───────────────────────────────────────────── */

.section-label {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 8pt; font-weight: 600; letter-spacing: 0.18em;
  text-transform: uppercase; color: var(--regen-700); margin: 0 0 2.5mm;
}

h2.plain-heading {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 19pt; font-weight: 600; letter-spacing: -0.014em;
  margin: 0 0 5mm; color: var(--ink); break-after: avoid;
}

.contents { break-before: page; break-after: page; }
.contents-grid { column-count: 2; column-gap: 9mm; margin-top: 6mm; }
.contents-part { break-inside: avoid; margin-bottom: 6mm; }
.contents-part-label {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 7.4pt; font-weight: 650; letter-spacing: 0.16em;
  text-transform: uppercase; color: var(--regen); margin-bottom: 1mm;
}
.contents-part-title {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 11pt; font-weight: 600; color: var(--ink);
  margin: 0 0 2mm; padding-bottom: 1.6mm; border-bottom: 0.5pt solid var(--line);
}
.contents-chapter {
  display: flex; gap: 2.6mm; font-size: 9pt; line-height: 1.42;
  margin-bottom: 1.1mm; color: var(--soft); break-inside: avoid;
}
.contents-chapter a { color: var(--soft); border: none; }
.contents-chapter .n {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 8pt; font-weight: 600; color: var(--regen-700);
  min-width: 5mm; text-align: right; flex: none;
  font-variant-numeric: tabular-nums lining-nums;
}

/* ── Part dividers ───────────────────────────────────────────────────────── */

.divider {
  break-before: page; break-after: page;
  width: 100%; height: 250mm;
  background: #fff; color: var(--ink);
  padding: 22mm 4mm 6mm;
  display: flex; flex-direction: column;
  position: relative; overflow: hidden;
}
.divider::before {
  content: ""; position: absolute; top: 0; left: 0; right: 0; height: 3.4mm;
  border-radius: 1.7mm;
  background: linear-gradient(90deg, ${p.regen800} 0%, ${p.regen} 100%);
}
.divider-numeral {
  position: absolute; right: 14mm; top: 22mm;
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 150pt; font-weight: 700; line-height: 0.9;
  color: var(--tint); letter-spacing: -0.05em;
  font-variant-numeric: lining-nums;
}
.divider .mark { position: absolute; left: 50%; top: 47%; transform: translate(-50%, -50%); width: 74mm; height: 74mm; opacity: 0.09; }
.divider-label {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 9pt; font-weight: 650; letter-spacing: 0.26em;
  text-transform: uppercase; color: var(--regen-700); margin-bottom: 5mm;
  position: relative; z-index: 1;
}
.divider-title {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 33pt; font-weight: 600; line-height: 1.1; letter-spacing: -0.02em;
  margin: 0 0 5mm; max-width: 140mm; color: var(--ink); position: relative; z-index: 1;
}
.divider-rule { width: 24mm; height: 1mm; background: var(--regen); border-radius: 1mm; margin-bottom: 5mm; position: relative; z-index: 1; }
.divider-audience {
  font-size: 11.5pt; line-height: 1.5; color: var(--soft);
  max-width: 128mm; margin: 0; font-style: italic; position: relative; z-index: 1;
}
.divider-chapters {
  margin-top: auto; padding-top: 7mm; position: relative; z-index: 1;
  border-top: 0.5pt solid var(--line);
  column-count: 2; column-gap: 10mm;
}
.divider-chapter {
  display: flex; gap: 3mm; font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 8.6pt; line-height: 1.5; margin-bottom: 1.4mm;
  color: var(--soft); break-inside: avoid;
}
.divider-chapter .n {
  color: var(--regen-700); font-weight: 650; min-width: 5mm; text-align: right; flex: none;
  font-variant-numeric: tabular-nums lining-nums;
}

/* ── Chapters ────────────────────────────────────────────────────────────── */

.chapter { break-inside: auto; }
h3 {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 15pt; font-weight: 600; line-height: 1.25; letter-spacing: -0.012em;
  color: var(--ink); margin: 9mm 0 3.6mm;
  break-after: avoid; break-inside: avoid;
  display: flex; align-items: flex-start; gap: 3.4mm;
}
.chapter:first-of-type > h3 { margin-top: 0; }
h3 .n {
  flex: none; display: inline-flex; align-items: center; justify-content: center;
  width: 7.4mm; height: 7.4mm; border-radius: 50%;
  background: var(--regen-700); color: #fff;
  font-size: 8.6pt; font-weight: 650; letter-spacing: 0;
  font-variant-numeric: tabular-nums lining-nums;
  margin-top: 0.7mm;
}
h4 {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 10.6pt; font-weight: 650; color: var(--regen-800);
  margin: 6mm 0 2mm; break-after: avoid;
}
h5, h6 {
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 9.4pt; font-weight: 650; color: var(--soft);
  letter-spacing: 0.06em; text-transform: uppercase;
  margin: 5mm 0 1.6mm; break-after: avoid;
}

/* ── Callouts ────────────────────────────────────────────────────────────── */

blockquote {
  margin: 0 0 5mm; padding: 3.6mm 4.6mm;
  background: var(--tint); border-left: 1.1mm solid var(--regen);
  border-radius: 0 1.8mm 1.8mm 0;
  font-size: 10pt; line-height: 1.52; color: var(--ink);
  break-inside: avoid;
}
blockquote > :last-child { margin-bottom: 0; }
blockquote strong:first-child { color: var(--regen-800); }
blockquote code { background: #fff; }

/* ── Tables ──────────────────────────────────────────────────────────────── */

table {
  width: 100%; border-collapse: separate; border-spacing: 0;
  margin: 0 0 5mm;
  font-family: "ReGen Sans", system-ui, sans-serif;
  font-size: 8.6pt; line-height: 1.42;
}
thead { display: table-header-group; }
thead th {
  background: var(--tint-2); color: var(--regen-800);
  font-weight: 650; font-size: 7.6pt; letter-spacing: 0.09em; text-transform: uppercase;
  text-align: left; padding: 2.2mm 2.8mm; vertical-align: bottom;
  border: none; border-bottom: 0.9pt solid var(--regen);
}
thead th:first-child { border-top-left-radius: 1.6mm; border-bottom-left-radius: 0; }
thead th:last-child { border-top-right-radius: 1.6mm; }
tbody td {
  padding: 2.1mm 2.8mm; vertical-align: top;
  border-bottom: 0.4pt solid var(--line);
  color: var(--soft);
}
tbody tr:nth-child(even) td { background: var(--tint); }
tbody tr { break-inside: avoid; }
tbody td:first-child { color: var(--ink); font-weight: 550; }
tbody td strong { font-weight: 650; }
table code { font-size: 8pt; background: rgba(255,255,255,0.8); border: 0.4pt solid var(--line); }
th[align="center"], td[align="center"] { text-align: center; }
th[align="right"], td[align="right"] { text-align: right; }

/* A table of two columns is a glossary: the term carries the weight, the meaning does not. */
table.pairs tbody td:first-child { width: 32%; }

/* ── Code ────────────────────────────────────────────────────────────────── */

code {
  font-family: "ReGen Mono", "SF Mono", Menlo, Consolas, monospace;
  font-size: 8.7pt; font-variant-numeric: normal;
  background: var(--tint-2); color: var(--regen-800);
  padding: 0.3mm 1.1mm; border-radius: 0.9mm;
  word-break: break-word;
}
pre:not(.mermaid) {
  background: var(--tint); border-left: 1.1mm solid var(--regen-700);
  border-radius: 0 1.8mm 1.8mm 0;
  padding: 3.4mm 4.4mm; margin: 0 0 5mm;
  break-inside: avoid; overflow: hidden;
}
pre:not(.mermaid) code {
  background: none; color: var(--ink); padding: 0; border: none;
  font-size: 8.2pt; line-height: 1.5;
  white-space: pre-wrap; word-break: break-word;
}

sub { vertical-align: baseline; font-size: 8.6pt; line-height: 1.5; color: var(--faint); }
sub code { font-size: 7.6pt; }

/* ── Diagrams and images ─────────────────────────────────────────────────── */

img {
  display: block; width: 100%; height: auto;
  max-height: 180mm; object-fit: contain;
  margin: 5mm auto 6mm; break-inside: avoid;
}
pre.mermaid {
  background: none; padding: 0; margin: 5mm auto 6mm;
  text-align: center; break-inside: avoid;
}
pre.mermaid svg { max-width: 100%; max-height: 195mm; height: auto; }

/* A diagram is worth a whole page where it would otherwise be cut in half. */
.figure-wide { break-inside: avoid; }
`;
}
