import { describe, expect, it } from 'vitest';
import { PRESETS } from '../sim/driver';
import { buildLabWorld } from '../sim/labWorld';
import { DT, Sim } from '../sim/sim';

const world = buildLabWorld();
const run = (sim: Sim, seconds: number, each?: () => void) => {
  for (let i = 0; i < seconds / DT; i++) {
    sim.step();
    each?.();
  }
};
/** Lane ids: [left, right] lanes arriving at an arm (0 north, 1 east, 2 south, 3 west). */
const inbound = (arm: number) => world.inbound[arm].map((id) => world.lanes[id]);
const deadEnds = world.lanes.filter((l) => l.kind === 'road' && !l.connectors);

describe('following', () => {
  it('slows for a slower car ahead without hitting it', () => {
    const sim = new Sim(world, 1);
    const lane = inbound(1)[0];
    const slow = sim.addCar({ lane: lane.id, s: 90, v: 5, drv: { ...PRESETS.Normal, speed: 0.6 }, turn: 'S' });
    const fast = sim.addCar({ lane: lane.id, s: 30, v: 13, turn: 'S' });
    // Keep both in their lane so this only tests following.
    slow.lcCooldownUntil = fast.lcCooldownUntil = 1e9;
    let minGap = Infinity;
    run(sim, 12, () => (minGap = Math.min(minGap, fast.gap)));
    expect(sim.stats.crashes).toBe(0);
    expect(minGap).toBeGreaterThan(1.5);
  });
});

describe('traffic light', () => {
  it('stops at a red light, short of the line, and goes on green', () => {
    const sim = new Sim(world, 1);
    sim.junction.hold = true;
    sim.junction.setPhase(3, 0); // east-west green: north arm is red
    const lane = inbound(0)[0];
    const car = sim.addCar({ lane: lane.id, s: lane.length - 80, v: 12, turn: 'S' });
    run(sim, 20);
    expect(car.lane).toBe(lane.id);
    expect(car.v).toBeLessThan(0.3);
    const toLine = lane.length - car.s - car.len / 2;
    expect(toLine).toBeGreaterThan(0);
    expect(toLine).toBeLessThan(3);

    sim.junction.setPhase(0, sim.time);
    run(sim, 10);
    expect(car.lane).not.toBe(lane.id);
    expect(sim.stats.passes).toBe(1);
  });

  it('makes a left turn wait for oncoming traffic', () => {
    const sim = new Sim(world, 1);
    sim.junction.hold = true; // north-south green
    const north = inbound(0)[0];
    const south = inbound(2)[0];
    const turner = sim.addCar({ lane: north.id, s: north.length - 30, v: 8, turn: 'L' });
    const oncoming = sim.addCar({ lane: south.id, s: south.length - 45, v: 12, turn: 'S' });
    let yielded = false;
    run(sim, 14, () => (yielded ||= turner.state === 'YIELD'));
    expect(yielded).toBe(true);
    expect(sim.stats.crashes).toBe(0);
    expect(sim.stats.passes).toBe(2);
    expect(oncoming.crashed).toBe(false);
  });
});

describe('all-way stop', () => {
  it('comes to a full stop before entering, even with nobody around', () => {
    const sim = new Sim(world, 1);
    sim.setMode('allway');
    const lane = inbound(3)[0];
    const car = sim.addCar({ lane: lane.id, s: lane.length - 70, v: 12, turn: 'S' });
    let slowest = Infinity;
    run(sim, 25, () => {
      if (car.lane === lane.id && lane.length - car.s < 8) slowest = Math.min(slowest, car.v);
    });
    expect(slowest).toBeLessThan(0.4);
    expect(sim.stats.passes).toBe(1);
  });
});

describe('lane changes', () => {
  it('merges out of a lane that ends', () => {
    const sim = new Sim(world, 1);
    const lane = deadEnds[0];
    const car = sim.addCar({ lane: lane.id, s: 20, v: 10 });
    run(sim, 20);
    expect(car.lane).not.toBe(lane.id);
    expect(sim.stats.crashes).toBe(0);
  });

  it('startles the driver behind when cut off', () => {
    const sim = new Sim(world, 1);
    const [left, right] = inbound(1);
    const victim = sim.addCar({ lane: left.id, s: 60, v: 12, drv: PRESETS.Calm, turn: 'S' });
    const cutter = sim.addCar({ lane: right.id, s: 69, v: 9, turn: 'S' });
    victim.lcCooldownUntil = 1e9;
    run(sim, 0.5);
    expect(sim.forceCutIn(cutter.id)).toBe(cutter);
    expect(cutter.lane).toBe(left.id);
    let startled = false;
    let hardest = 0;
    run(sim, 5, () => {
      startled ||= victim.state === 'STARTLED';
      hardest = Math.min(hardest, victim.a);
    });
    expect(startled).toBe(true);
    expect(hardest).toBeLessThan(-3);
  });
});

describe('whole track', () => {
  for (const mode of ['signal', 'allway'] as const) {
    it(`runs 40 mixed drivers for 8 minutes without a crash or gridlock (${mode})`, () => {
      const sim = new Sim(world, 11);
      sim.setMode(mode);
      for (let i = 0; i < 40; i++) sim.spawn(PRESETS.Normal, 0.4);
      // Everyone obeys the rules here; rule-breaking is tested by hand in the lab.
      for (const c of sim.cars) c.drv.compliance = 1;
      expect(sim.cars.length).toBe(40);
      run(sim, 240);
      const halfway = sim.stats.passes;
      run(sim, 240);
      expect(sim.stats.crashes).toBe(0);
      expect(halfway).toBeGreaterThan(40);
      expect(sim.stats.passes - halfway).toBeGreaterThan(40);
    });
  }

  it('replays exactly from the same seed', () => {
    const snapshot = () => {
      const sim = new Sim(world, 5);
      for (let i = 0; i < 25; i++) sim.spawn(PRESETS.Normal, 0.5);
      run(sim, 90);
      return sim.cars.map((c) => `${c.id}:${c.lane}:${c.s.toFixed(6)}:${c.v.toFixed(6)}`).join('|');
    };
    expect(snapshot()).toBe(snapshot());
  });
});
