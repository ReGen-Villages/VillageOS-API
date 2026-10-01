import { describe, it, expect, afterEach } from 'vitest';
import source from './GraphPage.tsx?raw';
import i18n from '../i18n';
import { SUPPORTED_LANGUAGES } from '../i18n/languages';

// A message that asks for a name the page does not pass shows the placeholder itself. The names are read
// from the page's source, because a test of the language files alone cannot know which names it passes.
function namesTheGraphPagePasses(): string[] {
  const call = source.match(/t\('graph\.toast\.fragmentApplied', \{([^}]*)\}/);
  if (!call) throw new Error('GraphPage.tsx no longer shows graph.toast.fragmentApplied with a literal object of values');
  return [...call[1].matchAll(/(\w+):/g)].map((name) => name[1]);
}

afterEach(async () => {
  await i18n.changeLanguage('en');
});

describe('the message the Graph page shows after a fragment is applied', () => {
  it('is given a value for each of the three counts', () => {
    expect(namesTheGraphPagePasses()).toHaveLength(3);
  });

  it.each(SUPPORTED_LANGUAGES.map((language) => language.code))('asks only for values the page passes, in %s', async (code) => {
    await i18n.changeLanguage(code);
    const values = Object.fromEntries(namesTheGraphPagePasses().map((name, position) => [name, 101 + position]));

    const message = i18n.t('graph.toast.fragmentApplied', values);

    expect(message).not.toContain('{{');
    for (const value of Object.values(values)) expect(message).toContain(String(value));
  });
});
