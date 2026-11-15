import type { ScaleLinear } from 'd3-scale';

/** A d3 linear scale from data values to pixels, as every drawing uses it. */
export type LinearScale = ScaleLinear<number, number>;

/** The line chart's margins. The brush lines up with them. */
export const Margin = { top: 16, right: 84, bottom: 28, left: 44 };

/** The narrowest chart that keeps its line-end names; roughly the 40rem breakpoint less panel padding. */
export const LabelledMinWidth = 600;

/** The right margin once the names are dropped: room for the last point, nothing more. */
const UnlabelledRight = 16;

/** The line chart's left and right insets in pixels, so the brush below can line up with it. */
export function plotInsets(width: number): { left: number; right: number } {
  return { left: Margin.left, right: width >= LabelledMinWidth ? Margin.right : UnlabelledRight };
}
