import { derive, freeAccel, idm, travelTime, MAX_BRAKE, PRESETS, DRIVER_RANGES, type Derived, type DriverParams } from './driver';
import { Junction, type JunctionMode } from './junction';
import { SOLID_LINE } from './labWorld';
import { conflictBetween, type Lane, type Turn, type World } from './network';
import { Rng } from './rng';

/** Length of one simulation step in seconds. The sim always advances in these steps. */
export const DT = 1 / 30;
const OBS_N = 64;
export const HIST_N = 300;
/** Ticks between history samples (10 per second). */
const HIST_EVERY = 3;
const LOOKAHEAD = 150;
const CURVE_LOOK = [0, 8, 16, 28, 42, 60];
/** Distance over which a driver who must change lanes gets steadily more insistent. */
const MANDATORY_DIST = 150;

export type CarState =
  | 'CRUISE'
  | 'FOLLOW'
  | 'BRAKE'
  | 'STOPPING'
  | 'WAIT'
  | 'YIELD'
  | 'LETTING IN'
  | 'MERGE'
  | 'LANE CHANGE'
  | 'STARTLED'
  | 'CRASH';

type Binding = 'free' | 'leader' | 'line' | 'yield' | 'coop' | 'end' | 'startle';

/** What a driver has decided about the intersection it is approaching. */
interface JunctionMemo {
  yellow?: 'go' | 'stop';
  redRoll?: boolean;
  /** Decided to run the red light. */
  run?: boolean;
  /** All-way stop: when the car came to rest at the line, and whether it has been let go. */
  arrival?: number;
  released?: boolean;
  order?: number;
}

export interface Car {
  id: number;
  color: number;
  len: number;
  wid: number;
  drv: DriverParams;

  lane: number;
  /** Distance of the car's center along its lane. */
  s: number;
  v: number;
  a: number;
  prevLane: number;
  turn: Turn;

  /** Sideways offset from the lane center during a lane change (positive is right). */
  lat: number;
  lcFrom: number;
  lcT: number;
  lcDur: number;
  /** Lane the car is still partly in, and its position there relative to s. */
  ghostLane: number;
  ghostOff: number;
  lcCooldownUntil: number;
  nextLcCheck: number;
  /** Side the driver needs to move to (-1 left, 1 right), and how badly (0 to 1). */
  wantSide: number;
  urgency: number;

  junc: JunctionMemo;
  startleAt: number;
  startleUntil: number;
  crashed: boolean;
  crashedAt: number;

  /** World pose, refreshed every step. */
  x: number;
  y: number;
  h: number;
  /** Position on the lane centerline (ignores the sideways offset). */
  cx: number;
  cy: number;

  /** Readouts for the overlay and inspector. */
  state: CarState;
  blinker: number;
  v0: number;
  leaderId: number;
  gap: number;
  stopDist: number;
  stopKind: Binding;

  aNext: number;
  aLead: number;
  obsGap: Float32Array;
  obsV: Float32Array;
  hist: { v: Float32Array; gap: Float32Array; a: Float32Array; n: number; i: number };
}

interface Entry {
  car: Car;
  s: number;
  ghost: boolean;
}

interface Neighbor {
  car: Car;
  /** Bumper-to-bumper gap. */
  gap: number;
}

const CAR_COLORS = [0xf25f5c, 0xffe066, 0x70c1b3, 0x4ea8de, 0xf4a261, 0xe9ecef, 0xb388eb, 0x8ac926, 0xff8fab, 0x90e0ef];

function clamp(x: number, lo: number, hi: number): number {
  return x < lo ? lo : x > hi ? hi : x;
}

export class Sim {
  readonly world: World;
  readonly lanes: Lane[];
  readonly junction = new Junction();
  cars: Car[] = [];
  time = 0;
  tick = 0;
  rng: Rng;
  stats = { crashes: 0, passes: 0 };
  /** Remove crashed cars after crashClearAfter seconds. */
  autoClear = true;
  crashClearAfter = 12;
  onCrash?: (a: Car, b: Car) => void;

  private nextId = 1;
  private laneCars: Entry[][];
  private active: { car: Car; conn: Lane; u: number }[] = [];
  private minRad: number[];

  constructor(world: World, seed = 1) {
    this.world = world;
    this.lanes = world.lanes;
    this.rng = new Rng(seed);
    this.laneCars = world.lanes.map(() => []);
    this.minRad = world.lanes.map((l) => l.path.minRadius());
  }

  // ---------------------------------------------------------------- cars

  addCar(opts: { lane: number; s: number; v?: number; drv?: DriverParams; turn?: Turn; color?: number }): Car {
    const id = this.nextId++;
    const car: Car = {
      id,
      color: opts.color ?? CAR_COLORS[id % CAR_COLORS.length],
      len: 4.5,
      wid: 1.9,
      drv: { ...(opts.drv ?? PRESETS.Normal) },
      lane: opts.lane,
      s: opts.s,
      v: opts.v ?? 0,
      a: 0,
      prevLane: -1,
      turn: opts.turn ?? this.pickTurn(),
      lat: 0,
      lcFrom: 0,
      lcT: 1,
      lcDur: 1,
      ghostLane: -1,
      ghostOff: 0,
      lcCooldownUntil: 0,
      nextLcCheck: this.time + this.rng.next() * 0.4,
      wantSide: 0,
      urgency: 0,
      junc: {},
      startleAt: 0,
      startleUntil: 0,
      crashed: false,
      crashedAt: 0,
      x: 0,
      y: 0,
      h: 0,
      cx: 0,
      cy: 0,
      state: 'CRUISE',
      blinker: 0,
      v0: 0,
      leaderId: -1,
      gap: Infinity,
      stopDist: Infinity,
      stopKind: 'free',
      aNext: 0,
      aLead: 0,
      obsGap: new Float32Array(OBS_N).fill(1e6),
      obsV: new Float32Array(OBS_N),
      hist: { v: new Float32Array(HIST_N), gap: new Float32Array(HIST_N), a: new Float32Array(HIST_N), n: 0, i: 0 },
    };
    this.cars.push(car);
    this.updatePose(car);
    return car;
  }

  /** Drop a car onto a free stretch of road. Returns null when the track is full. */
  spawn(base: DriverParams = PRESETS.Normal, variation = 0): Car | null {
    const roads = this.lanes.filter((l) => l.kind === 'road' && l.length > 60);
    for (let attempt = 0; attempt < 60; attempt++) {
      const lane = this.rng.pick(roads);
      const s = this.rng.range(15, lane.length - 30);
      let free = true;
      for (const c of this.cars) {
        if (c.lane === lane.id && Math.abs(c.s - s) < 16) free = false;
        if (c.ghostLane === lane.id && Math.abs(c.s + c.ghostOff - s) < 16) free = false;
      }
      if (!free) continue;
      const drv = { ...base };
      if (variation > 0) {
        for (const key of Object.keys(DRIVER_RANGES) as (keyof DriverParams)[]) {
          const [lo, hi] = DRIVER_RANGES[key];
          drv[key] = clamp(drv[key] + (this.rng.next() - 0.5) * variation * (hi - lo), lo, hi);
        }
      }
      return this.addCar({ lane: lane.id, s, v: 6, drv });
    }
    return null;
  }

  removeCar(id: number): void {
    this.cars = this.cars.filter((c) => c.id !== id);
  }

  clear(): void {
    this.cars = [];
  }

  clearCrashes(): void {
    this.cars = this.cars.filter((c) => !c.crashed);
  }

  carById(id: number): Car | undefined {
    return this.cars.find((c) => c.id === id);
  }

  setMode(mode: JunctionMode): void {
    if (this.junction.mode === mode) return;
    this.junction.mode = mode;
    for (const c of this.cars) c.junc = {};
  }

  private pickTurn(): Turn {
    const r = this.rng.next();
    return r < 0.25 ? 'L' : r < 0.75 ? 'S' : 'R';
  }

  // ---------------------------------------------------------------- step

  step(): void {
    this.junction.update(DT, this.time);
    this.buildLists();
    for (const car of this.cars) if (!car.crashed) this.perceive(car);
    this.laneChanges();
    this.integrate();
    this.detectCrashes();
    this.tick++;
    this.time = this.tick * DT;
  }

  private buildLists(): void {
    for (const list of this.laneCars) list.length = 0;
    for (const car of this.cars) {
      this.laneCars[car.lane].push({ car, s: car.s, ghost: false });
      if (car.ghostLane >= 0) {
        const gs = car.s + car.ghostOff;
        if (gs > 0 && gs < this.lanes[car.ghostLane].length) this.laneCars[car.ghostLane].push({ car, s: gs, ghost: true });
      }
    }
    for (const list of this.laneCars) list.sort((p, q) => p.s - q.s || p.car.id - q.car.id);
    this.active.length = 0;
    for (const car of this.cars) {
      const jc = this.junctionCoord(car);
      if (jc && (jc.u + car.len / 2 > 0 || car.junc.released)) this.active.push({ car, conn: jc.conn, u: jc.u });
    }
  }

  private updatePose(car: Car): void {
    const p = this.lanes[car.lane].path.at(car.s);
    car.cx = p.x;
    car.cy = p.y;
    car.x = p.x + Math.sin(p.h) * car.lat;
    car.y = p.y - Math.cos(p.h) * car.lat;
    car.h = p.h;
  }

  // ---------------------------------------------------------------- routes

  /** The path through the intersection this car will take from an inbound lane. */
  private plannedConn(car: Car, lane: Lane): Lane {
    const c = lane.connectors!;
    return this.lanes[(c[car.turn] ?? c.S ?? c.L ?? c.R)!];
  }

  private routeNext(car: Car, lane: Lane): number {
    if (lane.kind === 'connector') return lane.next!;
    if (lane.connectors && lane.id === car.lane) return this.plannedConn(car, lane).id;
    return -1;
  }

  /** The lane and position a given distance ahead of the car along its route. */
  ahead(car: Car, dist: number): { lane: Lane; s: number } | null {
    let lane = this.lanes[car.lane];
    let s = car.s + dist;
    while (s > lane.length) {
      const next = this.routeNext(car, lane);
      if (next < 0) return null;
      s -= lane.length;
      lane = this.lanes[next];
    }
    return { lane, s };
  }

  /**
   * Where a car is relative to its path through the intersection: negative
   * before the stop line, 0 at the line, past the path's length once clear.
   */
  private junctionCoord(car: Car): { conn: Lane; u: number } | null {
    const lane = this.lanes[car.lane];
    if (lane.kind === 'connector') return { conn: lane, u: car.s };
    if (lane.connectors) return { conn: this.plannedConn(car, lane), u: car.s - lane.length };
    if (car.prevLane >= 0 && car.s - car.len / 2 < 0) {
      const prev = this.lanes[car.prevLane];
      if (prev.kind === 'connector') return { conn: prev, u: prev.length + car.s };
    }
    return null;
  }

  // ---------------------------------------------------------------- seeing

  /** The nearest car ahead along this car's route. */
  private findLeader(car: Car): { gap: number; v: number; id: number } {
    const half = car.len / 2;
    const lane = this.lanes[car.lane];
    let gap = Infinity;
    let v = 0;
    let id = -1;
    const consider = (e: Entry, g: number) => {
      if (g < gap) {
        gap = g;
        v = e.car.v;
        id = e.car.id;
      }
    };
    for (const e of this.laneCars[car.lane]) {
      if (e.car === car || e.s < car.s || (e.s === car.s && e.car.id < car.id)) continue;
      consider(e, e.s - e.car.len / 2 - car.s - half);
      break;
    }
    if (lane.siblings) {
      for (const sib of lane.siblings) {
        for (const e of this.laneCars[sib.id]) {
          if (e.s > car.s && e.s - e.car.len / 2 < sib.sDiverge) consider(e, e.s - e.car.len / 2 - car.s - half);
        }
      }
    }
    if (id >= 0) return { gap, v, id };

    let dist = lane.length - car.s;
    let cur = lane;
    for (let depth = 0; depth < 3 && dist < LOOKAHEAD; depth++) {
      const nid = this.routeNext(car, cur);
      if (nid < 0) break;
      const nl = this.lanes[nid];
      const first = this.laneCars[nid][0];
      if (first) consider(first, dist + first.s - first.car.len / 2 - half);
      if (nl.siblings) {
        for (const sib of nl.siblings) {
          for (const e of this.laneCars[sib.id]) {
            if (e.s - e.car.len / 2 < sib.sDiverge) consider(e, dist + e.s - e.car.len / 2 - half);
          }
        }
      }
      if (id >= 0) break;
      dist += nl.length;
      cur = nl;
    }
    return { gap, v, id };
  }

  private connSpeed(conn: Lane, d: Derived): number {
    const r = this.minRad[conn.id];
    return r < 400 ? Math.sqrt(d.aLat * r) : Infinity;
  }

  /** Decide this car's acceleration for the step from what its driver can see. */
  private perceive(car: Car): void {
    const d = derive(car.drv);
    const drv = car.drv;
    const lane = this.lanes[car.lane];
    const half = car.len / 2;

    // Desired speed: the limit, scaled by the driver, slowed for curves ahead.
    let v0 = lane.speedLimit * drv.speed;
    for (const look of CURVE_LOOK) {
      const pt = this.ahead(car, look);
      if (!pt) break;
      const r = pt.lane.kind === 'connector' ? this.minRad[pt.lane.id] : pt.lane.path.radiusAt(pt.s);
      if (r < 400) {
        const vc = Math.sqrt(d.aLat * r);
        v0 = Math.min(v0, Math.sqrt(vc * vc + 2 * d.b * 0.6 * look));
      }
    }
    car.v0 = v0;

    const free = freeAccel(car.v, v0, d);
    let acc = free;
    let binding: Binding = 'free';
    const use = (a: number, kind: Binding) => {
      if (a < acc) {
        acc = a;
        binding = kind;
      }
    };

    // The car ahead, as the driver saw it one reaction time ago.
    const lead = this.findLeader(car);
    car.leaderId = lead.id;
    car.gap = lead.gap;
    const i = this.tick % OBS_N;
    car.obsGap[i] = Math.min(lead.gap, 1e6);
    car.obsV[i] = lead.v;
    const delay = Math.min(OBS_N - 1, Math.round(drv.reaction / DT));
    const j = (((this.tick - delay) % OBS_N) + OBS_N) % OBS_N;
    if (car.obsGap[j] < LOOKAHEAD) {
      // Drivers know their own speed, so they extrapolate the old gap forward;
      // what arrives late is news of the other car changing speed.
      const closing = car.v - car.obsV[j];
      const gapNow = car.obsGap[j] - closing * delay * DT;
      use(idm(car.v, v0, gapNow, closing, drv.headway, d), 'leader');
    }
    car.aLead = lead.gap < LOOKAHEAD ? Math.min(free, idm(car.v, v0, lead.gap, car.v - lead.v, drv.headway, d)) : free;

    // Fixed things to stop for: the end of the lane, the stop line, a gap to wait for.
    car.stopDist = Infinity;
    car.stopKind = 'free';
    const stopFor = (dist: number, kind: Binding) => {
      const a = idm(car.v, v0, Math.max(dist, 0) + d.s0, car.v, drv.headway, d);
      use(a, kind);
      if (dist < car.stopDist) {
        car.stopDist = dist;
        car.stopKind = kind;
      }
      return a;
    };
    if (lane.kind === 'road' && !lane.connectors) {
      const a = stopFor(lane.length - car.s - half - 0.5, 'end');
      car.aLead = Math.min(car.aLead, a);
    }
    const js = this.junctionStop(car, lane, d, v0);
    if (js) stopFor(js.dist, js.kind);

    // Courtesy: ease off for a car ahead in the next lane that needs to merge in.
    if (drv.politeness > 0.15 && lane.kind === 'road') {
      for (const side of [lane.lefts, lane.rights]) {
        for (const id of side) {
          for (const e of this.laneCars[id]) {
            const m = e.car;
            if (e.ghost || m.crashed || m.wantSide === 0 || m.urgency < 0.4) continue;
            const mLane = this.lanes[m.lane];
            const wantsMine = (m.wantSide < 0 ? mLane.lefts : mLane.rights).includes(lane.id);
            if (!wantsMine) continue;
            const pr = lane.path.project(m.cx, m.cy);
            if (pr.d > this.world.laneWidth * 1.4) continue;
            const g = pr.s - m.len / 2 - (car.s + half);
            if (g < 0.5 || g > 40) continue;
            use(Math.max(idm(car.v, v0, g, car.v - m.v, drv.headway, d), -d.b * drv.politeness), 'coop');
          }
        }
      }
    }

    // Startled by a cut-in: brake harder than needed for a moment.
    if (car.startleAt > 0 && this.time >= car.startleAt) {
      car.startleUntil = this.time + 0.5 + d.nerves;
      car.startleAt = 0;
    }
    if (this.time < car.startleUntil) {
      if (lead.gap > 1.3 * (d.s0 + car.v * drv.headway) || car.v < 0.5) car.startleUntil = 0;
      else use(-d.b * (1.3 + 1.2 * d.nerves), 'startle');
    }

    car.aNext = clamp(acc, -MAX_BRAKE, d.a);

    const b = binding as Binding;
    if (b === 'startle') car.state = 'STARTLED';
    else if (car.lcT < car.lcDur) car.state = 'LANE CHANGE';
    else if (b === 'leader') car.state = acc < -1.5 ? 'BRAKE' : 'FOLLOW';
    else if (b === 'line') car.state = car.v < 0.5 ? 'WAIT' : 'STOPPING';
    else if (b === 'yield') car.state = 'YIELD';
    else if (b === 'coop') car.state = 'LETTING IN';
    else if (b === 'end') car.state = 'MERGE';
    else car.state = 'CRUISE';

    // Turn signals.
    if (car.lcT < car.lcDur) car.blinker = car.lcFrom > 0 ? -1 : 1;
    else if (car.wantSide !== 0 && car.urgency > 0.1) car.blinker = car.wantSide;
    else {
      let turn: Turn = 'S';
      if (lane.kind === 'connector') turn = lane.turn!;
      else if (lane.connectors && lane.length - car.s < 55) turn = this.plannedConn(car, lane).turn!;
      car.blinker = turn === 'L' ? -1 : turn === 'R' ? 1 : 0;
    }
  }

  // ---------------------------------------------------------------- intersection

  /** Where, if anywhere, this car has to stop for the intersection. */
  private junctionStop(car: Car, lane: Lane, d: Derived, v0: number): { dist: number; kind: Binding } | null {
    const half = car.len / 2;
    let conn: Lane;
    let u: number;
    if (lane.kind === 'connector') {
      conn = lane;
      u = car.s;
    } else if (lane.connectors) {
      const dLine = lane.length - car.s - half;
      if (dLine > 90) return null;
      conn = this.plannedConn(car, lane);
      u = car.s - lane.length;
      if (dLine > -0.5 && !this.mayEnter(car, lane, conn, dLine, d)) return { dist: dLine - 0.6, kind: 'line' };
    } else {
      return null;
    }

    // Inside (or cleared to enter): wait short of any shared stretch another car needs first.
    let best: { dist: number; kind: Binding } | null = null;
    const myFront = u + half;
    const vc = Math.min(v0, this.connSpeed(conn, d));
    const margin = 0.8 + 1.2 * d.nerves;
    const myRank = this.rank(car, conn, u);
    for (const cz of conn.conflicts!) {
      if (myFront > cz.sIn) continue;
      const dIn = cz.sIn - myFront;
      if (dIn > 60) continue;
      const tIn = travelTime(dIn, car.v, d.a, vc);
      const tOut = travelTime(cz.sOut - (u - half), car.v, d.a, vc);
      const foeConn = this.lanes[cz.foe];
      let wait = false;
      this.forFoes(foeConn, (foe, uf) => {
        if (wait || foe === car) return;
        const fFront = uf + foe.len / 2;
        const fRear = uf - foe.len / 2;
        if (fRear > cz.foeOut) return;
        const inZone = fFront > cz.foeIn;
        if (foe.crashed) {
          if (inZone) wait = true;
          return;
        }
        if (!inZone && !this.isActive(foe, foeConn, uf)) return;
        const fd = derive(foe.drv);
        const fvc = Math.min(foeConn.speedLimit * foe.drv.speed, this.connSpeed(foeConn, fd));
        const fOut = travelTime(cz.foeOut - fRear, foe.v, fd.a, fvc);
        if (inZone) {
          if (tIn - margin * 0.5 < fOut) wait = true;
          return;
        }
        const fIn = travelTime(cz.foeIn - fFront, foe.v, fd.a, fvc);
        if (tIn - margin < fOut && fIn < tOut + margin) {
          const fr = this.rank(foe, foeConn, uf);
          if (fr > myRank || (fr === myRank && (fIn < tIn || (fIn === tIn && foe.id < car.id)))) wait = true;
        }
      });
      if (wait) {
        const dist = dIn - 1.0;
        if (!best || dist < best.dist) best = { dist, kind: 'yield' };
      }
    }
    return best;
  }

  /** May this car cross the stop line now? */
  private mayEnter(car: Car, lane: Lane, conn: Lane, dLine: number, d: Derived): boolean {
    const j = car.junc;
    const jn = this.junction;

    if (jn.mode === 'allway') {
      if (j.released) return true;
      const rolling = 0.4 + (1 - car.drv.compliance) * 4;
      if (dLine > 3.2 || car.v > rolling) return false;
      if (j.arrival === undefined) j.arrival = this.time;
      if (this.time - j.arrival < (0.4 + 0.6 * d.nerves) * car.drv.compliance) return false;
      for (const o of this.active) {
        if (o.car === car || o.conn.id === conn.id) continue;
        const cz = conflictBetween(conn, o.conn.id);
        if (cz && o.u - o.car.len / 2 < cz.foeOut) return false;
      }
      for (const o of this.cars) {
        const oj = o.junc;
        if (o === car || o.crashed || oj.arrival === undefined || oj.released) continue;
        const ol = this.lanes[o.lane];
        if (!ol.connectors) continue;
        if (oj.arrival < j.arrival || (oj.arrival === j.arrival && o.id < car.id)) {
          if (conflictBetween(conn, this.plannedConn(o, ol).id)) return false;
        }
      }
      if (!this.exitHasSpace(car, conn)) return false;
      j.released = true;
      j.order = jn.seq++;
      this.active.push({ car, conn, u: car.s - lane.length });
      return true;
    }

    if (j.run) return true;
    const light = jn.armState(lane.arm!, this.time - car.drv.reaction);
    let ok: boolean;
    if (light === 'G') {
      j.yellow = undefined;
      ok = true;
    } else if (light === 'Y') {
      if (j.yellow === undefined) {
        const brakingNeeded = (car.v * car.v) / (2 * Math.max(dLine, 0.5));
        j.yellow = brakingNeeded > 1.8 + 2.2 * car.drv.aggression ? 'go' : 'stop';
      }
      if (j.yellow === 'go' && car.v < 2 && dLine > 2) j.yellow = 'stop';
      ok = j.yellow === 'go';
    } else if (j.yellow === 'go' && car.v > 2) {
      ok = true;
    } else {
      if (!j.redRoll && dLine < 70) {
        j.redRoll = true;
        j.run = this.rng.next() >= car.drv.compliance;
      }
      ok = !!j.run;
    }
    return ok && this.exitHasSpace(car, conn);
  }

  /** Don't block the box: is there room on the far side? */
  private exitHasSpace(car: Car, conn: Lane): boolean {
    const out = this.lanes[conn.next!];
    const first = this.laneCars[out.id][0];
    if (!first) return true;
    if (first.car.v > 2.5 && !first.car.crashed) return true;
    let inside = 0;
    for (const p of out.preds) inside += this.laneCars[p].length;
    return first.s - first.car.len / 2 > (inside + 1) * (car.len + 2.5);
  }

  /** Is this car going to go through, so that others must plan around it? */
  private isActive(foe: Car, foeConn: Lane, uf: number): boolean {
    if (uf + foe.len / 2 > 0) return true;
    if (this.junction.mode === 'allway') return !!foe.junc.released;
    if (foe.junc.run) return true;
    const light = this.junction.armState(foeConn.fromArm!);
    if (light === 'G') return true;
    if (light === 'Y') return foe.junc.yellow === 'go' || (foe.junc.yellow === undefined && -uf - foe.len / 2 < 40);
    return foe.junc.yellow === 'go' && foe.v > 2;
  }

  /** Right of way: the higher rank goes first. */
  private rank(car: Car, conn: Lane, u: number): number {
    if (this.junction.mode === 'allway') return 1e6 - (car.junc.order ?? 1e5);
    const crossed = u + car.len / 2 > 0;
    if (crossed && this.junction.armState(conn.fromArm!) !== 'G') return 4; // clearing the box
    if (car.junc.run) return 0; // red-light runner
    return conn.turn === 'L' ? 2 : 3; // left turns give way to oncoming traffic
  }

  /** Every car using, approaching or just leaving a path through the intersection. */
  private forFoes(conn: Lane, fn: (foe: Car, u: number) => void): void {
    for (const e of this.laneCars[conn.id]) fn(e.car, e.s);
    const from = this.lanes[conn.from!];
    for (const e of this.laneCars[from.id]) {
      if (e.ghost || from.length - e.s > 80) continue;
      if (this.plannedConn(e.car, from) === conn) fn(e.car, e.s - from.length);
    }
    for (const e of this.laneCars[conn.next!]) {
      if (e.s - e.car.len / 2 >= 0) break;
      if (!e.ghost && e.car.prevLane === conn.id) fn(e.car, conn.length + e.s);
    }
  }

  // ---------------------------------------------------------------- lane changes

  private targetLane(car: Car, side: number): { lane: Lane; s: number; d: number } | null {
    const lane = this.lanes[car.lane];
    for (const id of side < 0 ? lane.lefts : lane.rights) {
      const t = this.lanes[id];
      const pr = t.path.project(car.cx, car.cy);
      if (pr.d < this.world.laneWidth * 1.4 && pr.s > 2 && pr.s < t.length - 2) return { lane: t, s: pr.s, d: pr.d };
    }
    return null;
  }

  private leaderIn(t: Lane, s: number, self: Car): Neighbor | null {
    for (const e of this.laneCars[t.id]) {
      if (e.car === self || e.s <= s) continue;
      return { car: e.car, gap: e.s - e.car.len / 2 - (s + self.len / 2) };
    }
    let nextId = -1;
    if (t.kind === 'connector') nextId = t.next!;
    else if (t.connectors) nextId = (t.connectors[self.turn] ?? t.connectors.S ?? t.connectors.L ?? t.connectors.R)!;
    if (nextId >= 0) {
      const first = this.laneCars[nextId][0];
      if (first && first.car !== self) return { car: first.car, gap: t.length - s + first.s - first.car.len / 2 - self.len / 2 };
    }
    return null;
  }

  private followerIn(t: Lane, s: number, self: Car): Neighbor | null {
    const list = this.laneCars[t.id];
    for (let i = list.length - 1; i >= 0; i--) {
      const e = list[i];
      if (e.car === self || e.s >= s) continue;
      return { car: e.car, gap: s - self.len / 2 - (e.s + e.car.len / 2) };
    }
    let best: Neighbor | null = null;
    for (const p of t.preds) {
      const pl = this.laneCars[p];
      const e = pl[pl.length - 1];
      if (!e || e.car === self) continue;
      const gap = s - self.len / 2 + (this.lanes[p].length - e.s) - e.car.len / 2;
      if (!best || gap < best.gap) best = { car: e.car, gap };
    }
    return best;
  }

  private laneChanges(): void {
    for (const car of this.cars) {
      if (car.crashed || car.lcT < car.lcDur) continue;
      const lane = this.lanes[car.lane];
      if (lane.kind !== 'road') {
        car.wantSide = 0;
        car.urgency = 0;
        continue;
      }
      if (this.time < car.nextLcCheck) continue;
      car.nextLcCheck = this.time + 0.4;
      const d = derive(car.drv);
      const half = car.len / 2;
      const remaining = lane.length - car.s - half;

      // Does the driver have to leave this lane?
      let need = 0;
      let urgency = 0;
      if (!lane.connectors) {
        need = lane.lefts.length ? -1 : 1;
        urgency = clamp(1 - (remaining - 5) / MANDATORY_DIST, 0, 1);
      } else if (!lane.connectors[car.turn]) {
        const toSolid = remaining - SOLID_LINE;
        if (toSolid < 5) {
          car.turn = this.plannedConn(car, lane).turn!; // too late: take what this lane allows
        } else {
          if (lane.lefts.some((id) => this.lanes[id].connectors?.[car.turn] !== undefined)) need = -1;
          else if (lane.rights.some((id) => this.lanes[id].connectors?.[car.turn] !== undefined)) need = 1;
          urgency = clamp(1 - toSolid / MANDATORY_DIST, 0, 1);
        }
      }
      car.wantSide = urgency > 0.05 ? need : 0;
      car.urgency = need !== 0 ? urgency : 0;

      if (lane.connectors && remaining < SOLID_LINE) continue;
      if (car.s < 12) continue;
      if (this.time < car.lcCooldownUntil && urgency < 0.5) continue;

      let best: { side: number; t: Lane; s: number; d: number; fol: Neighbor | null; score: number } | null = null;
      for (const side of [-1, 1]) {
        if (need !== 0 && side !== need && urgency > 0) continue;
        const tgt = this.targetLane(car, side);
        if (!tgt) continue;
        const t = tgt.lane;
        if (side === need) {
          if (lane.connectors && !t.connectors?.[car.turn]) continue;
        } else {
          if (!t.connectors && t.length - tgt.s < 220) continue; // don't hop into a lane that is about to end
          if (t.connectors && !t.connectors[car.turn] && remaining < 170) continue;
        }
        const lead = this.leaderIn(t, tgt.s, car);
        const fol = this.followerIn(t, tgt.s, car);
        if (lead && lead.gap < 1.0) continue;
        if (fol && fol.gap < 1.5) continue;

        const v0 = car.v0;
        let aNew = freeAccel(car.v, v0, d);
        if (lead) aNew = Math.min(aNew, idm(car.v, v0, lead.gap, car.v - lead.car.v, car.drv.headway, d));
        let folCost = 0;
        let aFol = 0;
        if (fol && !fol.car.crashed) {
          const f = fol.car;
          aFol = idm(f.v, Math.max(f.v0, 1), fol.gap, f.v - car.v, f.drv.headway, derive(f.drv));
          folCost = Math.min(0, aFol - f.aNext);
        }
        const bSafe = d.bSafe + urgency * 2.5;
        if (aFol < -bSafe || aNew < -bSafe) continue;
        const bias = side === need ? 0.3 + urgency * 3 : 0;
        const score = aNew - car.aLead + car.drv.politeness * folCost + bias;
        if (score > d.lcThreshold && (!best || score > best.score)) best = { side, t, s: tgt.s, d: tgt.d, fol, score };
      }
      if (best) this.changeLane(car, best.side, best.t, best.s, best.d, best.fol);
    }
  }

  private changeLane(car: Car, side: number, t: Lane, s: number, dist: number, fol: Neighbor | null): void {
    const d = derive(car.drv);
    car.ghostLane = car.lane;
    car.ghostOff = car.s - s;
    car.lane = t.id;
    car.s = s;
    car.prevLane = -1;
    car.lat = side < 0 ? dist : -dist;
    car.lcFrom = car.lat;
    car.lcT = 0;
    car.lcDur = d.lcTime;
    car.lcCooldownUntil = this.time + 2 + 3 * d.nerves;
    car.wantSide = 0;
    car.urgency = 0;
    car.obsGap.fill(1e6);
    const lead = this.leaderIn(t, s, car);
    if (lead) {
      car.obsGap.fill(lead.gap);
      car.obsV.fill(lead.car.v);
    }
    // Did that cut someone off?
    if (fol && !fol.car.crashed && fol.car.v > 2) {
      const f = fol.car;
      const closing = f.v - car.v;
      const timeGap = fol.gap / f.v;
      const ttc = closing > 0.1 ? fol.gap / closing : Infinity;
      if ((timeGap < 0.9 || ttc < 3.5) && f.startleAt === 0 && this.time >= f.startleUntil) {
        f.startleAt = this.time + f.drv.reaction;
      }
    }
    this.buildLists();
  }

  /**
   * Make a car change lanes right now, safe or not. With no id, picks the
   * pair of cars best lined up for a cut-off. Returns the car that cut in.
   */
  forceCutIn(id?: number): Car | null {
    this.buildLists();
    let best: { car: Car; side: number; t: Lane; s: number; d: number; fol: Neighbor | null; score: number } | null = null;
    for (const car of this.cars) {
      if (id !== undefined && car.id !== id) continue;
      if (car.crashed || car.lcT < car.lcDur || this.lanes[car.lane].kind !== 'road') continue;
      for (const side of [-1, 1]) {
        const tgt = this.targetLane(car, side);
        if (!tgt) continue;
        const lead = this.leaderIn(tgt.lane, tgt.s, car);
        const fol = this.followerIn(tgt.lane, tgt.s, car);
        if (lead && lead.gap < 0.5) continue;
        if (fol && fol.gap < 0.8) continue;
        let score: number;
        if (id !== undefined) score = fol ? 1000 - fol.gap : 0;
        else if (fol && fol.gap < 25 && fol.car.v > 3) score = 1000 - fol.gap;
        else continue;
        if (!best || score > best.score) best = { car, side, t: tgt.lane, s: tgt.s, d: tgt.d, fol, score };
      }
    }
    if (!best) return null;
    this.changeLane(best.car, best.side, best.t, best.s, best.d, best.fol);
    return best.car;
  }

  // ---------------------------------------------------------------- moving

  private integrate(): void {
    let removed = false;
    for (const car of this.cars) {
      if (car.crashed) {
        if (this.autoClear && this.time - car.crashedAt > this.crashClearAfter) removed = true;
        continue;
      }
      car.a = car.aNext;
      car.v = Math.max(0, car.v + car.a * DT);
      car.s += car.v * DT;

      let yaw = 0;
      if (car.lcT < car.lcDur) {
        car.lcT += DT;
        const f = clamp(car.lcT / car.lcDur, 0, 1);
        const before = car.lat;
        car.lat = car.lcFrom * (1 - f * f * (3 - 2 * f));
        yaw = Math.atan2(-(car.lat - before) / DT, Math.max(car.v, 3));
        if (f >= 0.5) car.ghostLane = -1;
        if (f >= 1) car.lat = 0;
      }

      let lane = this.lanes[car.lane];
      while (car.s > lane.length) {
        const next = this.routeNext(car, lane);
        if (next < 0) {
          car.s = lane.length;
          car.v = 0;
          break;
        }
        car.s -= lane.length;
        car.prevLane = lane.id;
        car.lane = next;
        if (lane.kind === 'connector') {
          car.junc = {};
          car.turn = this.pickTurn();
          this.stats.passes++;
        }
        lane = this.lanes[next];
      }
      this.updatePose(car);
      car.h += yaw;

      if (this.tick % HIST_EVERY === 0) {
        const h = car.hist;
        h.v[h.i] = car.v;
        h.gap[h.i] = Math.min(car.gap, 99);
        h.a[h.i] = car.a;
        h.i = (h.i + 1) % HIST_N;
        h.n = Math.min(h.n + 1, HIST_N);
      }
    }
    if (removed) this.cars = this.cars.filter((c) => !(c.crashed && this.time - c.crashedAt > this.crashClearAfter));
  }

  private detectCrashes(): void {
    const cars = this.cars;
    for (let i = 0; i < cars.length; i++) {
      const a = cars[i];
      for (let k = i + 1; k < cars.length; k++) {
        const b = cars[k];
        if (a.crashed && b.crashed) continue;
        const dx = b.x - a.x;
        const dy = b.y - a.y;
        if (dx * dx + dy * dy > 36) continue;
        if (!boxesOverlap(a, b)) continue;
        for (const c of [a, b]) {
          if (!c.crashed) {
            c.crashed = true;
            c.crashedAt = this.time;
            c.v = 0;
            c.a = 0;
            c.state = 'CRASH';
            c.blinker = 0;
            c.ghostLane = -1;
            c.lcT = c.lcDur;
          }
        }
        this.stats.crashes++;
        this.onCrash?.(a, b);
      }
    }
  }
}

/** Separating-axis test on the two car rectangles, shrunk a little so a near miss is not a crash. */
function boxesOverlap(a: Car, b: Car): boolean {
  const axes = [a.h, a.h + Math.PI / 2, b.h, b.h + Math.PI / 2];
  for (const ang of axes) {
    const ax = Math.cos(ang);
    const ay = Math.sin(ang);
    const ra = extent(a, ax, ay);
    const rb = extent(b, ax, ay);
    const dist = Math.abs((b.x - a.x) * ax + (b.y - a.y) * ay);
    if (dist > ra + rb) return false;
  }
  return true;
}

function extent(c: Car, ax: number, ay: number): number {
  const hl = c.len / 2 - 0.15;
  const hw = c.wid / 2 - 0.1;
  const fx = Math.cos(c.h);
  const fy = Math.sin(c.h);
  return hl * Math.abs(fx * ax + fy * ay) + hw * Math.abs(-fy * ax + fx * ay);
}
