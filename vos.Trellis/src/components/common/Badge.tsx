import clsx from 'clsx';

interface Props {
  label: string;
  color: 'green' | 'yellow' | 'red' | 'gray' | 'blue' | 'purple';
  dot?: boolean;
}

const colorMap = {
  green: 'bg-emerald-100 text-emerald-800 dark:bg-emerald-900/30 dark:text-emerald-400',
  yellow: 'bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-400',
  red: 'bg-red-100 text-red-800 dark:bg-red-900/30 dark:text-red-400',
  gray: 'bg-zinc-100 text-zinc-600 dark:bg-zinc-700 dark:text-zinc-400',
  blue: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-400',
  purple: 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-400',
};

const dotColorMap = {
  green: 'bg-emerald-500',
  yellow: 'bg-amber-500',
  red: 'bg-red-500',
  gray: 'bg-zinc-400',
  blue: 'bg-blue-500',
  purple: 'bg-purple-500',
};

export function Badge({ label, color, dot }: Props) {
  return (
    <span className={clsx('inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium', colorMap[color])}>
      {dot && <span className={clsx('w-1.5 h-1.5 rounded-full', dotColorMap[color])} />}
      {label}
    </span>
  );
}
