# CLAUDE.md: Traffic

A traffic game built in Unity 6 (project version 6000.6.3f1, Built-in Render Pipeline) on Windows 11. The current build is the Driver Lab, a small street grid for testing individual driver behavior. Zones, road building and events come later. The earlier web version (TypeScript and three.js) was removed once the Unity lab worked; it is in the git history.

## Layout

- `Assets/Scripts/Sim/`: the simulation (namespace `Traffic.Sim`). It has no Unity references, runs on a fixed 1/30 s step (`Simulation.Dt`) with a seeded `Rng`, and is replayable from a seed.
  - `Simulation.cs`: each step does signal updates, per-lane car lists, `Perceive` (each car picks an acceleration), lane changes, integration, crash detection.
  - `Driver.cs`: the six driver dials (`DriverParams`), values derived from them, and the IDM car-following formula.
  - `Network.cs`: `Lane`, `Node`, `Approach` and `World`. Paths through an intersection are lanes of kind `Connector`; `Connectors.Link` finds where they conflict.
  - `NetworkBuilder.cs`: builds a network from nodes joined by straight roads. Each direction of a road is a set of lanes in slots (0 next to the centerline); a lane can start late (turn pocket, added lane) or end early (merge). It generates connectors, turn assignments, pavement and paint.
  - `LabWorld.cs`: the lab grid, described in its header comment. `Junction.cs`: one controller per node, with modes `Free` (a bend), `Signal`, `ProtectedLeft` and `AllWay`.
- `Assets/Scripts/Lab/`: the Unity side. `LabBootstrap` creates a `LabApp` when Play is pressed, in any scene, so there is no scene file. Meshes, materials, camera and light are built in code (`MeshKit`, `RoadView`, `CarViews`). The UI is immediate-mode GUI (`LabUi.cs`) and reads mouse and keyboard from GUI events, so it works under either input setting.
- `Assets/Tests/Sim/`: NUnit tests of the simulation.

## Conventions

- A car's `S` is the position of its center along its lane. Traffic drives on the right.
- Sim units are meters and seconds; the UI shows mph and feet. Sim (x east, y north) maps to Unity (x, height, z = y); models face +x.
- Keep behavior deterministic: all randomness goes through `Simulation.Rng`.
- `SignalLight` and `PathPose` are named to avoid clashing with `UnityEngine.Light` and `UnityEngine.Pose`.
- Keep the C# to language version 9. Avoid Unity APIs marked obsolete in 6000.6 (for example `FindObjectsByType` with a sort mode).

## Checks

There is no Unity editor in the cloud container, so nothing here proves how the game looks or that it compiles in the installed Unity. With the .NET 8 SDK (`apt-get install -y dotnet-sdk-8.0`):

- `dotnet test Tools/SimTests` runs the simulation tests.
- `dotnet build Tools/LabCompile` type-checks all scripts against Unity 2021.3 reference assemblies. It catches typos and type errors only.

Unity generates `Packages/`, the rest of `ProjectSettings/` and the `.meta` files on first open. They are not committed yet.
