# Traffic

A traffic game in the making. The first piece is the **Driver Lab**: a small test track for building and tuning how individual drivers behave, before any city building.

## Driver Lab

One four-way intersection whose arms are joined by two loops, so cars circulate forever. One loop has a stretch where the right lane ends and traffic has to merge.

Every car drives itself:

- follows the car ahead, keeping a time gap (Intelligent Driver Model)
- reacts late, by its own reaction time
- changes lanes when it gains something and the gap is safe, and merges out of a lane that ends
- obeys the traffic light or the all-way stop, and waits for a gap before turning left across oncoming traffic
- brakes too hard when someone cuts it off
- crashes if all that fails, and blocks the road until cleared

Controls: add cars, pick or tune the driver for new cars, click any car to inspect and retune it live, force a cut-in, switch the intersection between a traffic light and an all-way stop, pause, step and change speed. Drag to pan, scroll or pinch to zoom.

## Run it

```bash
npm install
npm run dev
```

`npm test` runs the simulation tests. `npm run build` type-checks and builds to `dist/`.
