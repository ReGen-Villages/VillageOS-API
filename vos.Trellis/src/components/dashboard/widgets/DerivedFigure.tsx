import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { Binding, NumberFormat } from '@tendril-ui/tendril';
import type { ResolveContext } from '@tendril-ui/tendril';
import { hasBreakdown } from '../../../api/figureBreakdown';
import { FigurePanel } from './FigurePanel';

/** The dotted rule says the model derived the figure rather than someone typing it, and opens what it is made
 *  of. A figure with nothing behind it is drawn plainly, so the rule always means something. */
export function DerivedFigure({
  binding,
  context,
  title,
  format,
  unit,
  footnote,
  openDetail,
  children,
}: {
  binding: Binding;
  context: ResolveContext;
  title?: string;
  format?: NumberFormat;
  unit?: string;
  footnote?: string;
  openDetail?: (thingId: string) => void;
  children: React.ReactNode;
}) {
  const { t } = useTranslation();
  const [opened, setOpened] = useState(false);

  if (!hasBreakdown(binding)) return <>{children}</>;

  return (
    <>
      <button
        type="button"
        onClick={() => setOpened(true)}
        aria-label={title ? `${title} — ${t('breakdown.title')}` : t('breakdown.title')}
        className="text-left cursor-pointer border-b-[1.5px] border-dotted border-emerald-600/50 hover:border-emerald-600 focus-visible:border-emerald-600 dark:border-emerald-400/70 dark:hover:border-emerald-400"
      >
        {children}
      </button>
      {opened && (
        <FigurePanel
          binding={binding}
          context={context}
          title={title}
          format={format}
          unit={unit}
          footnote={footnote}
          openDetail={openDetail}
          onClose={() => setOpened(false)}
        />
      )}
    </>
  );
}
