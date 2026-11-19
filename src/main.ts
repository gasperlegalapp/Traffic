import * as THREE from 'three';
import './style.css';
import { CarViews } from './render/cars';
import { Overlay } from './render/overlay';
import { RoadView } from './render/roads';
import { Stage } from './render/stage';
import { DRIVER_RANGES, PRESETS, type DriverParams } from './sim/driver';
import type { JunctionMode, Light } from './sim/junction';
import { buildLabWorld } from './sim/labWorld';
import { DT, HIST_N, Sim, type Car } from './sim/sim';

const MPH = 2.23694;
const FEET = 3.28084;
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

const world = buildLabWorld();
const sim = new Sim(world, Date.now() & 0xffff);
const stage = new Stage($<HTMLCanvasElement>('view'), world.extent);
const roads = new RoadView(world);
const carViews = new CarViews();
const overlay = new Overlay($('tags'));
stage.scene.add(roads.group, carViews.group, overlay.lines);

const app = {
  paused: false,
  speed: 1,
  selected: -1,
  follow: false,
  newDriver: { ...PRESETS.Normal } as DriverParams,
  variation: 0.3,
};

// ------------------------------------------------------------------ sliders

const SLIDERS: { key: keyof DriverParams; label: string; fmt: (v: number) => string }[] = [
  { key: 'reaction', label: 'Reaction time', fmt: (v) => `${v.toFixed(2)} s` },
  { key: 'speed', label: 'Speed', fmt: (v) => `${Math.round(v * 100)}% of limit` },
  { key: 'headway', label: 'Following gap', fmt: (v) => `${v.toFixed(1)} s` },
  { key: 'aggression', label: 'Aggression', fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'politeness', label: 'Politeness', fmt: (v) => `${Math.round(v * 100)}%` },
  { key: 'compliance', label: 'Obeys lights and signs', fmt: (v) => `${Math.round(v * 100)}%` },
];

/** Build the six driver sliders. Returns a function that re-reads the values. */
function buildSliders(host: HTMLElement, get: () => DriverParams | undefined): () => void {
  const refreshers: (() => void)[] = [];
  for (const def of SLIDERS) {
    const [lo, hi] = DRIVER_RANGES[def.key];
    const label = document.createElement('label');
    label.className = 'slider';
    const head = document.createElement('span');
    const value = document.createElement('em');
    head.append(def.label, value);
    const input = document.createElement('input');
    input.type = 'range';
    input.min = String(lo);
    input.max = String(hi);
    input.step = String((hi - lo) / 100);
    input.addEventListener('input', () => {
      const target = get();
      if (!target) return;
      target[def.key] = Number(input.value);
      value.textContent = def.fmt(target[def.key]);
    });
    label.append(head, input);
    host.append(label);
    refreshers.push(() => {
      const target = get();
      if (!target) return;
      input.value = String(target[def.key]);
      value.textContent = def.fmt(target[def.key]);
    });
  }
  const refresh = () => refreshers.forEach((f) => f());
  refresh();
  return refresh;
}

const refreshNew = buildSliders($('new-sliders'), () => app.newDriver);
const refreshCar = buildSliders($('car-sliders'), () => sim.carById(app.selected)?.drv);

for (const name of Object.keys(PRESETS)) {
  const b = document.createElement('button');
  b.textContent = name;
  b.addEventListener('click', () => {
    Object.assign(app.newDriver, PRESETS[name]);
    refreshNew();
  });
  $('presets').append(b);
}
const variation = $<HTMLInputElement>('variation');
const showVariation = () => ($('variation-val').textContent = `${Math.round(app.variation * 100)}%`);
variation.addEventListener('input', () => {
  app.variation = Number(variation.value);
  showVariation();
});
showVariation();

// ------------------------------------------------------------------ controls

let toastTimer = 0;
function toast(text: string): void {
  const el = $('toast');
  el.textContent = text;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = window.setTimeout(() => el.classList.remove('show'), 2400);
}

function spawn(n: number): void {
  let made = 0;
  for (let i = 0; i < n; i++) if (sim.spawn(app.newDriver, app.variation)) made++;
  if (made < n) toast(made === 0 ? 'The road is full.' : `Only room for ${made} more.`);
}

function select(id: number): void {
  app.selected = id;
  if (id < 0) app.follow = false;
  $('inspector').hidden = id < 0;
  $('insp-follow').classList.toggle('on', app.follow);
  refreshCar();
}

const ICON_PLAY = '<svg viewBox="0 0 16 16" width="15" height="15"><path d="M4 2.5v11l9-5.5z" fill="currentColor"/></svg>';
const ICON_PAUSE = '<svg viewBox="0 0 16 16" width="15" height="15"><path d="M3.5 2.5h3v11h-3zM9.5 2.5h3v11h-3z" fill="currentColor"/></svg>';
const ICON_STEP = '<svg viewBox="0 0 16 16" width="15" height="15"><path d="M3 2.5v11l7-5.5zM11 2.5h2.5v11H11z" fill="currentColor"/></svg>';

function setPaused(p: boolean): void {
  app.paused = p;
  $('play').innerHTML = p ? ICON_PLAY : ICON_PAUSE;
}
setPaused(false);
$('step').innerHTML = ICON_STEP;

function segmented(id: string, attr: string, onPick: (value: string) => void): void {
  const host = $(id);
  host.addEventListener('click', (e) => {
    const btn = (e.target as HTMLElement).closest('button');
    if (!btn) return;
    for (const b of host.querySelectorAll('button')) b.classList.toggle('on', b === btn);
    onPick(btn.dataset[attr]!);
  });
}

$('spawn1').addEventListener('click', () => spawn(1));
$('spawn10').addEventListener('click', () => spawn(10));
$('clear').addEventListener('click', () => {
  sim.clear();
  select(-1);
});
$('clearcrash').addEventListener('click', () => sim.clearCrashes());
$('cutin').addEventListener('click', () => {
  const car = sim.forceCutIn();
  if (car) {
    select(car.id);
    toast(`Car ${car.id} cut in. Watch the car behind it.`);
  } else {
    toast('No two cars are lined up side by side right now. Try again in a moment.');
  }
});
segmented('mode', 'mode', (m) => sim.setMode(m as JunctionMode));
segmented('speeds', 'speed', (s) => {
  app.speed = Number(s);
  setPaused(false);
});
$('play').addEventListener('click', () => setPaused(!app.paused));
$('step').addEventListener('click', () => {
  setPaused(true);
  sim.step();
});
$<HTMLInputElement>('show-sensors').addEventListener('change', (e) => (overlay.sensors = (e.target as HTMLInputElement).checked));
$<HTMLInputElement>('show-labels').addEventListener('change', (e) => (overlay.labels = (e.target as HTMLInputElement).checked));
$('menu-toggle').addEventListener('click', () => document.body.classList.toggle('menu-open'));

$('insp-close').addEventListener('click', () => select(-1));
$('insp-follow').addEventListener('click', () => {
  app.follow = !app.follow;
  if (app.follow && stage.dist > 150) stage.dist = 110;
  $('insp-follow').classList.toggle('on', app.follow);
});
$('insp-cut').addEventListener('click', () => {
  if (!sim.forceCutIn(app.selected)) toast('No lane to cut into from here.');
});
$('insp-remove').addEventListener('click', () => {
  sim.removeCar(app.selected);
  select(-1);
});

window.addEventListener('keydown', (e) => {
  if ((e.target as HTMLElement).tagName === 'INPUT') return;
  if (e.code === 'Space') {
    e.preventDefault();
    setPaused(!app.paused);
  } else if (e.key === '.') {
    setPaused(true);
    sim.step();
  } else if (e.key === 'Escape') {
    select(-1);
  }
});

const raycaster = new THREE.Raycaster();
stage.onClick = (ndc) => {
  raycaster.setFromCamera(ndc, stage.camera);
  const hit = raycaster.intersectObjects(carViews.bodies, false)[0];
  select(hit ? (hit.object.userData.id as number) : -1);
};
stage.onUserPan = () => {
  if (app.follow) {
    app.follow = false;
    $('insp-follow').classList.remove('on');
  }
};

sim.onCrash = (a, b) => toast(`Crash: car ${a.id} and car ${b.id}.`);

// ------------------------------------------------------------------ readouts

function drawGraph(canvas: HTMLCanvasElement, car: Car, data: Float32Array, lo: number, hi: number, color: string, zeroLine: boolean): void {
  const dpr = Math.min(window.devicePixelRatio, 2);
  const w = canvas.clientWidth * dpr;
  const h = canvas.clientHeight * dpr;
  if (canvas.width !== w || canvas.height !== h) {
    canvas.width = w;
    canvas.height = h;
  }
  const g = canvas.getContext('2d')!;
  g.clearRect(0, 0, w, h);
  const yOf = (v: number) => h - 3 * dpr - ((Math.min(Math.max(v, lo), hi) - lo) / (hi - lo)) * (h - 21 * dpr);
  if (zeroLine) {
    g.strokeStyle = 'rgba(255,255,255,0.18)';
    g.lineWidth = dpr;
    g.beginPath();
    g.moveTo(0, yOf(0));
    g.lineTo(w, yOf(0));
    g.stroke();
  }
  const hist = car.hist;
  if (hist.n < 2) return;
  g.strokeStyle = color;
  g.lineWidth = 1.6 * dpr;
  g.beginPath();
  for (let k = 0; k < hist.n; k++) {
    const idx = (hist.i - hist.n + k + HIST_N) % HIST_N;
    const x = w - ((hist.n - 1 - k) / (HIST_N - 1)) * w;
    const y = yOf(data[idx]);
    if (k === 0) g.moveTo(x, y);
    else g.lineTo(x, y);
  }
  g.stroke();
}

const TURN_NAMES = { L: 'Left', S: 'Straight', R: 'Right' };
let frame = 0;

function updateReadouts(): void {
  frame++;
  if (frame % 6 === 0) {
    let v = 0;
    let moving = 0;
    for (const c of sim.cars) {
      if (c.crashed) continue;
      v += c.v;
      moving++;
    }
    $('st-cars').textContent = String(sim.cars.length);
    $('st-speed').textContent = `${moving ? Math.round((v / moving) * MPH) : 0} mph`;
    $('st-pass').textContent = String(sim.stats.passes);
    $('st-crash').textContent = String(sim.stats.crashes);
    const t = Math.floor(sim.time);
    $('st-clock').textContent = `${Math.floor(t / 60)}:${String(t % 60).padStart(2, '0')}`;
  }
  const car = sim.carById(app.selected);
  if (!car) {
    if (app.selected >= 0) select(-1);
    return;
  }
  $('insp-title').textContent = `Car ${car.id}`;
  $('insp-dot').style.background = `#${car.color.toString(16).padStart(6, '0')}`;
  const st = $('insp-state');
  st.textContent = car.state;
  $('ro-speed').textContent = `${Math.round(car.v * MPH)} mph`;
  $('ro-gap').textContent = car.gap < 90 ? `${Math.round(car.gap * FEET)} ft` : 'Clear';
  $('ro-turn').textContent = TURN_NAMES[car.turn];
  if (frame % 2 === 0) {
    const scaled = (src: Float32Array, k: number) => src.map((x) => x * k);
    drawGraph($<HTMLCanvasElement>('g-speed'), car, scaled(car.hist.v, MPH), 0, 45, '#5ee0c0', false);
    drawGraph($<HTMLCanvasElement>('g-gap'), car, scaled(car.hist.gap, FEET), 0, 200, '#6cb8ff', false);
    drawGraph($<HTMLCanvasElement>('g-acc'), car, car.hist.a, -8, 4, '#ffc93c', true);
  }
}

// ------------------------------------------------------------------ loop

let last = performance.now();
let owed = 0;

function loop(now: number): void {
  const elapsed = Math.min((now - last) / 1000, 0.1);
  last = now;
  if (!app.paused) {
    owed += elapsed * app.speed;
    let steps = 0;
    while (owed >= DT && steps < 24) {
      sim.step();
      owed -= DT;
      steps++;
    }
    if (owed > DT) owed = 0;
  }

  const car = sim.carById(app.selected);
  if (app.follow && car) stage.target.lerp(new THREE.Vector3(car.x, 0, -car.y), 0.12);

  const lights: Light[] = [0, 1, 2, 3].map((arm) => sim.junction.armState(arm));
  roads.update(lights, sim.time);
  carViews.sync(sim.cars, sim.time, app.selected);
  stage.render();
  overlay.update(sim, stage.camera, app.selected, stage.dist > 260);
  updateReadouts();
  requestAnimationFrame(loop);
}

spawn(10);
requestAnimationFrame(loop);

// For automated checks and for poking at the sim from the browser console.
declare global {
  interface Window {
    trafficLab: { sim: Sim; app: typeof app; stage: Stage; select: (id: number) => void };
  }
}
window.trafficLab = { sim, app, stage, select };
