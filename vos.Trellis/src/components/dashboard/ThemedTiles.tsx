/**
 * A dashboard's themed sections as a grid of tiles.
 *
 * The model names a theme on a section and declares what the theme looks like — a colour, an icon, a
 * place in the grid — so the report a submitter sees is a grid of coloured tiles rather than a list,
 * and adding a tile is a template edit. A tile's `kpi` widgets are its summary, shown while the tile is
 * hovered or focused; its other widgets are the gallery a click opens; and a gallery card opens the
 * widget full width. Sections naming no theme are not drawn here at all, so a page draws them as the
 * list it always did.
 */
import { useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { ArrowLeft, Maximize2 } from 'lucide-react';
import { DynamicIcon, iconNames, type IconName } from 'lucide-react/dynamic';
import type { ResolveContext } from '../../api/dashboardApi';
import { asNumber } from '../../api/dashboardApi';
import { useBindings } from '../../hooks/useDashboard';
import type { DashboardSection, DeclaredTheme, KpiWidget, Widget } from '../../types/dashboard';
import { inkFor } from '../../utils/colors';
import { formatNumber } from './widgets/format';
import { WidgetRenderer } from './widgets/WidgetRenderer';

/* Membership is asked once per tile on every render, against every name the icon set ships — a scan
   of the list would repeat that walk each time. */
const ICON_NAMES: ReadonlySet<string> = new Set(iconNames);

/** The face a tile takes where the model names a theme it does not declare, or declares no colour. */
const NEUTRAL_FACE = '#d4d4d8';

type View =
  | { kind: 'grid' }
  | { kind: 'gallery'; tile: number }
  | { kind: 'widget'; tile: number; card: number };

interface Tile {
  section: DashboardSection;
  theme: DeclaredTheme | undefined;
}

/** The themed sections in the order the themes declare; a section whose theme the model does not
 *  order, or does not declare, comes after every ordered one in the order the spec lists them. */
function tilesOf(sections: DashboardSection[], themes: DeclaredTheme[]): Tile[] {
  const byName = new Map(themes.map((theme) => [theme.name, theme]));
  return sections
    .filter((section) => section.theme !== undefined)
    .map((section) => ({ section, theme: byName.get(section.theme!) }))
    .sort((left, right) => (left.theme?.order ?? Infinity) - (right.theme?.order ?? Infinity));
}

function isSummary(widget: Widget): widget is KpiWidget {
  return widget.type === 'kpi';
}

export function ThemedTiles({
  sections,
  themes,
  ctx,
}: {
  sections: DashboardSection[];
  themes: DeclaredTheme[];
  ctx: ResolveContext;
}) {
  const { t } = useTranslation();
  const tiles = useMemo(() => tilesOf(sections, themes), [sections, themes]);
  const [view, setView] = useState<View>({ kind: 'grid' });

  if (tiles.length === 0) return null;

  if (view.kind === 'grid') {
    return (
      <div className="mt-4 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-4">
        {tiles.map((tile, at) => (
          <ThemeTile
            key={at}
            tile={tile}
            ctx={ctx}
            onOpen={() => setView({ kind: 'gallery', tile: at })}
          />
        ))}
      </div>
    );
  }

  const { section } = tiles[view.tile];
  const gallery = section.widgets.filter((widget) => !isSummary(widget));
  const heading = section.title ?? section.theme;

  if (view.kind === 'widget') {
    return (
      <div className="mt-4">
        <BackButton label={t('tiles.backToGallery')} onClick={() => setView({ kind: 'gallery', tile: view.tile })} />
        <h3 className="mb-3 text-base font-semibold text-zinc-900 dark:text-zinc-100">{heading}</h3>
        <WidgetRenderer widget={gallery[view.card]} ctx={ctx} />
      </div>
    );
  }

  return (
    <div className="mt-4">
      <BackButton label={t('tiles.backToTiles')} onClick={() => setView({ kind: 'grid' })} />
      <h3 className="mb-3 text-base font-semibold text-zinc-900 dark:text-zinc-100">{heading}</h3>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
        {gallery.map((widget, at) => (
          <div key={at} className="relative">
            <WidgetRenderer widget={widget} ctx={ctx} />
            <button
              type="button"
              onClick={() => setView({ kind: 'widget', tile: view.tile, card: at })}
              aria-label={t('tiles.open', { title: widget.title ?? '' })}
              className="absolute top-3 right-3 rounded-md p-1.5 text-zinc-400 hover:bg-zinc-100 hover:text-zinc-700 dark:hover:bg-zinc-700 dark:hover:text-zinc-200"
            >
              <Maximize2 size={14} aria-hidden="true" />
            </button>
          </div>
        ))}
      </div>
    </div>
  );
}

function BackButton({ label, onClick }: { label: string; onClick: () => void }) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="mb-3 inline-flex items-center gap-1.5 text-sm text-zinc-500 hover:text-zinc-900 dark:text-zinc-400 dark:hover:text-zinc-100"
    >
      <ArrowLeft size={14} aria-hidden="true" />
      {label}
    </button>
  );
}

function ThemeTile({ tile, ctx, onOpen }: { tile: Tile; ctx: ResolveContext; onOpen: () => void }) {
  const { t } = useTranslation();
  const { section, theme } = tile;
  const [revealed, setRevealed] = useState(false);
  const summary = section.widgets.filter(isSummary);
  const figures = useBindings(summary.map((widget) => widget.value), ctx);
  const assessed = section.widgets.length > 0;
  const face = theme?.colour ?? NEUTRAL_FACE;
  const ink = inkFor(face) === 'dark' ? 'text-zinc-800' : 'text-white';
  const showSummary = revealed && summary.length > 0;

  return (
    <button
      type="button"
      aria-label={section.title ?? section.theme}
      disabled={!assessed}
      onClick={onOpen}
      onMouseEnter={() => setRevealed(true)}
      onMouseLeave={() => setRevealed(false)}
      onFocus={() => setRevealed(true)}
      onBlur={() => setRevealed(false)}
      style={{ backgroundColor: face }}
      className={`relative flex aspect-square flex-col items-center justify-center rounded-2xl p-3 transition-transform focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500 focus-visible:ring-offset-2 dark:focus-visible:ring-offset-zinc-950 ${
        assessed ? 'cursor-pointer hover:scale-[1.02]' : 'opacity-50'
      } ${ink}`}
    >
      {showSummary ? (
        <dl className="w-full rounded-xl border border-current/40 px-2 py-3 text-center">
          <div className="mb-1 text-sm font-semibold">{t('tiles.summary')}</div>
          {summary.map((widget, at) => (
            <div key={at} className="flex items-baseline justify-center gap-1.5 text-xs leading-5">
              <dd className="font-semibold tabular-nums">
                {formatNumber(asNumber(figures[at]?.value ?? null), widget.format)}
                {widget.unit ? ` ${widget.unit}` : ''}
              </dd>
              <dt className="opacity-90">{widget.title}</dt>
            </div>
          ))}
        </dl>
      ) : (
        <ThemeIcon name={theme?.icon ?? null} />
      )}
      {!assessed && (
        <span className="absolute bottom-3 text-xs font-semibold">{t('tiles.notAssessed')}</span>
      )}
    </button>
  );
}

/** The icon the theme names, loaded on demand the way the navigation loads a dashboard's. A name the
 *  set does not have, or none at all, draws nothing: the tile's face and label are what name it. */
function ThemeIcon({ name }: { name: string | null }) {
  if (!name || !ICON_NAMES.has(name)) return null;
  return <DynamicIcon name={name as IconName} className="h-1/2 w-1/2" strokeWidth={1} aria-hidden="true" />;
}
