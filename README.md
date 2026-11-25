# Traffic

A traffic game in the making, built in Unity 6. The first piece is the **Driver Lab**: a small street grid for building and tuning how individual drivers behave, before any city building.

## Maps

- **Street grid:** nine blocks, described below.
- **Figure eight:** one intersection joined to itself by two loops, with a pinch where two lanes become one.
- **City and highway:** six intersections joined to a divided highway by a diamond interchange: a crossroad from town goes over the highway on a bridge, with an off-ramp and an on-ramp on each side meeting it at a traffic light. Vehicles arrive at both ends of the highway and on two roads south of town, in waves, and leave the map the same ways.

- **Draw your own:** lay out roads by clicking, straight or curved, at any angle. The road about to be built shows on the ground as itself: dark pavement at its real width with a line of light along each edge, arrows running along it the way its traffic will go, and a marker standing at each end. It turns red, with the reason, if it cannot be built. Where it will cross another road a pad of light marks the new intersection. A curve is a round arc that flows on from the road before it: click where it starts, click to point it (not needed when carrying on from a road end), then sweep it round and click where it ends. Roads that cross or touch make intersections, which get stop signs or a traffic light automatically; they may cross at a slant, down to 30 degrees. A road can be drawn in pieces as short as you like: pieces that carry on from one another become one road. Straight pieces of a street or avenue meet in a tight corner, as on a city block, so a grid can be laid out with the straight tool; a highway, or a corner with a curve in it, is swept round, and a corner with no room is refused. A road pointed too close beside a corner or an intersection for a junction of its own goes through it instead, and the picture on the ground shows where it will really go. Where a wide road carries on as a narrower one, its extra lanes end a little before they meet. A toolbar at the bottom holds the tools, each a button with its name under it (Road, Lanes, Signs, Move, Replace, Bulldoze, then Undo, Redo and Clear), a switch between day and night, a large Drive button, and above them a tray of road choices: straight or curve, and twelve kinds of road under Street (25 mph), Avenue (40 mph, two-way with a painted line, a grass median or a turning lane down the middle) and Highway (65 mph, one-way, one to four lanes). Pointing at any of them explains it. Each road has a price per meter: a white tag on the end of the road being drawn shows its price and length, and the total is at the top. A one-way road brought in alongside another road, on the right of that road's traffic, merges into it with a lane of its own that then ends; started on a road the same way, it peels off as an exit. The roundabout shape lays a ring: click its middle, pull out its size, click again. Roads crossing the ring join it and nobody stops for a light; traffic going round has the right of way. Roads can be built as bridges or tunnels, three levels each: they cross other roads without meeting them, need room at each end to climb or go down, and cost two to seven times as much. Replace changes a road to another kind where it lies, which is also how a road gains or loses lanes. The lane tool works on single lanes. Click a lane to pick it, and a card offers to add a lane on that side of the road or take one away, so a road can have any number of lanes each way up to four; for a lane that leads to a light or stop signs the card also has arrows for the ways it may go, which is how a lane is made a left-turn lane, and a button to put a turning lane down the middle of a two-way road. Drag along the side of a road to add a lane for just that part (room to pull over or pass, or, run to the end of the road, a lane to turn from). One-way roads that part or come together at a shallow angle do so lane to lane with no light, so a two-lane road can split into two single lanes that go different ways and merge back. The move tool drags a point, taking its roads with it, and bends a road by its middle or pulls it straight again. Snapping pulls a straight road into line with the roads it starts from (straight on, square, or halfway between). A grid can be switched on, with lines 33, 165, 330 or 660 ft apart: it shows on the ground, and every new point lands where two of its lines cross, so streets come out square and blocks the same size (330 ft is a city block at sixteen to the mile). The signs tool sets the rules, which follow Ohio's traffic law: click a lane, and a card sets what runs the intersection it leads to (a traffic light, a light with left arrows, stop signs all round, a stop or yield sign on that street only, or no signs at all) and the speed limit posted on its road, from 15 mph for an alley to 70 for a freeway in the country. The map has an edge, shown as a broken line: a road run to the edge brings traffic in and lets it out, and a road that stops short of it is a dead end, marked with a red and white board, where traffic turns round across the road and comes back. A few cars drive around while you build; press Drive to let the full traffic in.

## Look

The sun is low and warm, so trees, lamp posts and trucks throw long shadows. Roads have a lip of concrete along the edge, mown grass beside them, and street lamps along the avenues; wild flowers grow in drifts in the open. Traffic lights and the bars at the stop lines glow. Every vehicle has working lights: brake lights when it slows, turn signals before a turn or a lane change, hazard lights after a crash. Press K, or the Day button on the toolbar, for night: the street lamps come on, and every vehicle has headlights and tail lights.

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
