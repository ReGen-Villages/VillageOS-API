/**
 * A dashboard spec's sections, laid out and drawn.
 *
 * Shared by the two pages that draw one: the signed-in operations page, and the findings page a
 * submitter opens holding no credential. One definition, so a section that lays out one way for a
 * planner cannot lay out another way for the person whose land it is about.
 */
import type { ResolveContext } from '../../api/dashboardApi';
import type { DashboardSection } from '../../types/dashboard';
import { WidgetRenderer } from './widgets/WidgetRenderer';

export function DashboardSections({
  sections,
  ctx,
  isWide,
  openDetail,
  whenEmpty,
}: {
  sections: DashboardSection[];
  ctx: ResolveContext;
  isWide: boolean;
  openDetail?: (thingId: string) => void;
  /** What to say where the spec lists no sections. Each page says it in its own words; drawing
   *  nothing is how a figure the analysis has not computed reads, so a page with nothing to draw at
   *  all cannot look the same as one whose figures came back empty. */
  whenEmpty: React.ReactNode;
}) {
  if (sections.length === 0) return <>{whenEmpty}</>;

  return (
    <>
      {sections.map((section, i) => (
        <Section key={i} section={section} ctx={ctx} isWide={isWide} openDetail={openDetail} />
      ))}
    </>
  );
}

function Section({
  section,
  ctx,
  isWide,
  openDetail,
}: {
  section: DashboardSection;
  ctx: ResolveContext;
  isWide: boolean;
  openDetail?: (thingId: string) => void;
}) {
  const layout = section.layout ?? (section.widgets.every((w) => w.type === 'kpi') ? 'kpi-strip' : 'single');
  /* Every track states a zero minimum. A bare `1fr` track is `minmax(auto, 1fr)`, which grows to
     whatever its widest content needs — one long unbreakable cell in a table then widens the page
     rather than scrolling inside the card it was put in. The card states a zero minimum of its own
     as well, for the same defect from the other side. */
  let gridTemplateColumns = 'minmax(0, 1fr)';
  if (isWide) {
    if (layout === 'kpi-strip') {
      gridTemplateColumns = `repeat(${Math.min(section.widgets.length, 4)}, minmax(0, 1fr))`;
    } else if (layout === 'split') {
      const widths = section.widths ?? section.widgets.map(() => 1);
      gridTemplateColumns = widths.map((w) => `minmax(0, ${w}fr)`).join(' ');
    }
  }

  return (
    <section className="mb-2">
      {section.title && (
        <div className="flex items-center gap-3 mt-6 mb-3">
          <h3 className="text-[12px] uppercase tracking-wider font-bold text-zinc-400 dark:text-zinc-500">{section.title}</h3>
          {section.hint && <span className="text-[11px] text-zinc-400 dark:text-zinc-500">{section.hint}</span>}
          <div className="flex-1 h-px bg-zinc-200 dark:bg-zinc-800" />
        </div>
      )}
      <div className="grid gap-3.5" style={{ gridTemplateColumns }}>
        {section.widgets.map((widget, i) => (
          <WidgetRenderer key={i} widget={widget} ctx={ctx} openDetail={openDetail} />
        ))}
      </div>
    </section>
  );
}
