import { useTranslation } from 'react-i18next';
import { X } from 'lucide-react';
import type { DashboardSpecification } from '../../types/dashboard';
import { displayStringsOf } from '../../api/dashboardLocalization';
import { BASE_LANGUAGE, SUPPORTED_LANGUAGES, primarySubtag } from '../../i18n/languages';
import { useWrittenWhenLeft } from '../../hooks/useWrittenWhenLeft';

/**
 * Every display string the page carries, with a cell per language the console speaks. An empty cell
 * falls back to the base text, as it does for a seeded page, so a page is never half-translated into
 * blanks. The rows are the localiser's own walk, so nothing it reads is missing here.
 */
export function TranslationsPanel({
  specification,
  onEdit,
  onClose,
}: {
  specification: DashboardSpecification;
  onEdit: (change: (specification: DashboardSpecification) => DashboardSpecification) => void;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const strings = displayStringsOf(specification);
  const languages = localeBlocks();

  const write = (locale: string, base: string, text: string) =>
    onEdit((held) => {
      const translations = { ...(held.translations ?? {}) };
      const words = { ...(translations[locale] ?? {}) };
      if (text.trim() === '') delete words[base];
      else words[base] = text;
      if (Object.keys(words).length === 0) delete translations[locale];
      else translations[locale] = words;
      const { translations: _dropped, ...rest } = held;
      return Object.keys(translations).length === 0 ? rest : { ...rest, translations };
    });

  return (
    <aside className="w-full max-h-[45dvh] md:w-[36rem] md:max-h-none flex-shrink-0 flex flex-col border-t md:border-t-0 md:border-l border-zinc-200 dark:border-zinc-800 bg-white dark:bg-zinc-900">
      <div className="flex items-center justify-between px-3 pt-3 pb-1">
        <h2 className="text-[11px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">{t('design.translations.title')}</h2>
        <button type="button" onClick={onClose} aria-label={t('design.properties.close')} className="text-zinc-400 hover:text-zinc-700 dark:hover:text-zinc-200">
          <X size={14} />
        </button>
      </div>
      <div className="flex-1 min-h-0 overflow-auto p-3">
        <p className="mb-2 text-xs text-zinc-500 dark:text-zinc-400">{t('design.translations.intro')}</p>
        <table className="w-full text-xs">
          <thead>
            <tr className="text-left text-[10px] uppercase tracking-wide text-zinc-500 dark:text-zinc-400">
              <th className="pr-2 pb-1">{t('design.translations.base')}</th>
              {languages.map((language) => (
                <th key={language.code} className="pr-2 pb-1">
                  {language.name}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {strings.map((base) => (
              <tr key={base} className="align-top">
                <td className="pr-2 py-1 text-zinc-800 dark:text-zinc-200 break-words max-w-[14rem]">{base}</td>
                {languages.map((language) => (
                  <td key={language.code} className="pr-2 py-1">
                    <TranslationCell
                      label={t('design.translations.cell', { base, language: language.name })}
                      value={specification.translations?.[language.code]?.[base] ?? ''}
                      onCommit={(text) => write(language.code, base, text)}
                    />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </aside>
  );
}

/** The blocks a specification's translations are keyed by: the primary subtag of each language the
 *  console speaks, the base language aside, once each — a block written for `ar` serves every Arabic
 *  region, so the panel offers one column for it, named by the first language that speaks it. */
function localeBlocks(): { code: string; name: string }[] {
  const blocks: { code: string; name: string }[] = [];
  for (const language of SUPPORTED_LANGUAGES) {
    const code = primarySubtag(language.code);
    if (code === BASE_LANGUAGE || blocks.some((block) => block.code === code)) continue;
    blocks.push({ code, name: language.label.split(' (')[0] });
  }
  return blocks;
}

function TranslationCell({ label, value, onCommit }: { label: string; value: string; onCommit: (value: string) => void }) {
  const written = useWrittenWhenLeft(value, onCommit);
  return (
    <input
      {...written}
      aria-label={label}
      className="w-full min-w-[8rem] rounded border border-zinc-300 dark:border-zinc-600 bg-white dark:bg-zinc-900 px-1.5 py-0.5 text-xs text-zinc-900 dark:text-zinc-100"
    />
  );
}
