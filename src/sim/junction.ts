export type JunctionMode = 'signal' | 'allway';
export type Light = 'G' | 'Y' | 'R' | 'STOP';

/**
 * Controls the intersection. In signal mode it cycles: north-south green,
 * yellow, all red, east-west green, yellow, all red. In all-way mode every arm
 * has a stop sign.
 */
export class Junction {
  mode: JunctionMode = 'signal';
  green = 14;
  yellow = 3.5;
  allRed = 2;
  phase = 0;
  /** Seconds spent in the current phase. */
  phaseT = 0;
  /** When true the phase never advances (used by tests). */
  hold = false;
  /** Order in which cars were let through in all-way mode. */
  seq = 0;
  private log: { t: number; phase: number }[] = [{ t: -1e9, phase: 0 }];

  private duration(phase: number): number {
    const k = phase % 3;
    return k === 0 ? this.green : k === 1 ? this.yellow : this.allRed;
  }

  update(dt: number, time: number): void {
    if (this.hold) return;
    this.phaseT += dt;
    if (this.phaseT >= this.duration(this.phase)) this.setPhase((this.phase + 1) % 6, time);
  }

  setPhase(phase: number, time: number): void {
    this.phase = phase;
    this.phaseT = 0;
    this.log.push({ t: time, phase });
    if (this.log.length > 12) this.log.shift();
  }

  /** The light an arm shows (0 north, 1 east, 2 south, 3 west), now or at an earlier time. */
  armState(arm: number, at?: number): Light {
    if (this.mode === 'allway') return 'STOP';
    let phase = this.phase;
    if (at !== undefined) {
      for (let i = this.log.length - 1; i >= 0; i--) {
        if (this.log[i].t <= at) {
          phase = this.log[i].phase;
          break;
        }
      }
    }
    const northSouth = arm % 2 === 0;
    const mine = northSouth ? phase < 3 : phase >= 3;
    if (!mine) return 'R';
    const k = phase % 3;
    return k === 0 ? 'G' : k === 1 ? 'Y' : 'R';
  }
}
