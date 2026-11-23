# Traffic

A traffic game in the making, built in Unity 6. The first piece is the **Driver Lab**: a small street grid for building and tuning how individual drivers behave, before any city building.

## Maps

- **Street grid:** nine blocks, described below.
- **Figure eight:** one intersection joined to itself by two loops, with a pinch where two lanes become one.
- **City and highway:** six intersections beside a divided highway. Vehicles arrive at both ends of the highway and on two roads south of town, in waves. Some eastbound traffic takes the off-ramp into town, town traffic takes the on-ramp out, and everything that reaches the far end of the highway or the south edge leaves the map. Westbound traffic passes straight through.

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
- crashes if all that fails, and blocks the road until cleared (off by default for now; cars that touch are counted as bumps)

Special drivers stand out in a loud color and upset everyone around them:

- **Maniac:** speeds, tailgates, weaves, and pushes through red lights. Drivers with a green brake hard and freeze for a moment.
- **Slowpoke:** far under the limit with a huge gap.
- **On phone:** keeps looking away, so reacts very late and sits at green lights.
- **Drunk:** cannot hold a lane or a speed. Other drivers hang back and will not pass.

Controls: add cars, pick or tune the driver for new cars, click a car to inspect and retune it live, click an intersection to change how it is controlled, force a cut-in, pause, step and change speed. Drag to pan, scroll to zoom.

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
