import { Path, type Vec } from './path';

export type Turn = 'L' | 'S' | 'R';

/** Where two paths through the intersection come too close to share. */
export interface Conflict {
  /** Lane id of the other connector. */
  foe: number;
  /** Shared stretch, measured along this connector. */
  sIn: number;
  sOut: number;
  /** The same stretch, measured along the other connector. */
  foeIn: number;
  foeOut: number;
}

export interface Lane {
  id: number;
  kind: 'road' | 'connector';
  path: Path;
  length: number;
  /** Meters per second. */
  speedLimit: number;
  /** Neighbor lanes going the same way. A lane can have several along its length. */
  lefts: number[];
  rights: number[];
  /** Lanes that feed into the start of this one. */
  preds: number[];

  /** Road lanes that end at the stop line: which arm, and the turns allowed from here. */
  arm?: number;
  connectors?: Partial<Record<Turn, number>>;

  /** Connector lanes (paths through the intersection). */
  from?: number;
  next?: number;
  turn?: Turn;
  fromArm?: number;
  conflicts?: Conflict[];
  /** Connectors that start from the same lane, and where they split apart. */
  siblings?: { id: number; sDiverge: number }[];
}

export interface Strip {
  left: Vec[];
  right: Vec[];
}

export interface Marking {
  pts: Vec[];
  width: number;
  color: 'white' | 'yellow';
  /** Meters painted, meters skipped. */
  dash?: [number, number];
}

export interface StopLine {
  arm: number;
  a: Vec;
  b: Vec;
  /** Roadside spot for the signal head or stop sign. */
  post: Vec;
  /** Heading of traffic arriving at this line. */
  h: number;
}

export interface World {
  lanes: Lane[];
  laneWidth: number;
  /** Inbound and outbound lane ids for each arm (0 north, 1 east, 2 south, 3 west), left lane first. */
  inbound: number[][];
  outbound: number[][];
  surfaces: Strip[];
  box: Vec[];
  markings: Marking[];
  stopLines: StopLine[];
  /** Half the width of the square that contains everything. */
  extent: number;
}

/** Paths closer than this (centerline to centerline) cannot be used by two cars at once. */
const CONFLICT_DIST = 2.6;
const DIVERGE_DIST = 2.2;

/** Fill in conflicts and siblings for every connector. */
export function linkConnectors(lanes: Lane[]): void {
  const conns = lanes.filter((l) => l.kind === 'connector');
  for (const a of conns) {
    a.conflicts = [];
    a.siblings = [];
  }
  for (const a of conns) {
    for (const b of conns) {
      if (a.id === b.id) continue;
      if (a.from === b.from) {
        a.siblings!.push({ id: b.id, sDiverge: divergeAt(a.path, b.path) });
        continue;
      }
      if (a.fromArm === b.fromArm) continue;
      const mine = closeRange(a.path, b.path);
      const theirs = closeRange(b.path, a.path);
      if (mine && theirs) {
        a.conflicts!.push({ foe: b.id, sIn: mine[0], sOut: mine[1], foeIn: theirs[0], foeOut: theirs[1] });
      }
    }
    a.conflicts!.sort((p, q) => p.sIn - q.sIn);
  }
}

/** The stretch of path a that lies within CONFLICT_DIST of path b. */
function closeRange(a: Path, b: Path): [number, number] | null {
  let first = -1;
  let last = -1;
  const step = 0.25;
  for (let s = 0; s <= a.length + 1e-6; s += step) {
    const p = a.at(Math.min(s, a.length));
    if (b.project(p.x, p.y).d < CONFLICT_DIST) {
      if (first < 0) first = s;
      last = s;
    }
  }
  if (first < 0) return null;
  return [Math.max(0, first - step), Math.min(a.length, last + step)];
}

function divergeAt(a: Path, b: Path): number {
  for (let s = 0; s <= a.length; s += 0.25) {
    const p = a.at(s);
    if (b.project(p.x, p.y).d > DIVERGE_DIST) return s;
  }
  return a.length;
}

export function conflictBetween(a: Lane, bId: number): Conflict | undefined {
  if (!a.conflicts) return undefined;
  for (const c of a.conflicts) if (c.foe === bId) return c;
  return undefined;
}
