using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Traffic.Sim
{
    /// <summary>
    /// A kind of road the player can draw: how many lanes run each way, whether a median
    /// divides them, its speed limit, how tight it may curve and what a meter of it costs.
    /// </summary>
    public sealed class RoadType
    {
        public const int Street = 0, Avenue = 1, Highway = 2;
        public static readonly string[] GroupNames = { "Street", "Avenue", "Highway" };

        /// <summary>A short code, used when a drawing is saved.</summary>
        public string Id;
        /// <summary>Street, Avenue or Highway.</summary>
        public int Group;
        /// <summary>Lanes running the way the road is drawn, and lanes running back. None back makes it one-way.</summary>
        public int Forward, Backward;
        /// <summary>Width of the strip of ground between the two directions, meters. 0 for none.</summary>
        public double Median;
        /// <summary>
        /// A lane down the middle, between the two directions, for turning left from: traffic
        /// about to turn left at the junction ahead moves into it and waits there, out of the
        /// way of the traffic going on.
        /// </summary>
        public bool CenterLane;
        /// <summary>
        /// The median gives way to a lane for turning left on the way into each intersection, as
        /// on most divided avenues: the grass stops short, and traffic about to turn left moves
        /// over into the gap and waits there. The median is a lane wide to make room for it.
        /// </summary>
        public bool Bays;
        /// <summary>Speed limit in miles per hour.</summary>
        public int Mph;
        /// <summary>The tightest curve it may be drawn with, meters.</summary>
        public double MinRadius = 22;
        /// <summary>Dollars for each meter built.</summary>
        public double CostPerMeter;
        /// <summary>A one-way road sits centered on the line it is drawn along. (A road from an older drawing lies to the right of it.)</summary>
        public bool Centered = true;

        public bool OneWay => Backward == 0;
        public int Lanes => Forward + Backward;
        /// <summary>From one edge of the pavement to the other, meters.</summary>
        public double Width => Lanes * NetworkBuilder.LaneW + (OneWay ? 0 : CenterLane ? NetworkBuilder.LaneW : Median);
        /// <summary>How far every lane is moved sideways from the line drawn: see NetworkBuilder.AddRoad.</summary>
        public double Inner => OneWay ? (Centered ? -Forward * NetworkBuilder.LaneW / 2 : 0) : CenterLane ? NetworkBuilder.LaneW / 2 : Median / 2;

        public string Name => GroupNames[Group] + ", " + (OneWay
            ? (Forward == 1 ? "1 lane" : Forward + " lanes") + " one-way"
            : (Forward == Backward ? (Forward == 1 ? "1 lane" : Forward + " lanes") + " each way" : Forward + " lanes one way and " + Backward + " the other") + (Bays ? ", with a median and left-turn lanes" : Median > 0 ? ", with a median" : CenterLane ? ", with a turning lane in the middle" : ""));

        /// <summary>
        /// What lies between the two directions of a two-way road: a painted line, a grass median,
        /// a lane for turning left from, or a median that makes way for such a lane at each intersection.
        /// </summary>
        public const int Line = 0, Grass = 1, TurnLane = 2, TurnBays = 3;
        public int Middle => OneWay ? Line : CenterLane ? TurnLane : Bays ? TurnBays : Median > 0 ? Grass : Line;

        /// <summary>The most lanes a road may have in one direction.</summary>
        public const int MostLanes = 4;
        // What each class of road is like, whatever its lanes: speed limit, tightest curve, price of a lane, width of a median.
        private static readonly int[] GroupMph = { 25, 40, 65 };
        private static readonly double[] GroupRadius = { 22, 30, 60 }, GroupLanePrice = { 1, 1.5, 3 };
        private const double MedianWidth = 3;
        private static readonly Dictionary<string, RoadType> made = new Dictionary<string, RoadType>();

        /// <summary>
        /// A road of the same class as another but with its own count of lanes each way: this is
        /// how a road gains or loses a single lane. It is one of the listed roads where one
        /// matches; otherwise it is made up, with a code that says what it is (its class, its
        /// lanes each way, and whether it has a median), so that it can be saved and read back.
        /// </summary>
        public static RoadType With(RoadType like, int forward, int backward, int middle = -1)
        {
            if (middle < 0) middle = like.Middle;
            if (backward == 0) middle = Line;
            int group = like.Group;
            foreach (RoadType t in All)
            {
                if (t.Group == group && t.Forward == forward && t.Backward == backward && t.Middle == middle) return t;
            }
            string id = "G" + group + "-" + forward + "-" + backward + (middle == Grass ? "-m" : middle == TurnLane ? "-t" : middle == TurnBays ? "-b" : "");
            RoadType one;
            if (made.TryGetValue(id, out one)) return one;
            one = new RoadType
            {
                Id = id, Group = group, Forward = forward, Backward = backward, CenterLane = middle == TurnLane, Bays = middle == TurnBays,
                Median = middle == Grass ? MedianWidth : middle == TurnBays ? NetworkBuilder.LaneW : 0,
                Mph = GroupMph[group], MinRadius = GroupRadius[group], CostPerMeter = (forward + backward + (middle == TurnLane || middle == TurnBays ? 1 : 0)) * GroupLanePrice[group],
            };
            made[id] = one;
            return one;
        }

        /// <summary>
        /// Every road that has a short code of its own. The toolbar offers some of them (see
        /// Toolbar); the rest are reached by changing a road's lanes or its middle, or are kept
        /// so that older drawings still load.
        /// </summary>
        public static readonly RoadType[] All =
        {
            new RoadType { Id = "s2", Group = Street, Forward = 1, Backward = 1, Mph = 25, MinRadius = 22, CostPerMeter = 2 },
            new RoadType { Id = "s1", Group = Street, Forward = 1, Mph = 25, MinRadius = 22, CostPerMeter = 1 },
            new RoadType { Id = "s1x2", Group = Street, Forward = 2, Mph = 25, MinRadius = 22, CostPerMeter = 2 },
            new RoadType { Id = "s4", Group = Street, Forward = 2, Backward = 2, Mph = 25, MinRadius = 22, CostPerMeter = 4 },
            new RoadType { Id = "s4t", Group = Street, Forward = 2, Backward = 2, CenterLane = true, Mph = 25, MinRadius = 22, CostPerMeter = 5 },
            new RoadType { Id = "s4m", Group = Street, Forward = 2, Backward = 2, Median = 3, Mph = 25, MinRadius = 22, CostPerMeter = 4.5 },
            new RoadType { Id = "a4p", Group = Avenue, Forward = 2, Backward = 2, Mph = 40, MinRadius = 30, CostPerMeter = 5 },
            new RoadType { Id = "h4d", Group = Highway, Forward = 2, Backward = 2, Median = 3, Mph = 65, MinRadius = 60, CostPerMeter = 13 },
            new RoadType { Id = "a2p", Group = Avenue, Forward = 1, Backward = 1, Mph = 40, MinRadius = 30, CostPerMeter = 2.5 },
            new RoadType { Id = "a2", Group = Avenue, Forward = 1, Backward = 1, Median = 3, Mph = 40, MinRadius = 30, CostPerMeter = 3 },
            new RoadType { Id = "a4", Group = Avenue, Forward = 2, Backward = 2, Median = 3, Mph = 40, MinRadius = 30, CostPerMeter = 6 },
            new RoadType { Id = "a1", Group = Avenue, Forward = 1, Mph = 40, MinRadius = 30, CostPerMeter = 2 },
            new RoadType { Id = "a1x2", Group = Avenue, Forward = 2, Mph = 40, MinRadius = 30, CostPerMeter = 4 },
            new RoadType { Id = "a4t", Group = Avenue, Forward = 2, Backward = 2, CenterLane = true, Mph = 40, MinRadius = 30, CostPerMeter = 7.5 },
            new RoadType { Id = "a4b", Group = Avenue, Forward = 2, Backward = 2, Median = NetworkBuilder.LaneW, Bays = true, Mph = 40, MinRadius = 30, CostPerMeter = 7.5 },
            new RoadType { Id = "h1", Group = Highway, Forward = 1, Mph = 65, MinRadius = 60, CostPerMeter = 3 },
            new RoadType { Id = "h2", Group = Highway, Forward = 2, Mph = 65, MinRadius = 60, CostPerMeter = 6 },
            new RoadType { Id = "h3", Group = Highway, Forward = 3, Mph = 65, MinRadius = 60, CostPerMeter = 9 },
            new RoadType { Id = "h4", Group = Highway, Forward = 4, Mph = 65, MinRadius = 60, CostPerMeter = 12 },
        };

        private static RoadType[] toolbar;

        /// <summary>
        /// The roads on offer, in the order the toolbar shows them. A street and an avenue each
        /// come as one lane one-way, two lanes one-way, one lane each way, and two lanes each
        /// way with a painted line, a turning lane or a grass median between them, which is the
        /// list the player asked for. A highway comes one-way with one to four lanes, and as two
        /// lanes each way with a median. Everything else is made by changing a road's lanes.
        /// </summary>
        public static RoadType[] Toolbar
        {
            get
            {
                if (toolbar != null) return toolbar;
                string[] ids = { "s1", "s1x2", "s2", "s4", "s4t", "s4m", "a1", "a1x2", "a2p", "a4p", "a4t", "a4", "h1", "h2", "h3", "h4", "h4d" };
                toolbar = new RoadType[ids.Length];
                for (int i = 0; i < ids.Length; i++) toolbar[i] = ById(ids[i]);
                return toolbar;
            }
        }

        public static RoadType ById(string id)
        {
            foreach (RoadType t in All) if (t.Id == id) return t;
            // A made-up road: G, its class, and its lanes each way, with -m for a median, -t for a turning lane and -b for a median with left-turn lanes.
            string[] f = id.Split('-');
            int group, forward, backward;
            if (f.Length < 3 || f[0].Length != 2 || f[0][0] != 'G' || !int.TryParse(f[0].Substring(1), out group) || !int.TryParse(f[1], out forward) || !int.TryParse(f[2], out backward)) return null;
            if (group < 0 || group > Highway || forward < 1 || forward > MostLanes || backward < 0 || backward > MostLanes) return null;
            return With(new RoadType { Group = group }, forward, backward, f.Length > 3 && f[3] == "m" ? Grass : f.Length > 3 && f[3] == "t" ? TurnLane : f.Length > 3 && f[3] == "b" ? TurnBays : Line);
        }

        /// <summary>
        /// A road given only by its lane count (1 one-way, 2, or 4) and speed limit, the way
        /// roads were chosen before there were types. Drawings saved then are made of these.
        /// </summary>
        public static RoadType Plain(int lanes, int mph)
        {
            return new RoadType
            {
                Id = lanes + "@" + mph, Group = mph <= 25 ? Street : mph <= 45 ? Avenue : Highway,
                Forward = lanes == 4 ? 2 : 1, Backward = lanes == 1 ? 0 : lanes == 4 ? 2 : 1,
                Mph = mph, MinRadius = 22, CostPerMeter = lanes, Centered = false,
            };
        }
    }

    /// <summary>A named place at the edge of a level's map, where one of its roads comes in.</summary>
    public sealed class MapEnd
    {
        public string Name;
        /// <summary>Where the road runs off the map.</summary>
        public Vec At;
        /// <summary>A color for its label, as 0xRRGGBB.</summary>
        public int Color;
    }

    /// <summary>One drawn stretch of road between two points, straight or curved.</summary>
    public sealed class EditSegment
    {
        public int A, B;
        public RoadType Type;
        /// <summary>1 is a single one-way lane from A to B; 2 is one lane each way; 4 is two lanes each way.</summary>
        public int Lanes => Type.Lanes;
        /// <summary>Speed limit in miles per hour: the one posted on it, or the usual one for its kind of road.</summary>
        public int Mph => Limit > 0 ? Limit : Type.Mph;
        /// <summary>The speed limit the player has posted, miles per hour; 0 leaves the usual limit for its kind of road.</summary>
        public int Limit;
        /// <summary>A curved stretch is part of a circle, from A to B, passing through Mid halfway along.</summary>
        public bool Curved;
        public Vec Mid;
        /// <summary>0 on the ground; 1 to 3 for a bridge of that many levels; -1 to -3 for a tunnel that deep.</summary>
        public int Level;
        /// <summary>A road that came with the level: it cannot be removed and costs nothing.</summary>
        public bool Locked;
        /// <summary>Part of a roundabout. Where a road meets one there are no lights or signs: traffic going round has the right of way.</summary>
        public bool Ring;
        /// <summary>Lanes added along parts of it.</summary>
        public List<ExtraLane> Extras = new List<ExtraLane>();
        /// <summary>
        /// The ways each lane may go at the intersection at either end, where the player has
        /// said: for the lanes arriving at A and at B, one number a lane counting out from the
        /// middle of the road, made of 1 for left, 2 for straight on and 4 for right. 0, or no
        /// list at all, leaves the lane to be worked out.
        /// </summary>
        public int[] TurnsAtA, TurnsAtB;
        /// <summary>
        /// What the traffic arriving at each end meets there, where the player has said: one of
        /// EditableMap.AsLight, AsArrows, AsStop, AsYield or AsNoSign. 0 leaves it to be worked out.
        /// </summary>
        public int ControlAtA, ControlAtB;
        /// <summary>
        /// Where a one-way road carries on as a one-way road with more lanes or fewer, which of
        /// the wider road's lanes the narrower road's lanes line up with: the number of the
        /// first of them, counting from 1 at the left of the traffic. It is kept on the end of a
        /// piece at the place where the two meet; 0 leaves it to be worked out (the leftmost).
        /// This is how a road is built on from one lane of another.
        /// </summary>
        public int LaneAtA, LaneAtB;
    }

    /// <summary>
    /// What the player can ask for at an intersection, as seen from one of the roads that lead
    /// to it. The signs follow the Ohio Revised Code: a stop sign means stop at the line and
    /// then give way to anything in the intersection or close enough to be a danger, and a
    /// yield sign means slow down and stop only if that is needed (section 4511.43); roads
    /// that enter a through street face one or the other (4511.65); and with no signs at all,
    /// of two drivers who arrive together the one on the left gives way (4511.41).
    /// </summary>
    public enum Control
    {
        /// <summary>Left to the game: stop signs all round where only 25 mph streets meet, a light otherwise.</summary>
        Automatic,
        Light,
        /// <summary>A light with left-turn arrows.</summary>
        LightWithArrows,
        AllWayStop,
        /// <summary>A stop sign on this road, and on the same road across the intersection; the cross street has none.</summary>
        Stop,
        /// <summary>A yield sign likewise.</summary>
        Yield,
        /// <summary>This road is the through street: the cross street has the stop signs.</summary>
        RightOfWay,
        /// <summary>No signs or light at all.</summary>
        NoSigns,
    }

    /// <summary>
    /// An extra lane along part of a stretch of road, outside its ordinary lanes: room to pull
    /// over, a lane to pass in, or, where it runs to the end of the road, a lane to turn from.
    /// </summary>
    public sealed class ExtraLane
    {
        /// <summary>True on the side of the traffic that runs from the stretch's A end to its B end; false on the other side.</summary>
        public bool Forward;
        /// <summary>Where it starts and ends, as shares of the way from A to B (0 to 1).</summary>
        public double From, To;
    }

    /// <summary>A place where the roads as drawn cannot work, and why.</summary>
    public struct Fault
    {
        public Vec At;
        public string What;
    }

    /// <summary>Where a point lands when it is dropped on the map.</summary>
    public struct Snap
    {
        public Vec Point;
        /// <summary>Index of the road end or intersection it snapped to, or -1.</summary>
        public int Node;
        /// <summary>Index of the road it landed on, or -1, and how far along that road (0 to 1).</summary>
        public int Segment;
        public double T;
    }

    /// <summary>
    /// A road map the player draws: points joined by stretches of road, straight or curved,
    /// at any angle. Drawing across an existing road makes an intersection, and drawing to
    /// or from the middle of one splits it. ToWorld turns the drawing into a network that
    /// traffic can drive on, choosing stop signs or traffic lights from the roads that meet.
    /// </summary>
    public sealed class EditableMap
    {
        /// <summary>How close a click has to be to an existing point, or to a road, to land on it.</summary>
        public const double SnapNode = 13;
        public const double SnapRoad = 9;
        /// <summary>
        /// The sharpest angle allowed between two roads at one point, radians. It is as low as an
        /// intersection can sensibly be built: a road drawn across the corner of a block crosses
        /// one side at a slant, and the intersection there grows to fit.
        /// </summary>
        public const double MinAngle = 30 * Math.PI / 180;
        /// <summary>The tightest curve allowed, as a turning radius in meters.</summary>
        public const double MinRadius = 22;
        /// <summary>The furthest round one curve may turn: two thirds of a circle.</summary>
        public const double MaxTurn = 240 * Math.PI / 180;
        public const int MaxArms = 4;
        public static readonly int[] LaneChoices = { 1, 2, 4 };
        public static readonly int[] SpeedChoices = { 25, 40, 55, 65 };

        /// <summary>How many straight pieces stand in for a curve when measuring and testing it.</summary>
        private const int CurveSteps = 32;

        public List<Vec> Nodes = new List<Vec>();
        public List<EditSegment> Segments = new List<EditSegment>();
        /// <summary>Named places at the edge of the map, in a level: where its roads come in.</summary>
        public List<MapEnd> Ends = new List<MapEnd>();
        /// <summary>
        /// Roads may only be built within this distance of the middle, east-west and north-south:
        /// the edge of the map. A road that runs to the edge brings traffic in and lets it out; a
        /// road that stops short of it is a dead end, where traffic turns round. 0 for a map with
        /// no edge, on which any open road end is where traffic comes and goes.
        /// </summary>
        public double Bound;

        /// <summary>Whether a point is on the edge of the map.</summary>
        public bool OnEdge(Vec p)
        {
            return Bound > 0 && (Math.Abs(p.X) > Bound - 1 || Math.Abs(p.Y) > Bound - 1);
        }

        /// <summary>
        /// Whether a point where a single road ends is a dead end: on a map with an edge, any
        /// road end that is not on the edge and is not one of a level's named ways in.
        /// </summary>
        private bool DeadEndAt(int node)
        {
            if (Bound <= 0 || OnEdge(Nodes[node])) return false;
            foreach (MapEnd end in Ends) if (Dist(end.At, Nodes[node]) < 2) return false;
            return true;
        }

        public EditableMap Clone()
        {
            var copy = new EditableMap();
            copy.Nodes.AddRange(Nodes);
            copy.Ends = Ends;
            copy.Bound = Bound;
            copy.Waters = Waters;
            foreach (EditSegment s in Segments)
            {
                var piece = new EditSegment { A = s.A, B = s.B, Type = s.Type, Curved = s.Curved, Mid = s.Mid, Ring = s.Ring, Level = s.Level, Locked = s.Locked, Limit = s.Limit, ControlAtA = s.ControlAtA, ControlAtB = s.ControlAtB, LaneAtA = s.LaneAtA, LaneAtB = s.LaneAtB };
                foreach (ExtraLane x in s.Extras) piece.Extras.Add(new ExtraLane { Forward = x.Forward, From = x.From, To = x.To });
                piece.TurnsAtA = s.TurnsAtA == null ? null : (int[])s.TurnsAtA.Clone();
                piece.TurnsAtB = s.TurnsAtB == null ? null : (int[])s.TurnsAtB.Clone();
                copy.Segments.Add(piece);
            }
            return copy;
        }

        // ---------------------------------------------------------------- shapes

        private static Vec Lerp(Vec a, Vec b, double t)
        {
            return new Vec(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        private static double Dist(Vec a, Vec b)
        {
            return Geo.Hypot(a.X - b.X, a.Y - b.Y);
        }

        /// <summary>
        /// The circle through three points, as the arc that runs from a through via to b: its
        /// center and radius, the angle it starts at and how far round it turns (positive is
        /// counter-clockwise). False if the points are in a line, which has no circle.
        /// </summary>
        private static bool Arc(Vec a, Vec via, Vec b, out Vec center, out double radius, out double start, out double sweep)
        {
            center = default(Vec);
            radius = start = sweep = 0;
            double d = 2 * (a.X * (via.Y - b.Y) + via.X * (b.Y - a.Y) + b.X * (a.Y - via.Y));
            double chord = Dist(a, b);
            if (Math.Abs(d) < 1e-9 * Math.Max(1, chord * chord)) return false;
            double a2 = a.X * a.X + a.Y * a.Y, v2 = via.X * via.X + via.Y * via.Y, b2 = b.X * b.X + b.Y * b.Y;
            center = new Vec((a2 * (via.Y - b.Y) + v2 * (b.Y - a.Y) + b2 * (a.Y - via.Y)) / d, (a2 * (b.X - via.X) + v2 * (a.X - b.X) + b2 * (via.X - a.X)) / d);
            radius = Dist(center, a);
            if (radius > 1e6) return false;
            start = Math.Atan2(a.Y - center.Y, a.X - center.X);
            sweep = Math.Atan2(b.Y - center.Y, b.X - center.X) - start;
            // Going a, via, b is counter-clockwise when b lies to the left of the line from a to via.
            bool left = (via.X - a.X) * (b.Y - a.Y) - (via.Y - a.Y) * (b.X - a.X) > 0;
            if (left) while (sweep <= 0) sweep += 2 * Math.PI;
            else while (sweep >= 0) sweep -= 2 * Math.PI;
            return true;
        }

        /// <summary>A point on the round curve from p0 to p1 that passes through via: t runs 0 to 1.</summary>
        public static Vec CurvePoint(Vec p0, Vec via, Vec p1, double t)
        {
            Vec c;
            double r, start, sweep;
            if (!Arc(p0, via, p1, out c, out r, out start, out sweep)) return Lerp(p0, p1, t);
            return new Vec(c.X + Math.Cos(start + sweep * t) * r, c.Y + Math.Sin(start + sweep * t) * r);
        }

        /// <summary>How far round a curve through three points turns, in radians. A straight line turns 0.</summary>
        public static double CurveTurn(Vec p0, Vec via, Vec p1)
        {
            Vec c;
            double r, start, sweep;
            return Arc(p0, via, p1, out c, out r, out start, out sweep) ? Math.Abs(sweep) : 0;
        }

        /// <summary>
        /// The line a stretch of road follows, as points from one end to the other. A curved
        /// stretch is the part of a circle from p0 to p1 that passes through via.
        /// </summary>
        public static Vec[] Trace(Vec p0, Vec p1, bool curved, Vec via, int steps = CurveSteps)
        {
            Vec c;
            double r, start, sweep;
            if (!curved || !Arc(p0, via, p1, out c, out r, out start, out sweep)) return new[] { p0, p1 };
            var pts = new Vec[steps + 1];
            for (int i = 0; i <= steps; i++) pts[i] = new Vec(c.X + Math.Cos(start + sweep * i / steps) * r, c.Y + Math.Sin(start + sweep * i / steps) * r);
            // The ends are exactly the points given, whatever the rounding.
            pts[0] = p0;
            pts[steps] = p1;
            return pts;
        }

        public Vec[] Trace(EditSegment s)
        {
            return Trace(Nodes[s.A], Nodes[s.B], s.Curved, s.Mid);
        }

        private Vec PointAt(EditSegment s, double t)
        {
            return s.Curved ? CurvePoint(Nodes[s.A], s.Mid, Nodes[s.B], t) : Lerp(Nodes[s.A], Nodes[s.B], t);
        }

        private static double LengthOf(Vec[] pts)
        {
            double len = 0;
            for (int i = 1; i < pts.Length; i++) len += Dist(pts[i - 1], pts[i]);
            return len;
        }

        /// <summary>Length of the part of a stretch between two positions along it.</summary>
        private double LengthBetween(EditSegment s, double t0, double t1)
        {
            if (!s.Curved) return Dist(Nodes[s.A], Nodes[s.B]) * Math.Abs(t1 - t0);
            double len = 0;
            Vec prev = PointAt(s, t0);
            for (int i = 1; i <= CurveSteps; i++)
            {
                Vec next = PointAt(s, t0 + (t1 - t0) * i / CurveSteps);
                len += Dist(prev, next);
                prev = next;
            }
            return len;
        }

        /// <summary>The closest spot on a traced line to a point: how far along (0 to 1) and how far away.</summary>
        private static double Project(Vec[] pts, Vec p, out double t, out Vec on)
        {
            double best = double.PositiveInfinity;
            t = 0;
            on = pts[0];
            int steps = pts.Length - 1;
            for (int i = 0; i < steps; i++)
            {
                Vec a = pts[i], b = pts[i + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len2 = dx * dx + dy * dy;
                double f = len2 < 1e-12 ? 0 : Geo.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
                var q = new Vec(a.X + dx * f, a.Y + dy * f);
                double d = Dist(q, p);
                if (d < best)
                {
                    best = d;
                    t = (i + f) / steps;
                    on = q;
                }
            }
            return best;
        }

        /// <summary>The tightest turn in a traced line, as a radius.</summary>
        private static double TightestRadius(Vec[] pts)
        {
            double r = double.PositiveInfinity;
            for (int i = 1; i < pts.Length - 1; i++) r = Math.Min(r, Geo.Circumradius(pts[i - 1], pts[i], pts[i + 1]));
            return r;
        }

        /// <summary>The direction a stretch sets off in from one of its ends.</summary>
        private double HeadingFrom(EditSegment s, int node)
        {
            Vec from = Nodes[node];
            // A curve sets off along the circle, not toward its far end.
            Vec toward = s.Curved ? PointAt(s, s.A == node ? 0.02 : 0.98) : Nodes[s.A == node ? s.B : s.A];
            return Math.Atan2(toward.Y - from.Y, toward.X - from.X);
        }

        // ---------------------------------------------------------------- looking things up

        /// <summary>Find what a point would land on: an existing point, a road, or open ground.</summary>
        public Snap SnapTo(Vec p)
        {
            int node = -1;
            double best = SnapNode;
            for (int i = 0; i < Nodes.Count; i++)
            {
                double d = Dist(Nodes[i], p);
                if (d < best)
                {
                    best = d;
                    node = i;
                }
            }
            if (node >= 0) return new Snap { Point = Nodes[node], Node = node, Segment = -1 };

            Vec on;
            double t;
            int seg = NearestSegment(p, SnapRoad, out on, out t);
            if (seg >= 0) return new Snap { Point = on, Node = -1, Segment = seg, T = t };
            return new Snap { Point = p, Node = -1, Segment = -1 };
        }

        /// <summary>The road nearest a point, if within reach, and the closest spot on it.</summary>
        public int NearestSegment(Vec p, double reach, out Vec on)
        {
            double t;
            return NearestSegment(p, reach, out on, out t);
        }

        private int NearestSegment(Vec p, double reach, out Vec on, out double t)
        {
            int seg = -1;
            double best = reach;
            on = p;
            t = 0;
            for (int i = 0; i < Segments.Count; i++)
            {
                double ti;
                Vec q;
                double d = Project(Trace(Segments[i]), p, out ti, out q);
                if (d < best)
                {
                    best = d;
                    seg = i;
                    on = q;
                    t = ti;
                }
            }
            return seg;
        }

        // ---------------------------------------------------------------- drawing

        /// <summary>
        /// Draw a road from p to q: straight, or, given a third point, a round curve that passes
        /// through it. Returns null if it was added, or a short reason why it cannot be. With
        /// commit false nothing is changed: use that to preview.
        /// </summary>
        public string TryAdd(Vec p, Vec q, RoadType type, bool commit, Vec? through = null, int level = 0)
        {
            Vec[] line;
            List<Vec> meets;
            return TryAdd(p, q, type, commit, through, level, out line, out meets);
        }

        /// <summary>
        /// The same, and when the road can be built, the line it will follow and the places along
        /// it where it will meet roads that are already there. The line is not always the one
        /// asked for: a road that would cross too close beside a corner, a junction or the end of
        /// a road for a junction of its own to fit goes through that point instead.
        /// </summary>
        public string TryAdd(Vec p, Vec q, RoadType type, bool commit, Vec? through, int level, out Vec[] line, out List<Vec> meets)
        {
            line = null;
            meets = null;
            EditableMap work = null;
            string error = null;
            // As drawn first. If there is no room for that, try with whatever lands close beside a point of the map going
            // through the point: first only what is very close, then from further off.
            foreach (double beside in new[] { 0.0, 24, 40 })
            {
                EditableMap attempt = Clone();
                // Make sure the result can actually be built before accepting it.
                string problem = attempt.Add(p, q, type, through, level, beside) ?? attempt.Unbuildable(this);
                if (problem == null)
                {
                    work = attempt;
                    break;
                }
                if (error == null) error = problem;
                if (level != 0 || (problem != NoRoom && problem != TooSharpCorner && problem != TooNearCorner)) break;
            }
            if (work == null) return error;
            var along = new List<Vec>();
            meets = new List<Vec>();
            for (int i = work.Segments.Count - work.laid; i < work.Segments.Count; i++)
            {
                Vec[] part = work.Trace(work.Segments[i]);
                for (int k = along.Count == 0 ? 0 : 1; k < part.Length; k++) along.Add(part[k]);
                if (i + 1 < work.Segments.Count) meets.Add(work.Nodes[work.Segments[i].B]);
            }
            line = along.ToArray();
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        /// <summary>How many pieces the road last added was laid in: they are the last of the segments.</summary>
        private int laid;

        /// <summary>The same, for a road given by its lane count (1 one-way, 2, or 4) and speed limit.</summary>
        public string TryAdd(Vec p, Vec q, int lanes, int mph, bool commit, Vec? through = null, int level = 0)
        {
            return TryAdd(p, q, RoadType.Plain(lanes, mph), commit, through, level);
        }

        /// <summary>What a road from p to q would cost, and how long it would be, before any snapping.</summary>
        public static double CostOf(Vec p, Vec q, RoadType type, Vec? through, out double length, int level = 0)
        {
            length = LengthOf(Trace(p, q, through.HasValue, through ?? default(Vec)));
            return length * type.CostPerMeter * CostTimes(level);
        }

        // ---------------------------------------------------------------- flowing curves

        /// <summary>
        /// The round curve that leaves p heading a given way and ends at q, as the point halfway
        /// round it: pass that to TryAdd as the point to go through. False when q is dead ahead,
        /// which is a straight road, or dead behind, which no curve reaches.
        /// </summary>
        public static bool Bend(Vec p, double heading, Vec q, out Vec via)
        {
            via = default(Vec);
            double dx = q.X - p.X, dy = q.Y - p.Y, chord = Geo.Hypot(dx, dy);
            if (chord < 1e-6) return false;
            // How far round from the way the road sets off to the straight line to q: the curve turns twice that.
            double off = Math.Atan2(dy, dx) - heading;
            off = Math.Atan2(Math.Sin(off), Math.Cos(off));
            if (Math.Abs(off) > Math.PI - 0.01) return false;
            // The curve swings out to the right of the straight line when it turns left, and the other way about.
            double swing = chord / 2 * Math.Tan(off / 2);
            if (Math.Abs(swing) < 1e-6) return false;
            via = new Vec((p.X + q.X) / 2 + dy / chord * swing, (p.Y + q.Y) / 2 - dx / chord * swing);
            return true;
        }

        /// <summary>The way a road is heading when it arrives at q, having left p heading a given way on one round curve.</summary>
        public static double BendArrives(Vec p, double heading, Vec q)
        {
            double off = Math.Atan2(q.Y - p.Y, q.X - p.X) - heading;
            return heading + 2 * Math.Atan2(Math.Sin(off), Math.Cos(off));
        }

        /// <summary>
        /// Whether a road from p0 to p1 through via is a curve at all: a point this close to the
        /// straight line between the ends just makes a straight road. It is kept small, so that a
        /// gentle curve drawn to carry on smoothly from another road stays a curve.
        /// </summary>
        public static bool IsCurve(Vec p0, Vec via, Vec p1)
        {
            double t;
            Vec on;
            return Project(new[] { p0, p1 }, via, out t, out on) > CurveSlack;
        }

        private const double CurveSlack = 0.5;

        /// <summary>The radius of the round curve from p0 to p1 through via, or 0 if the three are in a line.</summary>
        public static double CurveRadius(Vec p0, Vec via, Vec p1)
        {
            Vec c;
            double r, start, sweep;
            return Arc(p0, via, p1, out c, out r, out start, out sweep) ? r : 0;
        }

        /// <summary>
        /// If exactly one road ends at a point, the way it is heading as it gets there: a road
        /// drawn on from that point in the same direction carries on from it smoothly.
        /// </summary>
        public bool FlowAt(Vec p, out double heading)
        {
            heading = 0;
            Snap snap = SnapTo(p);
            if (snap.Node < 0) return false;
            EditSegment only = null;
            foreach (EditSegment s in Segments)
            {
                if (s.A != snap.Node && s.B != snap.Node) continue;
                if (only != null) return false;
                only = s;
            }
            if (only == null) return false;
            heading = HeadingFrom(only, snap.Node) + Math.PI;
            return true;
        }

        /// <summary>
        /// The directions the roads at a point run in, for lining a new road up with them: each
        /// road at a point where roads meet, or both ways along a road the point lies on. Empty on open ground.
        /// </summary>
        public List<double> HeadingsAt(Vec p)
        {
            var headings = new List<double>();
            Snap snap = SnapTo(p);
            if (snap.Node >= 0)
            {
                foreach (EditSegment s in Segments) if (s.A == snap.Node || s.B == snap.Node) headings.Add(HeadingFrom(s, snap.Node));
            }
            else if (snap.Segment >= 0)
            {
                EditSegment s = Segments[snap.Segment];
                Vec before = PointAt(s, Math.Max(snap.T - 0.01, 0)), after = PointAt(s, Math.Min(snap.T + 0.01, 1));
                double along = Math.Atan2(after.Y - before.Y, after.X - before.X);
                headings.Add(along);
                headings.Add(along + Math.PI);
            }
            return headings;
        }

        /// <summary>
        /// Where a road drawn along a line on the ground would cross roads already there: the
        /// places that would become intersections. A bridge or tunnel passes over or under instead.
        /// </summary>
        public List<Vec> WouldCross(Vec[] path, int level)
        {
            var places = new List<Vec>();
            if (level != 0) return places;
            foreach (EditSegment s in Segments)
            {
                if (s.Level != 0) continue;
                foreach (Crossing x in Crossings(path, Trace(s))) places.Add(x.At);
            }
            return places;
        }

        // ---------------------------------------------------------------- extra lanes

        /// <summary>
        /// Where a point lies on the roads, for work on their lanes: the piece, how far along it
        /// (0 to 1), whether it is on the side of the traffic running from A to B, and how far
        /// out it is from the line the road is drawn along, to the right of the traffic on that
        /// side (on a one-way road, which lies across its line, that is negative on the left half).
        /// </summary>
        private bool OnRoad(Vec p, out int segment, out double t, out bool forward, out double across)
        {
            Vec on;
            forward = true;
            across = 0;
            segment = NearestSegment(p, SnapRoad * 2.2, out on, out t);
            if (segment < 0) return false;
            EditSegment s = Segments[segment];
            Vec a = PointAt(s, Math.Max(t - 0.01, 0)), b = PointAt(s, Math.Min(t + 0.01, 1));
            // To the right of the way from A to B is the side of the traffic going that way.
            bool right = (b.X - a.X) * (p.Y - on.Y) - (b.Y - a.Y) * (p.X - on.X) <= 0;
            forward = s.Type.OneWay || right;
            across = Dist(p, on) * (s.Type.OneWay && !right ? -1 : 1);
            return true;
        }

        /// <summary>
        /// The lane of a road that is under a point, as the line down its middle: the whole of
        /// it, from one junction or road end to the next, or for an added lane its own length.
        /// Null if the point is not on a road.
        /// </summary>
        public Vec[] LaneUnder(Vec p)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return null;
            EditSegment s = Segments[seg];
            int lanes = s.Type.OneWay || forward ? s.Type.Forward : s.Type.Backward;
            if ((s.Type.CenterLane || s.Type.Bays) && across < s.Type.Inner)
            {
                // The turning lane down the middle, or the median that makes way for one: the line the road is drawn along.
                foreach (Run run in Runs())
                {
                    if (!run.Pieces.Contains(s)) continue;
                    var middle = new List<KeyValuePair<EditSegment, ExtraLane>>();
                    for (int i = 0; i < run.Pieces.Count; i++) middle.Add(new KeyValuePair<EditSegment, ExtraLane>(run.Pieces[i], new ExtraLane { Forward = !run.Turned[i], From = 0, To = 1 }));
                    return LaneLine(middle, 0, true);
                }
            }
            if (across > LaneEdge(s.Type, forward))
            {
                // Out beyond the ordinary lanes: an added lane, if there is one here, or else the outside lane.
                List<KeyValuePair<EditSegment, ExtraLane>> added = ExtraParts(p);
                if (added != null) return LaneLine(added);
            }
            int slot = (int)Geo.Clamp(Math.Floor((across - s.Type.Inner) / NetworkBuilder.LaneW), 0, lanes - 1);
            foreach (Run run in Runs())
            {
                int index = run.Pieces.IndexOf(s);
                if (index < 0) continue;
                bool withRun = forward != run.Turned[index];
                var parts = new List<KeyValuePair<EditSegment, ExtraLane>>();
                for (int i = 0; i < run.Pieces.Count; i++) parts.Add(new KeyValuePair<EditSegment, ExtraLane>(run.Pieces[i], new ExtraLane { Forward = withRun != run.Turned[i], From = 0, To = 1 }));
                return LaneLine(parts, slot);
            }
            return null;
        }

        /// <summary>How far from the line a road is drawn along the outside edge of its ordinary lanes is, on one side.</summary>
        private static double LaneEdge(RoadType type, bool forward)
        {
            if (type.OneWay) return type.Forward * NetworkBuilder.LaneW + type.Inner;
            return (forward ? type.Forward : type.Backward) * NetworkBuilder.LaneW + type.Inner;
        }

        private static void AddExtra(EditSegment s, bool forward, double from, double to)
        {
            // Lanes on the same side that touch or overlap become one.
            for (int i = s.Extras.Count - 1; i >= 0; i--)
            {
                ExtraLane x = s.Extras[i];
                if (x.Forward != forward || x.From > to + 1e-4 || x.To < from - 1e-4) continue;
                from = Math.Min(from, x.From);
                to = Math.Max(to, x.To);
                s.Extras.RemoveAt(i);
            }
            s.Extras.Add(new ExtraLane { Forward = forward, From = from, To = to });
        }

        /// <summary>
        /// The stretch of one road between two points on it, as parts of its pieces: for each,
        /// the piece, the side, and where the stretch starts and ends on it (from A to B). The
        /// side is the one the first point is on. Null if the points are not on one road.
        /// </summary>
        private List<KeyValuePair<EditSegment, ExtraLane>> Between(Vec p, Vec q)
        {
            int sp, sq;
            double tp, tq, across;
            bool side, other;
            if (!OnRoad(p, out sp, out tp, out side, out across) || !OnRoad(q, out sq, out tq, out other, out across)) return null;
            foreach (Run run in Runs())
            {
                int ip = run.Pieces.IndexOf(Segments[sp]), iq = run.Pieces.IndexOf(Segments[sq]);
                if (ip < 0 || iq < 0) continue;
                // Measured along the run: which piece, and how far through it.
                double from = ip + (run.Turned[ip] ? 1 - tp : tp), to = iq + (run.Turned[iq] ? 1 - tq : tq);
                bool withRun = side != run.Turned[ip];
                if (from > to)
                {
                    double swap = from;
                    from = to;
                    to = swap;
                }
                var parts = new List<KeyValuePair<EditSegment, ExtraLane>>();
                for (int i = (int)Math.Floor(from); i <= Math.Min((int)Math.Floor(to), run.Pieces.Count - 1); i++)
                {
                    double f0 = Math.Max(from - i, 0), f1 = Math.Min(to - i, 1);
                    if (f1 - f0 < 1e-4) continue;
                    bool turned = run.Turned[i];
                    parts.Add(new KeyValuePair<EditSegment, ExtraLane>(run.Pieces[i], new ExtraLane { Forward = withRun != turned, From = turned ? 1 - f1 : f0, To = turned ? 1 - f0 : f1 }));
                }
                return parts;
            }
            return null;
        }

        /// <summary>
        /// Add a lane along a road from one point on it to another, on the side the first point
        /// is on: outside the lanes of the traffic on that side. Both points must be on one road,
        /// with no junction between them. Returns null if it was added, or a short reason why not.
        /// </summary>
        public string TryAddLane(Vec p, Vec q, bool commit)
        {
            EditableMap work = Clone();
            List<KeyValuePair<EditSegment, ExtraLane>> parts = work.Between(p, q);
            if (parts == null) return "A lane runs along one road: start and end it on the same road, with no junction between.";
            foreach (KeyValuePair<EditSegment, ExtraLane> part in parts)
            {
                if (part.Key.Locked) return "That road came with the level.";
                if (part.Key.Ring) return "A roundabout's ring has one lane.";
                if (part.Key.Level != 0) return "A bridge or tunnel cannot be widened.";
            }
            if (parts.Count == 0) return "Too short for a lane: it needs at least " + Math.Round(ShortestLane * 3.28084 / 10) * 10 + " ft.";
            foreach (KeyValuePair<EditSegment, ExtraLane> part in parts) AddExtra(part.Key, part.Value.Forward, part.Value.From, part.Value.To);
            // It is the lane that results that has to be long enough: a short stretch that lengthens a lane, or joins two, is fine.
            foreach (Run run in work.Runs())
            {
                int index = run.Pieces.IndexOf(parts[0].Key);
                if (index < 0) continue;
                double total = 0;
                foreach (EditSegment piece in run.Pieces) total += LengthOf(work.Trace(piece));
                foreach (double[] span in work.ExtrasAlong(run, parts[0].Value.Forward != run.Turned[index], false))
                {
                    if ((span[1] - span[0]) * total < ShortestLane) return "Too short for a lane: it needs at least " + Math.Round(ShortestLane * 3.28084 / 10) * 10 + " ft.";
                }
            }
            string problem = work.Unbuildable(this, "There is not room for a lane there.");
            if (problem != null) return problem;
            if (commit) Segments = work.Segments;
            return null;
        }

        /// <summary>
        /// The line down the middle of the lane that TryAddLane would add between two points,
        /// for showing it before it is built. Null if the points are not on one road.
        /// </summary>
        public Vec[] LaneLine(Vec p, Vec q)
        {
            List<KeyValuePair<EditSegment, ExtraLane>> parts = Between(p, q);
            return parts == null || parts.Count == 0 ? null : LaneLine(parts);
        }

        /// <summary>The line down the middle of a lane along parts of a road's pieces: one of its ordinary lanes (0 nearest the middle), or, with no lane given, the place outside them where an added lane goes.</summary>
        private Vec[] LaneLine(List<KeyValuePair<EditSegment, ExtraLane>> parts, int slot = -1, bool middle = false)
        {
            // Each part, run the way its own traffic goes; then the parts in whatever order joins them up.
            var lines = new List<Vec[]>();
            foreach (KeyValuePair<EditSegment, ExtraLane> part in parts)
            {
                EditSegment s = part.Key;
                ExtraLane x = part.Value;
                int steps = Math.Max(2, (int)Math.Ceiling((x.To - x.From) * LengthOf(Trace(s)) / 3));
                var along = new Vec[steps + 1];
                for (int k = 0; k <= steps; k++) along[k] = PointAt(s, x.From + (x.To - x.From) * k / steps);
                if (!x.Forward) Array.Reverse(along);
                lines.Add(Geo.Offset(along, middle ? 0 : slot < 0 ? LaneEdge(s.Type, x.Forward) + NetworkBuilder.LaneW / 2 : s.Type.Inner + (slot + 0.5) * NetworkBuilder.LaneW));
            }
            var line = new List<Vec>(lines[0]);
            for (int i = 1; i < lines.Count; i++)
            {
                Vec[] next = lines[i];
                if (Dist(line[line.Count - 1], next[0]) <= Dist(line[0], next[next.Length - 1])) line.AddRange(next);
                else line.InsertRange(0, next);
            }
            return line.ToArray();
        }

        /// <summary>The extra lane under a point, as the line down its middle, or null. The point has to be out over the lane itself, not the ordinary lanes inside it.</summary>
        public Vec[] ExtraLaneAt(Vec p)
        {
            List<KeyValuePair<EditSegment, ExtraLane>> found = ExtraParts(p);
            return found == null ? null : LaneLine(found);
        }

        /// <summary>The extra lane under a point, as the part of it on each piece it runs along: where neighboring pieces carry it on, all of it.</summary>
        private List<KeyValuePair<EditSegment, ExtraLane>> ExtraParts(Vec p)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return null;
            EditSegment s = Segments[seg];
            if (across < LaneEdge(s.Type, forward) - 1) return null;
            ExtraLane under = s.Extras.Find(x => x.Forward == forward && t >= x.From - 1e-6 && t <= x.To + 1e-6);
            if (under == null) return null;
            var parts = new List<KeyValuePair<EditSegment, ExtraLane>> { new KeyValuePair<EditSegment, ExtraLane>(s, under) };
            // Follow it on to the next piece at each end it runs right up to, for as long as that piece carries it on.
            foreach (bool towardB in new[] { true, false })
            {
                EditSegment piece = s;
                ExtraLane lane = under;
                bool goingToB = towardB;
                while (goingToB ? lane.To > 1 - 1e-4 : lane.From < 1e-4)
                {
                    int node = goingToB ? piece.B : piece.A;
                    List<EditSegment> here = RoadsAt(node);
                    if (!CarriesOn(here, node)) break;
                    EditSegment next = here[0] == piece ? here[1] : here[0];
                    // The same side of the road, seen from the next piece: the other way round if the two were drawn toward each other.
                    bool sameWay = (piece.B == node) != (next.B == node);
                    bool side = sameWay ? lane.Forward : !lane.Forward, fromA = next.A == node;
                    ExtraLane more = next.Extras.Find(x => x.Forward == side && (fromA ? x.From < 1e-4 : x.To > 1 - 1e-4));
                    if (more == null || parts.Exists(k => k.Value == more)) break;
                    parts.Add(new KeyValuePair<EditSegment, ExtraLane>(next, more));
                    piece = next;
                    lane = more;
                    goingToB = fromA;
                }
            }
            return parts;
        }

        /// <summary>Take away the extra lane under a point. Returns null if it was removed, or a short reason why not.</summary>
        public string TryRemoveLane(Vec p, bool commit)
        {
            EditableMap work = Clone();
            List<KeyValuePair<EditSegment, ExtraLane>> parts = work.ExtraParts(p);
            if (parts == null) return "There is no added lane there.";
            foreach (KeyValuePair<EditSegment, ExtraLane> part in parts)
            {
                if (part.Key.Locked) return "That road came with the level.";
                part.Key.Extras.Remove(part.Value);
            }
            if (commit) Segments = work.Segments;
            return null;
        }

        /// <summary>
        /// Every added lane, as the line down its middle, running the way its traffic does. A
        /// lane carried on from one piece of a road to the next is one lane. The move tool takes
        /// hold of a lane by either end of this line.
        /// </summary>
        public List<Vec[]> AddedLanes()
        {
            var lines = new List<Vec[]>();
            var seen = new HashSet<ExtraLane>();
            foreach (EditSegment s in Segments)
            {
                foreach (ExtraLane x in s.Extras)
                {
                    if (seen.Contains(x)) continue;
                    var own = new List<KeyValuePair<EditSegment, ExtraLane>> { new KeyValuePair<EditSegment, ExtraLane>(s, x) };
                    Vec[] line = LaneLine(own);
                    // The whole of it, where neighboring pieces carry it on; found from a point in its middle.
                    List<KeyValuePair<EditSegment, ExtraLane>> parts = ExtraParts(line[line.Length / 2]);
                    if (parts == null || !parts.Exists(k => k.Value == x)) parts = own;
                    foreach (KeyValuePair<EditSegment, ExtraLane> part in parts) seen.Add(part.Value);
                    lines.Add(LaneLine(parts));
                }
            }
            return lines;
        }

        /// <summary>
        /// Slide one end of an added lane along its road, making the lane longer or shorter. The
        /// lane is the one a point is on (anywhere along it but its very ends); the end is its
        /// first, where its traffic moves into it, or its last; and it goes to the place on the
        /// road nearest another point. Returns null if it was moved, or a short reason why not.
        /// </summary>
        public string TryMoveLaneEnd(Vec onLane, bool first, Vec to, bool commit)
        {
            EditableMap work = Clone();
            List<KeyValuePair<EditSegment, ExtraLane>> parts = work.ExtraParts(onLane);
            if (parts == null) return "There is no added lane there.";
            Vec[] line = work.LaneLine(parts);
            // The end that stays where it is; the lane is laid afresh from there.
            Vec keep = first ? line[line.Length - 1] : line[0];
            foreach (KeyValuePair<EditSegment, ExtraLane> part in parts)
            {
                if (part.Key.Locked) return "That road came with the level.";
                part.Key.Extras.Remove(part.Value);
            }
            string problem = work.TryAddLane(keep, to, true);
            if (problem != null) return problem;
            if (commit) Segments = work.Segments;
            return null;
        }

        /// <summary>Turn a piece end for end: the same road on the ground, described from its other end.</summary>
        private static void Flip(EditSegment s)
        {
            int a = s.A;
            s.A = s.B;
            s.B = a;
            int[] turns = s.TurnsAtA;
            s.TurnsAtA = s.TurnsAtB;
            s.TurnsAtB = turns;
            int control = s.ControlAtA;
            s.ControlAtA = s.ControlAtB;
            s.ControlAtB = control;
            int tie = s.LaneAtA;
            s.LaneAtA = s.LaneAtB;
            s.LaneAtB = tie;
            foreach (ExtraLane x in s.Extras)
            {
                double from = x.From;
                x.From = 1 - x.To;
                x.To = 1 - from;
                x.Forward = !x.Forward;
            }
        }

        /// <summary>
        /// Give the road under a point one lane more, or one lane fewer, on the side the point is
        /// on: for its whole length, from one junction or road end to the next. Returns null if
        /// it was changed, or a short reason why it cannot be. A road that loses all the lanes of
        /// one direction becomes one-way.
        /// </summary>
        public string TryChangeLanes(Vec p, int change, bool commit)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return "There is no road there.";
            EditableMap work = Clone();
            EditSegment under = work.Segments[seg];
            foreach (Run run in work.Runs())
            {
                int index = run.Pieces.IndexOf(under);
                if (index < 0) continue;
                foreach (EditSegment piece in run.Pieces)
                {
                    if (piece.Locked) return "That road came with the level.";
                    if (piece.Ring) return "A roundabout's ring has one lane.";
                }
                // The lanes that go the way the run does, and those that go against it.
                bool turned = run.Turned[index], withRun = forward != turned;
                RoadType type = under.Type;
                int with = turned ? type.Backward : type.Forward, against = turned ? type.Forward : type.Backward;
                if (withRun) with += change;
                else against += change;
                if (with + against < 1) return "That is its last lane. Use the bulldozer to remove the road.";
                if (with > RoadType.MostLanes || against > RoadType.MostLanes) return "A road can have up to " + RoadType.MostLanes + " lanes each way.";
                // Every piece is described the same way round, so that they are still one road; a road left with lanes one way only runs that way.
                for (int i = 0; i < run.Pieces.Count; i++) if (run.Turned[i] != (with == 0)) Flip(run.Pieces[i]);
                RoadType changed = with == 0 ? RoadType.With(type, against, 0) : RoadType.With(type, with, against);
                foreach (EditSegment piece in run.Pieces)
                {
                    piece.Type = changed;
                    if (changed.OneWay) piece.Extras.RemoveAll(x => !x.Forward);
                }
                var ends = new List<int>(run.Joints) { run.From, run.To };
                foreach (int node in ends)
                {
                    string problem = work.NodeProblem(node);
                    if (problem != null) return problem;
                }
                string noRoom = work.Unbuildable(this, change > 0 ? "There is not room for another lane there." : "The road cannot be built that way there.");
                if (noRoom != null) return noRoom;
                if (commit) Segments = work.Segments;
                return null;
            }
            return "There is no road there.";
        }

        /// <summary>Whether the lane under a point could be turned round to run the other way: one of a road's ordinary lanes, on a road the player may change.</summary>
        public bool CouldReverseLane(Vec p)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return false;
            EditSegment s = Segments[seg];
            return !s.Locked && !s.Ring && !MiddleLaneAt(p) && ExtraParts(p) == null;
        }

        /// <summary>
        /// Turn the lane under a point round, so that it runs the other way: for its whole
        /// length, from one junction or road end to the next. Its side of the road has a lane
        /// fewer and the other side a lane more. A one-way road with a single lane simply runs
        /// the other way; a one-way road with more becomes two-way; and a two-way road that
        /// gives up its last lane one way becomes one-way. The line down the middle of a two-way
        /// road stays where the road was drawn, so the pavement moves over where the two sides
        /// come out unequal. Lanes that had been told their turns go back to working them out.
        /// Returns null if it was changed, or a short reason why it cannot be.
        /// </summary>
        public string TryReverseLane(Vec p, bool commit)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return "There is no road there.";
            if (MiddleLaneAt(p)) return "The lane in the middle serves both directions.";
            if (ExtraParts(p) != null) return "An added lane runs the way of the lanes beside it.";
            EditableMap work = Clone();
            EditSegment under = work.Segments[seg];
            foreach (Run run in work.Runs())
            {
                int index = run.Pieces.IndexOf(under);
                if (index < 0) continue;
                foreach (EditSegment piece in run.Pieces)
                {
                    if (piece.Locked) return "That road came with the level.";
                    if (piece.Ring) return "Traffic goes one way round a roundabout.";
                }
                // The lanes that go the way the run does, and those that go against it: one changes sides.
                bool turned = run.Turned[index], withRun = forward != turned;
                RoadType type = under.Type;
                int with = turned ? type.Backward : type.Forward, against = turned ? type.Forward : type.Backward;
                if (withRun)
                {
                    with--;
                    against++;
                }
                else
                {
                    against--;
                    with++;
                }
                if (with < 0 || against < 0) return "There is no lane there to turn round.";
                if (with > RoadType.MostLanes || against > RoadType.MostLanes) return "A road can have up to " + RoadType.MostLanes + " lanes each way.";
                // Every piece is described the same way round, so that they are still one road; a road left with lanes one way only runs that way.
                for (int i = 0; i < run.Pieces.Count; i++) if (run.Turned[i] != (with == 0)) Flip(run.Pieces[i]);
                RoadType changed = with == 0 ? RoadType.With(type, against, 0) : RoadType.With(type, with, against);
                foreach (EditSegment piece in run.Pieces)
                {
                    piece.Type = changed;
                    piece.TurnsAtA = piece.TurnsAtB = null;
                    if (changed.OneWay) piece.Extras.RemoveAll(x => !x.Forward);
                }
                var ends = new List<int>(run.Joints) { run.From, run.To };
                foreach (int node in ends)
                {
                    string problem = work.NodeProblem(node);
                    if (problem != null) return problem;
                }
                string noRoom = work.Unbuildable(this, "The road cannot be built that way there.");
                if (noRoom != null) return noRoom;
                if (commit) Segments = work.Segments;
                return null;
            }
            return "There is no road there.";
        }

        /// <summary>Whether a point is over the turning lane down the middle of a road that has one, or over a median that makes way for left-turn lanes.</summary>
        public bool MiddleLaneAt(Vec p)
        {
            int seg;
            double t, across;
            bool forward;
            return OnRoad(p, out seg, out t, out forward, out across) && (Segments[seg].Type.CenterLane || Segments[seg].Type.Bays) && across < Segments[seg].Type.Inner;
        }

        /// <summary>Whether the road under a point could be given a turning lane down its middle, or left-turn lanes in its median: a two-way road that has neither.</summary>
        public bool CouldHaveMiddleLane(Vec p)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return false;
            EditSegment s = Segments[seg];
            return !s.Type.OneWay && !s.Type.CenterLane && !s.Type.Bays && !s.Locked && !s.Ring;
        }

        /// <summary>Whether the road under a point has a median, with or without left-turn lanes in it.</summary>
        public bool MedianAt(Vec p)
        {
            int seg;
            double t, across;
            bool forward;
            return OnRoad(p, out seg, out t, out forward, out across) && Segments[seg].Type.Median > 0;
        }

        /// <summary>
        /// Give the road under a point somewhere to turn left from, or take it away: for its whole
        /// length, from one junction to the next. A road with a painted line gets a turning lane
        /// down its middle. A road with a median keeps the median, which then makes way for a
        /// left-turn lane on the way into each intersection. Taking it away leaves what was
        /// there before, the line or the median. Returns null if it was changed, or a short
        /// reason why it cannot be.
        /// </summary>
        public string TrySetMiddleLane(Vec p, bool there, bool commit)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return "There is no road there.";
            EditableMap work = Clone();
            EditSegment under = work.Segments[seg];
            if (under.Type.OneWay) return "A one-way road has no middle to put a turning lane in.";
            foreach (Run run in work.Runs())
            {
                if (!run.Pieces.Contains(under)) continue;
                int now = under.Type.Middle;
                int middle = there ? (now == RoadType.Grass || now == RoadType.TurnBays ? RoadType.TurnBays : RoadType.TurnLane) : now == RoadType.TurnBays ? RoadType.Grass : RoadType.Line;
                RoadType changed = RoadType.With(under.Type, under.Type.Forward, under.Type.Backward, middle);
                foreach (EditSegment piece in run.Pieces)
                {
                    if (piece.Locked) return "That road came with the level.";
                    if (piece.Ring) return "A roundabout's ring has one lane.";
                    piece.Type = changed;
                }
                var ends = new List<int>(run.Joints) { run.From, run.To };
                foreach (int node in ends)
                {
                    string problem = work.NodeProblem(node);
                    if (problem != null) return problem;
                }
                string noRoom = work.Unbuildable(this, "There is not room for a turning lane there.");
                if (noRoom != null) return noRoom;
                if (commit) Segments = work.Segments;
                return null;
            }
            return "There is no road there.";
        }

        // ---------------------------------------------------------------- what cannot work

        /// <summary>
        /// The places where traffic could not flow as the roads are drawn: a point that traffic
        /// arrives at with no road to leave by, because the one-way roads there run the wrong
        /// way, or a road that nothing leads into. These are not refused while drawing, since a
        /// junction is often right only once its last road is in; they are shown, so that none is left by mistake.
        /// </summary>
        public List<Fault> Faults()
        {
            // How many of the roads at each point bring traffic to it, and how many take traffic away.
            var arrive = new int[Nodes.Count];
            var leave = new int[Nodes.Count];
            var roads = new int[Nodes.Count];
            foreach (EditSegment s in Segments)
            {
                roads[s.A]++;
                roads[s.B]++;
                arrive[s.B]++;
                leave[s.A]++;
                if (s.Type.OneWay) continue;
                arrive[s.A]++;
                leave[s.B]++;
            }
            var faults = new List<Fault>();
            var found = new bool[Nodes.Count];
            foreach (EditSegment s in Segments)
            {
                foreach (int node in new[] { s.A, s.B })
                {
                    if (found[node]) continue;
                    if (roads[node] < 2)
                    {
                        // A road end with nothing joined to it is the edge of the map, where traffic comes and goes, or a
                        // dead end, where it turns round: which a one-way road gives it no way to do.
                        if (!s.Type.OneWay || !DeadEndAt(node)) continue;
                        faults.Add(new Fault { At = Nodes[node], What = s.B == node ? "A one-way road stops here, so its traffic has nowhere to go. Carry it on to another road, or to the edge of the map."
                            : "A one-way road starts here from nothing, so no traffic can use it. Start it from another road, or from the edge of the map." });
                        found[node] = true;
                        continue;
                    }
                    bool brings = !s.Type.OneWay || s.B == node, takes = !s.Type.OneWay || s.A == node;
                    if (brings && leave[node] - (takes ? 1 : 0) == 0)
                    {
                        faults.Add(new Fault { At = Nodes[node], What = "Traffic arriving here has nowhere to go: no road leads away. A one-way road runs the way it was drawn." });
                        found[node] = true;
                    }
                    else if (takes && arrive[node] - (brings ? 1 : 0) == 0)
                    {
                        faults.Add(new Fault { At = Nodes[node], What = "Nothing can reach the road that leaves here: no road leads in. A one-way road runs the way it was drawn." });
                        found[node] = true;
                    }
                }
            }
            return faults;
        }

        // ---------------------------------------------------------------- moving and reshaping

        /// <summary>The point nearest a spot, if one is within reach, or -1.</summary>
        public int NodeAt(Vec p, double reach)
        {
            int node = -1;
            for (int i = 0; i < Nodes.Count; i++)
            {
                double d = Dist(Nodes[i], p);
                if (d < reach)
                {
                    reach = d;
                    node = i;
                }
            }
            return node;
        }

        /// <summary>The roads that end at a point.</summary>
        public List<EditSegment> RoadsAt(int node)
        {
            var here = new List<EditSegment>();
            foreach (EditSegment s in Segments) if (s.A == node || s.B == node) here.Add(s);
            return here;
        }

        /// <summary>
        /// Move a point, without checking the result. The roads that end there follow it, and a
        /// curved one keeps its shape: it turns and stretches about its other end.
        /// </summary>
        public void Shift(int node, Vec to)
        {
            Vec from = Nodes[node];
            foreach (EditSegment s in Segments)
            {
                if (!s.Curved || (s.A != node && s.B != node)) continue;
                Vec hold = Nodes[s.A == node ? s.B : s.A];
                double was = Dist(hold, from), now = Dist(hold, to);
                if (was < 1e-6) continue;
                double turn = Math.Atan2(to.Y - hold.Y, to.X - hold.X) - Math.Atan2(from.Y - hold.Y, from.X - hold.X);
                double cos = Math.Cos(turn) * now / was, sin = Math.Sin(turn) * now / was;
                double mx = s.Mid.X - hold.X, my = s.Mid.Y - hold.Y;
                s.Mid = new Vec(hold.X + mx * cos - my * sin, hold.Y + mx * sin + my * cos);
            }
            Nodes[node] = to;
        }

        /// <summary>
        /// Reshape a road, without checking the result: it becomes the round curve between its
        /// ends that passes through a point, or a straight road if the point is nearly in line.
        /// </summary>
        public void Reshape(int segment, Vec through)
        {
            EditSegment s = Segments[segment];
            double t;
            Vec on;
            s.Curved = Project(new[] { Nodes[s.A], Nodes[s.B] }, through, out t, out on) > 3;
            s.Mid = through;
        }

        /// <summary>
        /// Move a point to a new place, taking the ends of its roads with it. Returns null if it
        /// was moved, or a short reason why it cannot be. With commit false nothing is changed.
        /// </summary>
        public string TryMove(int node, Vec to, bool commit)
        {
            EditableMap work = Clone();
            work.Shift(node, to);
            string problem = work.ProblemWith(work.RoadsAt(node), node) ?? work.Unbuildable(this);
            if (problem != null) return problem;
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        /// <summary>
        /// Bend a road so that it passes through a point, or straighten it by pulling it back
        /// into line. Returns null if it was reshaped, or a short reason why it cannot be.
        /// </summary>
        public string TryBend(int segment, Vec through, bool commit)
        {
            EditableMap work = Clone();
            work.Reshape(segment, through);
            string problem = work.ProblemWith(new List<EditSegment> { work.Segments[segment] }, -1) ?? work.Unbuildable(this);
            if (problem != null) return problem;
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        /// <summary>
        /// What is wrong with roads that have just been moved or reshaped, if anything. Moving
        /// never joins roads together or makes a new intersection: that is what drawing is for.
        /// </summary>
        private string ProblemWith(List<EditSegment> changed, int moved)
        {
            if (moved >= 0 && Bound > 0 && (Math.Abs(Nodes[moved].X) > Bound + 0.5 || Math.Abs(Nodes[moved].Y) > Bound + 0.5)) return "That is outside the area you can build in.";
            foreach (EditSegment s in changed)
            {
                if (s.Locked) return "That road came with the level.";
                if (s.Ring) return "A roundabout cannot be reshaped. Remove it and place it again.";
            }
            if (moved >= 0)
            {
                for (int i = 0; i < Nodes.Count; i++) if (i != moved && Dist(Nodes[i], Nodes[moved]) < SnapNode) return "Too close to another point. To join roads, draw a road between them.";
            }
            var touched = new HashSet<int>();
            foreach (EditSegment s in changed)
            {
                touched.Add(s.A);
                touched.Add(s.B);
                Vec pa = Nodes[s.A], pb = Nodes[s.B];
                if (s.Curved && CurveTurn(pa, s.Mid, pb) > MaxTurn) return "That curve goes too far round.";
                Vec[] path = Trace(s);
                if (Bound > 0) foreach (Vec v in path) if (Math.Abs(v.X) > Bound + 0.5 || Math.Abs(v.Y) > Bound + 0.5) return "That is outside the area you can build in.";
                double length = LengthOf(path);
                if (s.Level != 0 && length < 2 * RampFor(s.Level) + 20) return "Too short to " + (s.Level > 0 ? "climb that high." : "go down that deep.");
                if (s.Curved && TightestRadius(path) < s.Type.MinRadius) return "That curve is too tight for this kind of road.";
                for (int i = 0; i < Nodes.Count; i++)
                {
                    if (i == s.A || i == s.B) continue;
                    double t;
                    Vec on;
                    if (!(Project(path, Nodes[i], out t, out on) < SnapRoad && t > 0 && t < 1)) continue;
                    if (s.Level == 0 || Math.Abs(HeightAlong(s.Level, t * length, length)) < Clearance) return "That runs into another road. Draw a road across to make an intersection.";
                }
                foreach (EditSegment o in Segments)
                {
                    if (o == s) continue;
                    bool same = (o.A == s.A && o.B == s.B) || (o.A == s.B && o.B == s.A);
                    if (same && !o.Curved && !s.Curved) return "There is already a road there.";
                    Vec[] other = Trace(o);
                    double otherLength = LengthOf(other);
                    foreach (Crossing x in Crossings(path, other))
                    {
                        // Touching at a shared end is not a crossing.
                        if ((o.A == s.A || o.B == s.A) && Dist(x.At, pa) < SnapNode * 1.5) continue;
                        if ((o.A == s.B || o.B == s.B) && Dist(x.At, pb) < SnapNode * 1.5) continue;
                        if (s.Level == 0 && o.Level == 0) return "That would cross another road. Draw a road across to make an intersection.";
                        double room = Math.Abs(HeightAlong(s.Level, x.T * length, length) - HeightAlong(o.Level, x.U * otherLength, otherLength));
                        if (room < Clearance) return "Not enough room between the roads there.";
                    }
                    // A moved end must not come to rest on a road it is no part of.
                    if (moved >= 0 && o.A != moved && o.B != moved && o.Level == 0)
                    {
                        double t;
                        Vec on;
                        if (Project(other, Nodes[moved], out t, out on) < SnapRoad) return "Too close to another road. To join roads, draw a road between them.";
                    }
                }
            }
            foreach (int node in touched)
            {
                string problem = NodeProblem(node);
                if (problem != null) return problem;
            }
            return null;
        }

        // ---------------------------------------------------------------- bridges and tunnels

        /// <summary>How far up or down each level is, meters.</summary>
        public const double LevelHeight = 5;
        /// <summary>How much height two roads need between them to pass one over the other.</summary>
        public const double Clearance = 4.5;
        public const int HighestLevel = 3;

        /// <summary>How long a ramp a road needs at each end to reach a level, meters.</summary>
        public static double RampFor(int level)
        {
            int n = Math.Abs(level);
            return n == 0 ? 0 : n == 1 ? 40 : n == 2 ? 70 : 110;
        }

        /// <summary>How many times the ordinary price a road at a level costs. Tunnels cost more than bridges.</summary>
        public static double CostTimes(int level)
        {
            return level == 0 ? 1 : level > 0 ? 1 + level : 1 + 2 * -level;
        }

        /// <summary>Height above the ground of a road at a level, a distance along its length.</summary>
        public static double HeightAlong(int level, double s, double length)
        {
            return level == 0 ? 0 : level * LevelHeight * Geo.Rise(s, length, RampFor(level));
        }

        /// <summary>What everything drawn so far costs.</summary>
        public double Cost()
        {
            double total = 0;
            foreach (EditSegment s in Segments)
            {
                if (s.Locked) continue;
                double length = LengthOf(Trace(s));
                total += length * s.Type.CostPerMeter * CostTimes(s.Level);
                foreach (ExtraLane x in s.Extras) total += (x.To - x.From) * length * LanePrice(s.Type);
            }
            return total;
        }

        private struct Stop
        {
            public double T;
            public int Node;
        }

        private struct Crossing
        {
            /// <summary>How far along the new road, and how far along the road it crosses.</summary>
            public double T, U;
            public Vec At;
        }

        private string Add(Vec p, Vec q, RoadType type, Vec? through, int level, double beside = 0)
        {
            string error;
            laid = 0;
            int a = Resolve(p, out error, beside, level == 0 ? type : null, SnapTo(q));
            if (a < 0) return error;
            // The first end may have cut the road it landed on in two: the far end keeps clear of both parts.
            int b = Resolve(q, out error, beside, level == 0 ? type : null, new Snap { Node = a, Segment = -1 });
            if (b < 0) return error;
            if (a == b) return "Too short.";

            Vec pa = Nodes[a], pb = Nodes[b];
            // A point that is almost on the straight line makes no real curve.
            bool curved = false;
            Vec c = default(Vec);
            if (through.HasValue)
            {
                curved = IsCurve(pa, through.Value, pb);
                c = through.Value;
                if (curved && CurveTurn(pa, c, pb) > MaxTurn) return "That curve goes too far round. Draw it in two parts.";
            }
            Vec[] path = Trace(pa, pb, curved, c);
            if (Bound > 0) foreach (Vec v in path) if (Math.Abs(v.X) > Bound + 0.5 || Math.Abs(v.Y) > Bound + 0.5) return "That is outside the area you can build in.";
            double pathLength = LengthOf(path);
            if (level != 0 && pathLength < 2 * RampFor(level) + 20)
            {
                return "Too short to " + (level > 0 ? "climb that high" : "go down that deep") + ": it needs at least " + Math.Round((2 * RampFor(level) + 20) * 3.28084 / 10) * 10 + " ft.";
            }

            // Everything the new road meets on the way becomes a point along it: a point of the map close beside its line,
            // or the end of a road so near that the new road would lie over it.
            var widest = new double[Nodes.Count];
            foreach (EditSegment s in Segments)
            {
                if (s.Level != 0) continue;
                widest[s.A] = Math.Max(widest[s.A], s.Type.Width);
                widest[s.B] = Math.Max(widest[s.B], s.Type.Width);
            }
            var stops = new List<Stop>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (i == a || i == b) continue;
                double t;
                Vec on;
                double within = level == 0 && widest[i] > 0 ? Math.Max(SnapRoad, (type.Width + widest[i]) / 2 + 1.5) : SnapRoad;
                if (!(Project(path, Nodes[i], out t, out on) < within && t > 0 && t < 1)) continue;
                if (level == 0) stops.Add(new Stop { T = t, Node = i });
                // A bridge or tunnel passes clean over or under a point, unless it is still too near the ground there.
                else if (Math.Abs(HeightAlong(level, t * pathLength, pathLength)) < Clearance) return "A bridge or tunnel can only meet other roads at its ends.";
            }
            int existing = Segments.Count;
            for (int i = 0; i < existing; i++)
            {
                EditSegment s = Segments[i];
                if (stops.Exists(k => k.Node == s.A || k.Node == s.B)) continue;
                List<Crossing> crossings = Crossings(path, Trace(s));
                // Touching at a shared end is not a crossing.
                crossings.RemoveAll(x => ((s.A == a || s.B == a) && Dist(x.At, pa) < SnapNode * 1.5) || ((s.A == b || s.B == b) && Dist(x.At, pb) < SnapNode * 1.5));
                // Cut the far end first, so each cut leaves the near part of the road in place.
                crossings.Sort((m, n) => n.U.CompareTo(m.U));
                double upTo = 1;
                foreach (Crossing x in crossings)
                {
                    double u = x.U / upTo;
                    if (level != 0 || s.Level != 0)
                    {
                        // Roads at different heights pass one over the other without meeting, given room between them.
                        double other = LengthOf(Trace(s));
                        double room = Math.Abs(HeightAlong(level, x.T * pathLength, pathLength) - HeightAlong(s.Level, u * other, other));
                        if (room < Clearance) return "Not enough room between the roads there. Cross nearer the middle of the bridge or tunnel.";
                        continue;
                    }
                    double fromA = LengthBetween(s, 0, u), fromB = LengthBetween(s, u, 1);
                    if (Dist(x.At, Nodes[s.A]) < SnapNode) stops.Add(new Stop { T = x.T, Node = s.A });
                    else if (Dist(x.At, Nodes[s.B]) < SnapNode) stops.Add(new Stop { T = x.T, Node = s.B });
                    // Crossing right beside the end of a piece: go through that end instead, a few meters out of line.
                    else if (Math.Min(fromA, fromB) < Beside(s, beside)) stops.Add(new Stop { T = x.T, Node = fromA <= fromB ? s.A : s.B });
                    else
                    {
                        stops.Add(new Stop { T = x.T, Node = Split(i, u) });
                        upTo = x.U;
                    }
                }
            }
            stops.Sort((m, n) => m.T.CompareTo(n.T));

            var chain = new List<Stop> { new Stop { T = 0, Node = a } };
            foreach (Stop stop in stops)
            {
                if (stop.Node == a || stop.Node == b || chain.Exists(k => k.Node == stop.Node)) continue;
                chain.Add(stop);
            }
            chain.Add(new Stop { T = 1, Node = b });

            var touched = new HashSet<int>();
            for (int i = 0; i + 1 < chain.Count; i++)
            {
                int from = chain[i].Node, to = chain[i + 1].Node;
                touched.Add(from);
                touched.Add(to);
                var piece = new EditSegment { A = from, B = to, Type = type, Level = level };
                if (curved)
                {
                    // The part of the curve between two positions is a piece of the same circle.
                    piece.Curved = true;
                    piece.Mid = CurvePoint(pa, c, pb, (chain[i].T + chain[i + 1].T) / 2);
                }
                Vec[] shape = Trace(Nodes[from], Nodes[to], piece.Curved, piece.Mid);
                if (piece.Curved && TightestRadius(shape) < type.MinRadius) return "That curve is too tight for this kind of road.";
                // Where the new road runs along one that is already there, that stretch is left as it is.
                bool there = false;
                foreach (EditSegment s in Segments)
                {
                    bool same = (s.A == from && s.B == to) || (s.A == to && s.B == from);
                    if (same && !s.Curved && !piece.Curved) there = true;
                }
                if (there) continue;
                Segments.Add(piece);
                laid++;
            }
            if (laid == 0) return "There is already a road there.";
            // A piece that carries on from a road with a speed limit of its own posted is under the same limit.
            for (int i = Segments.Count - laid; i < Segments.Count; i++)
            {
                EditSegment piece = Segments[i];
                foreach (int node in new[] { piece.A, piece.B })
                {
                    List<EditSegment> here = RoadsAt(node);
                    if (here.Count != 2 || piece.Limit != 0) continue;
                    EditSegment other = here[0] == piece ? here[1] : here[0];
                    if (other.Type.Id == piece.Type.Id && other.Level == 0 && piece.Level == 0) piece.Limit = other.Limit;
                }
            }
            for (int i = Segments.Count - laid; i < Segments.Count; i++)
            {
                if (Alongside(Segments[i])) return "Too close alongside another road. Leave a gap between them, or add lanes to that road with the lane tool.";
            }

            foreach (int node in touched)
            {
                string problem = NodeProblem(node);
                if (problem != null) return problem;
            }
            return null;
        }

        /// <summary>What is wrong with the way roads meet at a point, if anything.</summary>
        private string NodeProblem(int node)
        {
            var headings = new List<double>();
            foreach (EditSegment s in Segments) if (s.A == node || s.B == node) headings.Add(HeadingFrom(s, node));
            if (headings.Count > MaxArms) return "No more than " + MaxArms + " roads can meet at one point.";
            // A one-way road may run in alongside another road, or peel off it: that is a merge, not an intersection.
            if (JoinAt(node) != null) return null;
            // Where one-way roads split or come together they may lie much closer: the lanes just part, or close up.
            // That holds while the fork is still being drawn, too: two roads that arrive together before the road that leaves is in.
            int arrive;
            double least = Streams(node, out arrive) ? ForkAngle : MinAngle;
            for (int i = 0; i < headings.Count; i++)
            {
                for (int k = i + 1; k < headings.Count; k++)
                {
                    double between = Math.Abs(Math.Atan2(Math.Sin(headings[i] - headings[k]), Math.Cos(headings[i] - headings[k])));
                    if (between < least) return least < MinAngle ? "Too close together: the roads need to part a little more widely."
                        : "Too shallow an angle: the roads need to meet more squarely. Only a one-way road can merge in that closely.";
                }
            }
            return null;
        }

        /// <summary>The least angle between two one-way roads where they split or come together, and the most any of them may turn from the way the traffic is flowing there.</summary>
        public const double ForkAngle = 14 * Math.PI / 180, ForkSpread = 50 * Math.PI / 180;

        /// <summary>The way a one-way road's traffic is heading as it passes a point at one of its ends.</summary>
        private double FlowOf(EditSegment s, int node)
        {
            return HeadingFrom(s, node) + (s.B == node ? Math.PI : 0);
        }

        /// <summary>
        /// Whether the roads at a point are all one-way and all run through it nearly the same
        /// way, some arriving and some leaving: a fork, or roads coming together. There the
        /// lanes carry straight on from road to road, with no light and no stop sign.
        /// </summary>
        private bool FlowsThrough(int node)
        {
            int arriving;
            if (JoinAt(node) != null || !Streams(node, out arriving)) return false;
            int roads = RoadsAt(node).Count;
            return roads >= 3 && arriving > 0 && arriving < roads;
        }

        /// <summary>
        /// Whether two or more roads at a point are all one-way and their traffic all passes it
        /// heading nearly the same way: the shape of a fork or of roads coming together, whether
        /// or not all of its roads have been drawn yet. Also says how many of them arrive there.
        /// </summary>
        private bool Streams(int node, out int arriving)
        {
            arriving = 0;
            List<EditSegment> here = RoadsAt(node);
            if (here.Count < 2) return false;
            double x = 0, y = 0;
            foreach (EditSegment s in here)
            {
                if (!s.Type.OneWay || s.Ring) return false;
                double way = FlowOf(s, node);
                x += Math.Cos(way);
                y += Math.Sin(way);
                if (s.B == node) arriving++;
            }
            double flow = Math.Atan2(y, x);
            foreach (EditSegment s in here)
            {
                double off = FlowOf(s, node) - flow;
                if (Math.Abs(Math.Atan2(Math.Sin(off), Math.Cos(off))) > ForkSpread) return false;
            }
            // One road parting into several, or several closing up into one, cross nobody's path. But where two or more
            // arrive and two or more leave, their paths cross, and that is only a weave if they all run close to the same
            // way. Two one-way streets crossing at a right angle are an intersection, and need a light or signs.
            if (arriving > 1 && here.Count - arriving > 1)
            {
                foreach (EditSegment s in here)
                {
                    foreach (EditSegment o in here)
                    {
                        double apart = FlowOf(s, node) - FlowOf(o, node);
                        if (Math.Abs(Math.Atan2(Math.Sin(apart), Math.Cos(apart))) > ForkSpread) return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// Where one road arrives at a fork, which of the roads that leave each of its lanes
        /// carries on along: the lanes are dealt out from left to right among those roads, from
        /// left to right, by how many lanes each has. Null if the point is not a fork of that kind.
        /// </summary>
        private List<EditSegment>[] ForkLanes(int node, EditSegment arriving)
        {
            if (arriving.B != node || !FlowsThrough(node)) return null;
            var leaving = new List<EditSegment>();
            foreach (EditSegment s in RoadsAt(node))
            {
                if (s == arriving) continue;
                if (s.B == node) return null; // more than one road arrives: they are left to sort themselves out
                leaving.Add(s);
            }
            if (leaving.Count < 2) return null;
            double way = FlowOf(arriving, node);
            Func<EditSegment, double> off = s => Math.Atan2(Math.Sin(FlowOf(s, node) - way), Math.Cos(FlowOf(s, node) - way));
            leaving.Sort((a, b) => off(b).CompareTo(off(a)));
            int lanes = arriving.Type.Forward, total = 0;
            foreach (EditSegment s in leaving) total += s.Type.Forward;
            var given = new List<EditSegment>[lanes];
            for (int lane = 0; lane < lanes; lane++)
            {
                double from = (double)lane * total / lanes, to = (double)(lane + 1) * total / lanes;
                given[lane] = new List<EditSegment>();
                int before = 0;
                foreach (EditSegment s in leaving)
                {
                    if (before + s.Type.Forward > from + 1e-9 && before < to - 1e-9) given[lane].Add(s);
                    before += s.Type.Forward;
                }
            }
            return given;
        }

        /// <summary>
        /// Change the road nearest a point into another kind: wider, narrower, faster, one-way.
        /// Returns null if it was changed, or a short reason why it cannot be.
        /// </summary>
        public string TryReplace(Vec p, RoadType type, bool commit)
        {
            Vec on;
            int seg = NearestSegment(p, SnapRoad * 1.5, out on);
            if (seg < 0) return "There is no road there.";
            if (Segments[seg].Locked) return "That road came with the level.";
            if (Segments[seg].Ring) return "A roundabout's ring cannot be changed.";
            if (Segments[seg].Type.Id == type.Id) return "It is already that kind of road.";
            EditableMap work = Clone();
            EditSegment s = work.Segments[seg];
            s.Type = type;
            if (type.OneWay) s.Extras.RemoveAll(x => !x.Forward);
            if (s.Curved && TightestRadius(work.Trace(s)) < type.MinRadius) return "Its curve is too tight for that kind of road.";
            string problem = work.NodeProblem(s.A) ?? work.NodeProblem(s.B) ?? work.Unbuildable(this, "There is not room for that kind of road there.");
            if (problem != null) return problem;
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        /// <summary>Turn a dropped point into a point of the map, splitting a road if it lands on one.</summary>
        private int Resolve(Vec p, out string error, double beside = 0, RoadType type = null, Snap? farEnd = null)
        {
            error = null;
            Snap snap = SnapTo(p);
            if (snap.Node >= 0) return snap.Node;
            if (snap.Segment < 0 && type != null)
            {
                // On open ground, but so close beside a road that the end of the new one would lie on top of it: it lands on that
                // road. The roads at the new road's other end do not count: it leaves them there, and is bound to be close to them.
                double nearest = double.MaxValue;
                for (int i = 0; i < Segments.Count; i++)
                {
                    EditSegment near = Segments[i];
                    if (near.Level != 0) continue;
                    if (farEnd.HasValue && (farEnd.Value.Segment == i || (farEnd.Value.Node >= 0 && (near.A == farEnd.Value.Node || near.B == farEnd.Value.Node)))) continue;
                    double t;
                    Vec on;
                    double d = Project(Trace(near), p, out t, out on);
                    if (d >= Math.Min(nearest, Touching(type, near.Type))) continue;
                    nearest = d;
                    snap = new Snap { Point = on, Node = -1, Segment = i, T = t };
                }
            }
            if (snap.Segment < 0)
            {
                Nodes.Add(p);
                return Nodes.Count - 1;
            }
            EditSegment s = Segments[snap.Segment];
            if (s.Level != 0)
            {
                error = "A road can only join a bridge or tunnel at its ends.";
                return -1;
            }
            // Right beside the end of a piece: join at that end.
            double fromA = LengthBetween(s, 0, snap.T), fromB = LengthBetween(s, snap.T, 1);
            if (Math.Min(fromA, fromB) < Beside(s, beside)) return fromA <= fromB ? s.A : s.B;
            return Split(snap.Segment, snap.T);
        }

        /// <summary>How close the middles of two roads can be before their pavement touches, with a little to spare.</summary>
        private static double Touching(RoadType one, RoadType other)
        {
            return (one.Width + other.Width) / 2 + 1.5;
        }

        /// <summary>
        /// Whether a new piece of road runs side by side with another road, on top of it: the two
        /// heading nearly the same way (or opposite ways) with their pavement overlapping, for
        /// more than a few meters. Roads that cross, or that come together at an angle, do not count.
        /// </summary>
        private bool Alongside(EditSegment piece)
        {
            if (piece.Level != 0) return false;
            Vec[] line = Trace(piece);
            double length = LengthOf(line);
            int samples = Math.Max(2, (int)(length / 5));
            foreach (EditSegment s in Segments)
            {
                if (s == piece || s.Level != 0) continue;
                // Roads that meet at a point are judged there, by the angle between them.
                if (s.A == piece.A || s.A == piece.B || s.B == piece.A || s.B == piece.B) continue;
                // A one-way road runs in beside the road it joins, or peels away from it, for some way either side of where they meet.
                double clear = piece.Type.OneWay || s.Type.OneWay ? 90 : 0;
                Vec[] other = Trace(s);
                int run = 0;
                for (int i = 0; i <= samples; i++)
                {
                    PathPose here = PoseAlong(line, length * i / samples);
                    double t;
                    Vec on;
                    double d = Project(other, here.Point, out t, out on);
                    bool beside = false;
                    if (Dist(here.Point, Nodes[piece.A]) < clear || Dist(here.Point, Nodes[piece.B]) < clear) { }
                    else if (d < (piece.Type.Width + s.Type.Width) / 2 && t > 1e-6 && t < 1 - 1e-6)
                    {
                        Vec before = PointAt(s, Math.Max(t - 0.01, 0)), after = PointAt(s, Math.Min(t + 0.01, 1));
                        double off = Math.Atan2(after.Y - before.Y, after.X - before.X) - here.H;
                        beside = Math.Abs(Math.Sin(off)) < 0.17;
                    }
                    run = beside ? run + 1 : 0;
                    // Side by side for three samples running: at least 10 m of one road lying on the other.
                    if (run >= 3) return true;
                }
            }
            return false;
        }

        /// <summary>Cut a road in two at a position along it (0 to 1). Returns the new point.</summary>
        private int Split(int segment, double t)
        {
            EditSegment s = Segments[segment];
            Vec at = PointAt(s, t);
            Nodes.Add(at);
            int mid = Nodes.Count - 1;
            var rest = new EditSegment { A = mid, B = s.B, Type = s.Type, Ring = s.Ring, Locked = s.Locked, Limit = s.Limit };
            if (s.Curved)
            {
                // Each part of a curve is a piece of the same circle.
                rest.Curved = true;
                rest.Mid = PointAt(s, (1 + t) / 2);
                s.Mid = PointAt(s, t / 2);
            }
            // An extra lane stays where it is on the ground: on one part, the other, or both.
            var kept = new List<ExtraLane>();
            foreach (ExtraLane x in s.Extras)
            {
                if (x.From < t - 1e-6) kept.Add(new ExtraLane { Forward = x.Forward, From = x.From / t, To = Math.Min(x.To, t) / t });
                if (x.To > t + 1e-6) rest.Extras.Add(new ExtraLane { Forward = x.Forward, From = (Math.Max(x.From, t) - t) / (1 - t), To = (x.To - t) / (1 - t) });
            }
            s.Extras = kept;
            // What the lanes may do at the far end goes with the far part.
            rest.TurnsAtB = s.TurnsAtB;
            s.TurnsAtB = null;
            rest.ControlAtB = s.ControlAtB;
            s.ControlAtB = 0;
            rest.LaneAtB = s.LaneAtB;
            s.LaneAtB = 0;
            Segments.Add(rest);
            s.B = mid;
            return mid;
        }

        /// <summary>Every place two traced lines cross, strictly inside both.</summary>
        private static List<Crossing> Crossings(Vec[] path, Vec[] other)
        {
            var found = new List<Crossing>();
            int n = path.Length - 1, m = other.Length - 1;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < m; k++)
                {
                    Vec p = path[i], p2 = path[i + 1], q = other[k], q2 = other[k + 1];
                    double rx = p2.X - p.X, ry = p2.Y - p.Y;
                    double sx = q2.X - q.X, sy = q2.Y - q.Y;
                    double denom = rx * sy - ry * sx;
                    if (Math.Abs(denom) < 1e-12) continue;
                    double f = ((q.X - p.X) * sy - (q.Y - p.Y) * sx) / denom;
                    double g = ((q.X - p.X) * ry - (q.Y - p.Y) * rx) / denom;
                    if (f < 0 || f >= 1 || g < 0 || g >= 1) continue;
                    double t = (i + f) / n, u = (k + g) / m;
                    if (t <= 1e-6 || t >= 1 - 1e-6 || u <= 1e-6 || u >= 1 - 1e-6) continue;
                    found.Add(new Crossing { T = t, U = u, At = new Vec(p.X + rx * f, p.Y + ry * f) });
                }
            }
            return found;
        }

        // ---------------------------------------------------------------- roundabouts

        /// <summary>The smallest and largest a roundabout may be, as the radius of the middle of its ring.</summary>
        public const double MinRing = 16, MaxRing = 150;
        /// <summary>The shortest stretch of ring allowed between two roads that join it.</summary>
        public const double RingPiece = 24;

        /// <summary>
        /// The shortest piece a road may be cut into where another road joins or crosses it. It
        /// only keeps a new junction from landing right beside the end of a road; whether two
        /// junctions have room between them is settled when the network is built.
        /// </summary>
        private static double Shortest(EditSegment s)
        {
            return s.Ring ? RingPiece : SnapNode + 2;
        }

        /// <summary>
        /// How close to the end of a piece a new road may land on it and still get a junction of
        /// its own; any closer and it goes through the end. That is the shortest piece, unless
        /// the road is being fitted in where there is no room, when it is taken further.
        /// </summary>
        private static double Beside(EditSegment s, double beside)
        {
            return s.Ring ? Shortest(s) : Math.Max(Shortest(s), beside);
        }

        /// <summary>The road a roundabout's ring is made of: one lane, one-way, of the same class as the road chosen.</summary>
        public static RoadType RingType(RoadType chosen)
        {
            if (chosen.OneWay && chosen.Forward == 1 && chosen.Group != RoadType.Highway && RoadType.ById(chosen.Id) != null) return chosen;
            return RoadType.ById(chosen.Group == RoadType.Street ? "s1" : "a1");
        }

        /// <summary>
        /// Build a roundabout: a one-way ring that traffic goes round counter-clockwise. Roads
        /// that cross the ring are cut where they meet it and join it there; whatever lies
        /// inside is removed. Returns null if it was built, or a short reason why it cannot be.
        /// </summary>
        public string TryAddRoundabout(Vec center, double radius, RoadType chosen, bool commit)
        {
            if (radius < MinRing) return "Too small for a roundabout.";
            if (Bound > 0 && (Math.Abs(center.X) + radius > Bound || Math.Abs(center.Y) + radius > Bound)) return "That is outside the area you can build in.";
            if (radius > MaxRing) return "Too big for a roundabout.";
            EditableMap work = Clone();
            string error = work.AddRing(center, radius, RingType(chosen));
            if (error != null) return error;
            try
            {
                work.ToWorld();
            }
            catch (Exception)
            {
                return "Roads join the roundabout too close together.";
            }
            if (commit)
            {
                Nodes = work.Nodes;
                Segments = work.Segments;
            }
            return null;
        }

        private string AddRing(Vec c, double r, RoadType type)
        {
            foreach (EditSegment s in Segments) if (s.Ring && Dist(PointAt(s, 0.5), c) < r + 40) return "Too close to another roundabout.";

            // Cut every road where it crosses the ring.
            var onRing = new List<int>();
            int existing = Segments.Count;
            for (int i = 0; i < existing; i++)
            {
                List<Crossing> crossings = CircleCrossings(Trace(Segments[i]), c, r);
                if (crossings.Count > 0 && Segments[i].Level != 0) return "A bridge or tunnel is in the way.";
                if (crossings.Count > 0 && Segments[i].Locked) return "A road that came with the level is in the way.";
                crossings.Sort((m, n) => n.T.CompareTo(m.T));
                double upTo = 1;
                foreach (Crossing x in crossings)
                {
                    EditSegment s = Segments[i];
                    if (Dist(x.At, Nodes[s.A]) < 4 || Dist(x.At, Nodes[s.B]) < 4) return "A road ends right on the ring. Move the roundabout a little.";
                    // The road should come in across the ring, not along it.
                    Vec before = PointAt(s, Math.Max(0, x.T / upTo - 0.02)), after = PointAt(s, Math.Min(1, x.T / upTo + 0.02));
                    double along = Math.Atan2(after.Y - before.Y, after.X - before.X), outward = Math.Atan2(x.At.Y - c.Y, x.At.X - c.X);
                    if (Math.Abs(Math.Cos(along - outward)) < Math.Cos(Math.PI / 2 - MergeAngle)) return "A road meets the ring at too shallow an angle.";
                    int node = Split(i, x.T / upTo);
                    double d = Dist(Nodes[node], c);
                    Nodes[node] = new Vec(c.X + (Nodes[node].X - c.X) / d * r, c.Y + (Nodes[node].Y - c.Y) / d * r);
                    onRing.Add(node);
                    upTo = x.T;
                }
            }
            // Take away what is inside, and check that what is left outside is long enough to be a road.
            foreach (EditSegment s in Segments) if (s.Locked && Dist(PointAt(s, 0.5), c) < r) return "A road that came with the level is in the way.";
            Segments.RemoveAll(s => Dist(PointAt(s, 0.5), c) < r);
            foreach (EditSegment s in Segments)
            {
                if ((onRing.Contains(s.A) || onRing.Contains(s.B)) && LengthOf(Trace(s)) < 20) return "That would leave a road too short beside the roundabout.";
            }

            // The ring needs at least three points round it, and no stretch of more than half a turn.
            var angles = new List<KeyValuePair<double, int>>();
            foreach (int node in onRing) angles.Add(new KeyValuePair<double, int>(Math.Atan2(Nodes[node].Y - c.Y, Nodes[node].X - c.X), node));
            if (angles.Count == 0)
            {
                Nodes.Add(new Vec(c.X + r, c.Y));
                angles.Add(new KeyValuePair<double, int>(0, Nodes.Count - 1));
            }
            while (true)
            {
                angles.Sort((m, n) => m.Key.CompareTo(n.Key));
                int widest = 0;
                double gap = 0;
                for (int i = 0; i < angles.Count; i++)
                {
                    double g = angles[(i + 1) % angles.Count].Key - angles[i].Key;
                    if (g <= 0) g += 2 * Math.PI;
                    if (g > gap)
                    {
                        gap = g;
                        widest = i;
                    }
                }
                if (angles.Count >= 3 && gap <= Math.PI + 0.01) break;
                double at = angles[widest].Key + gap / 2;
                if (at > Math.PI) at -= 2 * Math.PI;
                Nodes.Add(new Vec(c.X + Math.Cos(at) * r, c.Y + Math.Sin(at) * r));
                angles.Add(new KeyValuePair<double, int>(at, Nodes.Count - 1));
            }
            for (int i = 0; i < angles.Count; i++)
            {
                double from = angles[i].Key, to = angles[(i + 1) % angles.Count].Key;
                if (to <= from) to += 2 * Math.PI;
                if ((to - from) * r < RingPiece) return "Roads join the roundabout too close together.";
                double mid = (from + to) / 2;
                Segments.Add(new EditSegment
                {
                    A = angles[i].Value, B = angles[(i + 1) % angles.Count].Value, Type = type, Curved = true, Ring = true,
                    Mid = new Vec(c.X + Math.Cos(mid) * r, c.Y + Math.Sin(mid) * r),
                });
            }
            DropUnusedNodes();
            return null;
        }

        /// <summary>Every place a traced line crosses a circle.</summary>
        private static List<Crossing> CircleCrossings(Vec[] path, Vec c, double r)
        {
            var found = new List<Crossing>();
            int n = path.Length - 1;
            for (int i = 0; i < n; i++)
            {
                // Where along this piece is it exactly r from the center? Solve |p + d f - c| = r for f.
                Vec p = path[i];
                double dx = path[i + 1].X - p.X, dy = path[i + 1].Y - p.Y, px = p.X - c.X, py = p.Y - c.Y;
                double qa = dx * dx + dy * dy, qb = 2 * (px * dx + py * dy), qc = px * px + py * py - r * r;
                double disc = qb * qb - 4 * qa * qc;
                if (qa < 1e-12 || disc <= 0) continue;
                foreach (double sign in new[] { -1.0, 1.0 })
                {
                    double f = (-qb + sign * Math.Sqrt(disc)) / (2 * qa);
                    if (f < 0 || f >= 1) continue;
                    double t = (i + f) / n;
                    if (t <= 1e-6 || t >= 1 - 1e-6) continue;
                    found.Add(new Crossing { T = t, At = new Vec(p.X + dx * f, p.Y + dy * f) });
                }
            }
            return found;
        }

        // ---------------------------------------------------------------- merging

        /// <summary>
        /// A place where a one-way road runs into another road alongside it (a merge), or peels
        /// off it (an exit), with no intersection: the road carries straight on through.
        /// </summary>
        private sealed class Join
        {
            /// <summary>The one-way road joining or leaving.</summary>
            public EditSegment Ramp;
            /// <summary>The road it joins, before and after the join in the direction of the traffic concerned.</summary>
            public EditSegment Up, Down;
            /// <summary>True where the ramp's traffic flows in; false where it flows out.</summary>
            public bool Merge;
            /// <summary>The way that traffic is heading on the road at the join.</summary>
            public Vec Along;
            /// <summary>How many lanes the road has in that direction, and how far their outside edge is from its centerline.</summary>
            public int Lanes;
            public double Edge;
        }

        /// <summary>Roads may join as a merge when they meet at less than this angle; steeper and they make an intersection.</summary>
        public const double MergeAngle = 40 * Math.PI / 180;
        /// <summary>The slight angle a ramp keeps to the road where it meets it.</summary>
        private const double RampTilt = 0.14;

        /// <summary>The merge or exit at a point, if the roads there make one.</summary>
        private Join JoinAt(int node)
        {
            var here = new List<EditSegment>();
            foreach (EditSegment s in Segments) if (s.A == node || s.B == node) here.Add(s);
            if (here.Count != 3) return null;
            // On a roundabout every way in is a give-way junction, never a merge.
            foreach (EditSegment s in here) if (s.Ring) return null;
            for (int i = 0; i < 3; i++)
            {
                EditSegment ramp = here[i], x = here[(i + 1) % 3], y = here[(i + 2) % 3];
                if (!ramp.Type.OneWay || ramp.A == ramp.B || x.Type.Id != y.Type.Id) continue;
                // The other two must be one road carrying straight on.
                double hx = HeadingFrom(x, node), hy = HeadingFrom(y, node);
                if (Math.Abs(Math.Atan2(Math.Sin(hx - hy), Math.Cos(hx - hy))) < Math.PI - 0.26) continue;
                double hr = HeadingFrom(ramp, node);
                bool merge = ramp.B == node;
                // The way the ramp's traffic is moving at the join.
                double ux = Math.Cos(hr) * (merge ? -1 : 1), uy = Math.Sin(hr) * (merge ? -1 : 1);
                bool alongX = ux * Math.Cos(hx) + uy * Math.Sin(hx) > 0;
                EditSegment down = alongX ? x : y, up = alongX ? y : x;
                double h = alongX ? hx : hy;
                var along = new Vec(Math.Cos(h), Math.Sin(h));
                if (Math.Acos(Geo.Clamp(ux * along.X + uy * along.Y, -1, 1)) >= MergeAngle) continue;
                // The ramp must lie to the right of the traffic it joins or leaves.
                if (along.X * Math.Sin(hr) - along.Y * Math.Cos(hr) >= 0) continue;
                // A one-way road only runs one way: it must be running the way the ramp needs.
                if (down.Type.OneWay && (up.B != node || down.A != node)) continue;
                int lanes = down.Type.OneWay || down.A == node ? down.Type.Forward : down.Type.Backward;
                return new Join { Ramp = ramp, Up = up, Down = down, Merge = merge, Along = along, Lanes = lanes, Edge = lanes * NetworkBuilder.LaneW + down.Type.Inner };
            }
            return null;
        }

        /// <summary>
        /// Reshape the end of a ramp so that it comes in beside the lanes of the road it joins,
        /// not at the middle of it. The trace is given running toward the join. Returns the
        /// trace to build (stopping just short of the join) and the sideways shift to build it with.
        /// </summary>
        private static Vec[] BesideTheRoad(Vec[] trace, Vec node, Join join, double rampWidth, out double shift)
        {
            double tilt = RampTilt, r = NetworkBuilder.FreeRadius;
            double side = join.Edge + rampWidth / 2;
            shift = (side - r * Math.Sin(tilt)) / Math.Cos(tilt);
            // Along the road and to its right, as seen by the traffic the ramp joins.
            Vec h = join.Along, right = new Vec(h.Y, -h.X);
            // A merge arrives a little before the join, heading slightly in; an exit is the same thing seen backward.
            double back = join.Merge ? 1 : -1;
            double along = (-r * Math.Cos(tilt) + shift * Math.Sin(tilt)) * back;
            var end = new Vec(node.X + h.X * along + right.X * side, node.Y + h.Y * along + right.Y * side);
            double heading = Math.Atan2(h.Y, h.X) + tilt * back + (join.Merge ? 0 : Math.PI);

            // Keep the ramp as drawn up to a point some way back, then sweep in to the new end.
            double total = LengthOf(trace), from = total - Math.Min(70, total * 0.55), run = 0;
            var path = new List<Vec>();
            int cut = 0;
            for (int i = 0; i + 1 < trace.Length && run + Dist(trace[i], trace[i + 1]) < from; i++)
            {
                run += Dist(trace[i], trace[i + 1]);
                path.Add(trace[i]);
                cut = i + 1;
            }
            Vec before = trace[Math.Max(cut - 1, 0)], at = trace[cut];
            if (cut == 0) before = new Vec(at.X - (trace[1].X - at.X), at.Y - (trace[1].Y - at.Y));
            var start = new PathPose { X = at.X, Y = at.Y, H = Math.Atan2(at.Y - before.Y, at.X - before.X) };
            var finish = new PathPose { X = end.X, Y = end.Y, H = heading };
            Vec[] sweep = Geo.Bezier(start, finish, Dist(at, end) * 0.4, 24);
            // Stop a little short: the builder adds the last point itself, from the shift.
            foreach (Vec p in sweep) if (Dist(p, end) > 2) path.Add(p);
            return path.ToArray();
        }

        // ---------------------------------------------------------------- removing

        /// <summary>Remove the road nearest a point. Returns false if none is within reach.</summary>
        public bool RemoveNear(Vec p)
        {
            Vec on;
            int seg = NearestSegment(p, SnapRoad * 1.5, out on);
            if (seg < 0 || Segments[seg].Locked) return false;
            Segments.RemoveAt(seg);
            DropUnusedNodes();
            return true;
        }

        /// <summary>Lay a road that comes with a level. It cannot be removed, costs nothing, and may run outside the building area.</summary>
        public void AddLocked(Vec p, Vec q, RoadType type, Vec? through = null)
        {
            double bound = Bound;
            Bound = 0;
            int before = Segments.Count;
            string error = Add(p, q, type, through, 0);
            Bound = bound;
            if (error != null) throw new InvalidOperationException("A level's own road could not be laid: " + error);
            for (int i = before; i < Segments.Count; i++) Segments[i].Locked = true;
        }

        private void DropUnusedNodes()
        {
            var used = new bool[Nodes.Count];
            foreach (EditSegment s in Segments) used[s.A] = used[s.B] = true;
            var renumber = new int[Nodes.Count];
            var kept = new List<Vec>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                renumber[i] = kept.Count;
                if (used[i]) kept.Add(Nodes[i]);
            }
            Nodes = kept;
            foreach (EditSegment s in Segments)
            {
                s.A = renumber[s.A];
                s.B = renumber[s.B];
            }
        }

        /// <summary>Remove everything the player has built. Roads that came with a level stay.</summary>
        public void Clear()
        {
            Segments.RemoveAll(s => !s.Locked);
            DropUnusedNodes();
        }

        // ---------------------------------------------------------------- building

        /// <summary>
        /// One road as traffic sees it: a run of stretches of the same kind joined end to end,
        /// at points where nothing else meets them, into a single line with its corners rounded.
        /// The player draws a road in as many pieces as they like; the network gets one road
        /// from each junction or road end to the next.
        /// </summary>
        private sealed class Run
        {
            public readonly List<EditSegment> Pieces = new List<EditSegment>();
            /// <summary>For each piece, whether the run goes along it from its B end to its A end.</summary>
            public readonly List<bool> Turned = new List<bool>();
            /// <summary>The points at its two ends, and the points between its pieces.</summary>
            public int From, To;
            public readonly List<int> Joints = new List<int>();
            /// <summary>For each joint, whether its corner is too sharp for the road: there was not room to round it.</summary>
            public bool[] Sharp;
            /// <summary>Whether each end meets other roads, and so loses its last few meters to the junction there.</summary>
            public bool JunctionAtFrom, JunctionAtTo;
            public Vec[] Trace;
        }

        /// <summary>A corner turns this little, in radians, without needing to be rounded.</summary>
        private const double Gentle = 0.035;
        /// <summary>How much of a road's end is left alone when rounding corners, where it meets a junction; and the less that a tight corner leaves.</summary>
        private const double JunctionRoom = 16, CornerRoom = 13;

        /// <summary>
        /// Whether straight stretches of this kind of road meet in a tight corner, like the corner
        /// of a city block, instead of a sweeping curve. Streets and avenues do: the player lays
        /// out grids with them. A highway always sweeps round.
        /// </summary>
        private static bool SquareCorners(RoadType type)
        {
            return type.Mph < 55;
        }

        /// <summary>The radius the middle of a road turns a tight corner with: it leaves the inside edge of the road a curve of 5 m.</summary>
        private static double CornerRadius(RoadType type)
        {
            return type.Width / 2 + 5;
        }

        /// <summary>
        /// Whether the two roads at a point carry on as one road: the same kind of road, on the
        /// ground, with nothing else there, and (if one-way) running the same way through.
        /// </summary>
        private bool CarriesOn(List<EditSegment> here, int node)
        {
            if (here.Count != 2) return false;
            EditSegment x = here[0], y = here[1];
            if (x == y || x.Ring || y.Ring || x.Level != 0 || y.Level != 0 || x.Type.Id != y.Type.Id || x.Limit != y.Limit) return false;
            // The lanes one way must match the lanes that way on the other piece.
            bool sameWay = (x.B == node) != (y.B == node);
            return sameWay || x.Type.Forward == x.Type.Backward;
        }

        /// <summary>Every road on the map, as runs of pieces.</summary>
        private List<Run> Runs()
        {
            var at = new List<EditSegment>[Nodes.Count];
            for (int i = 0; i < at.Length; i++) at[i] = new List<EditSegment>();
            foreach (EditSegment s in Segments)
            {
                at[s.A].Add(s);
                at[s.B].Add(s);
            }
            var joint = new bool[Nodes.Count];
            for (int i = 0; i < joint.Length; i++) joint[i] = CarriesOn(at[i], i);

            var runs = new List<Run>();
            var done = new HashSet<EditSegment>();
            Action<int, EditSegment> walk = (start, first) =>
            {
                var run = new Run { From = start };
                int node = start;
                EditSegment piece = first;
                while (true)
                {
                    run.Pieces.Add(piece);
                    run.Turned.Add(piece.A != node);
                    done.Add(piece);
                    node = piece.A == node ? piece.B : piece.A;
                    if (!joint[node]) break;
                    run.Joints.Add(node);
                    piece = at[node][0] == piece ? at[node][1] : at[node][0];
                }
                run.To = node;
                // A one-way road runs the way its traffic does, and a single stretch the way it was drawn.
                if (run.Turned[0] && (first.Type.OneWay || run.Pieces.Count == 1))
                {
                    run.Pieces.Reverse();
                    run.Turned.Reverse();
                    for (int i = 0; i < run.Turned.Count; i++) run.Turned[i] = !run.Turned[i];
                    run.Joints.Reverse();
                    int swap = run.From;
                    run.From = run.To;
                    run.To = swap;
                }
                runs.Add(run);
            };
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (joint[i]) continue;
                foreach (EditSegment s in at[i]) if (!done.Contains(s)) walk(i, s);
            }
            // What is left is rings of pieces with no junction anywhere on them: each needs one point to begin and end
            // at. That point stays a bend while the rest are rounded off, so take the one where the ring turns least.
            foreach (EditSegment s in Segments)
            {
                if (done.Contains(s)) continue;
                int begin = s.A, node = s.A;
                EditSegment first = s, piece = s;
                double least = double.MaxValue;
                do
                {
                    EditSegment other = at[node][0] == piece ? at[node][1] : at[node][0];
                    double off = HeadingFrom(piece, node) - HeadingFrom(other, node) - Math.PI;
                    double turns = Math.Abs(Math.Atan2(Math.Sin(off), Math.Cos(off)));
                    if (turns < least - 1e-6)
                    {
                        least = turns;
                        begin = node;
                        first = piece;
                    }
                    node = piece.A == node ? piece.B : piece.A;
                    piece = at[node][0] == piece ? at[node][1] : at[node][0];
                }
                while (node != s.A && joint[node]);
                joint[begin] = false;
                walk(begin, first);
            }
            foreach (Run run in runs)
            {
                // A dead end's turning place takes up the end of the road as a junction does.
                run.JunctionAtFrom = at[run.From].Count > 1 || DeadEndAt(run.From);
                run.JunctionAtTo = at[run.To].Count > 1 || DeadEndAt(run.To);
                Lay(run, run.JunctionAtFrom, run.JunctionAtTo);
            }
            // In the order their first pieces were drawn, so that the network comes out the same however the walk went.
            var drawn = new Dictionary<EditSegment, int>();
            for (int i = 0; i < Segments.Count; i++) drawn[Segments[i]] = i;
            Func<Run, int> earliest = run =>
            {
                int first = int.MaxValue;
                foreach (EditSegment s in run.Pieces) first = Math.Min(first, drawn[s]);
                return first;
            };
            runs.Sort((x, y) => earliest(x).CompareTo(earliest(y)));
            return runs;
        }

        /// <summary>The spot a distance along a traced line, and the way the line is heading there.</summary>
        private static PathPose PoseAlong(Vec[] line, double s)
        {
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double dx = line[i + 1].X - line[i].X, dy = line[i + 1].Y - line[i].Y, len = Geo.Hypot(dx, dy);
                if (len < 1e-9) continue;
                if (s <= len || i + 2 == line.Length)
                {
                    double f = Geo.Clamp(s / len, 0, 1);
                    return new PathPose { X = line[i].X + dx * f, Y = line[i].Y + dy * f, H = Math.Atan2(dy, dx) };
                }
                s -= len;
            }
            return new PathPose { X = line[line.Length - 1].X, Y = line[line.Length - 1].Y };
        }

        /// <summary>
        /// Work out the line a run follows. Where two of its pieces meet at an angle the corner
        /// is rounded. Two straight stretches of a street or avenue get a tight corner; where a
        /// curve is involved, or on a highway, the corner takes as much of the pieces either side
        /// as a comfortable curve needs, or as much as there is. A corner left tighter than the
        /// road allows is marked as sharp.
        /// </summary>
        private void Lay(Run run, bool junctionAtFrom, bool junctionAtTo)
        {
            RoadType type = run.Pieces[0].Type;
            int count = run.Pieces.Count;
            var lines = new Vec[count][];
            var length = new double[count];
            for (int i = 0; i < count; i++)
            {
                EditSegment s = run.Pieces[i];
                // A point every few meters, on straight pieces as well as curved ones.
                Vec a = Nodes[s.A], b = Nodes[s.B];
                int steps = Math.Max(s.Curved ? 8 : 1, (int)Math.Ceiling((s.Curved ? LengthOf(Trace(s)) : Dist(a, b)) / 3));
                lines[i] = s.Curved ? Trace(a, b, true, s.Mid, steps) : new Vec[steps + 1];
                if (!s.Curved) for (int k = 0; k <= steps; k++) lines[i][k] = Lerp(a, b, (double)k / steps);
                if (run.Turned[i]) Array.Reverse(lines[i]);
                length[i] = LengthOf(lines[i]);
            }
            run.Sharp = new bool[Math.Max(count - 1, 0)];
            if (count == 1)
            {
                run.Trace = lines[0];
                return;
            }

            // How far each corner turns, and how much of each piece it would like for its curve.
            var turn = new double[count - 1];
            var want = new double[count - 1];
            var take = new double[count - 1];
            var square = new bool[count - 1];
            for (int j = 0; j < count - 1; j++)
            {
                double arrive = PoseAlong(lines[j], length[j]).H, leave = PoseAlong(lines[j + 1], 0).H;
                turn[j] = Math.Abs(Math.Atan2(Math.Sin(leave - arrive), Math.Cos(leave - arrive)));
                double tan = Math.Tan(Math.Min(turn[j], 3) / 2), flowing = type.MinRadius * 1.5 * tan;
                // Two straight stretches of a street or an avenue make a corner, as at the corner of a city block: a
                // tight turn, not a sweep. (A slight change of direction still gets a curve long enough to look like one.)
                square[j] = SquareCorners(type) && !run.Pieces[j].Curved && !run.Pieces[j + 1].Curved;
                want[j] = turn[j] < Gentle ? 0 : square[j] ? Math.Min(flowing, Math.Max(CornerRadius(type) * tan, 8)) : flowing;
            }
            var room = new double[count];
            for (int i = 0; i < count; i++)
            {
                // A tight corner may come closer to a junction than a sweeping one: it only has to keep clear of the junction itself.
                double atFrom = i == 0 && junctionAtFrom ? (square[0] ? CornerRoom : JunctionRoom) : 0;
                double atTo = i == count - 1 && junctionAtTo ? (square[count - 2] ? CornerRoom : JunctionRoom) : 0;
                room[i] = Math.Max(length[i] - atFrom - atTo, 0);
            }
            // Share each piece between the corners at its two ends; then let each corner have what its neighbors left.
            for (int j = 0; j < count - 1; j++)
            {
                double before = room[j] * (j > 0 && want[j - 1] > 0 ? 0.5 : 1), after = room[j + 1] * (j + 2 < count && want[j + 1] > 0 ? 0.5 : 1);
                take[j] = Math.Min(want[j], Math.Min(before, after));
            }
            for (int j = 0; j < count - 1; j++)
            {
                double before = room[j] - (j > 0 ? take[j - 1] : 0), after = room[j + 1] - (j + 2 < count ? take[j + 1] : 0);
                take[j] = Math.Max(take[j], Math.Min(want[j], Math.Min(before, after)));
                // A corner that hardly turns is no trouble even left as it is. A square corner only has to leave
                // the inside edge of the road something to turn round; any other must be as gentle as the road's curves.
                double least = square[j] ? type.Width / 2 + 1 : type.MinRadius * 0.98;
                run.Sharp[j] = turn[j] > 0.14 && take[j] / Math.Tan(Math.Min(turn[j], 3) / 2) < least;
            }

            var trace = new List<Vec>();
            Action<Vec> add = p =>
            {
                if (trace.Count == 0 || Dist(trace[trace.Count - 1], p) > 0.05) trace.Add(p);
            };
            for (int i = 0; i < count; i++)
            {
                double from = i > 0 ? take[i - 1] : 0, to = length[i] - (i < count - 1 ? take[i] : 0);
                // The part of this piece that is left between its corners.
                add(PoseAlong(lines[i], from).Point);
                double along = 0;
                for (int k = 0; k + 1 < lines[i].Length; k++)
                {
                    along += Dist(lines[i][k], lines[i][k + 1]);
                    if (along > from + 0.05 && along < to - 0.05) add(lines[i][k + 1]);
                }
                PathPose end = PoseAlong(lines[i], to);
                add(end.Point);
                if (i == count - 1 || take[i] < 0.05) continue;
                // The corner: a smooth sweep from the way this piece is heading to the way the next one is.
                PathPose next = PoseAlong(lines[i + 1], take[i]);
                double bend = Math.Abs(Math.Atan2(Math.Sin(next.H - end.H), Math.Cos(next.H - end.H))), chord = Dist(end.Point, next.Point);
                double reach = bend < 1e-3 ? chord / 3 : 4.0 / 3 * Math.Tan(bend / 4) * chord / (2 * Math.Sin(bend / 2));
                Vec[] sweep = Geo.Bezier(end, next, reach, Math.Max(6, (int)Math.Ceiling(chord / 2.5)));
                for (int k = 1; k < sweep.Length - 1; k++) add(sweep[k]);
            }
            run.Trace = trace.ToArray();
        }

        /// <summary>
        /// The points where a road turns a corner too sharply for its kind: there was not room
        /// either side to round the corner off. New ones are refused as they are drawn.
        /// </summary>
        public List<int> SharpCorners()
        {
            var sharp = new List<int>();
            foreach (Run run in Runs())
            {
                for (int j = 0; j < run.Joints.Count; j++) if (run.Sharp[j]) sharp.Add(run.Joints[j]);
            }
            return sharp;
        }

        /// <summary>What a change is refused with when it would put two junctions too close together for the network to build.</summary>
        public const string NoRoom = "Not enough room there: the junctions would be too close together.";
        /// <summary>What a change is refused with when it would leave a corner with no room to be rounded off.</summary>
        public const string TooSharpCorner = "Too sharp a corner for this kind of road. Make the turn gentler, or leave more room either side of it.";
        /// <summary>The same, when the corner was there already and it is something new beside it that takes its room.</summary>
        public const string TooNearCorner = "Too close to the corner there. Leave it a little more room, or go through the corner itself.";

        /// <summary>How far a road turns at a point in the middle of it, where two of its pieces meet and nothing else does, in radians; -1 at any other point.</summary>
        private double TurnAt(int node)
        {
            var here = new List<EditSegment>();
            foreach (EditSegment s in Segments) if (s.A == node || s.B == node) here.Add(s);
            if (!CarriesOn(here, node)) return -1;
            double off = HeadingFrom(here[0], node) - HeadingFrom(here[1], node) - Math.PI;
            return Math.Abs(Math.Atan2(Math.Sin(off), Math.Cos(off)));
        }

        /// <summary>
        /// Where a road dropped at a point should really join. A point on a road that is too
        /// close to a junction, or to the end of the road, for a junction of its own to fit
        /// there is moved to that junction or road end; any other point is left where it is.
        /// </summary>
        public Vec JoinPoint(Vec p)
        {
            Snap snap = SnapTo(p);
            if (snap.Node >= 0 || snap.Segment < 0) return snap.Point;
            EditSegment on = Segments[snap.Segment];
            foreach (Run run in Runs())
            {
                int index = run.Pieces.IndexOf(on);
                if (index < 0) continue;
                // How far along the whole road the point is, from each end.
                double before = 0, total = 0;
                for (int i = 0; i < run.Pieces.Count; i++)
                {
                    double length = LengthOf(Trace(run.Pieces[i]));
                    if (i < index) before += length;
                    else if (i == index) before += length * (run.Turned[i] ? 1 - snap.T : snap.T);
                    total += length;
                }
                // A junction needs about 12 m to itself, and 15 m of road between it and the next thing; a road end needs less.
                double fromNeeds = run.JunctionAtFrom ? 40 : 30, toNeeds = run.JunctionAtTo ? 40 : 30;
                bool nearFrom = before < fromNeeds, nearTo = total - before < toNeeds;
                if (nearFrom && (!nearTo || before <= total - before)) return Nodes[run.From];
                if (nearTo) return Nodes[run.To];
                return snap.Point;
            }
            return snap.Point;
        }

        /// <summary>The junctions and road ends within reach of a point, nearest first: places a road that has no room where it is could join instead.</summary>
        public List<Vec> JoinsNear(Vec p, double reach)
        {
            var roads = new int[Nodes.Count];
            foreach (EditSegment s in Segments)
            {
                roads[s.A]++;
                roads[s.B]++;
            }
            var near = new List<Vec>();
            for (int i = 0; i < Nodes.Count; i++) if (roads[i] != 2 && Dist(Nodes[i], p) < reach && Dist(Nodes[i], p) > 0.5) near.Add(Nodes[i]);
            near.Sort((a, b) => Dist(a, p).CompareTo(Dist(b, p)));
            return near;
        }

        /// <summary>
        /// Why the map as it now stands cannot be built, given the map it was changed from: a
        /// corner the change has made too sharp, or junctions with no room between them. Null if it can be.
        /// </summary>
        // ---------------------------------------------------------------- water

        /// <summary>
        /// Lakes and rivers: each the corners of its shape, in order. The land is flat, and so
        /// is the water. A road on the ground cannot cross it; a bridge can, once it has
        /// climbed, and so can a tunnel, once it is down. Water comes with a level, like its
        /// roads and its edge, and is not saved with the drawing.
        /// </summary>
        public List<Vec[]> Waters = new List<Vec[]>();
        /// <summary>How high a bridge has to be over water, or how deep a tunnel under it, meters.</summary>
        public const double WaterClearance = 3;
        public const string InWater = "A road cannot cross water on the ground. It needs a bridge.";
        public const string RampInWater = "A bridge has to climb on dry land: start it further back from the water, and end it further on.";

        /// <summary>A river as a shape: so wide, along a line.</summary>
        public static Vec[] River(IReadOnlyList<Vec> line, double width)
        {
            Vec[] one = Geo.Offset(line, width / 2), other = Geo.Offset(line, -width / 2);
            var shape = new List<Vec>(one);
            for (int i = other.Length - 1; i >= 0; i--) shape.Add(other[i]);
            return shape.ToArray();
        }

        /// <summary>A lake as a shape: round, more or less, about a point. The bumps give it a shore that is not a circle.</summary>
        public static Vec[] Lake(Vec middle, double radius, int seed = 1)
        {
            const int sides = 28;
            var shape = new Vec[sides];
            for (int i = 0; i < sides; i++)
            {
                double a = i * 2 * Math.PI / sides;
                double r = radius * (1 + 0.14 * Math.Sin(a * 2 + seed) + 0.08 * Math.Sin(a * 3 + seed * 2.3) + 0.05 * Math.Sin(a * 5 + seed * 0.7));
                shape[i] = new Vec(middle.X + Math.Cos(a) * r, middle.Y + Math.Sin(a) * r);
            }
            return shape;
        }

        public bool OverWater(Vec p)
        {
            foreach (Vec[] water in Waters) if (Geo.Inside(p, water)) return true;
            return false;
        }

        /// <summary>
        /// What is wrong with a road along a line as far as water goes, at a level: nothing
        /// (null), or it is on the ground in the water, or it is a bridge or tunnel still
        /// near the ground where the water is.
        /// </summary>
        private string WaterProblem(Vec[] line, double width, int level)
        {
            if (Waters.Count == 0) return null;
            double total = LengthOf(line), along = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double dx = line[i + 1].X - line[i].X, dy = line[i + 1].Y - line[i].Y, len = Geo.Hypot(dx, dy);
                if (len < 1e-9) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(len / 3));
                for (int k = 0; k <= steps; k++)
                {
                    double f = (double)k / steps, x = line[i].X + dx * f, y = line[i].Y + dy * f;
                    // The middle of the road and both its edges.
                    bool wet = false;
                    foreach (double side in new[] { 0, width / 2, -width / 2 })
                    {
                        if (OverWater(new Vec(x + dy / len * side, y - dx / len * side))) wet = true;
                    }
                    if (!wet) continue;
                    if (level == 0) return InWater;
                    if (Math.Abs(HeightAlong(level, along + len * f, total)) < WaterClearance) return RampInWater;
                }
                along += len;
            }
            return null;
        }

        /// <summary>The first thing wrong with the roads as far as water goes, or null. Roads that came with a level are taken as they are.</summary>
        private string WaterProblem()
        {
            if (Waters.Count == 0) return null;
            foreach (EditSegment s in Segments)
            {
                if (s.Locked) continue;
                string problem = WaterProblem(Trace(s), s.Type.Width, s.Level);
                if (problem != null) return problem;
            }
            return null;
        }

        /// <summary>
        /// The level a road drawn from one point to another should be built at: the one asked
        /// for, except that a road asked for on the ground which would cross water is a bridge.
        /// (Whether a bridge can be built there is for TryAdd to say.)
        /// </summary>
        public int LevelOver(Vec p, Vec q, RoadType type, Vec? through, int level)
        {
            if (level != 0 || Waters.Count == 0) return level;
            Vec[] line = Trace(p, q, through.HasValue, through ?? default(Vec), 48);
            return WaterProblem(line, type.Width, 0) != null ? 1 : 0;
        }

        private string Unbuildable(EditableMap before, string noRoom = NoRoom)
        {
            string wet = WaterProblem();
            if (wet != null) return wet;
            var already = new HashSet<int>(before.SharpCorners());
            foreach (int node in SharpCorners())
            {
                if (already.Contains(node)) continue;
                // A corner that was there before, turning just as it does now, has been left without room by something landing close beside it.
                double turned = TurnAt(node), turnedBefore = node < before.Nodes.Count && Dist(before.Nodes[node], Nodes[node]) < 0.01 ? before.TurnAt(node) : -1;
                return turnedBefore >= 0 && Math.Abs(turned - turnedBefore) < 1e-3 ? TooNearCorner : TooSharpCorner;
            }
            try
            {
                ToWorld();
            }
            catch (Exception)
            {
                return noRoom;
            }
            return null;
        }

        /// <summary>
        /// Turn the drawing into a network. Stretches that carry on from one another become one
        /// road with its corners rounded. Where roads meet, the control is chosen from the roads
        /// themselves: two different roads are just a bend; three or more quiet streets (25 mph)
        /// get stop signs all round; anything faster gets a traffic light. A road end with
        /// nothing joined to it is the edge of the map, where traffic comes in and leaves.
        /// </summary>
        public World ToWorld()
        {
            var b = new NetworkBuilder();
            List<Run> runs = Runs();
            // A point between two pieces of one road is not a junction: the network never hears of it.
            var inside = new bool[Nodes.Count];
            foreach (Run run in runs) foreach (int node in run.Joints) inside[node] = true;
            var joins = new Join[Nodes.Count];
            var built = new int[Nodes.Count];
            // The sign each piece's traffic faces at each of its ends, where an intersection is run by signs.
            var signs = new Dictionary<EditSegment, Sign[]>();
            for (int i = 0; i < Nodes.Count; i++)
            {
                built[i] = -1;
                if (inside[i]) continue;
                int arms = 0, fastest = 0;
                foreach (EditSegment s in Segments)
                {
                    if (s.A != i && s.B != i) continue;
                    arms++;
                    fastest = Math.Max(fastest, s.Mph);
                }
                joins[i] = JoinAt(i);
                // On a roundabout nobody stops for a light or a sign: whoever is going round goes first.
                bool onRing = false;
                foreach (EditSegment s in Segments) if (s.Ring && (s.A == i || s.B == i)) onRing = true;
                JunctionMode mode = arms <= 2 || joins[i] != null || onRing || FlowsThrough(i) ? JunctionMode.Free : ControlOf(i, fastest, signs);
                built[i] = b.AddNode(Nodes[i].X, Nodes[i].Y, mode);
                if (arms == 1 && DeadEndAt(i)) b.DeadEnd(built[i]);
            }
            foreach (Run run in runs)
            {
                EditSegment first = run.Pieces[0], last = run.Pieces[run.Pieces.Count - 1];
                RoadType type = first.Type;
                double length = LengthOf(run.Trace);
                // The lanes that run from the start of the run to its end, and those that run back.
                bool withFirst = !run.Turned[0];
                // Where the run arrives at an exit, its lanes are told which roads they lead on to: the road ahead, and the ramp.
                Func<Join, EditSegment, int[]> exitRoads = (join, arriving) => join == null || join.Merge || join.Up != arriving ? null
                    : new[] { runs.FindIndex(r => r.Pieces.Contains(join.Down)), runs.FindIndex(r => r.Pieces.Contains(join.Ramp)) };
                int[] exitAtTo = exitRoads(joins[run.To], last), exitAtFrom = exitRoads(joins[run.From], first);
                TrackSpec[] fwd = Tracks(withFirst ? type.Forward : type.Backward, joins[run.To], last, joins[run.From], first, length, exitAtTo);
                // At a fork each lane is told which of the roads ahead it carries on along.
                List<EditSegment>[] fork = type.OneWay && joins[run.To] == null ? ForkLanes(run.To, last) : null;
                for (int i = 0; fork != null && i < fwd.Length; i++)
                {
                    if (fwd[i].Slot >= fork.Length || fwd[i].To != 1) continue;
                    var onto = new List<int>();
                    foreach (EditSegment s in fork[fwd[i].Slot]) onto.Add(runs.FindIndex(r => r.Pieces.Contains(s)));
                    fwd[i].Onto = onto.ToArray();
                }
                TrackSpec[] back = type.OneWay ? null : Tracks(withFirst ? type.Backward : type.Forward, joins[run.From], first, joins[run.To], last, length, exitAtFrom);
                fwd = WithExtras(fwd, run, true, exitAtTo);
                if (back != null) back = WithExtras(back, run, false, exitAtFrom);
                fwd = Narrowed(fwd, run.To, last, length);
                if (back != null) back = Narrowed(back, run.From, first, length);
                if (type.OneWay) fwd = Widened(fwd, run.From, first, length);
                if (type.CenterLane || type.Bays)
                {
                    // The turning lane is used at each end of the road by the traffic arriving there, where there is a left to take.
                    // In a median it is all there is of it: the builder stops the grass short to make room.
                    double share = Math.Min(70 / Math.Max(length, 1), 0.45);
                    if (joins[run.To] == null && LeftAhead(run.To, last)) fwd = Plus(fwd, new TrackSpec(-1, 1 - share, 1, Turn.Left));
                    if (joins[run.From] == null && LeftAhead(run.From, first)) back = Plus(back, new TrackSpec(-1, 1 - share, 1, Turn.Left));
                }
                // Lanes the player has given their turns keep to them.
                int lastPiece = run.Pieces.Count - 1;
                Told(fwd, run.Turned[lastPiece] ? last.TurnsAtA : last.TurnsAtB);
                if (back != null) Told(back, run.Turned[0] ? first.TurnsAtB : first.TurnsAtA);
                double speed = first.Mph * 0.44704;
                bool rampAtFrom = joins[run.From] != null && joins[run.From].Ramp == first, rampAtTo = joins[run.To] != null && joins[run.To].Ramp == last;
                if (rampAtFrom || rampAtTo)
                {
                    // A ramp: bring each joining end in beside the lanes of the road it joins.
                    var trace = (Vec[])run.Trace.Clone();
                    double shiftFrom = 0, shiftTo = 0;
                    if (rampAtTo) trace = BesideTheRoad(trace, Nodes[run.To], joins[run.To], type.Width, out shiftTo);
                    if (rampAtFrom)
                    {
                        Array.Reverse(trace);
                        trace = BesideTheRoad(trace, Nodes[run.From], joins[run.From], type.Width, out shiftFrom);
                        Array.Reverse(trace);
                    }
                    b.AddRamp(built[run.From], built[run.To], fwd, trace, speed, type.Inner, rampAtFrom, shiftFrom, rampAtTo, shiftTo);
                }
                else if (run.Pieces.Count == 1 && !first.Curved) b.AddRoad(built[run.From], built[run.To], fwd, back, null, speed, 0, 0, false, type.Inner);
                else b.AddCurvedRoad(built[run.From], built[run.To], fwd, back, run.Trace, speed, type.Inner);
                Sign atFrom = SignOf(signs, first, run.From), atTo = SignOf(signs, last, run.To);
                if (atFrom != Sign.None || atTo != Sign.None) b.Signs(atFrom, atTo);
                if (NoRed(first, run.From) || NoRed(last, run.To)) b.NoTurnOnRed(NoRed(first, run.From), NoRed(last, run.To));
                if (first.Level != 0) b.Raise(first.Level * LevelHeight, RampFor(first.Level));
                if (type.CenterLane) b.PaveMiddle();
            }
            World world = b.Build();
            // Keep the surroundings the same size however much has been drawn.
            world.Extent = Math.Max(world.Extent, 400);
            world.Waters.AddRange(Waters);
            // Name the places at the edge of the map.
            foreach (MapEnd end in Ends)
            {
                foreach (Node node in world.Nodes) if (node.Terminal && Dist(node.Pos, end.At) < 2) node.Name = end.Name;
            }
            return world;
        }

        /// <summary>
        /// Where a road carries on as a narrower one (an avenue becoming a street, say), the
        /// lanes the narrower road has no room for end a little before the place where they meet.
        /// Their traffic moves over in good time, as at any lane drop, instead of two lanes
        /// squeezing into one in the bend itself.
        /// </summary>
        private TrackSpec[] Narrowed(TrackSpec[] tracks, int node, EditSegment arriving, double length)
        {
            List<EditSegment> here = RoadsAt(node);
            if (here.Count != 2 || JoinAt(node) != null) return tracks;
            EditSegment onward = here[0] == arriving ? here[1] : here[0];
            int room = onward.A == node ? onward.Type.Forward : onward.Type.Backward;
            if (room < 1) return tracks;
            double until = 1 - Math.Min(70 / Math.Max(length, 1), 0.45);
            // The lanes that carry on are the leftmost, unless the narrower road was built on from one lane in particular.
            int first = LaneTie(node, arriving.Type.Forward - room);
            var result = (TrackSpec[])tracks.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if ((result[i].Slot < first || result[i].Slot >= first + room) && result[i].Slot >= 0 && result[i].To >= 1 && result[i].From < until - 0.2) result[i].To = until;
            }
            return result;
        }

        /// <summary>
        /// Where two one-way roads meet end to end and one has more lanes than the other, the
        /// number of the wider road's lane (0 at the left) that the narrower road's first lane
        /// lines up with: what the player chose, kept within what there is room for, or 0.
        /// </summary>
        private int LaneTie(int node, int spare)
        {
            if (spare <= 0) return 0;
            int tie = 0;
            foreach (EditSegment s in RoadsAt(node))
            {
                if (!s.Type.OneWay) return 0;
                tie = Math.Max(tie, s.A == node ? s.LaneAtA : s.LaneAtB);
            }
            return Math.Max(0, Math.Min(tie - 1, spare));
        }

        /// <summary>
        /// Where a one-way road carries on from a narrower one, the lanes the narrower road
        /// does not lead into start a little way along: the road widens there, as at any place
        /// where a lane is gained, and traffic moves over into the new lane.
        /// </summary>
        private TrackSpec[] Widened(TrackSpec[] tracks, int node, EditSegment leaving, double length)
        {
            List<EditSegment> here = RoadsAt(node);
            if (here.Count != 2 || JoinAt(node) != null) return tracks;
            EditSegment before = here[0] == leaving ? here[1] : here[0];
            if (!before.Type.OneWay || before.B != node || leaving.A != node) return tracks;
            int fed = before.Type.Forward, spare = leaving.Type.Forward - fed;
            if (spare <= 0) return tracks;
            int first = LaneTie(node, spare);
            double from = Math.Min(14 / Math.Max(length, 1), 0.3);
            var result = (TrackSpec[])tracks.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                if ((result[i].Slot < first || result[i].Slot >= first + fed) && result[i].Slot >= 0 && result[i].From <= 0 && result[i].To > from + 0.2) result[i].From = from;
            }
            return result;
        }

        /// <summary>
        /// Say which lane a road built on from another lines up with, at the place where the two
        /// meet end to end (see EditSegment.LaneAtA): 0 for the wider road's leftmost lane.
        /// It only matters where two one-way roads with different numbers of lanes meet. Returns
        /// false if no two roads meet at that point.
        /// </summary>
        public bool SetLaneTie(Vec at, int lane)
        {
            Snap snap = SnapTo(at);
            if (snap.Node < 0) return false;
            List<EditSegment> here = RoadsAt(snap.Node);
            if (here.Count != 2) return false;
            foreach (EditSegment s in here)
            {
                // Kept on the road that leaves; the other's end there is cleared so the two cannot disagree.
                if (s.A == snap.Node) s.LaneAtA = lane + 1;
                if (s.B == snap.Node) s.LaneAtB = 0;
            }
            return true;
        }

        /// <summary>
        /// The end of one lane at an open end of a road, for building on from it: where the
        /// middle of the lane is, which way its traffic is heading there, and whether that
        /// traffic is leaving the road through this end or setting out along the road from it.
        /// </summary>
        public struct LaneEnd
        {
            public Vec At, Node;
            public double Heading;
            public bool Leaving;
            /// <summary>Which lane it is among those going its way, counting from 0 at the left of the traffic, and how many there are.</summary>
            public int Lane, Lanes;
        }

        /// <summary>
        /// Every lane at every open road end: a road end that nothing else is joined to and that
        /// is not on the edge of the map. A road is built on from the lanes that leave, and
        /// brought in to the lanes that set out.
        /// </summary>
        public List<LaneEnd> LaneEnds()
        {
            var ends = new List<LaneEnd>();
            for (int node = 0; node < Nodes.Count; node++)
            {
                List<EditSegment> here = RoadsAt(node);
                if (here.Count != 1 || OnEdge(Nodes[node])) continue;
                EditSegment s = here[0];
                if (s.Ring || s.A == s.B) continue;
                // The way out of the road through this end, and the lanes going each way past it.
                double outward = HeadingFrom(s, node) + Math.PI;
                int leaving = s.B == node ? s.Type.Forward : s.Type.Backward, setting = s.B == node ? s.Type.Backward : s.Type.Forward;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool leaves = pass == 0;
                    int count = leaves ? leaving : setting;
                    double way = leaves ? outward : outward + Math.PI;
                    for (int k = 0; k < count; k++)
                    {
                        // A lane lies to the right of the line its road is drawn along, as its traffic sees it.
                        double off = s.Type.Inner + (k + 0.5) * NetworkBuilder.LaneW;
                        ends.Add(new LaneEnd
                        {
                            At = new Vec(Nodes[node].X + Math.Sin(way) * off, Nodes[node].Y - Math.Cos(way) * off), Node = Nodes[node],
                            Heading = way, Leaving = leaves, Lane = k, Lanes = count,
                        });
                    }
                }
            }
            return ends;
        }

        /// <summary>The kind of road a point is on, or null on open ground.</summary>
        public RoadType RoadAt(Vec p)
        {
            Vec on;
            int seg = NearestSegment(p, SnapRoad * 2.2, out on);
            return seg < 0 ? null : Segments[seg].Type;
        }

        /// <summary>
        /// The lane under a point partway along a road: where its middle is beside that point,
        /// which way its traffic runs, which lane it is among those going its way (0 at the
        /// left) and how many there are. False if the point is not on a road.
        /// </summary>
        public bool LaneAt(Vec p, out Vec at, out double heading, out int lane, out int lanes)
        {
            at = p;
            heading = 0;
            lane = lanes = 0;
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return false;
            EditSegment s = Segments[seg];
            lanes = s.Type.OneWay || forward ? s.Type.Forward : s.Type.Backward;
            if (lanes < 1) return false;
            Vec a = PointAt(s, Math.Max(t - 0.01, 0)), b = PointAt(s, Math.Min(t + 0.01, 1)), on = PointAt(s, t);
            heading = Math.Atan2(b.Y - a.Y, b.X - a.X) + (forward ? 0 : Math.PI);
            lane = (int)Geo.Clamp(Math.Floor((across - s.Type.Inner) / NetworkBuilder.LaneW), 0, lanes - 1);
            double off = s.Type.Inner + (lane + 0.5) * NetworkBuilder.LaneW;
            at = new Vec(on.X + Math.Sin(heading) * off, on.Y - Math.Cos(heading) * off);
            return true;
        }

        /// <summary>The lane of the wider road that the narrower one lines up with where two one-way roads meet end to end at a point: 0 at the left.</summary>
        public int LaneTieAt(Vec at)
        {
            Snap snap = SnapTo(at);
            if (snap.Node < 0) return 0;
            List<EditSegment> here = RoadsAt(snap.Node);
            if (here.Count != 2 || !here[0].Type.OneWay || !here[1].Type.OneWay) return 0;
            return LaneTie(snap.Node, Math.Abs(here[0].Type.Forward - here[1].Type.Forward));
        }

        private static TrackSpec[] Plus(TrackSpec[] tracks, TrackSpec one)
        {
            var all = new List<TrackSpec>(tracks) { one };
            return all.ToArray();
        }

        /// <summary>Whether traffic arriving at a point along a road has a road to turn left on to there: one leaving well to its left that it may drive along.</summary>
        private bool LeftAhead(int node, EditSegment arriving)
        {
            double way = HeadingFrom(arriving, node) + Math.PI;
            foreach (EditSegment s in RoadsAt(node))
            {
                if (s == arriving || (s.Type.OneWay && s.A != node)) continue;
                double off = Math.Atan2(Math.Sin(HeadingFrom(s, node) - way), Math.Cos(HeadingFrom(s, node) - way));
                if (off > Math.PI / 4 && off < Math.PI - 0.3) return true;
            }
            return false;
        }

        /// <summary>Give the lanes that reach the end of a road the turns the player has set for them, where they have.</summary>
        private static void Told(TrackSpec[] tracks, int[] turns)
        {
            if (turns == null) return;
            for (int i = 0; i < tracks.Length; i++)
            {
                int slot = tracks[i].Slot;
                if (tracks[i].To != 1 || tracks[i].Onto != null || slot < 0 || slot >= turns.Length || turns[slot] == 0) continue;
                var may = new List<Turn>();
                if ((turns[slot] & 1) != 0) may.Add(Turn.Left);
                if ((turns[slot] & 2) != 0) may.Add(Turn.Straight);
                if ((turns[slot] & 4) != 0) may.Add(Turn.Right);
                tracks[i].Turns = may.ToArray();
            }
        }

        /// <summary>The lane of a built network that a point is on, or null.</summary>
        private static Lane LaneOf(World world, Vec p)
        {
            Lane best = null;
            double nearest = NetworkBuilder.LaneW * 0.75;
            foreach (Lane lane in world.Lanes)
            {
                if (lane.Kind != LaneKind.Road) continue;
                double d = lane.Path.Project(p.X, p.Y).D;
                if (d >= nearest) continue;
                nearest = d;
                best = lane;
            }
            return best;
        }

        /// <summary>
        /// For the lane under a point, the ways it could go at the intersection it leads to
        /// (left, straight on, right: whichever roads are there) and the ways it may go as things
        /// stand. False if the lane does not lead to an intersection with a light or signs.
        /// </summary>
        public bool LaneTurns(Vec p, out bool[] exist, out bool[] allowed)
        {
            int slot;
            return LaneTurns(p, out exist, out allowed, out slot);
        }

        private bool LaneTurns(Vec p, out bool[] exist, out bool[] allowed, out int slot)
        {
            exist = new bool[3];
            allowed = new bool[3];
            slot = 0;
            World world;
            try
            {
                world = ToWorld();
            }
            catch (Exception)
            {
                return false;
            }
            Lane lane = LaneOf(world, p);
            if (lane == null || lane.Connectors == null || lane.ApproachTurns == null || !world.Nodes[lane.Node].Controlled) return false;
            slot = lane.Slot;
            for (int t = 0; t < 3; t++)
            {
                exist[t] = lane.ApproachTurns[t];
                allowed[t] = lane.Connectors[t] >= 0;
            }
            return true;
        }

        /// <summary>
        /// Let the lane under a point make a turn at the intersection it leads to, or forbid it
        /// to. The other lanes keep whatever they had. Returns null if it was set, or a short
        /// reason why it cannot be.
        /// </summary>
        public string TrySetTurn(Vec p, Turn turn, bool may, bool commit)
        {
            bool[] exist, allowed;
            int slot, seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across) || !LaneTurns(p, out exist, out allowed, out slot)) return "That lane does not lead to an intersection.";
            if ((int)turn > 2 || !exist[(int)turn]) return "There is no road to go on to that way.";
            allowed[(int)turn] = may;
            int ways = (allowed[0] ? 1 : 0) | (allowed[1] ? 2 : 0) | (allowed[2] ? 4 : 0);
            if (ways == 0) return "A lane has to lead somewhere.";
            EditableMap work = Clone();
            EditSegment under = work.Segments[seg];
            foreach (Run run in work.Runs())
            {
                int index = run.Pieces.IndexOf(under);
                if (index < 0) continue;
                // The piece at the end of the road this lane arrives at, and which of its ends that is.
                bool withRun = forward != run.Turned[index];
                int at = withRun ? run.Pieces.Count - 1 : 0;
                EditSegment piece = run.Pieces[at];
                if (piece.Locked) return "That road came with the level.";
                bool atB = withRun != run.Turned[at];
                int[] turns = atB ? piece.TurnsAtB : piece.TurnsAtA;
                if (turns == null || turns.Length <= slot) Array.Resize(ref turns, slot + 1);
                turns[slot] = ways;
                if (atB) piece.TurnsAtB = turns;
                else piece.TurnsAtA = turns;
                string problem = work.Unbuildable(this);
                if (problem != null) return problem;
                if (commit) Segments = work.Segments;
                return null;
            }
            return "That lane does not lead to an intersection.";
        }

        // ---------------------------------------------------------------- signs, lights and speed limits

        /// <summary>What the traffic at one end of a piece meets at the intersection there, as kept in EditSegment.ControlAtA and ControlAtB.</summary>
        public const int AsLight = 1, AsStop = 2, AsYield = 3, AsNoSign = 4, AsArrows = 5;
        /// <summary>Added to one of those: a sign at that end forbids its traffic to turn on a red light.</summary>
        public const int NoRedBit = 16;

        /// <summary>
        /// The speed limits a road can be posted with, miles per hour: the ones Ohio's law sets
        /// out (Revised Code 4511.21(B)). 15 is for alleys, 20 a school zone, 25 the streets of a
        /// town, 35 its through streets and state routes, 50 an expressway in town, 55 a highway
        /// out of town, 60 a divided highway in the country, 65 a freeway in town and 70 a
        /// freeway in the country.
        /// </summary>
        public static readonly int[] SpeedLimits = { 15, 20, 25, 35, 50, 55, 60, 65, 70 };

        /// <summary>What a speed limit is for, in a few words.</summary>
        public static string SpeedLimitFor(int mph)
        {
            switch (mph)
            {
                case 15: return "an alley";
                case 20: return "a school zone";
                case 25: return "a street in town";
                case 35: return "a through street in town";
                case 50: return "an expressway in town";
                case 55: return "a highway out of town";
                case 60: return "a divided highway in the country";
                case 65: return "a freeway in town";
                case 70: return "a freeway in the country";
                default: return "";
            }
        }

        /// <summary>The sign a piece's traffic faces at one of its ends, from a note of them kept by piece: the sign at A, then the sign at B.</summary>
        private static Sign SignOf(Dictionary<EditSegment, Sign[]> signs, EditSegment piece, int node)
        {
            Sign[] ends;
            return signs.TryGetValue(piece, out ends) ? ends[piece.A == node ? 0 : 1] : Sign.None;
        }

        private static void NoteSign(Dictionary<EditSegment, Sign[]> signs, EditSegment piece, int node, Sign sign)
        {
            Sign[] ends;
            if (!signs.TryGetValue(piece, out ends)) signs[piece] = ends = new Sign[2];
            ends[piece.A == node ? 0 : 1] = sign;
        }

        private static int ControlCode(EditSegment piece, int node)
        {
            return (piece.A == node ? piece.ControlAtA : piece.ControlAtB) & (NoRedBit - 1);
        }

        /// <summary>Set what the traffic at one end of a piece meets there. A no-turn-on-red sign at that end stays up.</summary>
        private static void SetControlCode(EditSegment piece, int node, int code)
        {
            if (piece.A == node) piece.ControlAtA = code | (piece.ControlAtA & NoRedBit);
            if (piece.B == node) piece.ControlAtB = code | (piece.ControlAtB & NoRedBit);
        }

        private static bool NoRed(EditSegment piece, int node)
        {
            return ((piece.A == node ? piece.ControlAtA : piece.ControlAtB) & NoRedBit) != 0;
        }

        /// <summary>Whether the lane under a point leads to an intersection where a sign forbids its road to turn on red.</summary>
        public bool NoTurnOnRedAt(Vec p)
        {
            EditSegment piece;
            int node;
            return ApproachUnder(p, out piece, out node) && NoRed(piece, node);
        }

        /// <summary>
        /// Put up a no-turn-on-red sign for the road of the lane under a point, at the
        /// intersection it leads to, or take it down (Ohio Revised Code 4511.13(C)(4)). It
        /// only matters while a light runs the intersection. Returns null if it was done, or
        /// a short reason why it cannot be.
        /// </summary>
        public string TrySetNoTurnOnRed(Vec p, bool on, bool commit)
        {
            EditSegment piece;
            int node;
            if (!ApproachUnder(p, out piece, out node)) return "That lane does not lead to an intersection.";
            if (piece.Locked) return "That road came with the level.";
            if (!commit) return null;
            if (piece.A == node) piece.ControlAtA = on ? piece.ControlAtA | NoRedBit : piece.ControlAtA & ~NoRedBit;
            if (piece.B == node) piece.ControlAtB = on ? piece.ControlAtB | NoRedBit : piece.ControlAtB & ~NoRedBit;
            return null;
        }

        /// <summary>
        /// What runs the intersection at a point: whatever the player asked for on the roads
        /// that meet there, or, where they asked for nothing, stop signs all round if only 25 mph
        /// streets meet and a light otherwise. The sign each road's traffic faces is noted in signs.
        /// </summary>
        private JunctionMode ControlOf(int node, int fastest, Dictionary<EditSegment, Sign[]> signs)
        {
            List<EditSegment> here = RoadsAt(node);
            bool asked = false, light = false, arrows = false, allStop = true;
            foreach (EditSegment piece in here)
            {
                int code = ControlCode(piece, node);
                asked |= code != 0;
                light |= code == AsLight;
                arrows |= code == AsArrows;
                allStop &= code == AsStop || code == 0;
            }
            if (!asked) return fastest <= 25 ? JunctionMode.AllWay : JunctionMode.Signal;
            if (arrows) return JunctionMode.ProtectedLeft;
            if (light) return JunctionMode.Signal;
            // Stop signs on every road, counting a road that has joined since they were put up.
            if (allStop) return JunctionMode.AllWay;
            foreach (EditSegment piece in here)
            {
                int code = ControlCode(piece, node);
                NoteSign(signs, piece, node, code == AsStop ? Sign.Stop : code == AsYield ? Sign.Yield : Sign.None);
            }
            return JunctionMode.Signs;
        }

        /// <summary>
        /// The piece and point at the end of the road that the lane under a point leads to, if
        /// that end is an intersection which a light or signs can run: three or more roads, and
        /// not a merge, a fork or a way on to a roundabout.
        /// </summary>
        private bool ApproachUnder(Vec p, out EditSegment piece, out int node)
        {
            piece = null;
            node = -1;
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return false;
            EditSegment under = Segments[seg];
            foreach (Run run in Runs())
            {
                int index = run.Pieces.IndexOf(under);
                if (index < 0) continue;
                bool withRun = forward != run.Turned[index];
                // A one-way road only arrives at one of its ends.
                if (under.Type.OneWay && !withRun) return false;
                piece = run.Pieces[withRun ? run.Pieces.Count - 1 : 0];
                node = withRun ? run.To : run.From;
                break;
            }
            if (piece == null || RoadsAt(node).Count < 3 || JoinAt(node) != null || FlowsThrough(node)) return false;
            foreach (EditSegment s in RoadsAt(node)) if (s.Ring) return false;
            return true;
        }

        /// <summary>The roads at an intersection that are the same street as one of them: itself, and the road most nearly straight across from it, if there is one.</summary>
        private List<EditSegment> SameStreet(EditSegment piece, int node)
        {
            var street = new List<EditSegment> { piece };
            EditSegment across = null;
            double widest = Math.PI - 25 * Math.PI / 180;
            foreach (EditSegment s in RoadsAt(node))
            {
                if (s == piece) continue;
                double apart = Math.Abs(Math.Atan2(Math.Sin(HeadingFrom(s, node) - HeadingFrom(piece, node)), Math.Cos(HeadingFrom(s, node) - HeadingFrom(piece, node))));
                if (apart <= widest) continue;
                widest = apart;
                across = s;
            }
            if (across != null) street.Add(across);
            return street;
        }

        /// <summary>
        /// What runs the intersection that the lane under a point leads to, as seen from that
        /// lane's road, and whether that was left to the game. False if the lane does not lead
        /// to an intersection that a light or signs can run.
        /// </summary>
        public bool ControlAt(Vec p, out Control control, out bool automatic)
        {
            control = Control.Automatic;
            automatic = true;
            EditSegment piece;
            int node;
            if (!ApproachUnder(p, out piece, out node)) return false;
            int fastest = 0;
            foreach (EditSegment s in RoadsAt(node))
            {
                fastest = Math.Max(fastest, s.Mph);
                if (ControlCode(s, node) != 0) automatic = false;
            }
            var signs = new Dictionary<EditSegment, Sign[]>();
            JunctionMode mode = ControlOf(node, fastest, signs);
            if (mode == JunctionMode.Signal) control = Control.Light;
            else if (mode == JunctionMode.ProtectedLeft) control = Control.LightWithArrows;
            else if (mode == JunctionMode.AllWay) control = Control.AllWayStop;
            else
            {
                Sign mine = SignOf(signs, piece, node);
                bool othersSigned = false;
                List<EditSegment> street = SameStreet(piece, node);
                foreach (EditSegment s in RoadsAt(node)) if (!street.Contains(s) && SignOf(signs, s, node) != Sign.None) othersSigned = true;
                control = mine == Sign.Stop ? Control.Stop : mine == Sign.Yield ? Control.Yield : othersSigned ? Control.RightOfWay : Control.NoSigns;
            }
            return true;
        }

        /// <summary>
        /// Say what should run the intersection that the lane under a point leads to. A stop or
        /// yield sign goes on that lane's road and on the same road across the intersection, and
        /// the cross street is left without; "right of way" is the other way about. Returns null
        /// if it was set, or a short reason why it cannot be.
        /// </summary>
        public string TrySetControl(Vec p, Control control, bool commit)
        {
            EditSegment piece;
            int node;
            if (!ApproachUnder(p, out piece, out node)) return "That lane does not lead to an intersection.";
            EditableMap work = Clone();
            piece = work.Segments[Segments.IndexOf(piece)];
            List<EditSegment> street = work.SameStreet(piece, node);
            foreach (EditSegment s in work.RoadsAt(node))
            {
                bool mine = street.Contains(s);
                int code;
                switch (control)
                {
                    case Control.Light: code = AsLight; break;
                    case Control.LightWithArrows: code = AsArrows; break;
                    case Control.AllWayStop: code = AsStop; break;
                    case Control.Stop: code = mine ? AsStop : AsNoSign; break;
                    case Control.Yield: code = mine ? AsYield : AsNoSign; break;
                    case Control.RightOfWay: code = mine ? AsNoSign : AsStop; break;
                    case Control.NoSigns: code = AsNoSign; break;
                    default: code = 0; break;
                }
                SetControlCode(s, node, code);
            }
            string problem = work.Unbuildable(this);
            if (problem != null) return problem;
            if (commit) Segments = work.Segments;
            return null;
        }

        /// <summary>The speed limit on the road under a point, miles per hour, and whether the player posted it; 0 where there is no road.</summary>
        public int SpeedAt(Vec p, out bool posted)
        {
            posted = false;
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return 0;
            posted = Segments[seg].Limit > 0;
            return Segments[seg].Mph;
        }

        /// <summary>
        /// Post a speed limit on the road under a point, for its whole length from one junction
        /// or road end to the next: one of SpeedLimits, or 0 to go back to the usual limit for
        /// its kind of road. Returns null if it was set, or a short reason why it cannot be.
        /// </summary>
        public string TrySetSpeed(Vec p, int mph, bool commit)
        {
            int seg;
            double t, across;
            bool forward;
            if (!OnRoad(p, out seg, out t, out forward, out across)) return "There is no road there.";
            if (mph != 0 && Array.IndexOf(SpeedLimits, mph) < 0) return "That is not a speed limit the law provides for.";
            EditableMap work = Clone();
            EditSegment under = work.Segments[seg];
            if (under.Ring) return "A roundabout's ring keeps its own speed.";
            foreach (Run run in work.Runs())
            {
                if (!run.Pieces.Contains(under)) continue;
                foreach (EditSegment piece in run.Pieces) piece.Limit = mph == piece.Type.Mph ? 0 : mph;
                break;
            }
            string problem = work.Unbuildable(this);
            if (problem != null) return problem;
            if (commit) Segments = work.Segments;
            return null;
        }

        /// <summary>How long an extra lane has to be, meters, and how near the end of its road it is taken to run right to the end.</summary>
        public const double ShortestLane = 24, LaneEndSnap = 10;
        /// <summary>About how far a junction's stop lines are from its middle: the network works out the real figure.</summary>
        private const double JunctionSize = 12;

        /// <summary>What an extra lane costs a meter: the price of one lane of the road it is on.</summary>
        public static double LanePrice(RoadType type)
        {
            return type.CostPerMeter / type.Lanes;
        }

        /// <summary>
        /// The extra lanes on one side of a run, as stretches of the run from its start to its
        /// end (shares of its length, 0 to 1). Extra lanes on neighboring pieces that meet end
        /// to end are one lane. withRun is the side whose traffic goes the way the run does.
        /// </summary>
        private List<double[]> ExtrasAlong(Run run, bool withRun, bool asBuilt = true)
        {
            var lengths = new double[run.Pieces.Count];
            double total = 0;
            for (int i = 0; i < lengths.Length; i++) total += lengths[i] = LengthOf(Trace(run.Pieces[i]));
            var spans = new List<double[]>();
            double before = 0;
            for (int i = 0; i < lengths.Length; i++)
            {
                foreach (ExtraLane x in run.Pieces[i].Extras)
                {
                    // A piece the run goes along backward has its sides, and its ends, the other way round.
                    bool sideWithRun = x.Forward != run.Turned[i];
                    if (sideWithRun != withRun) continue;
                    double from = run.Turned[i] ? 1 - x.To : x.From, to = run.Turned[i] ? 1 - x.From : x.To;
                    spans.Add(new[] { (before + from * lengths[i]) / total, (before + to * lengths[i]) / total });
                }
                before += lengths[i];
            }
            spans.Sort((x, y) => x[0].CompareTo(y[0]));
            for (int i = spans.Count - 1; i > 0; i--)
            {
                if (spans[i][0] > spans[i - 1][1] + 1.5 / total) continue;
                spans[i - 1][1] = Math.Max(spans[i - 1][1], spans[i][1]);
                spans.RemoveAt(i);
            }
            if (!asBuilt) return spans;
            // The network measures along the road between its junctions, which take up the first and last few meters of the run.
            double lostAtFrom = run.JunctionAtFrom ? JunctionSize : 0, lostAtTo = run.JunctionAtTo ? JunctionSize : 0;
            double road = Math.Max(total - lostAtFrom - lostAtTo, 1);
            foreach (double[] span in spans)
            {
                for (int k = 0; k < 2; k++) span[k] = Geo.Clamp((span[k] * total - lostAtFrom) / road, 0, 1);
                // One that all but reaches the end of the road runs right to it, to become a lane of the junction there.
                if (span[0] * road < LaneEndSnap) span[0] = 0;
                if ((1 - span[1]) * road < LaneEndSnap) span[1] = 1;
            }
            return spans;
        }

        /// <summary>
        /// The lanes of one direction of a run, with its extra lanes added outside them. Two
        /// cases tie an extra lane to a ramp. One that starts where a ramp merges in is the
        /// ramp's own lane carrying on, so its traffic need not move over. One that runs up to an
        /// exit (whose road ahead and ramp are given) is a lane for the exit alone, and the lane
        /// inside it then only goes on along the road.
        /// </summary>
        private TrackSpec[] WithExtras(TrackSpec[] tracks, Run run, bool withRun, int[] exit)
        {
            List<double[]> spans = ExtrasAlong(run, withRun);
            if (spans.Count == 0) return tracks;
            var all = new List<TrackSpec>(tracks);
            // The first place outside the ordinary lanes, and the lane a merge has already put there, if any.
            int slot = 0, merged = -1;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].From == 0 && all[i].To == 1) slot = Math.Max(slot, all[i].Slot + 1);
                else if (merged < 0 || all[i].Slot < all[merged].Slot) merged = i;
            }
            foreach (double[] span in spans)
            {
                // The run's own direction counts from its start; the other direction counts from its end.
                double from = withRun ? span[0] : 1 - span[1], to = withRun ? span[1] : 1 - span[0];
                if (merged >= 0 && from <= all[merged].To + 0.02)
                {
                    // It starts within the lane the ramp brought: that lane simply runs on to where this one ends.
                    if (to > all[merged].To) all[merged] = new TrackSpec(all[merged].Slot, 0, to);
                    if (to < 1) continue;
                    from = 0;
                    all.RemoveAt(merged);
                    merged = -1;
                }
                if (to - from <= 0.01) continue;
                if (to == 1 && exit != null)
                {
                    all.Add(new TrackSpec(slot, from, 1) { Onto = new[] { exit[1] } });
                    for (int i = 0; i < all.Count; i++) if (all[i].Slot == slot - 1 && all[i].To == 1) all[i] = new TrackSpec(all[i].Slot, all[i].From, 1) { Onto = new[] { exit[0] } };
                }
                else all.Add(new TrackSpec(slot, from, to));
            }
            return all.ToArray();
        }

        /// <summary>
        /// The lanes of a road in one direction. Where it ends at a merge or an exit its lanes
        /// are told which way they may go, and where it leaves a merge it gains a lane for each
        /// lane of the ramp, which ends further on so that traffic has room to move over. The
        /// join ahead is the one this direction arrives at, with the piece of the road that
        /// touches it; the join behind is the one it sets off from.
        /// </summary>
        private static TrackSpec[] Tracks(int count, Join ahead, EditSegment atAhead, Join behind, EditSegment atBehind, double length, int[] exit)
        {
            var tracks = new List<TrackSpec>();
            for (int i = 0; i < count; i++)
            {
                if (ahead == null) tracks.Add(new TrackSpec(i));
                // Traffic arriving at an exit may take it from the outside lane; the other lanes only carry on along the road.
                // The lanes are told the roads, not "straight on" and "right": where the road bends at the exit, the ramp can be the straighter of the two.
                else if (exit != null) tracks.Add(new TrackSpec(i) { Onto = i == count - 1 ? exit : new[] { exit[0] } });
                else tracks.Add(new TrackSpec(i, 0, 1, Turn.Straight));
            }
            if (behind != null && behind.Merge && atBehind == behind.Down)
            {
                double until = Math.Min(0.75, 150 / Math.Max(length, 1));
                for (int i = 0; i < behind.Ramp.Type.Forward; i++) tracks.Add(new TrackSpec(count + i, 0, until));
            }
            return tracks.ToArray();
        }

        // ---------------------------------------------------------------- saving

        private static string Num(double v)
        {
            return v.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>The whole map as one line of text.</summary>
        public string Serialize()
        {
            var sb = new StringBuilder();
            foreach (Vec n in Nodes) sb.Append(Num(n.X)).Append(',').Append(Num(n.Y)).Append(';');
            sb.Append('|');
            foreach (EditSegment s in Segments)
            {
                // A road of one of the listed types is saved by its code; an older, plain one by lanes and speed.
                sb.Append(s.A).Append(',').Append(s.B).Append(',');
                if (RoadType.ById(s.Type.Id) != null) sb.Append(s.Type.Id);
                else sb.Append(s.Lanes).Append(',').Append(s.Mph);
                if (s.Curved) sb.Append(',').Append(Num(s.Mid.X)).Append(',').Append(Num(s.Mid.Y)).Append(s.Ring ? ",ring" : ",arc");
                if (s.Level != 0) sb.Append(",L").Append(s.Level);
                if (s.Locked) sb.Append(",K");
                foreach (ExtraLane x in s.Extras) sb.Append(",X").Append(x.Forward ? '+' : '-').Append(x.From.ToString("F4", CultureInfo.InvariantCulture)).Append(':').Append(x.To.ToString("F4", CultureInfo.InvariantCulture));
                if (s.TurnsAtA != null) sb.Append(",TA:").Append(string.Join(".", s.TurnsAtA));
                if (s.TurnsAtB != null) sb.Append(",TB:").Append(string.Join(".", s.TurnsAtB));
                if (s.ControlAtA != 0) sb.Append(",CA:").Append(s.ControlAtA);
                if (s.ControlAtB != 0) sb.Append(",CB:").Append(s.ControlAtB);
                if (s.Limit > 0) sb.Append(",S").Append(s.Limit);
                if (s.LaneAtA != 0) sb.Append(",LA:").Append(s.LaneAtA);
                if (s.LaneAtB != 0) sb.Append(",LB:").Append(s.LaneAtB);
                sb.Append(';');
            }
            return sb.ToString();
        }

        /// <summary>Read a map written by Serialize. Text that does not parse gives an empty map.</summary>
        public static EditableMap Parse(string text)
        {
            var map = new EditableMap();
            if (string.IsNullOrEmpty(text)) return map;
            try
            {
                string[] halves = text.Split('|');
                foreach (string item in halves[0].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = item.Split(',');
                    map.Nodes.Add(new Vec(double.Parse(f[0], CultureInfo.InvariantCulture), double.Parse(f[1], CultureInfo.InvariantCulture)));
                }
                foreach (string item in halves[1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = item.Split(',');
                    var s = new EditSegment { A = int.Parse(f[0]), B = int.Parse(f[1]) };
                    // Last of all, the lane of the next road that each end lines up with (LA, LB).
                    while (f.Length > 3 && (f[f.Length - 1].StartsWith("LA:") || f[f.Length - 1].StartsWith("LB:")))
                    {
                        if (f[f.Length - 1][1] == 'A') s.LaneAtA = int.Parse(f[f.Length - 1].Substring(3));
                        else s.LaneAtB = int.Parse(f[f.Length - 1].Substring(3));
                        Array.Resize(ref f, f.Length - 1);
                    }
                    // Before that: a speed limit posted on it (S), and what its traffic meets at the intersection at each end (CA, CB).
                    while (f.Length > 3 && (f[f.Length - 1].StartsWith("CA:") || f[f.Length - 1].StartsWith("CB:") || (f[f.Length - 1].Length > 1 && f[f.Length - 1][0] == 'S' && char.IsDigit(f[f.Length - 1][1]))))
                    {
                        string last = f[f.Length - 1];
                        if (last[0] == 'S') s.Limit = int.Parse(last.Substring(1));
                        else if (last[1] == 'A') s.ControlAtA = int.Parse(last.Substring(3));
                        else s.ControlAtB = int.Parse(last.Substring(3));
                        Array.Resize(ref f, f.Length - 1);
                    }
                    // Before those, what the lanes may do at each end: TA or TB, and a number for each lane.
                    while (f[f.Length - 1].StartsWith("TA:") || f[f.Length - 1].StartsWith("TB:"))
                    {
                        int[] turns = Array.ConvertAll(f[f.Length - 1].Substring(3).Split('.'), int.Parse);
                        if (f[f.Length - 1][1] == 'A') s.TurnsAtA = turns;
                        else s.TurnsAtB = turns;
                        Array.Resize(ref f, f.Length - 1);
                    }
                    // Before that, extra lanes: X, the side, and where each starts and ends.
                    while (f[f.Length - 1].StartsWith("X"))
                    {
                        string[] ends = f[f.Length - 1].Substring(2).Split(':');
                        s.Extras.Insert(0, new ExtraLane { Forward = f[f.Length - 1][1] == '+', From = double.Parse(ends[0], CultureInfo.InvariantCulture), To = double.Parse(ends[1], CultureInfo.InvariantCulture) });
                        Array.Resize(ref f, f.Length - 1);
                    }
                    // Before them: whether it came with a level, and before that its height.
                    if (f[f.Length - 1] == "K")
                    {
                        s.Locked = true;
                        Array.Resize(ref f, f.Length - 1);
                    }
                    if (f[f.Length - 1].StartsWith("L"))
                    {
                        s.Level = int.Parse(f[f.Length - 1].Substring(1));
                        if (Math.Abs(s.Level) > HighestLevel) return new EditableMap();
                        Array.Resize(ref f, f.Length - 1);
                    }
                    if (s.A < 0 || s.B < 0 || s.A >= map.Nodes.Count || s.B >= map.Nodes.Count) return new EditableMap();
                    // The kind of road is either a type's code, or (in older drawings) a lane count and a speed limit.
                    int lanes, rest;
                    if (int.TryParse(f[2], out lanes))
                    {
                        s.Type = RoadType.Plain(lanes, int.Parse(f[3]));
                        rest = 4;
                    }
                    else
                    {
                        s.Type = RoadType.ById(f[2]);
                        if (s.Type == null) return new EditableMap();
                        rest = 3;
                    }
                    if (f.Length >= rest + 2)
                    {
                        s.Curved = true;
                        s.Mid = new Vec(double.Parse(f[rest], CultureInfo.InvariantCulture), double.Parse(f[rest + 1], CultureInfo.InvariantCulture));
                        // Curves saved before they became round arcs bent toward a corner point;
                        // the middle of that older curve is close to where the arc should pass.
                        s.Ring = f.Length > rest + 2 && f[rest + 2] == "ring";
                        if (f.Length == rest + 2) s.Mid = new Vec((map.Nodes[s.A].X + 2 * s.Mid.X + map.Nodes[s.B].X) / 4, (map.Nodes[s.A].Y + 2 * s.Mid.Y + map.Nodes[s.B].Y) / 4);
                    }
                    map.Segments.Add(s);
                }
                map.ToWorld();
                return map;
            }
            catch (Exception)
            {
                return new EditableMap();
            }
        }
    }
}
