# Traffic

A traffic game in the making, built in Unity 6. The first piece is the **Driver Lab**: a small street grid for building and tuning how individual drivers behave, before any city building.

## Maps

- **Street grid:** nine blocks, described below.
- **Figure eight:** one intersection joined to itself by two loops, with a pinch where two lanes become one.
- **City and highway:** six intersections joined to a divided highway by a diamond interchange: a crossroad from town goes over the highway on a bridge, with an off-ramp and an on-ramp on each side meeting it at a traffic light. Vehicles arrive at both ends of the highway and on two roads south of town, in waves, and leave the map the same ways.

- **Draw your own:** lay out roads by clicking, straight or curved, at any angle. The road about to be built shows on the ground at its real width, blue if it can be built and red, with the reason, if it cannot. A curve is a round arc that flows on from the road before it: click where it starts, click to point it (not needed when carrying on from a road end), then sweep it round and click where it ends. Roads that cross or touch make intersections, which get stop signs or a traffic light automatically. A road can be drawn in pieces as short as you like: pieces that carry on from one another become one road, with each corner rounded, and a corner with no room to be rounded is refused. A toolbar at the bottom holds the tools (road, lane, move, replace, bulldoze, undo, redo, clear) and a tray of road choices: straight or curve, and eleven kinds of road under Street (25 mph), Avenue (40 mph, two-way with or without a grass median) and Highway (65 mph, one-way, one to four lanes). Pointing at any of them explains it. Each road has a price per meter: a label beside the pointer shows the length and price of the road being drawn, and the total is at the top. A one-way road brought in alongside another road, on the right of that road's traffic, merges into it with a lane of its own that then ends; started on a road the same way, it peels off as an exit. The roundabout shape lays a ring: click its middle, pull out its size, click again. Roads crossing the ring join it and nobody stops for a light; traffic going round has the right of way. Roads can be built as bridges or tunnels, three levels each: they cross other roads without meeting them, need room at each end to climb or go down, and cost two to seven times as much. Replace changes a road to another kind where it lies, which is also how a road gains or loses lanes. The lane tool adds a lane along part of a road, on the side you click (room to pull over or pass, or, run to the end of the road, a lane to turn from), and removes an added lane with a click. The move tool drags a point, taking its roads with it, and bends a road by its middle or pulls it straight again. Snapping pulls a straight road into line with the roads it starts from (straight on, square, or halfway between) and can keep points to a 30 ft grid. Road ends left open are where traffic enters and leaves. A few cars drive around while you build; press Drive to let the full traffic in.

## Puzzles

Four so far. Each starts with roads coming in at the edges of an empty square, traffic that wants to get from some of them to others, and a budget. Build roads to join every route, press Drive, and the level is solved once enough journeys have been finished. Stars go by how little the traffic was held up.

- **First Link:** join two streets.
- **Four Corners:** four roads, everyone going everywhere. A crossroads works; a roundabout works better.
- **On and Off:** get traffic between a highway and a side road with an exit and a merge.
- **Over or Through:** join two roads across a busy avenue without stopping it.

## Driver Lab

A three-by-three grid of streets where each road and intersection tests something different:

- **Center:** a traffic light with left-turn arrows. Main St arrives with a left-turn pocket, two through lanes and a right-turn pocket, and allows U-turns. Center Ave has a left-turn pocket and does not allow U-turns.
- **West and south:** plain traffic lights, where left turns give way to oncoming traffic.
- **East and north:** all-way stops.
- **Around the edge:** roads with one or two lanes each way, a right lane that ends, a left lane that ends, and a road that gains a lane.

Every car drives itself:

- follows the car ahead, keeping a time gap (Intelligent Driver Model)
- reacts late, by its own reaction time
- changes lanes when it gains something and the gap is safe, gets into the right lane for its next turn, and merges out of a lane that ends
- obeys lights, arrows and stop signs, and waits for a gap before turning across traffic
- brakes too hard when someone cuts it off
- turns right on red after stopping, when there is a gap
- crashes if all that fails: a fender bender holds both drivers up briefly, and a hard hit leaves wrecks that block the road until they are towed. Other drivers go round a wreck where they can and slow down to look

Special drivers stand out in a loud color and upset everyone around them:

- **Maniac:** speeds, tailgates, weaves, and pushes through red lights. Drivers with a green brake hard and freeze for a moment.
- **Slowpoke:** far under the limit with a huge gap.
- **On phone:** keeps looking away, so reacts very late and sits at green lights.
- **Drunk:** cannot hold a lane or a speed. Other drivers hang back and will not pass.

Controls: add cars, pick or tune the driver for new cars, click a car to inspect and retune it live, click an intersection to change how it is controlled, force a cut-in, pause, step and change speed. Hold the right mouse button and drag to move the view, scroll to zoom, and hold the middle button (or press Q and E) to turn it; W, A, S, D and the arrow keys also move it, and N puts north back up. The left button is only for building and picking things.

## Art

The cars, trucks and rocks are from [Kenney's](https://www.kenney.nl) Car Kit and Nature Kit, and the ground, asphalt, concrete and gravel textures are built from photo scans by [ambientCG](https://ambientcg.com). All are public domain (CC0). Trees and bushes are shaped in code and covered with a foliage texture. The sources are in `Art/`; `Tools/BakeArt/bake.py` converts them into the one file the game loads. Motorcycles, roads, signals and the interface are drawn by code.

## Run it

Open the folder in Unity Hub (Add, then Add project from disk) with Unity 6000.6.3f1 and press Play. Everything is created by code, so any scene works.

## Check it without Unity

With the .NET 8 SDK installed:

```bash
dotnet test Tools/SimTests
```

```bash
dotnet build Tools/LabCompile
```

The first runs the simulation tests. The second type-checks all scripts against older Unity reference assemblies.

```bash
dotnet run --project Tools/UiPreview toolbar.html
```

This draws the toolbar in each of its states, and every icon, to a web page.
