import { useTranslation } from 'react-i18next';

/** Tiny area+line sparkline with an emphasised endpoint, optionally read against a dashed
 *  reference line. Pure SVG, theme-aware. */
export function Sparkline({
  values,
  baseline,
  width = 108,
  height = 40,
  stroke = 'var(--spark, #3b82f6)',
}: {
  values: number[];
  baseline?: number | null;
  width?: number;
  height?: number;
  stroke?: string;
}) {
  const { t } = useTranslation();
  if (!values || values.length < 2) {
    return <div style={{ width, height }} className="opacity-40" aria-hidden />;
  }
  const pad = 3;
  const domain = baseline === null || baseline === undefined ? values : [...values, baseline];
  const min = Math.min(...domain);
  const max = Math.max(...domain);
  const span = max - min || 1;
  const x = (i: number) => pad + (i * (width - 2 * pad)) / (values.length - 1);
  const y = (v: number) => height - pad - ((v - min) / span) * (height - 2 * pad);
  const line = values.map((v, i) => `${i ? 'L' : 'M'}${x(i).toFixed(1)},${y(v).toFixed(1)}`).join(' ');
  const area = `${line} L${x(values.length - 1).toFixed(1)},${height} L${x(0).toFixed(1)},${height} Z`;
  const gid = `spark-${Math.abs(hashValues(values))}`;
  return (
    <svg width={width} height={height} viewBox={`0 0 ${width} ${height}`} role="img" aria-label={t('widgets.sparkline.trend')}>
      <defs>
        <linearGradient id={gid} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={stroke} stopOpacity="0.26" />
          <stop offset="100%" stopColor={stroke} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={area} fill={`url(#${gid})`} />
      {baseline !== null && baseline !== undefined && (
        <line
          x1={x(0)}
          x2={x(values.length - 1)}
          y1={y(baseline)}
          y2={y(baseline)}
          stroke="currentColor"
          className="text-zinc-400 dark:text-zinc-500"
          strokeWidth="1"
          strokeDasharray="3 3"
        />
      )}
      <path d={line} fill="none" stroke={stroke} strokeWidth="2" strokeLinejoin="round" strokeLinecap="round" />
      <circle
        cx={x(values.length - 1)}
        cy={y(values[values.length - 1])}
        r="3.2"
        fill={stroke}
        stroke="var(--card-bg, #fff)"
        strokeWidth="1.5"
      />
    </svg>
  );
}

function hashValues(values: number[]): number {
  let h = 0;
  for (const v of values) h = (h * 31 + Math.round(v * 100)) | 0;
  return h;
}
