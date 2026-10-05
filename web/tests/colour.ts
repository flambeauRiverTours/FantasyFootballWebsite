import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

/** oklch(L% C H) -> linear sRGB, clamped. */
export function toLinearRgb(l: number, c: number, hDeg: number): [number, number, number] {
  const h = (hDeg * Math.PI) / 180;
  const a = c * Math.cos(h);
  const b = c * Math.sin(h);

  const lc = (l + 0.3963377774 * a + 0.2158037573 * b) ** 3;
  const mc = (l - 0.1055613458 * a - 0.0638541728 * b) ** 3;
  const sc = (l - 0.0894841775 * a - 1.291485548 * b) ** 3;

  return [
    4.0767416621 * lc - 3.3077115913 * mc + 0.2309699292 * sc,
    -1.2684380046 * lc + 2.6097574011 * mc - 0.3413193965 * sc,
    -0.0041960863 * lc - 0.7034186147 * mc + 1.707614701 * sc,
  ].map((v) => Math.min(1, Math.max(0, v))) as [number, number, number];
}

export function luminance(oklch: string): number {
  const m = oklch.match(/oklch\(\s*([\d.]+)%\s+([\d.]+)\s+([\d.]+)/);
  if (!m) throw new Error(`not an oklch colour: ${oklch}`);
  const [r, g, b] = toLinearRgb(Number(m[1]) / 100, Number(m[2]), Number(m[3]));
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

export function contrast(a: string, b: string): number {
  const [hi, lo] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

/** The token values as declared, for the light theme and the dark override. */
export function tokens(): { light: Record<string, string>; dark: Record<string, string> } {
  const css = readFileSync(resolve(process.cwd(), 'src/index.css'), 'utf8');
  const darkAt = css.indexOf('@media (prefers-color-scheme: dark)');
  const read = (text: string) => {
    const out: Record<string, string> = {};
    for (const [, name, value] of text.matchAll(/(--[\w-]+):\s*(oklch\([^)]*\))/g)) {
      out[name] = value;
    }
    return out;
  };
  const light = read(css.slice(0, darkAt));
  return { light, dark: { ...light, ...read(css.slice(darkAt)) } };
}
