import type { ScaleLinear } from 'd3-scale';

/** A d3 linear scale from data values to pixels, as every drawing uses it. */
export type LinearScale = ScaleLinear<number, number>;
