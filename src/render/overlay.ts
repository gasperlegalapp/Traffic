import * as THREE from 'three';
import type { Car, Sim } from '../sim/sim';

const MAX_SEGMENTS = 600;

const STATE_CLASS: Record<string, string> = {
  CRUISE: 'ok',
  FOLLOW: 'ok',
  'LANE CHANGE': 'info',
  MERGE: 'info',
  'LETTING IN': 'info',
  STOPPING: 'warn',
  WAIT: 'warn',
  YIELD: 'warn',
  BRAKE: 'bad',
  STARTLED: 'bad',
  CRASH: 'bad',
};

/**
 * The debug view: a line from each car to whatever it is reacting to, and a
 * tag saying what the driver is doing.
 */
export class Overlay {
  readonly lines: THREE.LineSegments;
  sensors = true;
  labels = true;

  private pos = new Float32Array(MAX_SEGMENTS * 6);
  private col = new Float32Array(MAX_SEGMENTS * 6);
  private layer: HTMLElement;
  private tags = new Map<number, HTMLElement>();
  private v = new THREE.Vector3();

  constructor(layer: HTMLElement) {
    this.layer = layer;
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.BufferAttribute(this.pos, 3));
    geo.setAttribute('color', new THREE.BufferAttribute(this.col, 3));
    this.lines = new THREE.LineSegments(geo, new THREE.LineBasicMaterial({ vertexColors: true, transparent: true, opacity: 0.9, depthTest: false }));
    this.lines.frustumCulled = false;
    this.lines.renderOrder = 4;
  }

  update(sim: Sim, camera: THREE.Camera, selectedId: number, zoomedOut: boolean): void {
    let n = 0;
    const seg = (x1: number, y1: number, x2: number, y2: number, r: number, g: number, b: number) => {
      if (n >= MAX_SEGMENTS) return;
      this.pos.set([x1, 1.7, -y1, x2, 1.7, -y2], n * 6);
      this.col.set([r, g, b, r, g, b], n * 6);
      n++;
    };

    if (this.sensors) {
      const byId = new Map<number, Car>();
      for (const c of sim.cars) byId.set(c.id, c);
      for (const car of sim.cars) {
        if (car.crashed) continue;
        const fx = car.x + Math.cos(car.h) * car.len * 0.5;
        const fy = car.y + Math.sin(car.h) * car.len * 0.5;
        const lead = byId.get(car.leaderId);
        if (lead && car.gap < 45) {
          const timeGap = car.gap / Math.max(car.v, 1);
          const [r, g, b] = timeGap < 0.8 ? [1, 0.3, 0.3] : timeGap < 1.5 ? [1, 0.78, 0.25] : [0.3, 0.9, 0.55];
          seg(fx, fy, lead.x - Math.cos(lead.h) * lead.len * 0.5, lead.y - Math.sin(lead.h) * lead.len * 0.5, r, g, b);
        }
        if (car.stopDist < 70) {
          const pt = sim.ahead(car, Math.max(car.stopDist, 0) + car.len / 2);
          if (pt) {
            const p = pt.lane.path.at(pt.s);
            const [r, g, b] = car.stopKind === 'yield' ? [0.95, 0.45, 1] : [0.55, 0.8, 1];
            seg(fx, fy, p.x, p.y, r, g, b);
            const nx = Math.sin(p.h) * 1.4;
            const ny = -Math.cos(p.h) * 1.4;
            seg(p.x - nx, p.y - ny, p.x + nx, p.y + ny, r, g, b);
          }
        }
      }
    }
    this.lines.geometry.setDrawRange(0, n * 2);
    this.lines.geometry.attributes.position.needsUpdate = true;
    this.lines.geometry.attributes.color.needsUpdate = true;

    const seen = new Set<number>();
    if (this.labels) {
      const w = window.innerWidth;
      const h = window.innerHeight;
      for (const car of sim.cars) {
        // From far away only the interesting states get a tag.
        if (zoomedOut && car.id !== selectedId && (car.state === 'CRUISE' || car.state === 'FOLLOW')) continue;
        this.v.set(car.x, 2.4, -car.y).project(camera);
        if (this.v.x < -1.05 || this.v.x > 1.05 || this.v.y < -1.05 || this.v.y > 1.05) continue;
        seen.add(car.id);
        let tag = this.tags.get(car.id);
        if (!tag) {
          tag = document.createElement('div');
          tag.className = 'tag';
          this.layer.appendChild(tag);
          this.tags.set(car.id, tag);
        }
        const cls = `tag ${STATE_CLASS[car.state] ?? 'ok'}${car.id === selectedId ? ' sel' : ''}`;
        if (tag.className !== cls) tag.className = cls;
        if (tag.textContent !== car.state) tag.textContent = car.state;
        tag.style.transform = `translate(${((this.v.x + 1) / 2) * w}px, ${((1 - this.v.y) / 2) * h}px) translate(-50%, -160%)`;
      }
    }
    for (const [id, tag] of this.tags) {
      if (!seen.has(id)) {
        tag.remove();
        this.tags.delete(id);
      }
    }
  }
}
