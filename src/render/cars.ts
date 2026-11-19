import * as THREE from 'three';
import { RoundedBoxGeometry } from 'three/addons/geometries/RoundedBoxGeometry.js';
import type { Car } from '../sim/sim';

interface CarView {
  group: THREE.Group;
  body: THREE.Mesh;
  paint: THREE.MeshStandardMaterial;
  brakes: THREE.Mesh[];
  left: THREE.Mesh[];
  right: THREE.Mesh[];
  color: number;
}

const bodyGeo = new RoundedBoxGeometry(4.5, 0.8, 1.9, 3, 0.28);
const cabinGeo = new RoundedBoxGeometry(2.3, 0.6, 1.62, 3, 0.22);
const lampGeo = new THREE.BoxGeometry(0.14, 0.2, 0.46);
const cornerGeo = new THREE.BoxGeometry(0.3, 0.2, 0.3);
const glass = new THREE.MeshStandardMaterial({ color: 0x161d26, roughness: 0.25, metalness: 0.3 });
const brakeOn = new THREE.MeshBasicMaterial({ color: 0xff2d2d });
const brakeOff = new THREE.MeshBasicMaterial({ color: 0x5c1414 });
const blinkOn = new THREE.MeshBasicMaterial({ color: 0xffb020 });
const blinkOff = new THREE.MeshBasicMaterial({ color: 0x4a3a18 });
const headlight = new THREE.MeshBasicMaterial({ color: 0xfff6d8 });

/** Draws every car: a simple rounded body with working brake lights and turn signals. */
export class CarViews {
  readonly group = new THREE.Group();
  readonly bodies: THREE.Mesh[] = [];
  private views = new Map<number, CarView>();
  private ring: THREE.Mesh;

  constructor() {
    this.ring = new THREE.Mesh(
      new THREE.RingGeometry(3.3, 3.8, 40),
      new THREE.MeshBasicMaterial({ color: 0x5ee0c0, side: THREE.DoubleSide, transparent: true, opacity: 0.95, depthTest: false }),
    );
    this.ring.rotation.x = -Math.PI / 2;
    this.ring.renderOrder = 5;
    this.ring.visible = false;
    this.group.add(this.ring);
  }

  private make(car: Car): CarView {
    const group = new THREE.Group();
    const paint = new THREE.MeshStandardMaterial({ color: car.color, roughness: 0.45, metalness: 0.1 });
    const body = new THREE.Mesh(bodyGeo, paint);
    body.position.y = 0.6;
    body.castShadow = true;
    body.userData.id = car.id;
    const cabin = new THREE.Mesh(cabinGeo, glass);
    cabin.position.set(-0.3, 1.15, 0);
    group.add(body, cabin);

    const lamp = (geo: THREE.BufferGeometry, mat: THREE.Material, x: number, z: number) => {
      const m = new THREE.Mesh(geo, mat);
      m.position.set(x, 0.86, z);
      group.add(m);
      return m;
    };
    // Forward is +x; the driver's right is +z.
    const brakes = [lamp(lampGeo, brakeOff, -2.2, -0.42), lamp(lampGeo, brakeOff, -2.2, 0.42)];
    const left = [lamp(cornerGeo, blinkOff, 2.08, -0.8), lamp(cornerGeo, blinkOff, -2.08, -0.8)];
    const right = [lamp(cornerGeo, blinkOff, 2.08, 0.8), lamp(cornerGeo, blinkOff, -2.08, 0.8)];
    lamp(lampGeo, headlight, 2.2, -0.36);
    lamp(lampGeo, headlight, 2.2, 0.36);

    this.group.add(group);
    this.bodies.push(body);
    return { group, body, paint, brakes, left, right, color: car.color };
  }

  sync(cars: Car[], time: number, selectedId: number): void {
    const seen = new Set<number>();
    const blink = (time * 1.6) % 1 < 0.5;
    this.ring.visible = false;
    for (const car of cars) {
      seen.add(car.id);
      let view = this.views.get(car.id);
      if (!view) {
        view = this.make(car);
        this.views.set(car.id, view);
      }
      view.group.position.set(car.x, 0, -car.y);
      view.group.rotation.y = car.h;
      const braking = car.a < -0.6 || (car.v < 0.2 && !car.crashed);
      for (const b of view.brakes) b.material = braking ? brakeOn : brakeOff;
      const hazard = car.crashed && blink;
      for (const b of view.left) b.material = hazard || (car.blinker < 0 && blink) ? blinkOn : blinkOff;
      for (const b of view.right) b.material = hazard || (car.blinker > 0 && blink) ? blinkOn : blinkOff;
      view.paint.color.setHex(car.color);
      if (car.crashed) view.paint.color.multiplyScalar(0.35);
      if (car.id === selectedId) {
        this.ring.visible = true;
        this.ring.position.set(car.x, 0.12, -car.y);
      }
    }
    for (const [id, view] of this.views) {
      if (seen.has(id)) continue;
      this.group.remove(view.group);
      this.bodies.splice(this.bodies.indexOf(view.body), 1);
      view.paint.dispose();
      this.views.delete(id);
    }
  }
}
