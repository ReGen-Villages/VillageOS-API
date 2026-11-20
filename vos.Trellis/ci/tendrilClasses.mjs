const CLASS_NAME = /^[a-z0-9!-][a-z0-9:/.[\]()%#_,!-]*$/i;

/** Also the value of each `…_CLASS` constant, because a `className` can name one. */
function classNameExpressions(source) {
  const found = [];
  for (const match of source.matchAll(/className=(["{])/g)) {
    const start = match.index + match[0].length;
    if (match[1] === '"') {
      found.push(`"${source.slice(start, source.indexOf('"', start))}"`);
      continue;
    }
    found.push(source.slice(start, closingBrace(source, start)));
  }
  for (const match of source.matchAll(/\bconst\s+[A-Z][A-Z_]*_CLASS\s*=\s*([^;]*);/g)) found.push(match[1]);
  return found;
}

function closingBrace(text, from) {
  let depth = 1;
  let at = from;
  while (at < text.length && depth > 0) {
    if (text[at] === '{') depth += 1;
    if (text[at] === '}') depth -= 1;
    at += 1;
  }
  return at - 1;
}

/** A template's own text counts, and so do the strings inside its `${…}` parts. */
function stringsIn(expression) {
  const found = [];
  let at = 0;
  while (at < expression.length) {
    const character = expression[at];
    if (character === '"' || character === "'") {
      const end = expression.indexOf(character, at + 1);
      found.push(expression.slice(at + 1, end));
      at = end + 1;
    } else if (character === '`') {
      let text = '';
      at += 1;
      while (at < expression.length && expression[at] !== '`') {
        if (expression.startsWith('${', at)) {
          const end = closingBrace(expression, at + 2);
          found.push(...stringsIn(expression.slice(at + 2, end)));
          text += ' ';
          at = end + 1;
        } else {
          text += expression[at];
          at += 1;
        }
      }
      found.push(text);
      at += 1;
    } else {
      at += 1;
    }
  }
  return found;
}

/** Includes each class a condition chooses between, because each must be in the stylesheet when chosen. */
export function classNamesIn(source) {
  const names = new Set();
  for (const expression of classNameExpressions(source)) {
    for (const text of stringsIn(expression)) {
      for (const token of text.split(/\s+/)) {
        if (CLASS_NAME.test(token)) names.add(token);
      }
    }
  }
  return [...names];
}

function selectorFor(className) {
  return `.${className.replace(/[:/.[\]()%#,!]/g, (character) => `\\${character}`)}`;
}

/** A selector counts only where it ends, so `min-w-4` is not found inside `min-w-40`. */
export function missingFrom(stylesheet, classNames) {
  return classNames.filter((className) => {
    const selector = selectorFor(className).replace(/[\\^$.*+?()[\]{}|]/g, (character) => `\\${character}`);
    return !new RegExp(`${selector}(?![a-z0-9_-])`, 'i').test(stylesheet);
  });
}
