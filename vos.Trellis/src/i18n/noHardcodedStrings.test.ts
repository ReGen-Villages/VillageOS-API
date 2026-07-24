/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, basename } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * Guards that no component reintroduces a hardcoded user-visible English string
 * after the i18n sweep (#6004). Heuristic, not a parser: it flags
 *  - human-facing attribute literals (placeholder / title / aria-label / alt), and
 *  - JSX text nodes of two or more words, or ending in sentence punctuation,
 * that are not wrapped in a t(...) call. Single-word JSX text is intentionally
 * not flagged — it collides with TypeScript generics like `Promise<T>` — so a new
 * one-word label still needs review, but every multi-word string and every
 * user-facing attribute is caught.
 *
 * A finding is silenced only by adding it to ALLOWED with a reason.
 */

const SRC = join(dirname(fileURLToPath(import.meta.url)), '..');

// Exact strings that are legitimately literal. Keep this list short and justified.
const ALLOWED = new Set<string>([
  // Technical example placeholders — field paths and a JSONata snippet, not prose.
  'e.g. user.id',
  'e.g. a',
  'e.g. {"name": firstName & " " & lastName}',
]);

const VISIBLE_ATTRS = ['placeholder', 'title', 'aria-label', 'alt'];
const HAS_LETTER = /[A-Za-z]{2,}/;

function tsxFiles(dir: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(dir)) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) {
      out.push(...tsxFiles(full));
    } else if (entry.endsWith('.tsx') && !entry.includes('.test.')) {
      out.push(full);
    }
  }
  return out;
}

function findings(file: string): string[] {
  const found: string[] = [];
  const lines = readFileSync(file, 'utf-8').split('\n');
  for (const line of lines) {
    const trimmed = line.trim();
    if (trimmed.startsWith('//') || trimmed.startsWith('*') || trimmed.startsWith('/*')) continue;

    for (const attr of VISIBLE_ATTRS) {
      const re = new RegExp(`\\b${attr}=("([^"]*)"|'([^']*)')`, 'g');
      for (const m of line.matchAll(re)) {
        const value = (m[2] ?? m[3] ?? '').trim();
        if (HAS_LETTER.test(value) && !ALLOWED.has(value)) found.push(`${attr}="${value}"`);
      }
    }

    // JSX text: >Some words< — require ≥2 words or terminal punctuation to skip generics.
    for (const m of line.matchAll(/>\s*([A-Z][^<>{}]*?[A-Za-z.!?])\s*</g)) {
      const text = m[1].trim();
      const multiWord = /\s/.test(text);
      const sentence = /[.!?]$/.test(text);
      if ((multiWord || sentence) && HAS_LETTER.test(text) && !ALLOWED.has(text)) {
        found.push(`text:${text}`);
      }
    }
  }
  return found;
}

describe('no hardcoded user-visible strings', () => {
  for (const file of tsxFiles(SRC)) {
    const rel = `${basename(dirname(file))}/${basename(file)}`;
    it(`${rel} routes visible text through i18n`, () => {
      expect(findings(file)).toEqual([]);
    });
  }
});
