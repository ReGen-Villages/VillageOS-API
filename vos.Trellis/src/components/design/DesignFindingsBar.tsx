import { useTranslation } from 'react-i18next';
import clsx from 'clsx';
import type { DesignFinding } from '../../utils/designFindings';
import type { DesignSelection } from '../../utils/designEdits';

/** What the page would be refused for, and what is worth saying, under the canvas. Each finding
 *  selects what it is about. */
export function DesignFindingsBar({ findings, onSelect }: { findings: DesignFinding[]; onSelect: (selection: DesignSelection) => void }) {
  const { t } = useTranslation();
  if (findings.length === 0) {
    return (
      <footer className="px-4 py-2 border-t border-zinc-200 dark:border-zinc-700 text-xs text-green-700 dark:text-green-400">
        {t('design.findings.none')}
      </footer>
    );
  }
  return (
    <footer className="px-4 py-2 border-t border-zinc-200 dark:border-zinc-700 max-h-28 overflow-y-auto space-y-1">
      {findings.map((finding, index) => (
        <button
          key={`${finding.code}-${finding.section ?? ''}-${finding.widget ?? ''}-${index}`}
          type="button"
          onClick={() =>
            onSelect(
              finding.widget !== undefined && finding.section !== undefined
                ? { on: 'widget', section: finding.section, widget: finding.widget }
                : finding.section !== undefined
                  ? { on: 'section', section: finding.section }
                  : { on: 'page' },
            )
          }
          className={clsx(
            'block w-full text-left text-xs hover:underline',
            finding.severity === 'refusal' ? 'text-red-700 dark:text-red-400' : 'text-amber-700 dark:text-amber-400',
          )}
        >
          {t(`design.finding.${finding.code}`, { named: finding.named ?? '' })}
        </button>
      ))}
    </footer>
  );
}
