/// <reference types="node" />
import { describe, it, expect } from 'vitest';
import { readdirSync, readFileSync, statSync } from 'node:fs';
import { join, dirname, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

/**
 * Guards that no source file shows a person words that did not come from the translation files.
 *
 * Every source file is read with the TypeScript parser, so a string is found however it is written:
 * as text between tags, as an attribute, as a default argument, as an error message a page later
 * shows, or inside a template. What is flagged:
 *  - text between JSX tags, of any length;
 *  - a string or template written straight into the page, or into an attribute a person reads;
 *  - anywhere else, a string or template that reads as prose: two words, or one ending in a
 *    full stop, question mark, exclamation mark or ellipsis.
 * Class lists, addresses, the arguments of `t(...)` and of the console, and module names are not
 * words a person reads and are passed over.
 *
 * A finding is silenced only by adding it to ALLOWED with a reason.
 */

const SOURCE = join(dirname(fileURLToPath(import.meta.url)), '..');

const ALLOWED = new Map<string, string>([
  ['e.g. user.id', 'A field path, the same in every language.'],
  ['e.g. {"name": firstName & " " & lastName}', 'A JSONata expression, the same in every language.'],
  ['ReGen Villages', "The company's name. It is the mark's accessible name, and a proper noun is the same in every language."],
  ['Aa', 'The match-case switch, drawn as the two letters it compares rather than written as a word.'],
  ['العربية (السعودية)', 'A language is offered under its own name, so a reader finds it whatever language the page is in.'],
  ['العربية (الإمارات)', 'A language is offered under its own name, so a reader finds it whatever language the page is in.'],
  ['useAuth must be used within AuthProvider', 'A programming mistake caught while the page is being built; it never reaches a person.'],
  ['No user logged in', 'Reached only by calling the password change without a signed-in person, which no page does.'],
  ['No token to refresh', 'Reached only by refreshing a session that was never opened, which no page does.'],
  ['Refresh failed: {}', 'The session timer that refreshes a token signs the person out on this, and never shows it.'],
  ['This page was handed no way to reduce a property series.', 'A findings page is always built with its reader; this names a programming mistake.'],
  ['A findings page reduces no Things by time bucket.', 'A findings page is built without that reading; this names a programming mistake.'],
  ['A findings page calls no service.', 'A findings page is built without that reading; this names a programming mistake.'],
]);

/** Attributes a person reads. `emptyLabel` is not one: the design editor passes
 *  it the specification's own word for a blank choice, shown beside the options, which are also the
 *  specification's words. */
const VISIBLE_ATTRIBUTES = new Set([
  'placeholder', 'title', 'aria-label', 'alt', 'label', 'heading', 'hint', 'message', 'description',
]);

/** Attributes whose value is read by the browser, not by a person. */
const MACHINE_ATTRIBUTES = new Set(['className', 'key', 'i18nKey', 'data-testid', 'style', 'rel', 'target', 'href', 'media', 'type', 'name', 'id', 'role', 'autoComplete', 'inputMode']);

/** Calls whose string arguments are keys, class names or log lines rather than words on a page. */
const NOT_SHOWN_CALLS = /^(t|i18n\.t|i18next\.t|console\.\w+|clsx|cn|twMerge|import|require|(.+\.)?(matchMedia|getPropertyValue|querySelector|querySelectorAll|setAttribute|getAttribute|setRequestHeader|includes|startsWith|endsWith))$/;

const CLASS_WORD = /^(flex|grid|block|hidden|inline|contents|relative|absolute|fixed|sticky|static|truncate|underline|italic|uppercase|lowercase|capitalize|rounded|border|shadow|transition|grow|shrink|ring|outline|group|peer|visible|invisible|isolate|resize|antialiased|tabular-nums|shadow-sm|shadow-lg)$/;

function isClassList(text: string): boolean {
  const words = text.split(/\s+/).filter((word) => word && word !== '{}');
  return words.length > 0 && words.every((word) => /[-:[/]/.test(word) || CLASS_WORD.test(word));
}

/** Addresses, file names, credentials, media queries and style values: text a machine reads. */
function isForAMachine(text: string): boolean {
  return /^([/.#?&]|\w+:\/\/|\{\}\/|Bearer |\(prefers-)/.test(text)
    || /^[\w.{}-]+\.(md|csv|log|json|tsx?)$/.test(text)
    || /\b(\d*px|span|minmax|calc|var|sans-serif|system-ui|monospace)\b/.test(text);
}

function wordsIn(text: string): string[] {
  return text.replace(/\{\}/g, ' ').split(/\s+/).filter((token) => /\p{L}{2,}/u.test(token));
}

function readsAsProse(text: string): boolean {
  const words = wordsIn(text).length;
  const sentence = /[.!?…]\s*$/.test(text.trim());
  return (words >= 2 || (words === 1 && sentence)) && !isClassList(text) && !isForAMachine(text);
}

function sourceFiles(directory: string): string[] {
  const out: string[] = [];
  for (const entry of readdirSync(directory)) {
    const full = join(directory, entry);
    if (statSync(full).isDirectory()) {
      if (entry !== 'locales') out.push(...sourceFiles(full));
    } else if (/\.tsx?$/.test(entry) && !entry.includes('.test.') && !entry.endsWith('.d.ts')) {
      out.push(full);
    }
  }
  return out;
}

function textOf(node: ts.Node): string | null {
  if (ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node)) return node.text;
  if (ts.isTemplateExpression(node)) {
    return node.head.text + node.templateSpans.map((span) => `{}${span.literal.text}`).join('');
  }
  return null;
}

function calleeName(call: ts.CallExpression): string {
  return call.expression.getText().replace(/\s+/g, '');
}

/** Where a string ends up, looking through the expressions that only choose between strings. */
function destinationOf(node: ts.Node): ts.Node {
  let current = node.parent;
  while (
    ts.isParenthesizedExpression(current) ||
    ts.isConditionalExpression(current) ||
    (ts.isBinaryExpression(current) && [ts.SyntaxKind.BarBarToken, ts.SyntaxKind.QuestionQuestionToken, ts.SyntaxKind.AmpersandAmpersandToken].includes(current.operatorToken.kind))
  ) {
    current = current.parent;
  }
  return current;
}

function isNeverShown(node: ts.Node): boolean {
  const parent = node.parent;
  if (ts.isImportDeclaration(parent) || ts.isExportDeclaration(parent) || ts.isLiteralTypeNode(parent)) return true;
  if (ts.isExternalModuleReference(parent) || ts.isImportTypeNode(parent)) return true;
  if (ts.isElementAccessExpression(parent) || ts.isCaseClause(parent)) return true;
  if (ts.isBinaryExpression(parent) && /^[!=]==?$/.test(parent.operatorToken.getText())) return true;
  for (let ancestor: ts.Node = parent; ancestor && !ts.isSourceFile(ancestor); ancestor = ancestor.parent) {
    if (ts.isCallExpression(ancestor) && NOT_SHOWN_CALLS.test(calleeName(ancestor))) return true;
    if (ts.isJsxAttribute(ancestor) && MACHINE_ATTRIBUTES.has(ancestor.name.getText())) return true;
    if (ts.isPropertyAssignment(ancestor) && ancestor.name.getText() === 'className') return true;
    if (ts.isBlock(ancestor) || ts.isJsxElement(ancestor)) break;
  }
  return false;
}

/** Every piece of text the rules flag in a file, before the allowed exceptions are taken out. */
function flagged(file: string): string[] {
  const code = readFileSync(file, 'utf-8');
  const source = ts.createSourceFile(file, code, ts.ScriptTarget.Latest, true, file.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  const found: string[] = [];

  const flag = (node: ts.Node, text: string) => {
    const trimmed = text.replace(/\s+/g, ' ').trim();
    const { line } = source.getLineAndCharacterOfPosition(node.getStart());
    found.push(`${line + 1}: ${trimmed}`);
  };

  const visit = (node: ts.Node) => {
    if (ts.isJsxText(node)) {
      if (/\p{L}{2,}/u.test(node.text)) flag(node, node.text);
    } else {
      const text = textOf(node);
      if (text !== null && /\p{L}{2,}/u.test(text) && !isNeverShown(node)) {
        const destination = destinationOf(node);
        const intoThePage = ts.isJsxExpression(destination) && (ts.isJsxElement(destination.parent) || ts.isJsxFragment(destination.parent));
        const intoAnAttribute = ts.isJsxAttribute(destination) || (ts.isJsxExpression(destination) && ts.isJsxAttribute(destination.parent));
        const attribute = intoAnAttribute
          ? (ts.isJsxAttribute(destination) ? destination : destination.parent as ts.JsxAttribute).name.getText()
          : null;
        const shown = intoThePage || (attribute !== null && VISIBLE_ATTRIBUTES.has(attribute));
        if (shown ? !isClassList(text) && !isForAMachine(text) : readsAsProse(text)) flag(node, text);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(source);
  return found;
}

function findings(file: string): string[] {
  return flagged(file).filter((finding) => !ALLOWED.has(finding.slice(finding.indexOf(': ') + 2)));
}

describe('no hardcoded user-visible strings', () => {
  for (const file of sourceFiles(SOURCE)) {
    it(`${relative(SOURCE, file)} routes visible text through i18n`, () => {
      expect(findings(file)).toEqual([]);
    });
  }

  it('silences nothing the source no longer says', () => {
    // An exception outliving its string would let the same words back in unnoticed.
    const everything = new Set(sourceFiles(SOURCE).flatMap(flagged).map((finding) => finding.slice(finding.indexOf(': ') + 2)));
    const stale = [...ALLOWED.keys()].filter((text) => !everything.has(text));
    expect(stale).toEqual([]);
  });
});
