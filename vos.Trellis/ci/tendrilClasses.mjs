const CLASS_NAME = /^[a-z0-9!-][a-z0-9:/.[\]()%#_,!-]*$/i;

/** The expression a `className` is given: a quoted string, or a braced expression up to its
 *  matching brace. */
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

/** The text of every string in an expression, a template's own text included and the strings inside
 *  its `${…}` parts read the same way. */
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

/** Every class name a source file gives a `className`, including the ones a condition chooses
 *  between, because each still has to be in the stylesheet when it is chosen. */
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

/** The class names a stylesheet has no rule for. A selector counts only where it ends there, so
 *  `min-w-4` is not found inside `min-w-40`. */
export function missingFrom(stylesheet, classNames) {
  return classNames.filter((className) => {
    const selector = selectorFor(className).replace(/[\\^$.*+?()[\]{}|]/g, (character) => `\\${character}`);
    return !new RegExp(`${selector}(?![a-z0-9_-])`, 'i').test(stylesheet);
  });
}
