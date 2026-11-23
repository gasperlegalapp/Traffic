# CLAUDE.md: Traffic

A browser traffic game built with Vite, TypeScript and three.js (0.184.0). The current build is the Driver Lab, a test track for individual driver behavior. Zones, road building and events come later. Nothing here is shared with the Race270 repo.

## Layout

- `src/sim/`: the simulation. It has no three.js or DOM imports, runs on a fixed 1/30 s step (`DT`) with a seeded `Rng`, and is replayable from a seed.
  - `sim.ts`: the `Sim` class. Each step: signal update, per-lane car lists, `perceive` (each car picks an acceleration), lane changes, integration, crash detection.
  - `driver.ts`: the six driver dials (`DriverParams`), values derived from them, and the IDM car-following formula.
  - `network.ts`: `Lane` and `World` types. Paths through the intersection are lanes of kind `connector`; `linkConnectors` finds where they conflict.
  - `labWorld.ts`: builds the test track. `junction.ts`: traffic light cycle and all-way stop mode. `path.ts`: polylines measured by distance.
- `src/render/`: three.js views (`stage.ts` camera and lights, `roads.ts`, `cars.ts`, `overlay.ts` debug lines and tags). Sim (x east, y north) maps to three.js (x, height, -y).
- `src/main.ts`: UI wiring and the frame loop. `window.trafficLab` exposes `sim`, `app`, `stage` and `select` for automated checks.
- `src/__tests__/`: Vitest tests of the sim.

## Conventions

- A car's `s` is the position of its center along its lane. Lane 0 is the leftmost lane; traffic drives on the right.
- Sim units are meters and seconds. The UI shows mph and feet.
- Keep behavior deterministic: all randomness goes through `sim.rng`.

## Commands

`npm install`, `npm run dev`, `npm test`, `npm run build`. `.npmrc` sets `legacy-peer-deps` because npm 10 fails to resolve Vitest's optional peers otherwise.

Headless Chromium in a cloud container renders slowly. When scripting checks, pause the app (`trafficLab.app.paused = true`) and call `trafficLab.sim.step()` directly rather than waiting on real time.

## Unity port (in progress)

The game is moving to Unity 6 (project version 6000.3.26f1) on Windows 11. The web version stays until the Unity driver lab is confirmed working.

- `Assets/Scripts/Sim/`: a line-for-line C# translation of `src/sim/` (namespace `Traffic.Sim`, class `Simulation`), with no Unity references. It gives identical results to the TypeScript version for the same seed. `SignalLight` and `PathPose` are named to avoid clashing with `UnityEngine.Light` and `UnityEngine.Pose`.
- `Assets/Scripts/Lab/`: the Unity driver lab. `LabBootstrap` creates a `LabApp` object when Play is pressed, in any scene, so there is no scene file to maintain. Everything (meshes, materials, camera, light) is built in code. The UI is immediate-mode GUI (`LabUi.cs`) and reads mouse and keyboard from GUI events, so it does not depend on the Input Manager or Input System setting. Sim (x east, y north) maps to Unity (x, height, z = y); models face +x.
- `Assets/Tests/Sim/`: the sim tests in NUnit.
- Only `ProjectSettings/ProjectVersion.txt` is checked in. Unity generates the rest of `ProjectSettings/`, `Packages/` and the `.meta` files on first open; commit them once that has happened. The code assumes the Built-in Render Pipeline.

Checks without the Unity editor (need the .NET 8 SDK): `dotnet test Tools/SimTests` runs the sim tests, and `dotnet build Tools/LabCompile` type-checks all scripts against older Unity reference assemblies (2021.3), which catches typos but does not prove they compile or look right in the installed Unity. Keep the C# to language version 9.
