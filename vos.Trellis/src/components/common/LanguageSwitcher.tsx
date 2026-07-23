import { useEffect, useRef, useState } from 'react';
import { ChevronDown } from 'lucide-react';
import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import { changeLanguage } from '../../i18n';
import { SUPPORTED_LANGUAGES } from '../../i18n/languages';

/** A dropdown of language flags: the trigger shows the active flag, the menu lists every
 *  language (flag + name). Opens upward in the sidebar footer; pass `openDirection="down"`
 *  where it sits near the top of the viewport (e.g. the login page). */
export function LanguageSwitcher({ openDirection = 'up' }: { openDirection?: 'up' | 'down' }) {
  const { t, i18n } = useTranslation();
  const [isOpen, setIsOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  const active =
    SUPPORTED_LANGUAGES.find((language) => language.code === i18n.language) ?? SUPPORTED_LANGUAGES[0];

  useEffect(() => {
    if (!isOpen) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setIsOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setIsOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [isOpen]);

  const select = (code: (typeof SUPPORTED_LANGUAGES)[number]['code']) => {
    changeLanguage(code);
    setIsOpen(false);
  };

  return (
    <div ref={rootRef} className="relative">
      <button
        type="button"
        onClick={() => setIsOpen((open) => !open)}
        aria-haspopup="listbox"
        aria-expanded={isOpen}
        aria-label={t('language.select')}
        title={active.label}
        className="flex w-full items-center justify-center gap-1.5 rounded px-2 py-1.5 text-zinc-600 hover:bg-zinc-200 dark:text-zinc-300 dark:hover:bg-zinc-800 transition-colors"
      >
        <span aria-hidden="true" className="text-base leading-none">{active.flag}</span>
        <ChevronDown size={14} className={clsx('transition-transform', isOpen && 'rotate-180')} />
      </button>

      {isOpen && (
        <ul
          role="listbox"
          aria-label={t('language.select')}
          className={clsx(
            'absolute left-0 z-20 max-h-64 w-44 overflow-auto rounded-md border border-zinc-200 bg-white py-1 shadow-lg dark:border-zinc-700 dark:bg-zinc-800',
            openDirection === 'down' ? 'top-full mt-1' : 'bottom-full mb-1',
          )}
        >
          {SUPPORTED_LANGUAGES.map((language) => {
            const isActive = language.code === active.code;
            return (
              <li key={language.code}>
                <button
                  type="button"
                  role="option"
                  aria-selected={isActive}
                  aria-label={language.label}
                  onClick={() => select(language.code)}
                  className={clsx(
                    'flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-sm transition-colors',
                    isActive
                      ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                      : 'text-zinc-700 hover:bg-zinc-100 dark:text-zinc-200 dark:hover:bg-zinc-700',
                  )}
                >
                  <span aria-hidden="true" className="text-base leading-none">{language.flag}</span>
                  {language.label}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}
