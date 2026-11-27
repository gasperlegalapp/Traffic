using System;
using System.Collections.Generic;
using System.Globalization;
using Traffic.Sim;

namespace Traffic.Ui
{
    /// <summary>
    /// The hand that builds roads: it takes the pointer's position on the ground and its
    /// clicks and drags, changes the map, and keeps a picture (Ghost) of what the next click
    /// would do, with a short note to show beside the pointer. All of the feel of drawing
    /// lives here, with no Unity references, so it can be tested and drawn outside the editor.
    ///
    /// Drawing a road is a chain of clicks. A straight road runs from each click to the next.
    /// A curve leaves the last point the way the road was already heading and sweeps round to
    /// the pointer, so pieces flow into each other with no corner; from a point with no road
    /// to carry on from, one extra click first points the road the way it should set off.
    /// </summary>
    public sealed class RoadPen
    {
        /// <summary>The tools, numbered as the toolbar numbers them.</summary>
        public const int Road = 0, Bulldoze = 1, Replace = 2, Move = 3, Lane = 4, Signs = 5;
        /// <summary>The shapes a road can be drawn in.</summary>
        public const int Straight = 0, Curve = 1, Roundabout = 2;

        /// <summary>
        /// The grids that points can be kept to, by the spacing of their lines in feet; 0 is no
        /// grid. They are round numbers of feet, each a whole number of the one before: 25 ft is
        /// about two lanes, and 250 and 500 ft are a short and a long city block. (They were the
        /// land survey's chains and furlongs, 33, 165, 330 and 660 ft; the player asked for
        /// multiples of 25 ft instead.)
        /// </summary>
        public static readonly int[] GridFeet = { 0, 25, 50, 100, 250, 500 };
        /// <summary>The spacing of the grid's stronger lines, feet: a block.</summary>
        public const int BlockFeet = 250;
        /// <summary>How near a straight road has to come to lining up with another before it is pulled into line, radians.</summary>
        public const double Magnet = 7 * Math.PI / 180;
        private const double Feet = 3.28084;

        private EditableMap map;
        private int tool, shape, level;
        private RoadType type = RoadType.All[0];
        private bool snapAngles = true;
        /// <summary>The spacing of the grid, meters; 0 when there is none.</summary>
        private double grid;
        private int gridFeet;

        /// <summary>The map being built. Giving it a new one forgets any half-drawn road.</summary>
        public EditableMap Map
        {
            get { return map; }
            set
            {
                map = value;
                mapChanges++;
                Cancel();
            }
        }

        public int Tool
        {
            get { return tool; }
            set
            {
                if (tool == value) return;
                tool = value;
                Cancel();
            }
        }

        public int Shape
        {
            get { return shape; }
            set
            {
                if (shape == value) return;
                // The start stays, so a road can change from straight to curved partway along.
                if (shape == Roundabout || value == Roundabout) hasStart = false;
                shape = value;
                hasAim = false;
                ownAim = false;
                stamp++;
            }
        }

        public RoadType Type
        {
            get { return type; }
            set
            {
                type = value;
                stamp++;
            }
        }

        /// <summary>The height new roads are built at: 0 on the ground, up to 3 for a bridge, down to -3 for a tunnel.</summary>
        public int Level
        {
            get { return level; }
            set
            {
                level = value;
                stamp++;
            }
        }

        /// <summary>Pull a straight road into line with the roads it starts from: straight on, square, or halfway between.</summary>
        public bool SnapAngles
        {
            get { return snapAngles; }
            set
            {
                snapAngles = value;
                stamp++;
            }
        }

        /// <summary>
        /// Keep new points on a grid with lines this many feet apart (one of GridFeet), which is
        /// drawn on the ground while building; 0 for none. With a grid on, every new point lands
        /// where two of its lines cross, so roads come out square and blocks the same size.
        /// </summary>
        public int Grid
        {
            get { return gridFeet; }
            set
            {
                gridFeet = Math.Max(value, 0);
                grid = gridFeet / Feet;
                stamp++;
            }
        }

        /// <summary>Whether there is a grid.</summary>
        public bool SnapGrid => grid > 0;

        /// <summary>What the next click would do, as a picture to lay on the ground.</summary>
        public readonly Ghost Ghost = new Ghost();
        /// <summary>A few words to show beside the pointer: the length and price of the road, or why it cannot be built.</summary>
        public string Note = "";
        public bool NoteBad;
        /// <summary>
        /// When what the next click would do has a price: the price, and a few words on what it
        /// buys ("545 ft of street"). They go on a tag, in place of the note. Price is empty otherwise.
        /// </summary>
        public string Price = "", Detail = "";
        /// <summary>Where on the map the tag belongs (the end of the road in hand), when TagPlaced; otherwise it goes beside the pointer.</summary>
        public Vec TagAt;
        public bool TagPlaced;
        /// <summary>Goes up each time the picture changes.</summary>
        public int Version { get; private set; }

        // The road being drawn.
        private bool hasStart;
        private Vec start;
        /// <summary>The way a road arriving at the start is heading, if there is one to carry on from.</summary>
        private bool hasFlow;
        private double flow;
        /// <summary>
        /// The road being drawn was started on a lane partway along a road: it leaves along
        /// that lane, as an exit does, if it is then drawn the way the lane runs.
        /// </summary>
        private bool laneStart;
        private double laneFlow;
        /// <summary>
        /// The road being drawn was started from one lane's arrow at the end of a road: which
        /// lane that was (0 at the left of the traffic) and how many lanes leave that end. -1 if not.
        /// </summary>
        private int fromLane = -1, fromLanes;
        /// <summary>The way the player has pointed a curve to set off, when they have.</summary>
        private bool hasAim;
        private double aim;
        /// <summary>The player has chosen to point the curve by hand and not carry on from the road before.</summary>
        private bool ownAim;

        // What the move tool has hold of.
        private int grabNode = -1, grabSegment = -1;
        private Vec grabFrom, grabTo;
        private bool grabMoved;
        /// <summary>
        /// The move tool has hold of one end of an added lane: a point in the middle of that lane (which
        /// says which lane), whether the end in hand is the one its traffic comes in at, and where its other end is.
        /// </summary>
        private bool grabLane, grabLaneFirst;
        private Vec grabLaneOn, grabLaneKeep;

        private bool hasPointer;
        private Vec pointer;
        private int stamp, builtStamp = -1;
        private Vec builtAt;
        private double builtPixel;
        private bool builtPointer;
        // Checking whether a road can be built means building the whole network, so the answer is kept until something changes.
        private int checkedStamp = -1;
        private Vec checkedAt;
        private string checkedProblem;

        /// <summary>True while a road is partly drawn, or something is being dragged.</summary>
        public bool Busy => hasStart || grabNode >= 0 || grabSegment >= 0 || laneDrag || grabLane;

        // The lane tool: a lane picked by a click, and a drag along a road to add a lane for part of it.
        private bool picked, laneDrag;
        private Vec pickedAt, laneFrom, laneTo;

        /// <summary>True while a lane is picked: the lane tool's card then offers what can be done with it.</summary>
        public bool Picked => picked;
        /// <summary>The spot on the picked lane that was clicked.</summary>
        public Vec PickedAt => pickedAt;
        /// <summary>Whether the picked lane is one added along part of its road, which Remove takes away by itself.</summary>
        public bool PickedIsAdded => picked && map.ExtraLaneAt(pickedAt) != null;

        private string turnsFor;
        /// <summary>Goes up each time this pen changes the map, or is given another.</summary>
        private int mapChanges;
        private bool turnsThere;
        private bool[] turnsExist = new bool[3], turnsMay = new bool[3];

        /// <summary>
        /// For the picked lane, whether it leads to an intersection with a light or signs, and
        /// if so the ways on that are there (left, straight on, right) and the ways it may go.
        /// </summary>
        public bool PickedTurns(out bool[] exist, out bool[] may)
        {
            // Finding this out means building the network, so it is done once and kept until the map or the pick changes.
            string now = picked ? pickedAt.X + "," + pickedAt.Y + "," + map.Segments.Count + "," + mapChanges : "";
            if (turnsFor != now)
            {
                turnsFor = now;
                turnsThere = picked && map.LaneTurns(pickedAt, out turnsExist, out turnsMay);
            }
            exist = turnsExist;
            may = turnsMay;
            return turnsThere;
        }

        /// <summary>Let the picked lane go one of the ways (0 left, 1 straight on, 2 right) if it may not now, or forbid it if it may. Returns true if the map changed.</summary>
        public bool ToggleTurn(int way, out string refused)
        {
            bool[] exist, may;
            refused = !PickedTurns(out exist, out may) ? "That lane does not lead to an intersection." : map.TrySetTurn(pickedAt, (Turn)way, !may[way], true);
            stamp++;
            mapChanges++;
            return refused == null;
        }

        /// <summary>Give the road of the picked lane one more lane on that side, for its whole length. Returns true if the map changed.</summary>
        public bool AddLane(out string refused)
        {
            refused = !picked ? "Pick a lane first." : PickedIsMiddle ? "Pick a lane on the side you want another lane on." : map.TryChangeLanes(pickedAt, 1, true);
            stamp++;
            mapChanges++;
            return refused == null;
        }

        /// <summary>Whether the picked lane is the turning lane down the middle of its road.</summary>
        public bool PickedIsMiddle => picked && map.MiddleLaneAt(pickedAt);
        /// <summary>Whether the road of the picked lane could be given a turning lane down its middle.</summary>
        public bool CanAddMiddle => picked && map.CouldHaveMiddleLane(pickedAt);
        /// <summary>Whether the road of the picked lane has a median: what it can be given is then left-turn lanes in the median.</summary>
        public bool PickedHasMedian => picked && map.MedianAt(pickedAt);

        /// <summary>Give the road of the picked lane a turning lane down its middle. Returns true if the map changed.</summary>
        public bool AddMiddleLane(out string refused)
        {
            refused = picked ? map.TrySetMiddleLane(pickedAt, true, true) : "Pick a lane first.";
            stamp++;
            mapChanges++;
            return refused == null;
        }

        /// <summary>Whether the picked lane could be turned round to run the other way.</summary>
        public bool CanReverse => picked && map.CouldReverseLane(pickedAt);

        /// <summary>Turn the picked lane round, so that it runs the other way. Returns true if the map changed.</summary>
        public bool ReverseLane(out string refused)
        {
            refused = picked ? map.TryReverseLane(pickedAt, true) : "Pick a lane first.";
            stamp++;
            mapChanges++;
            // The lane that was picked may not be where it was: on a road left with unequal sides the pavement has moved over.
            if (refused == null) picked = map.LaneUnder(pickedAt) != null;
            return refused == null;
        }

        /// <summary>Take the picked lane away: an added lane or the middle turning lane by itself, or else one lane off that side of the road. Returns true if the map changed.</summary>
        public bool RemoveLane(out string refused)
        {
            refused = !picked ? "Pick a lane first." : PickedIsMiddle ? map.TrySetMiddleLane(pickedAt, false, true)
                : PickedIsAdded ? map.TryRemoveLane(pickedAt, true) : map.TryChangeLanes(pickedAt, -1, true);
            stamp++;
            if (refused == null) picked = false;
            return refused == null;
        }

        // ---------------------------------------------------------------- signs, lights and speed limits

        /// <summary>The tools that work on a lane picked with a click: the lane tool, and the signs tool.</summary>
        private bool Picking => tool == Lane || tool == Signs;

        /// <summary>
        /// For the picked lane, whether it leads to an intersection that a light or signs can
        /// run, and if so what runs it now, as seen from that lane's road, and whether that was
        /// left to the game.
        /// </summary>
        public bool PickedControl(out Control control, out bool automatic)
        {
            control = Control.Automatic;
            automatic = true;
            return picked && map.ControlAt(pickedAt, out control, out automatic);
        }

        /// <summary>Say what should run the intersection the picked lane leads to. Returns true if the map changed.</summary>
        public bool SetControl(Control control, out string refused)
        {
            refused = picked ? map.TrySetControl(pickedAt, control, true) : "Pick a lane first.";
            stamp++;
            mapChanges++;
            return refused == null;
        }

        /// <summary>Whether a sign forbids the road of the picked lane to turn on red at the intersection it leads to.</summary>
        public bool PickedNoTurnOnRed => picked && map.NoTurnOnRedAt(pickedAt);

        /// <summary>Put that sign up, or take it down. Returns true if the map changed.</summary>
        public bool SetNoTurnOnRed(bool on, out string refused)
        {
            refused = picked ? map.TrySetNoTurnOnRed(pickedAt, on, true) : "Pick a lane first.";
            stamp++;
            mapChanges++;
            return refused == null;
        }

        /// <summary>The speed limit on the road of the picked lane, miles per hour, and whether the player posted it.</summary>
        public int PickedSpeed(out bool posted)
        {
            posted = false;
            return picked ? map.SpeedAt(pickedAt, out posted) : 0;
        }

        /// <summary>Post the next higher (or lower) of the speed limits the law provides on the road of the picked lane. Returns true if the map changed.</summary>
        public bool StepSpeed(int direction, out string refused)
        {
            bool posted;
            int now = PickedSpeed(out posted), next = now;
            int[] limits = EditableMap.SpeedLimits;
            if (direction > 0) foreach (int mph in limits) { if (mph > now) { next = mph; break; } }
            if (direction < 0) for (int i = limits.Length - 1; i >= 0; i--) { if (limits[i] < now) { next = limits[i]; break; } }
            refused = !picked ? "Pick a lane first." : next == now ? (direction > 0 ? "That is the highest speed limit there is." : "That is the lowest speed limit there is.") : map.TrySetSpeed(pickedAt, next, true);
            stamp++;
            mapChanges++;
            return refused == null;
        }

        /// <summary>Take down the speed limit posted on the road of the picked lane, leaving the usual one for its kind of road. Returns true if the map changed.</summary>
        public bool UsualSpeed(out string refused)
        {
            refused = picked ? map.TrySetSpeed(pickedAt, 0, true) : "Pick a lane first.";
            stamp++;
            mapChanges++;
            return refused == null;
        }

        // ---------------------------------------------------------------- the pointer

        /// <summary>The pointer is over this spot on the ground. Pixel is how many meters one pixel of screen covers.</summary>
        public void Hover(Vec p, double pixel)
        {
            bool same = hasPointer && builtPointer && stamp == builtStamp && Dist(p, builtAt) < pixel * 0.25 && Math.Abs(pixel - builtPixel) < builtPixel * 0.03;
            pointer = p;
            hasPointer = true;
            if (same) return;
            Ghost.Pixel = pixel;
            Build();
        }

        /// <summary>The pointer is off the ground: over the toolbar or a panel.</summary>
        public void Away(double pixel)
        {
            if (!hasPointer && !builtPointer && stamp == builtStamp && Math.Abs(pixel - builtPixel) < builtPixel * 0.03) return;
            hasPointer = false;
            Ghost.Pixel = pixel;
            Build();
        }

        /// <summary>Forget the road that was half drawn, and let go of anything being dragged.</summary>
        public void Cancel()
        {
            hasStart = false;
            hasAim = false;
            ownAim = false;
            hasFlow = false;
            grabNode = grabSegment = -1;
            picked = laneDrag = grabLane = false;
            stamp++;
        }

        /// <summary>Go back one step: right-click, or Esc.</summary>
        public void Back()
        {
            stamp++;
            if (grabNode >= 0 || grabSegment >= 0 || laneDrag || grabLane)
            {
                grabNode = grabSegment = -1;
                laneDrag = grabLane = false;
                return;
            }
            if (Picking)
            {
                picked = false;
                return;
            }
            if (shape == Curve && hasStart)
            {
                // From a curve that is being swept round: back to pointing it. From pointing it: stop.
                if (hasAim)
                {
                    hasAim = false;
                    return;
                }
                if (hasFlow && !ownAim)
                {
                    ownAim = true;
                    return;
                }
            }
            hasStart = false;
            hasAim = false;
            ownAim = false;
        }

        /// <summary>A line of help about what the next click will do.</summary>
        public string Hint
        {
            get
            {
                if (tool == Bulldoze) return "Click a road to remove it.";
                if (tool == Replace) return "Click a road to change it to the kind chosen.";
                if (tool == Move) return "Drag a point to move it. Drag the middle of a road to bend it, or pull it back straight.";
                if (tool == Signs) return picked ? "Choose what runs the intersection ahead, or post a speed limit, with the card. Click another lane to pick it."
                    : "Click a lane. The card then sets the sign or light at the intersection it leads to, and the speed limit on its road.";
                if (tool == Lane) return picked ? "Add or remove a lane with the card. Click another lane to pick it, or drag along a road to add a lane for part of it."
                    : "Click a lane to pick it. Drag along the side of a road to add a lane for just that part of it.";
                if (shape == Roundabout) return hasStart ? "Pull out to the size you want, then click. Right-click to go back." : "Click where the middle of the roundabout should be.";
                if (!hasStart)
                {
                    if (map != null && map.Segments.Count > 0) return type.OneWay ? "Click where the road starts. Click a lane's blue arrow, or a lane along a road, to build off that lane."
                        : "Click where the road starts, or a lane's blue arrow to build on from it. Touch or cross another road to make an intersection.";
                    return map != null && map.Bound > 0 ? "Click where the road starts. Run a road to the edge of the map, the broken line, to bring traffic in."
                        : "Click where the road starts. An open road end is where traffic comes in.";
                }
                if (shape == Straight) return "Click where the road ends. Right-click to stop.";
                double heading;
                if (!Heading(out heading)) return "Click to point the road the way it should set off. Right-click to stop.";
                return hasAim ? "Sweep the road round to where it should end, and click. Right-click to point it again."
                    : "The road carries on smoothly from here. Click where it should end. Right-click to point it yourself.";
            }
        }

        // ---------------------------------------------------------------- clicks

        /// <summary>
        /// A click on the ground. Returns true if it changed the map. If the click could not do
        /// what it was meant to, refused says why.
        /// </summary>
        public bool Click(Vec p, out string refused)
        {
            refused = null;
            pointer = p;
            hasPointer = true;
            stamp++;
            if (tool == Bulldoze)
            {
                Vec on;
                int seg = map.NearestSegment(p, EditableMap.SnapRoad * 1.5, out on);
                if (seg >= 0 && map.Segments[seg].Locked) refused = "That road came with the level.";
                return seg >= 0 && map.RemoveNear(p);
            }
            if (tool == Replace)
            {
                refused = map.TryReplace(p, type, true);
                return refused == null;
            }
            if (tool == Move) return false;
            // The lane tool works by presses and drags: see Press and Release.
            if (Picking) return false;

            if (shape == Roundabout)
            {
                // The first click is its middle, the second sets its size.
                Vec at = OnGrid(p);
                if (!hasStart)
                {
                    start = at;
                    hasStart = true;
                    return false;
                }
                refused = map.TryAddRoundabout(start, Dist(at, start), type, true);
                if (refused != null) return false;
                hasStart = false;
                return true;
            }

            bool lined;
            double line;
            Vec end = OnRing(p, map.JoinPoint(Snapped(p, out lined, out line), hasStart ? start : (Vec?)null));
            if (!hasStart)
            {
                start = end;
                hasStart = true;
                hasAim = false;
                ownAim = false;
                laneStart = false;
                fromLane = -1;
                // Started on a lane's arrow at the end of a road: the road is built on from that lane.
                // The ring beyond the end takes the road as a whole, as a click on the end itself does.
                EditableMap.LaneEnd knob;
                Vec whole;
                bool onKnob = false;
                if (WholeEndAt(p, out whole)) start = whole;
                else if (LaneEndAt(p, true, out knob))
                {
                    start = knob.Node;
                    fromLane = knob.Lane;
                    fromLanes = knob.Lanes;
                    onKnob = true;
                    flow = knob.Heading;
                }
                if (onKnob) hasFlow = true;
                else hasFlow = map.FlowAt(start, out flow);
                // Started on a lane partway along a road, with a one-way road in hand: it leaves along that lane.
                Vec on;
                int lane, lanes;
                Snap at = map.SnapTo(start);
                if (!hasFlow && type.OneWay && at.Node < 0 && at.Segment >= 0 && map.LaneAt(p, out on, out laneFlow, out lane, out lanes))
                {
                    laneStart = true;
                    hasFlow = true;
                    flow = laneFlow;
                }
                return false;
            }
            double heading = 0;
            if (shape == Curve && !Heading(out heading))
            {
                // Nothing to carry on from: this click points the road.
                if (Dist(end, start) < 3) return false;
                aim = Math.Atan2(end.Y - start.Y, end.X - start.X);
                hasAim = true;
                return false;
            }
            end = Eased(end, heading);
            Vec via;
            bool behind;
            bool curved = Sweep(end, heading, out via, out behind);
            if (behind)
            {
                refused = TooSharp;
                return false;
            }
            Vec[] built;
            List<Vec> meets;
            // Built on from a lane, or ended on one lane's ring: the road starts or ends where that lane is.
            int tieFrom, tieTo;
            Ties(end, out tieFrom, out tieTo);
            refused = map.TryAdd(start, end, type, true, curved ? via : (Vec?)null, Over(end, curved, via), out built, out meets, tieFrom, tieTo);
            if (refused != null) return false;
            laneStart = false;
            fromLane = -1;
            // Carry on from where this stretch ended (which may be a corner or junction close by that it was taken to), heading the way it arrived.
            Vec arrived = built[built.Length - 1], before = built[built.Length - 2];
            // A road brought in to a lane ends beside the point its road starts at: it is that point the next stretch carries on from.
            if (map.NodeAt(arrived, 0.01) < 0 && map.NodeAt(arrived, NetworkBuilder.LaneW * 3) >= 0) arrived = map.Nodes[map.NodeAt(arrived, NetworkBuilder.LaneW * 3)];
            bool asPointed = Dist(arrived, end) < 0.5;
            flow = curved && asPointed && EditableMap.IsCurve(start, via, end) ? EditableMap.BendArrives(start, heading, end) : Math.Atan2(arrived.Y - before.Y, arrived.X - before.X);
            hasFlow = true;
            hasAim = false;
            ownAim = false;
            start = asPointed ? map.SnapTo(end).Point : arrived;
            return true;
        }

        private const string TooSharp = "The road cannot double back on itself. Right-click to point it another way.";

        /// <summary>
        /// The level the road in hand will be built at if it ends at a point: the one chosen on
        /// the toolbar, except that a road drawn on the ground across water becomes a bridge,
        /// which is what anyone drawing it means.
        /// </summary>
        private int Over(Vec end, bool curved, Vec via)
        {
            return map.LevelOver(start, end, type, curved ? via : (Vec?)null, level);
        }

        /// <summary>Whether a road from the start to a point could be built, given the way a curve sets off: null if it could, or the reason.</summary>
        private string Attempt(Vec end, double heading)
        {
            Vec via;
            bool behind;
            bool curved = Sweep(end, heading, out via, out behind);
            if (behind) return TooSharp;
            Vec[] line;
            List<Vec> meets;
            int tieFrom, tieTo;
            Ties(end, out tieFrom, out tieTo);
            return map.TryAdd(start, end, type, false, curved ? via : (Vec?)null, Over(end, curved, via), out line, out meets, tieFrom, tieTo);
        }

        /// <summary>
        /// The shape of the piece from the start to a point: true if it is swept round, through
        /// the point given, and false if it is straight. With the curve tool it sweeps from the
        /// way the road is heading. With the straight tool it is straight, with two exceptions
        /// that make a one-way road leave or join a lane as a ramp does, along the lane and not
        /// across it: a road started on a lane and drawn the way that lane runs sweeps away from
        /// it, and a road ended on a lane it is already heading along sweeps in to it.
        /// </summary>
        private bool Sweep(Vec end, double heading, out Vec via, out bool behind)
        {
            via = default(Vec);
            behind = false;
            if (shape == Curve)
            {
                bool swept = EditableMap.Bend(start, heading, end, out via);
                behind = !swept && Behind(heading, end);
                return swept;
            }
            double along = Math.Atan2(end.Y - start.Y, end.X - start.X);
            if (laneStart && Math.Cos(along - laneFlow) > 0.3)
            {
                // To the right it leaves along the lane, as a ramp does. To the left it can only do so from a one-way road,
                // where it makes a fork, and the two ways on have to part by a few degrees from the first.
                bool toLeft = Math.Sin(along - laneFlow) > 0;
                if (!toLeft) return EditableMap.Bend(start, laneFlow, end, out via);
                RoadType under = map.RoadAt(start);
                return under != null && under.OneWay && EditableMap.Bend(start, laneFlow + ForkAngle, end, out via);
            }
            double joins;
            if (!laneStart && !hasFlow && type.OneWay && JoinsLane(end, out joins) && Math.Cos(along - joins) > 0.3)
            {
                // The same curve drawn from the far end: it leaves the lane backward and comes round to the start.
                bool fromLeft = Math.Sin(along - joins) < 0;
                if (!fromLeft) return EditableMap.Bend(end, joins + Math.PI, start, out via);
                RoadType under = map.RoadAt(end);
                return under != null && under.OneWay && EditableMap.Bend(end, joins - ForkAngle + Math.PI, start, out via);
            }
            return false;
        }

        /// <summary>How far apart, in radians, two one-way roads set off where one leaves the other on its left: a little more than the least a fork allows.</summary>
        private const double ForkAngle = 0.3;

        /// <summary>Whether a point where the road in hand would end is partway along a road, and if so the way the lane under the pointer there runs.</summary>
        private bool JoinsLane(Vec end, out double heading)
        {
            heading = 0;
            Snap at = map.SnapTo(end);
            Vec on;
            int lane, lanes;
            return at.Node < 0 && at.Segment >= 0 && map.LaneAt(pointer, out on, out heading, out lane, out lanes);
        }

        /// <summary>The lane's end within reach of a point, at an open end of a road: one that traffic leaves by, to build on from, or one it sets out from, to bring a road in to.</summary>
        private bool LaneEndAt(Vec p, bool leaving, out EditableMap.LaneEnd found)
        {
            found = default(EditableMap.LaneEnd);
            double nearest = Math.Max(NetworkBuilder.LaneW * 0.6, Ghost.Pixel * 10);
            bool any = false;
            foreach (EditableMap.LaneEnd end in map.LaneEnds())
            {
                if (end.Leaving != leaving || Dist(end.At, p) >= nearest) continue;
                nearest = Dist(end.At, p);
                found = end;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// Which lane the road in hand is built on from, if it was started on a lane's arrow and
        /// is one-way: 0 for the leftmost. A road with no more lanes than the road it leaves
        /// takes the lane that was picked, and the lanes beside it if it has more than one. A
        /// road with more lanes gains its new lanes on the side it is drawn toward, or on the
        /// left if it is drawn straight on: the number is then the one of its own lanes that the
        /// lane picked leads into. -1 if it was not started on a lane.
        /// </summary>
        private int LaneFrom(Vec end)
        {
            if (fromLane < 0 || !type.OneWay) return -1;
            if (type.Forward <= fromLanes) return Math.Min(fromLane, fromLanes - type.Forward);
            double side = Math.Cos(flow) * (end.Y - start.Y) - Math.Sin(flow) * (end.X - start.X);
            return side < -2 ? 0 : type.Forward - fromLanes;
        }

        /// <summary>The lanes the road in hand would be built on from and brought in to, if it ended at a point with the pointer where it is: -1 for neither.</summary>
        private void Ties(Vec end, out int tieFrom, out int tieTo)
        {
            tieFrom = LaneFrom(end);
            tieTo = -1;
            EditableMap.LaneEnd ring;
            if (!hasStart || !type.OneWay || !hasPointer || !LaneEndAt(pointer, false, out ring) || Dist(ring.Node, end) > 0.5) return;
            tieTo = ring.Lanes >= type.Forward ? Math.Min(ring.Lane, ring.Lanes - type.Forward) : 0;
        }

        /// <summary>Where the road in hand ends, given where it would otherwise: on a lane's ring, with a one-way road in hand, it ends at the point that lane's road starts from.</summary>
        private Vec OnRing(Vec p, Vec end)
        {
            EditableMap.LaneEnd ring;
            Vec whole;
            if (!hasStart || !type.OneWay || WholeEndAt(p, out whole)) return end;
            return LaneEndAt(p, false, out ring) ? ring.Node : end;
        }

        /// <summary>How far beyond the end of a road the ring that stands for the whole road is drawn, and how big it is.</summary>
        private double WholeOut { get { return Math.Max(5.6, Ghost.Pixel * 16); } }
        private double WholeSize { get { return Math.Max(2.7, Ghost.Pixel * 8); } }

        /// <summary>
        /// The open road ends that have more than one lane to choose between: for each, the
        /// point the road ends at and the place just beyond it where the ring for the whole
        /// road is drawn.
        /// </summary>
        private List<Vec[]> WholeEnds()
        {
            var found = new List<Vec[]>();
            var count = new Dictionary<long, int>();
            List<EditableMap.LaneEnd> ends = map.LaneEnds();
            Func<Vec, long> key = v => ((long)Math.Round(v.X * 10) << 32) ^ (uint)(int)Math.Round(v.Y * 10);
            foreach (EditableMap.LaneEnd end in ends)
            {
                int n;
                count.TryGetValue(key(end.Node), out n);
                count[key(end.Node)] = n + 1;
            }
            var seen = new HashSet<long>();
            foreach (EditableMap.LaneEnd end in ends)
            {
                if (!end.Open || count[key(end.Node)] < 2 || !seen.Add(key(end.Node))) continue;
                found.Add(new[] { end.Node, new Vec(end.Node.X + Math.Cos(end.Outward) * WholeOut, end.Node.Y + Math.Sin(end.Outward) * WholeOut) });
            }
            return found;
        }

        /// <summary>Whether a point is on the ring that stands for a whole road at its open end, and if so the point that road ends at.</summary>
        private bool WholeEndAt(Vec p, out Vec node)
        {
            node = p;
            if (map == null || tool != Road || shape == Roundabout) return false;
            foreach (Vec[] whole in WholeEnds())
            {
                if (Dist(whole[1], p) > WholeSize * 1.15) continue;
                node = whole[0];
                return true;
            }
            return false;
        }

        private int easedStamp = -1;
        private Vec easedFrom, easedTo;

        /// <summary>
        /// Where the road being drawn should end, when the place asked for leaves no room for a
        /// junction of its own: the nearest junction or road end that it can reach instead. The
        /// player points roughly, and the road joins up where it can.
        /// </summary>
        private Vec Eased(Vec end, double heading)
        {
            if (easedStamp == stamp && Dist(end, easedFrom) < 0.05) return easedTo;
            easedStamp = stamp;
            easedFrom = end;
            easedTo = end;
            if (Attempt(end, heading) != EditableMap.NoRoom) return end;
            int tried = 0;
            foreach (Vec join in map.JoinsNear(end, 48))
            {
                if (Dist(join, start) < 1) continue;
                if (Attempt(join, heading) == null)
                {
                    easedTo = join;
                    break;
                }
                if (++tried >= 3) break;
            }
            return easedTo;
        }

        /// <summary>The way a curve from the start sets off, if that is settled: as pointed by hand, or carrying on from the road before.</summary>
        private bool Heading(out double heading)
        {
            heading = hasAim ? aim : flow;
            return hasAim || (hasFlow && !ownAim);
        }

        private bool Behind(double heading, Vec end)
        {
            return (end.X - start.X) * Math.Cos(heading) + (end.Y - start.Y) * Math.Sin(heading) < 0;
        }

        // ---------------------------------------------------------------- dragging (the move tool)

        /// <summary>How near the pointer has to be to a point, or to a road, to catch on it: a set distance on the ground, or a few pixels if that is more.</summary>
        private double NodeReach => Math.Max(EditableMap.SnapNode - 0.5, Ghost.Pixel * 14);
        private double RoadReach => Math.Max(EditableMap.SnapRoad - 0.5, Ghost.Pixel * 10);

        private bool Fixed(EditSegment s)
        {
            return s.Locked || s.Ring;
        }

        /// <summary>The point within reach of a spot that the move tool could take hold of, or -1.</summary>
        private int HandleAt(Vec p)
        {
            int node = map.NodeAt(p, NodeReach);
            if (node < 0) return -1;
            foreach (EditSegment s in map.RoadsAt(node)) if (Fixed(s)) return -1;
            return node;
        }

        /// <summary>The road within reach of a spot that the move tool could bend, or -1.</summary>
        private int BendableAt(Vec p)
        {
            Vec on;
            int seg = map.NearestSegment(p, RoadReach, out on);
            return seg >= 0 && !Fixed(map.Segments[seg]) ? seg : -1;
        }

        /// <summary>
        /// The end of an added lane within reach of a spot, for the move tool to slide along its
        /// road: where that end is, a point in the middle of the lane, whether it is the end the
        /// lane's traffic comes in at, and where the other end is. False if there is none.
        /// </summary>
        private bool LaneEndAt(Vec p, out Vec end, out Vec onLane, out bool first, out Vec keep)
        {
            end = onLane = keep = default(Vec);
            first = false;
            double nearest = Math.Max(5, Ghost.Pixel * 12);
            bool found = false;
            foreach (Vec[] line in map.AddedLanes())
            {
                for (int k = 0; k < 2; k++)
                {
                    Vec at = k == 0 ? line[0] : line[line.Length - 1];
                    if (Dist(at, p) >= nearest) continue;
                    nearest = Dist(at, p);
                    end = at;
                    onLane = line[line.Length / 2];
                    first = k == 0;
                    keep = k == 0 ? line[line.Length - 1] : line[0];
                    found = true;
                }
            }
            return found;
        }

        /// <summary>The button went down with the move tool out. True if it took hold of a point or a road.</summary>
        public bool Press(Vec p)
        {
            if (Picking && map != null)
            {
                // Every press of the lane tool is taken: let go where it went down it picks a lane, dragged along a road it lays one.
                // (The signs tool picks a lane the same way, and a drag with it does nothing more.)
                laneDrag = true;
                laneFrom = laneTo = p;
                grabMoved = false;
                stamp++;
                return true;
            }
            if (tool != Move || map == null) return false;
            grabNode = HandleAt(p);
            Vec end;
            // A point first; then the end of an added lane; then the road itself, to bend it.
            grabLane = grabNode < 0 && LaneEndAt(p, out end, out grabLaneOn, out grabLaneFirst, out grabLaneKeep);
            grabSegment = grabNode >= 0 || grabLane ? -1 : BendableAt(p);
            if (grabNode < 0 && grabSegment < 0 && !grabLane) return false;
            grabFrom = grabTo = p;
            grabMoved = false;
            stamp++;
            return true;
        }

        /// <summary>The pointer moved with the button held.</summary>
        public void Drag(Vec p)
        {
            if (laneDrag)
            {
                if (Dist(p, laneFrom) > Math.Max(1.5, Ghost.Pixel * 5)) grabMoved = true;
                laneTo = pointer = p;
                hasPointer = true;
                stamp++;
                return;
            }
            if (grabNode < 0 && grabSegment < 0 && !grabLane) return;
            if (Dist(p, grabFrom) > Math.Max(1.5, Ghost.Pixel * 5)) grabMoved = true;
            grabTo = grabNode >= 0 ? OnGrid(p) : p;
            pointer = p;
            hasPointer = true;
            stamp++;
        }

        /// <summary>The button came up. Returns true if the map changed; refused says why if the move could not be made.</summary>
        public bool Release(out string refused)
        {
            refused = null;
            stamp++;
            if (laneDrag)
            {
                laneDrag = false;
                if (grabMoved && tool == Lane)
                {
                    refused = map.TryAddLane(laneFrom, laneTo, true);
                    mapChanges++;
                    return refused == null;
                }
                // A click: pick the lane there. A click off the road lets go of the one picked.
                picked = map.LaneUnder(laneFrom) != null;
                pickedAt = laneFrom;
                return false;
            }
            if (grabLane)
            {
                grabLane = false;
                if (!grabMoved) return false;
                refused = map.TryMoveLaneEnd(grabLaneOn, grabLaneFirst, grabTo, true);
                mapChanges++;
                return refused == null;
            }
            int node = grabNode, segment = grabSegment;
            grabNode = grabSegment = -1;
            if (!grabMoved || (node < 0 && segment < 0)) return false;
            refused = node >= 0 ? map.TryMove(node, grabTo, true) : map.TryBend(segment, grabTo, true);
            return refused == null;
        }

        // ---------------------------------------------------------------- snapping

        /// <summary>
        /// On a map with an edge, a point at the edge or beyond it lands exactly on it: that is
        /// how a road is run off the map, to bring traffic in.
        /// </summary>
        private Vec ToEdge(Vec p)
        {
            if (map.Bound <= 0) return p;
            double near = map.Bound - Math.Max(6, Ghost.Pixel * 10);
            return new Vec(Math.Abs(p.X) > near ? Math.Sign(p.X) * map.Bound : p.X, Math.Abs(p.Y) > near ? Math.Sign(p.Y) * map.Bound : p.Y);
        }

        private Vec OnGrid(Vec p)
        {
            return grid > 0 ? new Vec(OnGrid(p.X), OnGrid(p.Y)) : p;
        }

        /// <summary>The nearest line of the grid. The edge of the map counts as one, in place of a line that would fall just outside it.</summary>
        private double OnGrid(double v)
        {
            double line = Math.Round(v / grid) * grid;
            bool edged = map != null && map.Bound > 0;
            if (edged && (Math.Abs(v) > map.Bound - 1e-6 || Math.Abs(line) > map.Bound)) return Math.Sign(v) * map.Bound;
            return line;
        }

        /// <summary>
        /// Where a click really lands once snapping has had its say. Joining a road that is
        /// already there always wins. Otherwise the point is kept to the grid, if that is on;
        /// and a straight road that is nearly in line with the roads it starts from (straight
        /// on, square to them, or halfway between) is pulled exactly into line.
        /// </summary>
        private Vec Snapped(Vec p, out bool lined, out double heading)
        {
            lined = false;
            heading = 0;
            p = ToEdge(p);
            // From far out a road is only a few pixels wide, so reach for it by what the eye sees as near.
            // A short piece can be drawn: the far end is not pulled back to the point the road starts from, or on to the
            // end of the road it leaves, once it is a short piece's length away.
            bool clear = hasStart && Dist(p, start) >= EditableMap.MinPiece;
            int node = map.NodeAt(p, NodeReach);
            if (node >= 0 && clear && Dist(map.Nodes[node], start) < 0.01)
            {
                node = -1;
                for (int i = 0; i < map.Nodes.Count; i++)
                {
                    if (Dist(map.Nodes[i], start) < 0.01 || Dist(map.Nodes[i], p) >= NodeReach) continue;
                    if (node < 0 || Dist(map.Nodes[i], p) < Dist(map.Nodes[node], p)) node = i;
                }
            }
            if (node >= 0) return map.Nodes[node];
            Vec on;
            if (map.NearestSegment(p, RoadReach, out on) >= 0 && !(clear && Dist(on, start) < 0.5))
            {
                // On a road. With a grid, at the place where the road meets the grid, if it runs through one close by.
                Vec there;
                if (grid > 0 && map.NearestSegment(OnGrid(p), 2, out there) >= 0) return there;
                return on;
            }
            p = OnGrid(p);
            // With a grid on, the grid has the last word: lining up with other roads would take the point off it.
            if (grid > 0) return p;
            double settled;
            bool pointing = shape == Curve && !Heading(out settled);
            if (!snapAngles || !hasStart || !(shape == Straight || pointing)) return p;
            double dx = p.X - start.X, dy = p.Y - start.Y;
            if (Geo.Hypot(dx, dy) < 2) return p;
            double want = Math.Atan2(dy, dx);
            // With no road at the start to line up with, line up with the compass.
            List<double> roads = map.HeadingsAt(start);
            if (roads.Count == 0) roads.Add(0);
            double best = Magnet;
            foreach (double road in roads)
            {
                for (int k = 0; k < 8; k++)
                {
                    double way = road + k * Math.PI / 4;
                    double off = Math.Abs(Math.Atan2(Math.Sin(way - want), Math.Cos(way - want)));
                    if (off >= best) continue;
                    best = off;
                    heading = way;
                    lined = true;
                }
            }
            if (!lined) return p;
            // Keep the length the pointer asked for, so the end stays under it as nearly as it can.
            double along = dx * Math.Cos(heading) + dy * Math.Sin(heading);
            return new Vec(start.X + Math.Cos(heading) * along, start.Y + Math.Sin(heading) * along);
        }

        // ---------------------------------------------------------------- the picture

        private static double Dist(Vec a, Vec b)
        {
            return Geo.Hypot(a.X - b.X, a.Y - b.Y);
        }

        private static string Money(double dollars)
        {
            return "$" + Math.Round(dollars).ToString("N0", CultureInfo.InvariantCulture);
        }

        private static string Ft(double meters)
        {
            return Math.Round(meters * Feet).ToString("N0", CultureInfo.InvariantCulture) + " ft";
        }

        private Vec[] checkedLine;
        private List<Vec> checkedMeets;

        /// <summary>How far the furthest of some points lies from a line.</summary>
        private static double Aside(Vec[] line, List<Vec> points)
        {
            double most = 0;
            foreach (Vec p in points)
            {
                double least = double.MaxValue;
                for (int i = 0; i + 1 < line.Length; i++)
                {
                    double dx = line[i + 1].X - line[i].X, dy = line[i + 1].Y - line[i].Y, len = dx * dx + dy * dy;
                    double t = len < 1e-12 ? 0 : Geo.Clamp(((p.X - line[i].X) * dx + (p.Y - line[i].Y) * dy) / len, 0, 1);
                    least = Math.Min(least, Geo.Hypot(p.X - line[i].X - dx * t, p.Y - line[i].Y - dy * t));
                }
                most = Math.Max(most, least);
            }
            return most;
        }

        /// <summary>The answer to "can this be built", worked out afresh only when the pointer has moved or something has changed.</summary>
        private string Checked(Vec at, Func<string> check)
        {
            if (checkedStamp == stamp && Dist(at, checkedAt) < Math.Max(0.3, Ghost.Pixel * 0.6)) return checkedProblem;
            checkedProblem = check();
            checkedStamp = stamp;
            checkedAt = at;
            return checkedProblem;
        }

        private void Build()
        {
            builtStamp = stamp;
            builtAt = pointer;
            builtPixel = Ghost.Pixel;
            builtPointer = hasPointer;
            Version++;
            Ghost.Clear();
            Note = "";
            NoteBad = false;
            Price = "";
            Detail = "";
            TagPlaced = false;
            if (map == null) return;
            GridPicture();
            Marks();
            if (!hasPointer)
            {
                // Over the toolbar or a panel: only where the half-drawn road starts.
                if (hasStart && tool == Road)
                {
                    Ghost.Pin(start, true, false);
                    Ghost.Stand(start, true, false);
                }
                if (Picking) LanePicture();
                return;
            }
            if (tool == Bulldoze || tool == Replace) OverRoad();
            else if (tool == Move) Handles();
            else if (Picking) LanePicture();
            else if (shape == Roundabout) RingPicture();
            else RoadPicture();
            // With nothing else to say, say what is wrong with a ringed place the pointer is on.
            if (Note.Length > 0 || Busy) return;
            foreach (Fault fault in faults)
            {
                if (Dist(fault.At, pointer) > NodeReach) continue;
                Note = fault.What;
                NoteBad = true;
                break;
            }
        }

        /// <summary>
        /// The grid, as faint lines on the ground under everything else, while a road is being
        /// drawn or moved. Lines a block apart (250 ft) are a little stronger. From far out, where
        /// the lines chosen would crowd together, only every fifth, tenth or twentieth is drawn.
        /// </summary>
        private void GridPicture()
        {
            if (grid <= 0 || !(tool == Road || tool == Move)) return;
            double half = map.Bound > 0 ? map.Bound : 600, block = BlockFeet / Feet;
            double step = grid;
            foreach (int feet in GridFeet)
            {
                if (feet / Feet < grid - 1e-6 || step >= Ghost.Pixel * 10) continue;
                step = feet / Feet;
            }
            double thin = Ghost.AtLeast(0.22, 1), thick = Ghost.AtLeast(0.4, 1.5);
            for (int k = (int)Math.Ceiling(-half / step); k * step <= half; k++)
            {
                double at = k * step;
                // The edge of the map is drawn already, as a broken line.
                if (map.Bound > 0 && Math.Abs(at) > half - step / 2) continue;
                bool strong = Math.Abs(at / block - Math.Round(at / block)) < 1e-6;
                Ghost.Stroke(new[] { new Vec(at, -half), new Vec(at, half) }, strong ? thick : thin, strong ? GridStrong : GridFaint);
                Ghost.Stroke(new[] { new Vec(-half, at), new Vec(half, at) }, strong ? thick : thin, strong ? GridStrong : GridFaint);
            }
        }

        private const uint GridFaint = 0x30FFFFFF, GridStrong = 0x5CFFFFFF;

        private List<Fault> faults = new List<Fault>();

        /// <summary>Marks on the map itself: the open road ends a new road can carry on from, or everything the move tool can drag.</summary>
        private void Marks()
        {
            // A red ring round every place where traffic could not flow, whatever tool is out.
            faults = map.Faults();
            foreach (Fault fault in faults)
            {
                Ghost.Ring(fault.At, Ghost.AtLeast(7, 13), Ghost.AtLeast(0.9, 3), Ghost.BadInk);
                Ghost.Dot(fault.At, 1.2, 4, Ghost.BadInk);
            }
            if (tool != Road && tool != Move) return;
            var roads = new int[map.Nodes.Count];
            var held = new bool[map.Nodes.Count];
            foreach (EditSegment s in map.Segments)
            {
                roads[s.A]++;
                roads[s.B]++;
                if (Fixed(s)) held[s.A] = held[s.B] = true;
            }
            if (tool == Move && !grabLane)
            {
                // Each end of an added lane can be slid along its road: a smaller handle than a point's.
                foreach (Vec[] line in map.AddedLanes())
                {
                    foreach (Vec end in new[] { line[0], line[line.Length - 1] })
                    {
                        Ghost.Dot(end, 1.25, 4.6, Ghost.GoodInk);
                        Ghost.Dot(end, 0.75, 2.8, Ghost.White);
                    }
                }
            }
            for (int i = 0; i < map.Nodes.Count; i++)
            {
                if (tool == Move)
                {
                    if (held[i] || i == grabNode) continue;
                    Ghost.Dot(map.Nodes[i], 1.5, 5.5, Ghost.GoodInk);
                    Ghost.Dot(map.Nodes[i], 0.95, 3.5, Ghost.White);
                }
                else if (roads[i] == 1)
                {
                    Ghost.Dot(map.Nodes[i], 1.1, 3.5, Ghost.Faint);
                }
            }
            if (tool != Road || shape == Roundabout) return;
            // At each open end of a road, every lane by itself: an arrow on each lane that traffic leaves by, to build on
            // from, before a road is started; and once one is, a ring on each lane that sets out from an end, to bring it in to.
            EditableMap.LaneEnd near = default(EditableMap.LaneEnd);
            bool over = hasPointer && LaneEndAt(pointer, !hasStart, out near);
            Vec wholeNode;
            bool onWhole = hasPointer && WholeEndAt(pointer, out wholeNode);
            foreach (EditableMap.LaneEnd end in map.LaneEnds())
            {
                if (end.Leaving == hasStart) continue;
                // Only a one-way road is brought in to a single lane.
                if (!end.Leaving && !type.OneWay) continue;
                bool hot = over && !onWhole && Dist(end.At, near.At) < 0.01;
                if (end.Leaving) Ghost.Knob(end.At, end.Heading, hot);
                else Ghost.Socket(end.At, hot);
            }
            // And a larger ring just beyond the end, for the road as a whole.
            foreach (Vec[] whole in WholeEnds()) Ghost.Whole(whole[1], WholeSize, hasPointer && Dist(whole[1], pointer) <= WholeSize * 1.15);
        }

        /// <summary>The road under the pointer, as the bulldozer or the replace tool would leave it.</summary>
        private void OverRoad()
        {
            Vec on;
            int seg = map.NearestSegment(pointer, EditableMap.SnapRoad * 1.5, out on);
            if (seg < 0) return;
            EditSegment s = map.Segments[seg];
            Vec[] line = map.Trace(s);
            if (tool == Bulldoze)
            {
                Ghost.Road(line, s.Type.Width, s.Type.OneWay, false);
                if (s.Locked)
                {
                    Note = "That road came with the level.";
                    NoteBad = true;
                }
                return;
            }
            string problem = Checked(pointer, () => map.TryReplace(pointer, type, false));
            Ghost.Road(line, type.Width, type.OneWay, problem == null);
            double change = (type.CostPerMeter - s.Type.CostPerMeter) * Ghost.Length(line) * EditableMap.CostTimes(s.Level);
            Note = problem ?? (change >= 0 ? "+" : "−") + Money(Math.Abs(change));
            NoteBad = problem != null;
            if (problem == null) Tag(Note, type.Name);
        }

        /// <summary>Put a price on the tag, with a few words on what it buys.</summary>
        private void Tag(string price, string detail)
        {
            Price = price;
            Detail = detail;
        }

        /// <summary>A name as it reads in the middle of a sentence.</summary>
        private static string Lower(string name)
        {
            return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <summary>How near a road the pointer has to be to work on its lanes.</summary>
        private const double LaneReach = EditableMap.SnapRoad * 2.2;

        /// <summary>A lane that is not there yet, or one about to go: a band one lane wide with a firm line down each side.</summary>
        private void LaneBand(Vec[] line, bool good)
        {
            double half = NetworkBuilder.LaneW / 2, rim = Ghost.AtLeast(0.4, 2.2);
            Ghost.Band(line, -half, half, good ? Ghost.GoodFill : Ghost.BadFill);
            Ghost.Band(line, -half - rim / 2, -half + rim / 2, good ? Ghost.GoodEdge : Ghost.BadEdge);
            Ghost.Band(line, half - rim / 2, half + rim / 2, good ? Ghost.GoodEdge : Ghost.BadEdge);
        }

        /// <summary>
        /// The lane tool: the lane picked, lit up for its whole length; the lane the pointer is
        /// on, lit more faintly; and, during a drag, the lane about to be added along the road.
        /// </summary>
        private void LanePicture()
        {
            if (laneDrag && grabMoved && tool == Lane)
            {
                string problem = Checked(laneTo, () => map.TryAddLane(laneFrom, laneTo, false));
                Vec[] line = map.LaneLine(laneFrom, laneTo);
                double length = line == null ? 0 : Ghost.Length(line);
                if (length > 1) LaneBand(line, problem == null);
                // Off the road altogether there is no lane to show, only the reason.
                if (length <= 1 && problem == null) return;
                Vec on;
                int road = map.NearestSegment(laneFrom, LaneReach, out on);
                Note = problem ?? Ft(length) + "   " + Money(length * EditableMap.LanePrice(map.Segments[road].Type));
                NoteBad = problem != null;
                if (problem == null) Tag(Money(length * EditableMap.LanePrice(map.Segments[road].Type)), Ft(length) + " of extra lane");
                return;
            }
            Vec[] held = picked ? map.LaneUnder(pickedAt) : null;
            if (picked && held == null) picked = false;
            if (hasPointer)
            {
                Vec[] under = map.LaneUnder(pointer);
                bool same = under != null && held != null && under.Length == held.Length && Dist(under[0], held[0]) < 0.01 && Dist(under[under.Length - 1], held[held.Length - 1]) < 0.01;
                if (under != null && !same) Ghost.Band(under, -NetworkBuilder.LaneW / 2, NetworkBuilder.LaneW / 2, 0x55FFFFFF);
            }
            if (held != null) LaneBand(held, true);
        }

        /// <summary>The move tool: what it would take hold of, or what it has hold of and where that is going.</summary>
        private void Handles()
        {
            if (grabLane)
            {
                LaneEndPicture();
                return;
            }
            if (grabNode < 0 && grabSegment < 0)
            {
                int node = HandleAt(pointer);
                if (node >= 0)
                {
                    Ghost.Pin(map.Nodes[node], true, true);
                    return;
                }
                Vec end, on, keep;
                bool first;
                if (LaneEndAt(pointer, out end, out on, out first, out keep))
                {
                    Ghost.Pin(end, true, true);
                    return;
                }
                int seg = BendableAt(pointer);
                if (seg >= 0) Ghost.Road(map.Trace(map.Segments[seg]), map.Segments[seg].Type.Width, map.Segments[seg].Type.OneWay, true);
                return;
            }
            if (!grabMoved)
            {
                if (grabNode >= 0) Ghost.Pin(map.Nodes[grabNode], true, true);
                return;
            }
            EditableMap moved = map.Clone();
            List<EditSegment> changed;
            string problem;
            if (grabNode >= 0)
            {
                moved.Shift(grabNode, grabTo);
                changed = moved.RoadsAt(grabNode);
                problem = Checked(grabTo, () => map.TryMove(grabNode, grabTo, false));
            }
            else
            {
                moved.Reshape(grabSegment, grabTo);
                changed = new List<EditSegment> { moved.Segments[grabSegment] };
                problem = Checked(grabTo, () => map.TryBend(grabSegment, grabTo, false));
            }
            foreach (EditSegment s in changed) Ghost.Road(moved.Trace(s), s.Type.Width, s.Type.OneWay, problem == null);
            Ghost.Pin(grabTo, problem == null, false);
            double change = moved.Cost() - map.Cost();
            Note = problem ?? (change >= 0 ? "+" : "−") + Money(Math.Abs(change));
            NoteBad = problem != null;
            if (problem == null) Tag(Note, change >= 0 ? "more road" : "less road");
        }

        /// <summary>An end of an added lane being slid along its road: the lane as it is, faintly, and as it would become.</summary>
        private void LaneEndPicture()
        {
            Vec[] now = map.LaneUnder(grabLaneOn);
            if (now != null) Ghost.Band(now, -NetworkBuilder.LaneW / 2, NetworkBuilder.LaneW / 2, 0x55FFFFFF);
            if (!grabMoved)
            {
                if (now != null) Ghost.Pin(grabLaneFirst ? now[0] : now[now.Length - 1], true, true);
                return;
            }
            string problem = Checked(grabTo, () => map.TryMoveLaneEnd(grabLaneOn, grabLaneFirst, grabTo, false));
            Vec[] line = map.LaneLine(grabLaneKeep, grabTo);
            double length = line == null ? 0 : Ghost.Length(line);
            if (length > 1)
            {
                LaneBand(line, problem == null);
                // The end in hand is whichever end of the new lane is not the one that stayed put.
                Ghost.Pin(Dist(line[0], grabLaneKeep) < Dist(line[line.Length - 1], grabLaneKeep) ? line[line.Length - 1] : line[0], problem == null, false);
            }
            Note = problem ?? Ft(length);
            NoteBad = problem != null;
            if (problem != null) return;
            EditableMap after = map.Clone();
            after.TryMoveLaneEnd(grabLaneOn, grabLaneFirst, grabTo, true);
            double change = after.Cost() - map.Cost();
            Tag((change >= 0 ? "+" : "−") + Money(Math.Abs(change)), Ft(length) + " of extra lane");
        }

        /// <summary>The roundabout about to be placed: its ring at its real width, with arrows going the way traffic will.</summary>
        private void RingPicture()
        {
            Vec at = OnGrid(pointer);
            if (!hasStart)
            {
                Ghost.Pin(at, true, false);
                Ghost.Stand(at, true, true);
                return;
            }
            double radius = Dist(at, start);
            string problem = Checked(at, () => map.TryAddRoundabout(start, radius, type, false));
            RoadType ring = EditableMap.RingType(type);
            if (radius > 1)
            {
                const int sides = 96;
                var circle = new Vec[sides + 1];
                for (int i = 0; i <= sides; i++) circle[i] = new Vec(start.X + Math.Cos(i * 2 * Math.PI / sides) * radius, start.Y + Math.Sin(i * 2 * Math.PI / sides) * radius);
                Ghost.Road(circle, ring.Width, true, problem == null);
            }
            Ghost.Pin(start, problem == null, false);
            Ghost.Stand(start, problem == null, false);
            Note = problem ?? Ft(radius * 2) + " across   " + Money(2 * Math.PI * radius * ring.CostPerMeter);
            NoteBad = problem != null;
            if (problem == null) Tag(Money(2 * Math.PI * radius * ring.CostPerMeter), "Roundabout, " + Ft(radius * 2) + " across");
        }

        /// <summary>The road about to be placed: straight to the pointer, or swept round to it.</summary>
        private void RoadPicture()
        {
            bool lined;
            double line;
            Vec end = OnRing(pointer, map.JoinPoint(Snapped(pointer, out lined, out line), hasStart ? start : (Vec?)null));
            Vec overWhole;
            if (WholeEndAt(pointer, out overWhole)) end = overWhole;
            Snap snap = map.SnapTo(end);
            bool caught = snap.Node >= 0 || snap.Segment >= 0;
            if (!hasStart)
            {
                // Over a lane partway along a road, with a one-way road in hand: that lane's arrow, to build off it.
                Vec lanePoint;
                double laneWay;
                int lane, lanes;
                EditableMap.LaneEnd knob;
                if (WholeEndAt(pointer, out overWhole) || LaneEndAt(pointer, true, out knob)) return;
                if (type.OneWay && snap.Node < 0 && snap.Segment >= 0 && map.LaneAt(pointer, out lanePoint, out laneWay, out lane, out lanes))
                {
                    Ghost.Knob(lanePoint, laneWay, true);
                    return;
                }
                Ghost.Pin(end, true, caught);
                Ghost.Stand(end, true, true);
                return;
            }
            double heading = 0;
            bool settled = shape != Curve || Heading(out heading);
            if (settled)
            {
                // If there is no room to make a junction where the pointer is, reach for the nearest one.
                Vec eased = Eased(end, heading);
                if (Dist(eased, end) > 0.05)
                {
                    end = eased;
                    caught = true;
                    lined = false;
                }
            }
            double reach = Dist(end, start);
            if (shape == Curve && !Heading(out heading))
            {
                // Pointing the road: a broken line from the start, with an arrowhead, and nothing built yet.
                if (reach > 1)
                {
                    double way = Math.Atan2(end.Y - start.Y, end.X - start.X);
                    double head = Ghost.AtLeast(2.6, 11);
                    var shaft = new[] { start, new Vec(end.X - Math.Cos(way) * head * 0.8, end.Y - Math.Sin(way) * head * 0.8) };
                    if (reach > head) Ghost.Dashes(shaft, Ghost.AtLeast(0.5, 2.6), Ghost.AtLeast(3, 9), Ghost.AtLeast(2.5, 7), Ghost.GoodEdge);
                    Ghost.Arrowhead(end, way, head, Ghost.GoodEdge);
                }
                Ghost.Pin(start, true, false);
                Ghost.Stand(start, true, false);
                return;
            }

            Vec via, from = start;
            bool behind;
            bool curved = Sweep(end, heading, out via, out behind);
            // The line the road will really follow, and where it will meet other roads: not always quite the line
            // pointed out, since a road that crosses close beside a corner or a junction goes through it.
            // Across water, a road asked for on the ground is a bridge.
            int level = Over(end, curved, via);
            int tieFrom, tieTo;
            Ties(end, out tieFrom, out tieTo);
            string problem = behind ? TooSharp : Checked(end, () => map.TryAdd(start, end, type, false, curved ? via : (Vec?)null, level, out checkedLine, out checkedMeets, tieFrom, tieTo));
            bool good = problem == null;

            if (shape == Curve && reach > 1)
            {
                // A faint line the way the road sets off, so it is plain why the curve leaves as it does.
                double far = Math.Min(Math.Max(reach * 0.6, Ghost.AtLeast(12, 40)), 140);
                Ghost.Dashes(new[] { start, new Vec(start.X + Math.Cos(heading) * far, start.Y + Math.Sin(heading) * far) }, Ghost.AtLeast(0.3, 1.5), Ghost.AtLeast(1.2, 4), Ghost.AtLeast(1.8, 6), Ghost.Faint);
            }
            if (lined && reach > 1)
            {
                // Pulled into line: a faint line running on past the end shows what it is lined up with.
                double far = reach + Ghost.AtLeast(30, 90);
                Ghost.Dashes(new[] { end, new Vec(start.X + Math.Cos(line) * far, start.Y + Math.Sin(line) * far) }, Ghost.AtLeast(0.3, 1.5), Ghost.AtLeast(1.2, 4), Ghost.AtLeast(1.8, 6), Ghost.Faint);
            }

            if (reach > 1 && !behind)
            {
                double length;
                double cost = EditableMap.CostOf(start, end, type, curved ? via : (Vec?)null, out length, level);
                int steps = curved ? (int)Geo.Clamp(length / 3, 16, 120) : 1;
                Vec[] path = EditableMap.Trace(start, end, curved, via, steps);
                bool laid = good && checkedLine != null && checkedLine.Length >= 2;
                // A curve is drawn as pointed out, which is finer than the map's own tracing of it, unless it has been pulled aside.
                // (One built on from a lane starts beside the point, where the lane is: the map's line shows that.)
                bool asPointed = !laid || (curved && tieFrom < 0 && tieTo < 0 && Aside(path, checkedMeets) < 0.5);
                Ghost.Road(asPointed ? path : checkedLine, type.Width, type.OneWay, good);
                if (laid)
                {
                    // An end that has been taken to a corner or a junction close by is pinned there.
                    if (Dist(checkedLine[0], start) > 0.5) from = checkedLine[0];
                    if (Dist(checkedLine[checkedLine.Length - 1], end) > 0.5)
                    {
                        end = checkedLine[checkedLine.Length - 1];
                        caught = true;
                    }
                }
                // A pad of light wherever it meets a road on the way: an intersection will be made there.
                // It says so too, unless there are so many that the words would crowd the picture.
                var pads = new List<Vec>();
                foreach (Vec place in laid ? checkedMeets : map.WouldCross(path, level))
                {
                    if (Dist(place, start) < EditableMap.SnapNode || Dist(place, end) < EditableMap.SnapNode) continue;
                    pads.Add(place);
                }
                foreach (Vec place in pads)
                {
                    Ghost.Pad(place, Ghost.AtLeast(type.Width * 0.5 + 1.2, 8), good);
                    if (good && pads.Count <= 3) Ghost.Say(place, "New intersection", true);
                }
                if (good)
                {
                    Note = (level > 0 ? "Bridge   " : level < 0 ? "Tunnel   " : "") + Ft(length);
                    if (curved && EditableMap.IsCurve(start, via, end)) Note += "   radius " + Ft(EditableMap.CurveRadius(start, via, end));
                    Note += "   " + Money(cost);
                    if (map.OnEdge(end) && !caught) Note += "   to the edge of the map";
                    string what = Ft(length) + " of " + Lower(RoadType.GroupNames[type.Group]) + (level > 0 ? " on a bridge" : level < 0 ? " in a tunnel" : "");
                    if (curved && EditableMap.IsCurve(start, via, end)) what += "  ·  radius " + Ft(EditableMap.CurveRadius(start, via, end));
                    if (map.OnEdge(end) && !caught) what += "  ·  to the edge of the map";
                    Tag(Money(cost), what);
                }
            }
            if (!good && reach > 1)
            {
                Note = problem;
                NoteBad = true;
            }
            Ghost.Pin(from, good || reach <= 1, false);
            Ghost.Pin(end, good || reach <= 1, caught);
            Ghost.Stand(from, good || reach <= 1, false);
            Ghost.Stand(end, good || reach <= 1, true);
            // Ending on a lane partway along a road, with a one-way road in hand: a ring on the lane it will join.
            double joinWay;
            Vec joinPoint;
            int joinLane, joinLanes;
            if (type.OneWay && JoinsLane(end, out joinWay) && map.LaneAt(pointer, out joinPoint, out joinWay, out joinLane, out joinLanes)) Ghost.Socket(joinPoint, true);
            // The price rides on the end in hand.
            TagAt = end;
            TagPlaced = Price.Length > 0;
        }
    }
}
