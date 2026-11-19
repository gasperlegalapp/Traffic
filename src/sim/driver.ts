/** The six dials that describe a driver. Everything else is derived from them. */
export interface DriverParams {
  /** Seconds before the driver acts on what they see. */
  reaction: number;
  /** Desired speed as a multiple of the speed limit. */
  speed: number;
  /** Following gap in seconds. */
  headway: number;
  /** 0 calm to 1 pushy: harder acceleration and braking, tighter gaps, bolder lane changes. */
  aggression: number;
  /** 0 selfish to 1 courteous: how much slowing others down counts against a lane change. */
  politeness: number;
  /** 0 to 1: chance of obeying each red light or stop sign. */
  compliance: number;
}

export const DRIVER_RANGES: Record<keyof DriverParams, [number, number]> = {
  reaction: [0.1, 1.6],
  speed: [0.6, 1.4],
  headway: [0.5, 2.6],
  aggression: [0, 1],
  politeness: [0, 1],
  compliance: [0, 1],
};

export const PRESETS: Record<string, DriverParams> = {
  Calm: { reaction: 0.7, speed: 0.9, headway: 2.0, aggression: 0.15, politeness: 0.85, compliance: 1 },
  Normal: { reaction: 0.5, speed: 1.0, headway: 1.4, aggression: 0.4, politeness: 0.5, compliance: 1 },
  Pushy: { reaction: 0.35, speed: 1.2, headway: 0.8, aggression: 0.9, politeness: 0.1, compliance: 0.9 },
  Sleepy: { reaction: 1.3, speed: 0.85, headway: 1.5, aggression: 0.3, politeness: 0.5, compliance: 0.95 },
};

export interface Derived {
  /** Maximum acceleration, m/s². */
  a: number;
  /** Comfortable braking, m/s². */
  b: number;
  /** Gap kept when stopped, m. */
  s0: number;
  /** Sideways acceleration tolerated in curves, m/s². */
  aLat: number;
  /** Seconds a lane change takes. */
  lcTime: number;
  /** Gain needed before a lane change is worth it, m/s². */
  lcThreshold: number;
  /** Braking the driver is willing to force on the car behind when changing lanes, m/s². */
  bSafe: number;
  /** 0 to 1: how hard the driver overreacts when startled. */
  nerves: number;
}

export function derive(p: DriverParams): Derived {
  const g = p.aggression;
  return {
    a: 1.4 + 1.6 * g,
    b: 1.8 + 1.7 * g,
    s0: 2.4 - 1.0 * g,
    aLat: 1.8 + 1.6 * g,
    lcTime: 3.6 - 1.6 * g,
    lcThreshold: 0.15 + 0.45 * (1 - g),
    bSafe: 2.5 + 2.5 * g,
    nerves: 1 - g,
  };
}

/** Hardest braking a car can physically do, m/s². */
export const MAX_BRAKE = 8;

/** Acceleration on an empty road toward desired speed v0. */
export function freeAccel(v: number, v0: number, d: Derived): number {
  if (v0 <= 0.01) return -d.b;
  if (v <= v0) return d.a * (1 - Math.pow(v / v0, 4));
  return -d.b * (1 - (v0 / v) * (v0 / v));
}

/**
 * Intelligent Driver Model: acceleration given the bumper-to-bumper gap to
 * whatever is ahead and the closing speed dv (own speed minus theirs).
 */
export function idm(v: number, v0: number, gap: number, dv: number, headway: number, d: Derived): number {
  const want = d.s0 + Math.max(0, v * headway + (v * dv) / (2 * Math.sqrt(d.a * d.b)));
  const g = Math.max(gap, 0.05);
  const acc = freeAccel(v, v0, d) - d.a * (want / g) * (want / g);
  return Math.max(acc, -MAX_BRAKE);
}

/** Seconds to cover distance dist starting at v, accelerating at a up to vCap. */
export function travelTime(dist: number, v: number, a: number, vCap: number): number {
  if (dist <= 0) return 0;
  v = Math.max(v, 0);
  if (v >= vCap) return dist / Math.max(v, 0.1);
  const ramp = (vCap * vCap - v * v) / (2 * a);
  if (dist <= ramp) return (-v + Math.sqrt(v * v + 2 * a * dist)) / a;
  return (vCap - v) / a + (dist - ramp) / vCap;
}
