/**
 * A dashboard spec's sections, laid out and drawn.
 *
 * Shared by the two pages that draw one: the signed-in operations page, and the findings page a
 * submitter opens holding no credential. One definition, so a section that lays out one way for a
 * planner cannot lay out another way for the person whose land it is about.
 */
import type { ResolveContext } from '../../api/dashboardApi';
import type { DashboardSection } from '../../types/dashboard';
import { sectionGrid } from '../../utils/gridLayout';
import { WidgetRenderer } from './widgets/WidgetRenderer';

export function DashboardSections({
  sections,
  context,
  isWide,
  openDetail,
  whenEmpty,
}: {
  sections: DashboardSection[];
  context: ResolveContext;
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
        <Section key={i} section={section} context={context} isWide={isWide} openDetail={openDetail} />
      ))}
    </>
  );
}

function Section({
  section,
  context,
  isWide,
  openDetail,
}: {
  section: DashboardSection;
  context: ResolveContext;
  isWide: boolean;
  openDetail?: (thingId: string) => void;
}) {
  const grid = sectionGrid(section, isWide);

  return (
    <section className="mb-2">
      {section.title && (
        <div className="flex items-center gap-3 mt-6 mb-3">
          <h3 className="text-[12px] uppercase tracking-wider font-bold text-zinc-400 dark:text-zinc-500">{section.title}</h3>
          {section.hint && <span className="text-[11px] text-zinc-400 dark:text-zinc-500">{section.hint}</span>}
          <div className="flex-1 h-px bg-zinc-200 dark:bg-zinc-800" />
        </div>
      )}
      <div className="grid gap-3.5" style={{ gridTemplateColumns: grid.gridTemplateColumns, gridAutoRows: grid.gridAutoRows }}>
        {section.widgets.map((widget, i) => (
          <div key={i} className="grid min-w-0 min-h-0" style={grid.cells[i]}>
            <WidgetRenderer widget={widget} context={context} openDetail={openDetail} />
          </div>
        ))}
      </div>
    </section>
  );
}
