import { linearScale, niceTicks, type Scale } from './chartScale';

export const TOP_GUTTER = 8;
export const PLOT_HEIGHT = 220;
export const LEFT_GUTTER = 40;
export const RIGHT_GUTTER = 8;
export const AXIS_BAND = 22;
/** The width drawn at before the card's own width has been measured. */
export const FALLBACK_WIDTH = 480;
const TICK_COUNT = 5;

type ChartFrame = {
  width: number;
  height: number;
  plotTop: number;
  plotBottom: number;
  plotLeft: number;
  plotRight: number;
  y: Scale;
  ticks: number[];
};

export function frameOf(measuredWidth: number, domain: [number, number]): ChartFrame {
  const width = measuredWidth || FALLBACK_WIDTH;
  const plotTop = TOP_GUTTER;
  const plotBottom = TOP_GUTTER + PLOT_HEIGHT;
  return {
    width,
    height: plotBottom + AXIS_BAND,
    plotTop,
    plotBottom,
    plotLeft: LEFT_GUTTER,
    plotRight: width - RIGHT_GUTTER,
    y: linearScale(domain, [plotBottom, plotTop]),
    ticks: niceTicks(domain[0], domain[1], TICK_COUNT).filter((tick) => tick >= domain[0] && tick <= domain[1]),
  };
}
