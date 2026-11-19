import { describe, expect, it } from 'vitest';
import { Path, offsetPolyline } from '../sim/path';
import { buildLabWorld, LANE_W } from '../sim/labWorld';

describe('Path', () => {
  const p = new Path([{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 10 }]);

  it('measures length and samples along it', () => {
    expect(p.length).toBeCloseTo(20);
    expect(p.at(5)).toMatchObject({ x: 5, y: 0 });
    expect(p.at(15).x).toBeCloseTo(10);
    expect(p.at(15).y).toBeCloseTo(5);
    expect(p.at(15).h).toBeCloseTo(Math.PI / 2);
  });

  it('projects a point onto the closest spot', () => {
    const pr = p.project(4, 3);
    expect(pr.s).toBeCloseTo(4);
    expect(pr.d).toBeCloseTo(3);
  });

  it('offsets to the right of travel', () => {
    const east = offsetPolyline([{ x: 0, y: 0 }, { x: 10, y: 0 }], 2);
    expect(east[0].y).toBeCloseTo(-2); // heading east, right is south
  });
});

describe('lab world', () => {
  const world = buildLabWorld();

  it('has two lanes in and out of every arm', () => {
    for (let arm = 0; arm < 4; arm++) {
      expect(world.inbound[arm]).toHaveLength(2);
      expect(world.outbound[arm]).toHaveLength(2);
    }
  });

  it('keeps neighboring lanes one lane width apart', () => {
    const left = world.lanes[world.inbound[0][0]];
    const right = world.lanes[world.inbound[0][1]];
    const mid = left.path.at(left.length / 2);
    expect(right.path.project(mid.x, mid.y).d).toBeCloseTo(LANE_W, 1);
  });

  it('makes left turns conflict with oncoming through traffic but not with the opposite left turn', () => {
    const lane = (arm: number, i: number) => world.lanes[world.inbound[arm][i]];
    const northLeft = world.lanes[lane(0, 0).connectors!.L!];
    const southLeft = lane(2, 0).connectors!.L!;
    const southStraight = lane(2, 0).connectors!.S!;
    const foes = northLeft.conflicts!.map((c) => c.foe);
    expect(foes).toContain(southStraight);
    expect(foes).not.toContain(southLeft);
  });
});
