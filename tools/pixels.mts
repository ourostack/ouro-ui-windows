// What a control drew under its words, against what it says it drew. dump.ps1 records each Button's and Badge's middle
// row of pixels (`textLine`), which crosses its words. Words drawn as reported are the label colour on the fill, so
// every pixel on that row is the fill, the label, or a blend of the two (anti-aliased edges). Anything else is a
// colour the control didn't report, such as the black backplate WinUI put behind words under a contrast theme.

const rgb = (hex: string) => [3, 5, 7].map((i) => parseInt(hex.slice(i, i + 2), 16));

/** How far a colour is from the blends of `a` and `b`, in RGB units. */
export function offBlend(c: string, a: string, b: string): number {
  const [p, x, y] = [rgb(c), rgb(a), rgb(b)];
  const d = y.map((v, i) => v - x[i]!);
  const len = d.reduce((s, v) => s + v * v, 0);
  const t = len === 0 ? 0 : Math.min(1, Math.max(0, d.reduce((s, v, i) => s + v * (p[i]! - x[i]!), 0) / len));
  return Math.hypot(...p.map((v, i) => v - (x[i]! + t * d[i]!)));
}

/** The furthest a pixel may be from the fill-to-label blends: rounding and gamma in anti-aliasing stay well inside. */
export const TOLERANCE = 32;

/**
 * Whether a row of pixels under a control's words shows only its reported fill and label. `ground` is the fill, or
 * what shows through when the fill is transparent. Up to 2% of the row may stray, for a stray sub-pixel.
 */
export function wordsAsReported(line: string[], ground: string, label: string): { ok: boolean; why: string } {
  const stray = line.filter((c) => offBlend(c, ground, label) > TOLERANCE);
  // Thin strokes are anti-aliased on a single row, so ink is any pixel at least halfway from the fill to the label.
  const inked = line.some((c) => offBlend(c, label, label) <= offBlend(ground, label, label) / 2);
  const ok = line.length > 0 && inked && stray.length <= Math.floor(line.length * 0.02);
  const seen = [...new Set(stray)].slice(0, 3).join(", ");
  return { ok, why: `${stray.length}/${line.length} pixels off ${label} on ${ground}${seen ? ` (${seen})` : ""}${inked ? "" : "; no pixel even halfway to the label colour"}` };
}
