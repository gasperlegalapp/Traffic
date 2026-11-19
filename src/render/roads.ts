import * as THREE from 'three';
import { Path, offsetPolyline, type Vec } from '../sim/path';
import type { Light } from '../sim/junction';
import type { World } from '../sim/network';
import { Rng } from '../sim/rng';

/** Collects flat triangles and turns them into one mesh. */
class FlatBuilder {
  private pos: number[] = [];
  private idx: number[] = [];

  private vertex(p: Vec, y: number): number {
    this.pos.push(p.x, y, -p.y);
    return this.pos.length / 3 - 1;
  }

  strip(left: Vec[], right: Vec[], y: number): void {
    let l0 = this.vertex(left[0], y);
    let r0 = this.vertex(right[0], y);
    for (let i = 1; i < left.length; i++) {
      const l1 = this.vertex(left[i], y);
      const r1 = this.vertex(right[i], y);
      this.idx.push(l0, r0, l1, r0, r1, l1);
      l0 = l1;
      r0 = r1;
    }
  }

  fan(center: Vec, ring: Vec[], y: number): void {
    const c = this.vertex(center, y);
    const first = this.vertex(ring[0], y);
    let prev = first;
    for (let i = 1; i < ring.length; i++) {
      const cur = this.vertex(ring[i], y);
      this.idx.push(c, cur, prev);
      prev = cur;
    }
    this.idx.push(c, first, prev);
  }

  line(pts: Vec[], width: number, y: number): void {
    if (pts.length < 2) return;
    this.strip(offsetPolyline(pts, -width / 2), offsetPolyline(pts, width / 2), y);
  }

  dashed(pts: Vec[], width: number, on: number, off: number, y: number): void {
    if (pts.length < 2) return;
    const path = new Path(pts);
    for (let s = 0; s < path.length; s += on + off) {
      const end = Math.min(s + on, path.length);
      const seg: Vec[] = [];
      for (let t = s; t < end; t += 1) seg.push(path.at(t));
      seg.push(path.at(end));
      this.line(seg, width, y);
    }
  }

  mesh(material: THREE.Material): THREE.Mesh {
    const geo = new THREE.BufferGeometry();
    geo.setAttribute('position', new THREE.Float32BufferAttribute(this.pos, 3));
    const normals = new Float32Array(this.pos.length);
    for (let i = 1; i < normals.length; i += 3) normals[i] = 1;
    geo.setAttribute('normal', new THREE.BufferAttribute(normals, 3));
    geo.setIndex(this.idx);
    const mesh = new THREE.Mesh(geo, material);
    mesh.receiveShadow = true;
    return mesh;
  }
}

const LIGHT_COLORS: Record<Light, number> = { G: 0x35e07a, Y: 0xffc93c, R: 0xff4040, STOP: 0xff4040 };
const LAMP_DIM = { R: 0x4a1616, Y: 0x4a3a12, G: 0x12402a };

/** Ground, pavement, paint, trees and the traffic signals. */
export class RoadView {
  readonly group = new THREE.Group();
  private stopLines: THREE.MeshBasicMaterial[] = [];
  private lamps: THREE.MeshBasicMaterial[][] = [];
  private signalHeads: THREE.Group[] = [];
  private stopSigns: THREE.Group[] = [];

  constructor(world: World) {
    const ground = new THREE.Mesh(new THREE.PlaneGeometry(3000, 3000), new THREE.MeshStandardMaterial({ color: 0x2c3d33, roughness: 1 }));
    ground.rotation.x = -Math.PI / 2;
    ground.receiveShadow = true;
    this.group.add(ground);

    const flat = (color: number, offset: number) =>
      new THREE.MeshStandardMaterial({ color, roughness: 0.95, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: offset, polygonOffsetUnits: offset });

    const asphalt = new FlatBuilder();
    for (const s of world.surfaces) asphalt.strip(s.left, s.right, 0.03);
    asphalt.fan({ x: 0, y: 0 }, world.box, 0.03);
    this.group.add(asphalt.mesh(flat(0x353a42, -1)));

    const white = new FlatBuilder();
    const yellow = new FlatBuilder();
    for (const m of world.markings) {
      const b = m.color === 'white' ? white : yellow;
      if (m.dash) b.dashed(m.pts, m.width, m.dash[0], m.dash[1], 0.06);
      else b.line(m.pts, m.width, 0.06);
    }
    this.group.add(white.mesh(flat(0xe8ecf1, -3)));
    this.group.add(yellow.mesh(flat(0xf2c230, -3)));

    for (const sl of world.stopLines) {
      const back = { x: -Math.cos(sl.h) * 0.55, y: -Math.sin(sl.h) * 0.55 };
      const b = new FlatBuilder();
      b.strip([sl.a, sl.b], [{ x: sl.a.x + back.x, y: sl.a.y + back.y }, { x: sl.b.x + back.x, y: sl.b.y + back.y }], 0.07);
      const mat = new THREE.MeshBasicMaterial({ color: 0xffffff, side: THREE.DoubleSide, polygonOffset: true, polygonOffsetFactor: -4, polygonOffsetUnits: -4 });
      const mesh = b.mesh(mat);
      mesh.receiveShadow = false;
      this.group.add(mesh);
      this.stopLines[sl.arm] = mat;

      const post = new THREE.Group();
      post.position.set(sl.post.x, 0, -sl.post.y);
      post.rotation.y = sl.h;
      const dark = new THREE.MeshStandardMaterial({ color: 0x15181c, roughness: 0.6 });
      const pole = new THREE.Mesh(new THREE.CylinderGeometry(0.14, 0.14, 4.4, 8), dark);
      pole.position.y = 2.2;
      pole.castShadow = true;
      post.add(pole);

      // Signal head on an arm reaching over the road, lamps facing up so they read from above.
      const head = new THREE.Group();
      const armBar = new THREE.Mesh(new THREE.BoxGeometry(0.16, 0.16, 3.4), dark);
      armBar.position.set(0, 4.3, -1.7);
      head.add(armBar);
      const housing = new THREE.Mesh(new THREE.BoxGeometry(3.3, 0.5, 1.15), dark);
      housing.position.set(0, 4.5, -3.2);
      housing.castShadow = true;
      head.add(housing);
      const lamps: THREE.MeshBasicMaterial[] = [];
      for (let i = 0; i < 3; i++) {
        const mat2 = new THREE.MeshBasicMaterial({ color: LAMP_DIM.R });
        const lamp = new THREE.Mesh(new THREE.CircleGeometry(0.42, 20), mat2);
        lamp.rotation.x = -Math.PI / 2;
        lamp.position.set(-1.05 + i * 1.05, 4.76, -3.2);
        head.add(lamp);
        lamps.push(mat2);
      }
      this.lamps[sl.arm] = lamps;
      this.signalHeads[sl.arm] = head;
      post.add(head);

      const sign = new THREE.Group();
      const plate = new THREE.Mesh(new THREE.CylinderGeometry(1.0, 1.0, 0.1, 8), new THREE.MeshBasicMaterial({ color: 0xe5383b }));
      plate.position.y = 4.5;
      plate.rotation.y = Math.PI / 8;
      const rim = new THREE.Mesh(new THREE.CylinderGeometry(1.15, 1.15, 0.06, 8), new THREE.MeshBasicMaterial({ color: 0xffffff }));
      rim.position.y = 4.46;
      rim.rotation.y = Math.PI / 8;
      sign.add(rim, plate);
      sign.visible = false;
      this.stopSigns[sl.arm] = sign;
      post.add(sign);

      this.group.add(post);
    }

    this.addTrees(world);
  }

  private addTrees(world: World): void {
    const rng = new Rng(42);
    const spots: { x: number; y: number; scale: number }[] = [];
    const range = world.extent + 60;
    for (let i = 0; i < 900 && spots.length < 150; i++) {
      const x = rng.range(-range, range);
      const y = rng.range(-range, range);
      if (Math.hypot(x, y) < 24) continue;
      let clear = true;
      for (const lane of world.lanes) {
        if (lane.kind === 'road' && lane.path.project(x, y).d < 10) {
          clear = false;
          break;
        }
      }
      if (clear) spots.push({ x, y, scale: rng.range(0.7, 1.4) });
    }
    const geo = new THREE.ConeGeometry(2.4, 6, 7);
    geo.translate(0, 3.4, 0);
    const mat = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 1, flatShading: true });
    const trees = new THREE.InstancedMesh(geo, mat, spots.length);
    trees.castShadow = true;
    const m = new THREE.Matrix4();
    const color = new THREE.Color();
    spots.forEach((s, i) => {
      m.makeScale(s.scale, s.scale, s.scale);
      m.setPosition(s.x, 0, -s.y);
      trees.setMatrixAt(i, m);
      color.setHSL(0.36 + rng.range(-0.03, 0.04), 0.32, 0.2 + rng.range(0, 0.07));
      trees.setColorAt(i, color);
    });
    this.group.add(trees);
  }

  /** Show the current light for each arm. */
  update(states: Light[], time: number): void {
    for (let arm = 0; arm < 4; arm++) {
      const st = states[arm];
      const stop = st === 'STOP';
      this.signalHeads[arm].visible = !stop;
      this.stopSigns[arm].visible = stop;
      const flash = stop ? (time % 1 < 0.5 ? 1 : 0.45) : 1;
      this.stopLines[arm].color.setHex(LIGHT_COLORS[st]).multiplyScalar(flash);
      const lamps = this.lamps[arm];
      lamps[0].color.setHex(st === 'R' ? LIGHT_COLORS.R : LAMP_DIM.R);
      lamps[1].color.setHex(st === 'Y' ? LIGHT_COLORS.Y : LAMP_DIM.Y);
      lamps[2].color.setHex(st === 'G' ? LIGHT_COLORS.G : LAMP_DIM.G);
    }
  }
}
