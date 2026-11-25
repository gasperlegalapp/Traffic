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
        public const int Road = 0, Bulldoze = 1, Replace = 2, Move = 3, Lane = 4;
        /// <summary>The shapes a road can be drawn in.</summary>
        public const int Straight = 0, Curve = 1, Roundabout = 2;

        /// <summary>The spacing of the grid that points can be kept to, meters (30 ft).</summary>
        public const double GridStep = 9.144;
        /// <summary>How near a straight road has to come to lining up with another before it is pulled into line, radians.</summary>
        public const double Magnet = 7 * Math.PI / 180;
        private const double Feet = 3.28084;

        private EditableMap map;
        private int tool, shape, level;
        private RoadType type = RoadType.All[0];
        private bool snapAngles = true, snapGrid;

        /// <summary>The map being built. Giving it a new one forgets any half-drawn road.</summary>
        public EditableMap Map
        {
            get { return map; }
            set
            {
                map = value;
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

        /// <summary>Keep new points on a 30 ft grid.</summary>
        public bool SnapGrid
        {
            get { return snapGrid; }
            set
            {
                snapGrid = value;
                stamp++;
            }
        }

        /// <summary>What the next click would do, as a picture to lay on the ground.</summary>
        public readonly Ghost Ghost = new Ghost();
        /// <summary>A few words to show beside the pointer: the length and price of the road, or why it cannot be built.</summary>
        public string Note = "";
        public bool NoteBad;
        /// <summary>Goes up each time the picture changes.</summary>
        public int Version { get; private set; }

        // The road being drawn.
        private bool hasStart;
        private Vec start;
        /// <summary>The way a road arriving at the start is heading, if there is one to carry on from.</summary>
        private bool hasFlow;
        private double flow;
        /// <summary>The way the player has pointed a curve to set off, when they have.</summary>
        private bool hasAim;
        private double aim;
        /// <summary>The player has chosen to point the curve by hand and not carry on from the road before.</summary>
        private bool ownAim;

        // What the move tool has hold of.
        private int grabNode = -1, grabSegment = -1;
        private Vec grabFrom, grabTo;
        private bool grabMoved;

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
        public bool Busy => hasStart || grabNode >= 0 || grabSegment >= 0;

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
            stamp++;
        }

        /// <summary>Go back one step: right-click, or Esc.</summary>
        public void Back()
        {
            stamp++;
            if (grabNode >= 0 || grabSegment >= 0)
            {
                grabNode = grabSegment = -1;
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
                if (tool == Lane) return hasStart ? "Click where the lane should end, on the same road. Right-click to stop." : "Click the side of a road where a lane should start. Click an added lane to remove it.";
                if (shape == Roundabout) return hasStart ? "Pull out to the size you want, then click. Right-click to go back." : "Click where the middle of the roundabout should be.";
                if (!hasStart)
                {
                    return map == null || map.Segments.Count == 0 ? "Click where the road starts. An open road end is where traffic comes in."
                        : "Click where the road starts. Touch or cross another road to make an intersection.";
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
            if (tool == Lane)
            {
                if (hasStart)
                {
                    refused = map.TryAddLane(start, p, true);
                    if (refused != null) return false;
                    hasStart = false;
                    return true;
                }
                // On an added lane, a click takes it away; anywhere else on a road, it starts a new one.
                if (map.ExtraLaneAt(p) != null)
                {
                    refused = map.TryRemoveLane(p, true);
                    return refused == null;
                }
                Vec on;
                if (map.NearestSegment(p, LaneReach, out on) < 0)
                {
                    refused = "Click the side of a road where the lane should start.";
                    return false;
                }
                start = p;
                hasStart = true;
                return false;
            }

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
            Vec end = map.SnapTo(Snapped(p, out lined, out line)).Point;
            if (!hasStart)
            {
                start = end;
                hasStart = true;
                hasAim = false;
                ownAim = false;
                hasFlow = map.FlowAt(start, out flow);
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
            Vec via = default(Vec);
            bool curved = false;
            if (shape == Curve)
            {
                curved = EditableMap.Bend(start, heading, end, out via);
                if (!curved && Behind(heading, end))
                {
                    refused = TooSharp;
                    return false;
                }
            }
            refused = map.TryAdd(start, end, type, true, curved ? via : (Vec?)null, level);
            if (refused != null) return false;
            // Carry on from where this stretch ended, heading the way it arrived.
            flow = curved && EditableMap.IsCurve(start, via, end) ? EditableMap.BendArrives(start, heading, end) : Math.Atan2(end.Y - start.Y, end.X - start.X);
            hasFlow = true;
            hasAim = false;
            ownAim = false;
            start = map.SnapTo(end).Point;
            return true;
        }

        private const string TooSharp = "The road cannot double back on itself. Right-click to point it another way.";

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

        /// <summary>The button went down with the move tool out. True if it took hold of a point or a road.</summary>
        public bool Press(Vec p)
        {
            if (tool != Move || map == null) return false;
            grabNode = HandleAt(p);
            grabSegment = grabNode >= 0 ? -1 : BendableAt(p);
            if (grabNode < 0 && grabSegment < 0) return false;
            grabFrom = grabTo = p;
            grabMoved = false;
            stamp++;
            return true;
        }

        /// <summary>The pointer moved with the button held.</summary>
        public void Drag(Vec p)
        {
            if (grabNode < 0 && grabSegment < 0) return;
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
            int node = grabNode, segment = grabSegment;
            grabNode = grabSegment = -1;
            stamp++;
            if (!grabMoved || (node < 0 && segment < 0)) return false;
            refused = node >= 0 ? map.TryMove(node, grabTo, true) : map.TryBend(segment, grabTo, true);
            return refused == null;
        }

        // ---------------------------------------------------------------- snapping

        private Vec OnGrid(Vec p)
        {
            return snapGrid ? new Vec(Math.Round(p.X / GridStep) * GridStep, Math.Round(p.Y / GridStep) * GridStep) : p;
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
            // From far out a road is only a few pixels wide, so reach for it by what the eye sees as near.
            int node = map.NodeAt(p, NodeReach);
            if (node >= 0) return map.Nodes[node];
            Vec on;
            if (map.NearestSegment(p, RoadReach, out on) >= 0) return on;
            p = OnGrid(p);
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
            if (snapGrid) along = Math.Round(along / GridStep) * GridStep;
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
            if (map == null) return;
            Marks();
            if (!hasPointer)
            {
                // Over the toolbar or a panel: only where the half-drawn road starts.
                if (hasStart && (tool == Road || tool == Lane)) Ghost.Pin(start, true, false);
                return;
            }
            if (tool == Bulldoze || tool == Replace) OverRoad();
            else if (tool == Move) Handles();
            else if (tool == Lane) LanePicture();
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

        /// <summary>The lane tool: the lane about to be added along the side of a road, or the added lane about to be removed.</summary>
        private void LanePicture()
        {
            Vec on;
            if (!hasStart)
            {
                Vec[] there = map.ExtraLaneAt(pointer);
                if (there != null)
                {
                    LaneBand(there, false);
                    Note = "Click to remove this lane.";
                }
                else if (map.NearestSegment(pointer, LaneReach, out on) >= 0) Ghost.Pin(on, true, true);
                return;
            }
            string problem = Checked(pointer, () => map.TryAddLane(start, pointer, false));
            Vec[] line = map.LaneLine(start, pointer);
            double length = line == null ? 0 : Ghost.Length(line);
            if (length > 1) LaneBand(line, problem == null);
            int road = map.NearestSegment(start, LaneReach, out on);
            if (road >= 0) Ghost.Pin(on, problem == null || length <= 1, false);
            // Off the road altogether there is no lane to show, only the reason.
            if (length <= 1 && (problem == null || Dist(pointer, start) < 5)) return;
            Note = problem ?? Ft(length) + "   " + Money(length * EditableMap.LanePrice(map.Segments[road].Type));
            NoteBad = problem != null;
        }

        /// <summary>The move tool: what it would take hold of, or what it has hold of and where that is going.</summary>
        private void Handles()
        {
            if (grabNode < 0 && grabSegment < 0)
            {
                int node = HandleAt(pointer);
                if (node >= 0)
                {
                    Ghost.Pin(map.Nodes[node], true, true);
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
        }

        /// <summary>The roundabout about to be placed: its ring at its real width, with arrows going the way traffic will.</summary>
        private void RingPicture()
        {
            Vec at = OnGrid(pointer);
            if (!hasStart)
            {
                Ghost.Pin(at, true, false);
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
            Note = problem ?? Ft(radius * 2) + " across   " + Money(2 * Math.PI * radius * ring.CostPerMeter);
            NoteBad = problem != null;
        }

        /// <summary>The road about to be placed: straight to the pointer, or swept round to it.</summary>
        private void RoadPicture()
        {
            bool lined;
            double line;
            Snap snap = map.SnapTo(Snapped(pointer, out lined, out line));
            Vec end = snap.Point;
            bool caught = snap.Node >= 0 || snap.Segment >= 0;
            if (!hasStart)
            {
                Ghost.Pin(end, true, caught);
                return;
            }
            double reach = Dist(end, start);
            double heading = 0;
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
                return;
            }

            Vec via = default(Vec);
            bool curved = shape == Curve && EditableMap.Bend(start, heading, end, out via);
            bool behind = shape == Curve && !curved && Behind(heading, end);
            string problem = behind ? TooSharp : Checked(end, () => map.TryAdd(start, end, type, false, curved ? via : (Vec?)null, level));
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
                Ghost.Road(path, type.Width, type.OneWay, good);
                // A ring wherever it crosses a road on the way: an intersection will be made there.
                foreach (Vec place in map.WouldCross(path, level))
                {
                    if (Dist(place, start) < EditableMap.SnapNode || Dist(place, end) < EditableMap.SnapNode) continue;
                    Ghost.Ring(place, Ghost.AtLeast(type.Width * 0.5 + 1.2, 8), Ghost.AtLeast(0.5, 2.4), good ? Ghost.GoodEdge : Ghost.BadEdge);
                }
                if (good)
                {
                    Note = (level > 0 ? "Bridge   " : level < 0 ? "Tunnel   " : "") + Ft(length);
                    if (curved && EditableMap.IsCurve(start, via, end)) Note += "   radius " + Ft(EditableMap.CurveRadius(start, via, end));
                    Note += "   " + Money(cost);
                }
            }
            if (!good && reach > 1)
            {
                Note = problem;
                NoteBad = true;
            }
            Ghost.Pin(start, good || reach <= 1, false);
            Ghost.Pin(end, good || reach <= 1, caught);
        }
    }
}
