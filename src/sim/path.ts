export interface Vec {
  x: number;
  y: number;
}

export interface Pose extends Vec {
  /** Heading in radians, counter-clockwise from +x (east). */
  h: number;
}

export interface Projection {
  /** Distance along the path of the closest point. */
  s: number;
  /** Distance from the point to the path. */
  d: number;
}

/** A polyline measured by distance along it. Plane: x east, y north, meters. */
export class Path {
  readonly pts: Vec[];
  readonly cum: Float64Array;
  /** Turn radius at each vertex (Infinity on straights). */
  readonly rad: Float64Array;
  readonly length: number;

  constructor(points: Vec[]) {
    const pts: Vec[] = [];
    for (const p of points) {
      const last = pts[pts.length - 1];
      if (!last || Math.hypot(p.x - last.x, p.y - last.y) > 1e-6) pts.push({ x: p.x, y: p.y });
    }
    if (pts.length < 2) throw new Error('A path needs at least two distinct points');
    this.pts = pts;
    this.cum = new Float64Array(pts.length);
    for (let i = 1; i < pts.length; i++) {
      this.cum[i] = this.cum[i - 1] + Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y);
    }
    this.length = this.cum[pts.length - 1];
    this.rad = new Float64Array(pts.length).fill(Infinity);
    for (let i = 1; i < pts.length - 1; i++) {
      this.rad[i] = circumradius(pts[i - 1], pts[i], pts[i + 1]);
    }
    if (pts.length > 2) {
      this.rad[0] = this.rad[1];
      this.rad[pts.length - 1] = this.rad[pts.length - 2];
    }
  }

  /** Index i of the segment [i, i+1] that contains distance s. */
  private segment(s: number): number {
    const n = this.pts.length;
    if (s <= 0) return 0;
    if (s >= this.length) return n - 2;
    let lo = 0;
    let hi = n - 1;
    while (hi - lo > 1) {
      const mid = (lo + hi) >> 1;
      if (this.cum[mid] <= s) lo = mid;
      else hi = mid;
    }
    return lo;
  }

  /** Position and heading at distance s. Outside the path it continues straight. */
  at(s: number): Pose {
    const i = this.segment(s);
    const a = this.pts[i];
    const b = this.pts[i + 1];
    const len = this.cum[i + 1] - this.cum[i];
    const t = (s - this.cum[i]) / len;
    return {
      x: a.x + (b.x - a.x) * t,
      y: a.y + (b.y - a.y) * t,
      h: Math.atan2(b.y - a.y, b.x - a.x),
    };
  }

  radiusAt(s: number): number {
    const i = this.segment(s);
    return Math.min(this.rad[i], this.rad[i + 1]);
  }

  minRadius(): number {
    let r = Infinity;
    for (let i = 0; i < this.rad.length; i++) r = Math.min(r, this.rad[i]);
    return r;
  }

  /** Closest point on the path to (x, y). */
  project(x: number, y: number): Projection {
    let bestD2 = Infinity;
    let bestS = 0;
    for (let i = 0; i < this.pts.length - 1; i++) {
      const a = this.pts[i];
      const b = this.pts[i + 1];
      const dx = b.x - a.x;
      const dy = b.y - a.y;
      const len2 = dx * dx + dy * dy;
      let t = ((x - a.x) * dx + (y - a.y) * dy) / len2;
      t = t < 0 ? 0 : t > 1 ? 1 : t;
      const px = a.x + dx * t;
      const py = a.y + dy * t;
      const d2 = (x - px) * (x - px) + (y - py) * (y - py);
      if (d2 < bestD2) {
        bestD2 = d2;
        bestS = this.cum[i] + Math.sqrt(len2) * t;
      }
    }
    return { s: bestS, d: Math.sqrt(bestD2) };
  }
}

function circumradius(a: Vec, b: Vec, c: Vec): number {
  const ab = Math.hypot(b.x - a.x, b.y - a.y);
  const bc = Math.hypot(c.x - b.x, c.y - b.y);
  const ca = Math.hypot(a.x - c.x, a.y - c.y);
  const cross = Math.abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x));
  if (cross < 1e-9) return Infinity;
  return (ab * bc * ca) / (2 * cross);
}

/**
 * Shift a polyline sideways. A positive offset is to the right of the direction
 * of travel. The offset can vary by vertex.
 */
export function offsetPolyline(pts: Vec[], offset: number | ((i: number) => number)): Vec[] {
  const out: Vec[] = [];
  for (let i = 0; i < pts.length; i++) {
    const a = pts[Math.max(0, i - 1)];
    const b = pts[Math.min(pts.length - 1, i + 1)];
    const tx = b.x - a.x;
    const ty = b.y - a.y;
    const len = Math.hypot(tx, ty) || 1;
    const off = typeof offset === 'number' ? offset : offset(i);
    out.push({ x: pts[i].x + (ty / len) * off, y: pts[i].y - (tx / len) * off });
  }
  return out;
}

/** Cubic Bezier from pose a to pose b, sampled as a polyline. */
export function bezier(a: Pose, b: Pose, k: number, samples = 24): Vec[] {
  const p1 = { x: a.x + Math.cos(a.h) * k, y: a.y + Math.sin(a.h) * k };
  const p2 = { x: b.x - Math.cos(b.h) * k, y: b.y - Math.sin(b.h) * k };
  const out: Vec[] = [];
  for (let i = 0; i <= samples; i++) {
    const t = i / samples;
    const u = 1 - t;
    const w0 = u * u * u;
    const w1 = 3 * u * u * t;
    const w2 = 3 * u * t * t;
    const w3 = t * t * t;
    out.push({
      x: w0 * a.x + w1 * p1.x + w2 * p2.x + w3 * b.x,
      y: w0 * a.y + w1 * p1.y + w2 * p2.y + w3 * b.y,
    });
  }
  return out;
}
