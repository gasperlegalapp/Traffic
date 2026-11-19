import { Path, bezier, offsetPolyline, type Vec } from './path';
import { linkConnectors, type Lane, type Marking, type StopLine, type Strip, type Turn, type World } from './network';

/**
 * The driver lab test track: one four-way intersection whose arms are joined in
 * pairs by two loops, so cars circulate forever and every turn is possible.
 *
 * - North and east arms are joined by a plain loop with two lanes each way.
 * - South and west arms are joined by a loop where the right lane ends, the
 *   road runs single-lane for a stretch, then the right lane opens again.
 *
 * Traffic drives on the right. Plane: x east, y north, meters.
 */

export const LANE_W = 3.5;
/** Distance from the center of the intersection to each stop line. */
export const BOX = 12;
/** Radius of the loops, and how far the arms reach from the center. */
const LOOP = 48;
/** 30 mph. */
const SPEED_LIMIT = 13.4;
/** Share of the loop where the right lane ends, and where it opens again. */
const DROP_AT = 0.36;
const REOPEN_AT = 0.62;
const TAPER = 18;
/** Lane changes are not allowed this close to the stop line. */
export const SOLID_LINE = 20;

function loopCenterline(sign: 1 | -1): Vec[] {
  const pts: Vec[] = [];
  for (let y = BOX; y < LOOP; y += 1) pts.push({ x: 0, y });
  const steps = Math.ceil(1.5 * Math.PI * LOOP);
  for (let i = 0; i <= steps; i++) {
    const ang = Math.PI - (1.5 * Math.PI * i) / steps;
    pts.push({ x: LOOP + LOOP * Math.cos(ang), y: LOOP + LOOP * Math.sin(ang) });
  }
  for (let x = LOOP - 1; x >= BOX; x -= 1) pts.push({ x, y: 0 });
  return pts.map((p) => ({ x: p.x * sign, y: p.y * sign }));
}

function stations(pts: Vec[]): number[] {
  const st = [0];
  for (let i = 1; i < pts.length; i++) {
    st.push(st[i - 1] + Math.hypot(pts[i].x - pts[i - 1].x, pts[i].y - pts[i - 1].y));
  }
  return st;
}

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * Math.min(1, Math.max(0, t));
}

export function buildLabWorld(): World {
  const lanes: Lane[] = [];
  const surfaces: Strip[] = [];
  const markings: Marking[] = [];
  const stopLines: StopLine[] = [];
  const inbound: number[][] = [[], [], [], []];
  const outbound: number[][] = [[], [], [], []];

  const addRoadLane = (pts: Vec[]): Lane => {
    const path = new Path(pts);
    const lane: Lane = {
      id: lanes.length,
      kind: 'road',
      path,
      length: path.length,
      speedLimit: SPEED_LIMIT,
      lefts: [],
      rights: [],
      preds: [],
    };
    lanes.push(lane);
    return lane;
  };

  /** One direction of a loop: lanes, pavement and paint. */
  const buildDirection = (center: Vec[], hasDrop: boolean, fromArm: number, toArm: number, paintCenter: boolean) => {
    const st = stations(center);
    const total = st[st.length - 1];
    const drop = total * DROP_AT;
    const reopen = total * REOPEN_AT;
    const edge = (i: number): number => {
      if (!hasDrop) return 2 * LANE_W;
      const s = st[i];
      if (s <= drop) return 2 * LANE_W;
      if (s < drop + TAPER) return lerp(2 * LANE_W, LANE_W, (s - drop) / TAPER);
      if (s < reopen - TAPER) return LANE_W;
      if (s < reopen) return lerp(LANE_W, 2 * LANE_W, (s - (reopen - TAPER)) / TAPER);
      return 2 * LANE_W;
    };
    const slice = (pts: Vec[], from: number, to: number) => pts.filter((_, i) => st[i] >= from - 1e-6 && st[i] <= to + 1e-6);

    const leftPts = offsetPolyline(center, 0.5 * LANE_W);
    const rightPts = offsetPolyline(center, 1.5 * LANE_W);
    const left = addRoadLane(leftPts);
    let rightStart: Lane;
    let rightEnd: Lane;
    if (hasDrop) {
      rightStart = addRoadLane(slice(rightPts, 0, drop));
      rightEnd = addRoadLane(slice(rightPts, reopen, total));
      left.rights.push(rightStart.id, rightEnd.id);
      rightStart.lefts.push(left.id);
      rightEnd.lefts.push(left.id);
    } else {
      rightStart = rightEnd = addRoadLane(rightPts);
      left.rights.push(rightStart.id);
      rightStart.lefts.push(left.id);
    }
    outbound[fromArm] = [left.id, rightStart.id];
    inbound[toArm] = [left.id, rightEnd.id];
    left.arm = toArm;
    rightEnd.arm = toArm;
    left.connectors = {};
    rightEnd.connectors = {};

    surfaces.push({ left: center, right: offsetPolyline(center, edge) });
    markings.push({ pts: offsetPolyline(center, (i) => edge(i) - 0.25), width: 0.14, color: 'white' });
    if (paintCenter) {
      markings.push({ pts: offsetPolyline(center, 0.16), width: 0.12, color: 'yellow' });
      markings.push({ pts: offsetPolyline(center, -0.16), width: 0.12, color: 'yellow' });
    }
    const divider = offsetPolyline(center, LANE_W);
    const dashed = (from: number, to: number) => markings.push({ pts: slice(divider, from, to), width: 0.14, color: 'white', dash: [3, 6] });
    if (hasDrop) {
      dashed(0, drop);
      dashed(reopen, total - SOLID_LINE);
    } else {
      dashed(0, total - SOLID_LINE);
    }
    markings.push({ pts: slice(divider, total - SOLID_LINE, total), width: 0.14, color: 'white' });

    const end = center[center.length - 1];
    const before = center[center.length - 2];
    const h = Math.atan2(end.y - before.y, end.x - before.x);
    const rx = Math.sin(h);
    const ry = -Math.cos(h);
    stopLines.push({
      arm: toArm,
      a: { x: end.x + rx * 0.3, y: end.y + ry * 0.3 },
      b: { x: end.x + rx * (2 * LANE_W - 0.3), y: end.y + ry * (2 * LANE_W - 0.3) },
      post: { x: end.x + rx * (2 * LANE_W + 1.6), y: end.y + ry * (2 * LANE_W + 1.6) },
      h,
    });
  };

  // Arms: 0 north, 1 east, 2 south, 3 west.
  const ne = loopCenterline(1);
  const sw = loopCenterline(-1);
  buildDirection(ne, false, 0, 1, true);
  buildDirection([...ne].reverse(), false, 1, 0, false);
  buildDirection(sw, true, 2, 3, true);
  buildDirection([...sw].reverse(), true, 3, 2, false);

  // Paths through the intersection.
  const connect = (fromId: number, toId: number, turn: Turn) => {
    const from = lanes[fromId];
    const to = lanes[toId];
    const a = from.path.at(from.length);
    const b = to.path.at(0);
    const chord = Math.hypot(b.x - a.x, b.y - a.y);
    const pts = turn === 'S' ? [a, b] : bezier(a, b, (0.5523 * chord) / Math.SQRT2);
    const path = new Path(pts);
    const lane: Lane = {
      id: lanes.length,
      kind: 'connector',
      path,
      length: path.length,
      speedLimit: SPEED_LIMIT,
      lefts: [],
      rights: [],
      preds: [fromId],
      from: fromId,
      next: toId,
      turn,
      fromArm: from.arm,
    };
    lanes.push(lane);
    from.connectors![turn] = lane.id;
    to.preds.push(lane.id);
  };
  for (let arm = 0; arm < 4; arm++) {
    const [in0, in1] = inbound[arm];
    connect(in0, outbound[(arm + 1) % 4][0], 'L');
    connect(in0, outbound[(arm + 2) % 4][0], 'S');
    connect(in1, outbound[(arm + 2) % 4][1], 'S');
    connect(in1, outbound[(arm + 3) % 4][1], 'R');
  }
  linkConnectors(lanes);

  // Pavement inside the intersection, with rounded corners.
  const half = 2 * LANE_W;
  const r = BOX - half;
  const box: Vec[] = [];
  const corners: [number, number, number][] = [
    [1, 1, Math.PI],
    [1, -1, Math.PI / 2],
    [-1, -1, 0],
    [-1, 1, -Math.PI / 2],
  ];
  for (const [sx, sy, start] of corners) {
    for (let i = 0; i <= 8; i++) {
      const ang = start + (Math.PI / 2) * (i / 8);
      box.push({ x: sx * BOX + r * Math.cos(ang), y: sy * BOX + r * Math.sin(ang) });
    }
  }

  return {
    lanes,
    laneWidth: LANE_W,
    inbound,
    outbound,
    surfaces,
    box,
    markings,
    stopLines,
    extent: 2 * LOOP + 2 * LANE_W + 8,
  };
}
