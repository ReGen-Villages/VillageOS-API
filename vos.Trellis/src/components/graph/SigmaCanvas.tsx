import { useMemo } from 'react';
import Graph from 'graphology';
import { SigmaContainer } from '@react-sigma/core';
import type { VosThing, VosRelationship } from '../../types/vos';
import type { SearchOptions } from '../../utils/searchFilter';
import { BRIGHT_EDGE_COLOR } from '../../utils/reducerHelpers';
import { GraphDataLoader } from './GraphDataLoader';
import { ClusterComputer } from './ClusterComputer';
import { GraphEvents } from './GraphEvents';
import { LayoutController } from './LayoutController';
import { LogicalNodeController } from './LogicalNodeController';
import { NodeReducer } from './NodeReducer';
import { GraphToolbar } from './GraphToolbar';
import { WebGLContextGuard } from './WebGLContextGuard';

function drawRoundedPill(
  ctx: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number,
): void {
  ctx.beginPath();
  ctx.moveTo(x + radius, y);
  ctx.lineTo(x + width - radius, y);
  ctx.quadraticCurveTo(x + width, y, x + width, y + radius);
  ctx.lineTo(x + width, y + height - radius);
  ctx.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
  ctx.lineTo(x + radius, y + height);
  ctx.quadraticCurveTo(x, y + height, x, y + height - radius);
  ctx.lineTo(x, y + radius);
  ctx.quadraticCurveTo(x, y, x + radius, y);
  ctx.closePath();
}

/** Returns null if truncation would leave fewer than 4 chars. */
function truncateLabel(
  ctx: CanvasRenderingContext2D,
  text: string,
  maxWidth: number,
): string | null {
  let label = text;
  let textLength = ctx.measureText(label).width;
  if (textLength <= maxWidth) return label;

  const ellipsis = '\u2026';
  label = label + ellipsis;
  textLength = ctx.measureText(label).width;
  while (textLength > maxWidth && label.length > 1) {
    label = label.slice(0, -2) + ellipsis;
    textLength = ctx.measureText(label).width;
  }
  if (label.length < 4) return null;
  return label;
}

interface Props {
  things: VosThing[];
  relationships: VosRelationship[];
  searchQuery: string;
  searchOptions: SearchOptions;
}

/**
 * Custom hover renderer for dark-themed backgrounds.
 *
 * Sigma's built-in `drawDiscNodeHover` hardcodes a white (#FFF) label
 * background, which makes our light label text (#e4e4e7) unreadable.
 * This replacement draws a dark rounded box (zinc-800) with a subtle
 * glow, then renders the label in the configured labelColor — producing
 * high-contrast light-on-dark text.
 */
function drawDarkNodeHover(
  context: CanvasRenderingContext2D,
  data: { x: number; y: number; size: number; label: string | null; color: string },
  settings: { labelSize: number; labelFont: string; labelWeight: string; labelColor: { color?: string; attribute?: string } },
): void {
  const size = settings.labelSize;
  const font = settings.labelFont;
  const weight = settings.labelWeight;

  context.font = `${weight} ${size}px ${font}`;

  // Dark background box
  context.fillStyle = '#27272a'; // zinc-800
  context.shadowOffsetX = 0;
  context.shadowOffsetY = 0;
  context.shadowBlur = 8;
  context.shadowColor = 'rgba(0, 0, 0, 0.6)';

  const PADDING = 3;

  if (typeof data.label === 'string') {
    const textWidth = context.measureText(data.label).width;
    const boxWidth = Math.round(textWidth + 5);
    const boxHeight = Math.round(size + 2 * PADDING);
    const radius = Math.max(data.size, size / 2) + PADDING;

    const angleRadian = Math.asin(boxHeight / 2 / radius);
    const xDeltaCoord = Math.sqrt(
      Math.abs(Math.pow(radius, 2) - Math.pow(boxHeight / 2, 2)),
    );

    context.beginPath();
    context.moveTo(data.x + xDeltaCoord, data.y + boxHeight / 2);
    context.lineTo(data.x + radius + boxWidth, data.y + boxHeight / 2);
    context.lineTo(data.x + radius + boxWidth, data.y - boxHeight / 2);
    context.lineTo(data.x + xDeltaCoord, data.y - boxHeight / 2);
    context.arc(data.x, data.y, radius, angleRadian, -angleRadian);
    context.closePath();
    context.fill();
  } else {
    // No label — just draw halo around node
    context.beginPath();
    context.arc(data.x, data.y, data.size + PADDING, 0, Math.PI * 2);
    context.closePath();
    context.fill();
  }

  // Reset shadow before drawing text
  context.shadowOffsetX = 0;
  context.shadowOffsetY = 0;
  context.shadowBlur = 0;

  // Draw the label text in our configured label colour
  if (data.label) {
    const color = settings.labelColor.color || '#ffffff';
    context.fillStyle = color;
    context.font = `${weight} ${size}px ${font}`;
    context.fillText(data.label, data.x + data.size + 3, data.y + size / 3);
  }
}

/**
 * Clearance around each node where edge labels are suppressed.
 * Node labels extend to the right of nodes; this keeps edge text clear of
 * that zone.  The clearance is proportional to the node's rendered size so
 * it scales with zoom (large nodes = long labels = wider clearance).
 * Multiplied by node size; the minimum is enforced with NODE_LABEL_MIN_CLEARANCE.
 */
const NODE_LABEL_SIZE_FACTOR = 8;
const NODE_LABEL_MIN_CLEARANCE = 50;

/**
 * Custom edge label renderer that centers text on the edge line
 * instead of offset below it (Sigma's default).
 *
 * Features:
 * - Dark background pill behind text so labels are readable over edges
 * - Edge labels whose midpoint falls within clearance of either endpoint
 *   are suppressed to avoid colliding with node labels
 * - Non-highlighted labels are drawn semi-transparent (0.45 alpha)
 * - When an edge is highlighted (color === BRIGHT_EDGE_COLOR), the label
 *   is drawn brighter, bolder, and fully opaque with a more prominent pill
 */
function drawCenteredEdgeLabel(
  context: CanvasRenderingContext2D,
  edgeData: { label?: string | null; size: number; color: string },
  sourceData: { x: number; y: number; size: number },
  targetData: { x: number; y: number; size: number },
  settings: {
    edgeLabelSize: number;
    edgeLabelFont: string;
    edgeLabelWeight: string;
    edgeLabelColor: { color?: string; attribute?: string };
  },
): void {
  const highlighted = edgeData.color === BRIGHT_EDGE_COLOR;
  const size = highlighted ? settings.edgeLabelSize + 2 : settings.edgeLabelSize;
  const font = settings.edgeLabelFont;
  const weight = highlighted ? 'bold' : settings.edgeLabelWeight;

  let label = edgeData.label;
  if (!label) return;

  context.font = `${weight} ${size}px ${font}`;

  const sSize = sourceData.size;
  const tSize = targetData.size;
  let sx = sourceData.x;
  let sy = sourceData.y;
  let tx = targetData.x;
  let ty = targetData.y;
  let dx = tx - sx;
  let dy = ty - sy;
  let d = Math.sqrt(dx * dx + dy * dy);
  if (d < sSize + tSize) return;

  // Offset by node radii
  sx += (dx * sSize) / d;
  sy += (dy * sSize) / d;
  tx -= (dx * tSize) / d;
  ty -= (dy * tSize) / d;
  const cx = (sx + tx) / 2;
  const cy = (sy + ty) / 2;
  dx = tx - sx;
  dy = ty - sy;
  d = Math.sqrt(dx * dx + dy * dy);

  // Skip edge labels whose midpoint is too close to either endpoint.
  // Clearance scales with node size (bigger node → longer label → wider zone).
  const sClearance = Math.max(sSize * NODE_LABEL_SIZE_FACTOR, NODE_LABEL_MIN_CLEARANCE);
  const tClearance = Math.max(tSize * NODE_LABEL_SIZE_FACTOR, NODE_LABEL_MIN_CLEARANCE);
  const dSource = Math.sqrt((cx - sourceData.x) ** 2 + (cy - sourceData.y) ** 2);
  const dTarget = Math.sqrt((cx - targetData.x) ** 2 + (cy - targetData.y) ** 2);
  if (dSource < sClearance || dTarget < tClearance) return;

  // Ellipsis for long labels
  const truncated = truncateLabel(context, label, d);
  if (truncated === null) return;
  label = truncated;
  const textLength = context.measureText(label).width;

  // Compute angle — keep text readable (never upside-down)
  let angle: number;
  if (dx > 0) {
    angle = dy > 0 ? Math.acos(dx / d) : Math.asin(dy / d);
  } else {
    angle = dy > 0 ? Math.acos(dx / d) + Math.PI : Math.asin(dx / d) + Math.PI / 2;
  }

  const alpha = highlighted ? 0.95 : 0.45;
  const PAD_X = 4;
  const PAD_Y = 2;

  context.save();
  context.globalAlpha = alpha;
  context.translate(cx, cy);
  context.rotate(angle);

  // Dark background pill so text is readable over edges and map features
  const pillW = textLength + PAD_X * 2;
  const pillH = size + PAD_Y * 2;
  const pillR = 3; // border-radius
  const pillX = -textLength / 2 - PAD_X;
  const pillY = -size / 2 - PAD_Y;

  context.fillStyle = highlighted ? '#18181b' : '#09090bcc'; // zinc-900 / near-black
  drawRoundedPill(context, pillX, pillY, pillW, pillH, pillR);
  context.fill();

  // Draw text centered on the edge line
  context.fillStyle = highlighted ? '#e4e4e7' : (settings.edgeLabelColor.color || '#a1a1aa');
  context.fillText(label, -textLength / 2, size / 3);
  context.restore();
}

/**
 * Custom node label renderer with a dark background pill.
 *
 * Sigma's default `drawLabel` renders bare text over the canvas, so
 * edge lines crossing behind the label make it unreadable.  This draws
 * a subtle dark pill behind each label — matching the approach used
 * by `drawCenteredEdgeLabel` — so text is always legible.
 */
function drawNodeLabelWithBackground(
  context: CanvasRenderingContext2D,
  data: { x: number; y: number; size: number; label: string | null; color: string },
  settings: { labelSize: number; labelFont: string; labelWeight: string; labelColor: { color?: string; attribute?: string } },
): void {
  if (!data.label) return;

  const size = settings.labelSize;
  const font = settings.labelFont;
  const weight = settings.labelWeight;

  context.font = `${weight} ${size}px ${font}`;

  const textWidth = context.measureText(data.label).width;
  const PAD_X = 4;
  const PAD_Y = 2;
  const pillX = data.x + data.size + 3 - PAD_X;
  const pillY = data.y - size / 2 - PAD_Y;
  const pillW = textWidth + PAD_X * 2;
  const pillH = size + PAD_Y * 2 + 1;
  const pillR = 3;

  // Dark semi-transparent background pill
  context.fillStyle = '#09090bcc'; // zinc-950 at 80% opacity
  drawRoundedPill(context, pillX, pillY, pillW, pillH, pillR);
  context.fill();

  // Draw text
  context.fillStyle = settings.labelColor.color || '#ffffff';
  context.fillText(data.label, data.x + data.size + 3, data.y + size / 3);
}

// Sigma settings — stable reference so SigmaContainer doesn't recreate the instance
const SIGMA_SETTINGS = {
  // Rendering
  renderLabels: true,
  renderEdgeLabels: true,
  defaultEdgeType: 'arrow' as const,
  enableEdgeEvents: true,
  // Labels — Sigma's label grid prevents overlapping labels.
  // labelGridCellSize controls how far apart labels must be (in px).
  // labelDensity < 1 shows fewer labels; > 1 shows more.
  // labelRenderedSizeThreshold hides labels for nodes below this rendered size.
  labelColor: { color: '#ffffff' },
  labelSize: 10,
  labelFont: 'Inter, system-ui, sans-serif',
  labelGridCellSize: 120,
  labelDensity: 0.5,
  labelRenderedSizeThreshold: 4,
  edgeLabelColor: { color: '#a1a1aa' },
  edgeLabelSize: 11,
  edgeLabelFont: 'Inter, system-ui, sans-serif',
  // Node labels with dark background pill so text is readable over edges
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  defaultDrawNodeLabel: drawNodeLabelWithBackground as any,
  // Edge labels centered on the edge line instead of offset below
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  defaultDrawEdgeLabel: drawCenteredEdgeLabel as any,
  // Hover — custom dark-themed hover renderer (replaces Sigma's white-box default)
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  defaultDrawNodeHover: drawDarkNodeHover as any,
  // Interaction
  zIndex: true,
  minCameraRatio: 0.02,
  maxCameraRatio: 20,
  // Performance — hide edges & labels during camera movement (pan / zoom / force
  // layout animation) so Sigma can skip the expensive per-edge WebGL draw and
  // per-label Canvas layout passes while the viewport is changing.  They reappear
  // the instant movement stops, so the visual impact is minimal.
  hideEdgesOnMove: true,
  hideLabelsOnMove: true,
};

/**
 * Container style forces a GPU-composited stacking context to prevent
 * ghost trails during animation and ensure Safari respects child z-order.
 */
const CONTAINER_STYLE: React.CSSProperties = {
  width: '100%',
  height: '100%',
  transform: 'translateZ(0)',
  WebkitTransform: 'translateZ(0)',
  willChange: 'transform',
  // Ensure proper opacity composition
  isolation: 'isolate',
};

/**
 * Top-level graph visualisation component.
 * Wraps a Sigma.js instance with all supporting child components.
 *
 * IMPORTANT: We pass a multi-directed Graph instance to SigmaContainer
 * because the default Graph() it creates is simple (no parallel edges),
 * which breaks when importing our multi-edge data.
 */
export function SigmaCanvas({ things, relationships, searchQuery, searchOptions }: Props) {
  // Stable graph instance — SigmaContainer uses this as its internal graph.
  // Must be multi+directed to support parallel relationships.
  const graph = useMemo(() => new Graph({ multi: true, type: 'directed' }), []);

  return (
    <SigmaContainer
      graph={graph}
      style={CONTAINER_STYLE}
      settings={SIGMA_SETTINGS}
    >
      <GraphDataLoader things={things} relationships={relationships} />
      <ClusterComputer />
      <GraphEvents />
      <LayoutController />
      <LogicalNodeController />
      <WebGLContextGuard />
      <NodeReducer searchQuery={searchQuery} searchOptions={searchOptions} />
      <GraphToolbar />
    </SigmaContainer>
  );
}
