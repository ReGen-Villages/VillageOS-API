import clsx from 'clsx';
import { useTranslation } from 'react-i18next';
import type { ConnectionState } from '../../types/connection';

const COLOUR: Record<ConnectionState, string> = {
  connecting: 'bg-amber-500 motion-safe:animate-pulse',
  live: 'bg-emerald-500',
  lost: 'bg-red-500',
};

interface Props {
  state: ConnectionState;
  /** Set where the mark stands beside a name rather than the state in words, so the state is not
   *  told by colour alone. */
  saysItsState?: boolean;
  className?: string;
}

/** The one drawing of a connection's state, so every page shows the same state the same way. */
export function ConnectionMark({ state, saysItsState = false, className = 'w-2 h-2' }: Props) {
  const { t } = useTranslation();
  const classes = clsx('inline-block rounded-full flex-shrink-0', COLOUR[state], className);
  if (!saysItsState) return <span className={classes} aria-hidden="true" />;
  const word = t(`connection.${state}`);
  return <span className={classes} role="img" aria-label={word} title={word} />;
}
