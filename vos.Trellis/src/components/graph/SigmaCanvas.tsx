import { useMemo } from 'react';
import Graph from 'graphology';
import { SigmaContainer } from '@react-sigma/core';
import type { VosThing, VosRelationship } from '@tendril-ui/tendril';
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
import { ViewFitController } from './ViewFitController';

function drawRoundedPill(
  context: CanvasRenderingContext2D,
  x: number,
  y: number,
  width: number,
  height: number,
  radius: number,
): void {
  context.beginPath();
  context.moveTo(x + radius, y);
  context.lineTo(x + width - radius, y);
  context.quadraticCurveTo(x + width, y, x + width, y + radius);
  context.lineTo(x + width, y + height - radius);
  context.quadraticCurveTo(x + width, y + height, x + width - radius, y + height);
  context.lineTo(x + radius, y + height);
  context.quadraticCurveTo(x, y + height, x, y + height - radius);
  context.lineTo(x, y + radius);
  context.quadraticCurveTo(x, y, x + radius, y);
  context.closePath();
}

/** Returns null if truncation would leave fewer than 4 chars. */
function truncateLabel(
  context: CanvasRenderingContext2D,
  text: string,
  maxWidth: number,
): string | null {
  let label = text;
  let textLength = context.measureText(label).width;
  if (textLength <= maxWidth) return label;

  const ellipsis = '\u2026';
  label = label + ellipsis;
  textLength = context.measureText(label).width;
  while (textLength > maxWidth && label.length > 1) {
    label = label.slice(0, -2) + ellipsis;
    textLength = context.measureText(label).width;
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

/** Sigma's own `drawDiscNodeHover` draws a white label background, on which the light label text cannot be
 *  read, so this draws a dark box instead. */
function drawDarkNodeHover(
  context: CanvasRenderingContext2D,
  data: { x: number; y: number; size: number; label: string | null; color: string },
  settings: { labelSize: number; labelFont: string; labelWeight: string; labelColor: { color?: string; attribute?: string } },
): void {
  const size = settings.labelSize;
  const font = settings.labelFont;
  const weight = settings.labelWeight;

  context.font = `${weight} ${size}px ${font}`;

  context.fillStyle = '#27272a';
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
    context.beginPath();
    context.arc(data.x, data.y, data.size + PADDING, 0, Math.PI * 2);
    context.closePath();
    context.fill();
  }

  context.shadowOffsetX = 0;
  context.shadowOffsetY = 0;
  context.shadowBlur = 0;

  if (data.label) {
    const color = settings.labelColor.color || '#ffffff';
    context.fillStyle = color;
    context.font = `${weight} ${size}px ${font}`;
    context.fillText(data.label, data.x + data.size + 3, data.y + size / 3);
  }
}

/** No edge label is drawn this many node sizes from either end, because a node's label runs out to its right and
 *  grows with the node as the view zooms. */
const NODE_LABEL_SIZE_FACTOR = 8;
const NODE_LABEL_MIN_CLEARANCE = 50;

/** Centred on the edge rather than below it, as Sigma draws it, on a dark background so it can be read over
 *  other edges. */
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

  sx += (dx * sSize) / d;
  sy += (dy * sSize) / d;
  tx -= (dx * tSize) / d;
  ty -= (dy * tSize) / d;
  const cx = (sx + tx) / 2;
  const cy = (sy + ty) / 2;
  dx = tx - sx;
  dy = ty - sy;
  d = Math.sqrt(dx * dx + dy * dy);

  const sClearance = Math.max(sSize * NODE_LABEL_SIZE_FACTOR, NODE_LABEL_MIN_CLEARANCE);
  const tClearance = Math.max(tSize * NODE_LABEL_SIZE_FACTOR, NODE_LABEL_MIN_CLEARANCE);
  const dSource = Math.sqrt((cx - sourceData.x) ** 2 + (cy - sourceData.y) ** 2);
  const dTarget = Math.sqrt((cx - targetData.x) ** 2 + (cy - targetData.y) ** 2);
  if (dSource < sClearance || dTarget < tClearance) return;

  const truncated = truncateLabel(context, label, d);
  if (truncated === null) return;
  label = truncated;
  const textLength = context.measureText(label).width;

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

  const pillW = textLength + PAD_X * 2;
  const pillH = size + PAD_Y * 2;
  const pillR = 3;
  const pillX = -textLength / 2 - PAD_X;
  const pillY = -size / 2 - PAD_Y;

  context.fillStyle = highlighted ? '#18181b' : '#09090bcc';
  drawRoundedPill(context, pillX, pillY, pillW, pillH, pillR);
  context.fill();

  context.fillStyle = highlighted ? '#e4e4e7' : (settings.edgeLabelColor.color || '#a1a1aa');
  context.fillText(label, -textLength / 2, size / 3);
  context.restore();
}

/** Sigma's own `drawLabel` draws bare text, which edges crossing behind it make unreadable, so this draws a dark
 *  background behind each label. */
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

  context.fillStyle = '#09090bcc';
  drawRoundedPill(context, pillX, pillY, pillW, pillH, pillR);
  context.fill();

  context.fillStyle = settings.labelColor.color || '#ffffff';
  context.fillText(data.label, data.x + data.size + 3, data.y + size / 3);
}

// Kept outside the component, so the container is never handed new settings and does not rebuild Sigma.
const SIGMA_SETTINGS = {
  renderLabels: true,
  renderEdgeLabels: true,
  defaultEdgeType: 'arrow' as const,
  enableEdgeEvents: true,
  labelColor: { color: '#ffffff' },
  labelSize: 10,
  labelFont: 'Inter, system-ui, sans-serif',
  labelGridCellSize: 120,
  labelDensity: 0.5,
  labelRenderedSizeThreshold: 4,
  edgeLabelColor: { color: '#a1a1aa' },
  edgeLabelSize: 11,
  edgeLabelFont: 'Inter, system-ui, sans-serif',
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  defaultDrawNodeLabel: drawNodeLabelWithBackground as any,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  defaultDrawEdgeLabel: drawCenteredEdgeLabel as any,
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  defaultDrawNodeHover: drawDarkNodeHover as any,
  zIndex: true,
  minCameraRatio: 0.02,
  maxCameraRatio: 20,
  // Edges and labels are left out while the camera or the layout moves, which skips the costliest drawing; they
  // come back as soon as movement stops.
  hideEdgesOnMove: true,
  hideLabelsOnMove: true,
};

/** Drawn as its own composited layer, so animation leaves no trail of old frames and Safari keeps the children's
 *  stacking order. */
const CONTAINER_STYLE: React.CSSProperties = {
  width: '100%',
  height: '100%',
  transform: 'translateZ(0)',
  WebkitTransform: 'translateZ(0)',
  willChange: 'transform',
  isolation: 'isolate',
};

export function SigmaCanvas({ things, relationships, searchQuery, searchOptions }: Props) {
  // The container's own graph allows no parallel edges, and importing parallel relationships into it fails.
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
      <ViewFitController />
      <LogicalNodeController />
      <WebGLContextGuard />
      <NodeReducer searchQuery={searchQuery} searchOptions={searchOptions} />
      <GraphToolbar />
    </SigmaContainer>
  );
}
