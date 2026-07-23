import { LogOut, ArrowLeftRight } from 'lucide-react';
import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../hooks/useAuth';
import { ThemeToggleButton } from '../common/ThemeToggleButton';
import { LanguageSwitcher } from '../common/LanguageSwitcher';

/** The always-present chrome shared by every page: theme toggle, switch model,
 *  log out, and the language switcher. Lives in the sidebar footer so it is
 *  reachable from any route without each page re-implementing it. Lays the
 *  controls out in a row when the sidebar is expanded and stacks them when it
 *  collapses to icon width. */
export function SessionControls({ isCollapsed }: { isCollapsed: boolean }) {
  const { logout, switchModel } = useAuth();
  const { t } = useTranslation();

  const buttonClass =
    'flex items-center justify-center rounded p-1.5 text-zinc-600 hover:bg-zinc-200 dark:text-zinc-300 dark:hover:bg-zinc-800 transition-colors';

  const actions = (
    <>
      <ThemeToggleButton />
      <button
        type="button"
        onClick={switchModel}
        title={t('common.switchModel')}
        aria-label={t('common.switchModel')}
        className={buttonClass}
      >
        <ArrowLeftRight size={16} />
      </button>
      <button
        type="button"
        onClick={logout}
        title={t('common.logout')}
        aria-label={t('common.logout')}
        className={clsx(
          buttonClass,
          'hover:bg-red-100 hover:text-red-600 dark:hover:bg-red-900/30 dark:hover:text-red-400',
        )}
      >
        <LogOut size={16} />
      </button>
    </>
  );

  if (isCollapsed) {
    return (
      <div className="flex flex-col items-center gap-1">
        {actions}
        <LanguageSwitcher />
      </div>
    );
  }

  return (
    <div className="flex items-center justify-between gap-1">
      <div className="flex items-center gap-1">{actions}</div>
      <LanguageSwitcher align="right" />
    </div>
  );
}
