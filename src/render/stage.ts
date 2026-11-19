import * as THREE from 'three';

/**
 * Renderer, lights and the camera: a top-down view with a slight tilt.
 * Sim coordinates (x east, y north) map to three.js as (x, height, -y).
 */
export class Stage {
  readonly renderer: THREE.WebGLRenderer;
  readonly scene = new THREE.Scene();
  readonly camera: THREE.PerspectiveCamera;
  readonly target = new THREE.Vector3();
  dist = 460;
  /** Tilt away from straight down, radians. */
  tilt = 0.4;
  onClick?: (ndc: THREE.Vector2) => void;
  onUserPan?: () => void;

  private canvas: HTMLCanvasElement;
  private pointers = new Map<number, { x: number; y: number }>();
  private moved = 0;
  private pinch = 0;

  constructor(canvas: HTMLCanvasElement, extent: number) {
    this.canvas = canvas;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
    this.renderer.shadowMap.enabled = true;
    this.renderer.shadowMap.type = THREE.PCFSoftShadowMap;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.scene.background = new THREE.Color(0x1c2622);

    this.camera = new THREE.PerspectiveCamera(30, 1, 10, 4000);

    this.scene.add(new THREE.HemisphereLight(0xdfe9ff, 0x2a3a30, 1.5));
    const sun = new THREE.DirectionalLight(0xfff1d6, 2.2);
    sun.position.set(-140, 260, 120);
    sun.castShadow = true;
    sun.shadow.mapSize.set(2048, 2048);
    const sc = sun.shadow.camera;
    const r = extent * 1.25;
    sc.left = -r;
    sc.right = r;
    sc.top = r;
    sc.bottom = -r;
    sc.near = 10;
    sc.far = 800;
    sun.shadow.bias = -0.0006;
    this.scene.add(sun);

    this.fit(extent);
    this.bind();
    this.resize();
    window.addEventListener('resize', () => this.resize());
  }

  /** Pull back far enough to see the whole track. */
  fit(extent: number): void {
    const aspect = window.innerWidth / window.innerHeight;
    const halfFov = THREE.MathUtils.degToRad(this.camera.fov / 2);
    const forHeight = (extent * 1.08) / Math.tan(halfFov);
    const forWidth = (extent * 1.08) / (Math.tan(halfFov) * aspect);
    this.dist = Math.max(forHeight, forWidth);
    this.target.set(0, 0, 0);
  }

  resize(): void {
    const w = window.innerWidth;
    const h = window.innerHeight;
    this.renderer.setSize(w, h, false);
    this.camera.aspect = w / h;
    this.camera.updateProjectionMatrix();
  }

  render(): void {
    this.camera.position.set(this.target.x, this.target.y + this.dist * Math.cos(this.tilt), this.target.z + this.dist * Math.sin(this.tilt));
    this.camera.lookAt(this.target);
    this.renderer.render(this.scene, this.camera);
  }

  private bind(): void {
    const el = this.canvas;
    el.addEventListener('pointerdown', (e) => {
      el.setPointerCapture(e.pointerId);
      this.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
      if (this.pointers.size === 1) this.moved = 0;
      if (this.pointers.size === 2) this.pinch = this.pinchDistance();
    });
    el.addEventListener('pointermove', (e) => {
      const p = this.pointers.get(e.pointerId);
      if (!p) return;
      const dx = e.clientX - p.x;
      const dy = e.clientY - p.y;
      p.x = e.clientX;
      p.y = e.clientY;
      if (this.pointers.size === 1) {
        this.moved += Math.abs(dx) + Math.abs(dy);
        const perPixel = (2 * this.dist * Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2))) / window.innerHeight;
        this.target.x -= dx * perPixel;
        this.target.z -= (dy * perPixel) / Math.cos(this.tilt);
        if (this.moved > 6) this.onUserPan?.();
      } else if (this.pointers.size === 2) {
        const now = this.pinchDistance();
        if (this.pinch > 0 && now > 0) this.zoom(this.pinch / now);
        this.pinch = now;
        this.moved = 99;
      }
    });
    const up = (e: PointerEvent) => {
      if (!this.pointers.delete(e.pointerId)) return;
      if (this.pointers.size === 0 && this.moved < 6 && e.type === 'pointerup') {
        this.onClick?.(new THREE.Vector2((e.clientX / window.innerWidth) * 2 - 1, -(e.clientY / window.innerHeight) * 2 + 1));
      }
    };
    el.addEventListener('pointerup', up);
    el.addEventListener('pointercancel', up);
    el.addEventListener(
      'wheel',
      (e) => {
        e.preventDefault();
        this.zoom(Math.exp(e.deltaY * 0.0012));
      },
      { passive: false },
    );
  }

  private pinchDistance(): number {
    const [a, b] = [...this.pointers.values()];
    return Math.hypot(a.x - b.x, a.y - b.y);
  }

  zoom(factor: number): void {
    this.dist = THREE.MathUtils.clamp(this.dist * factor, 35, 900);
  }
}
