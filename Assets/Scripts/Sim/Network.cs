using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    public enum Turn
    {
        Left = 0,
        Straight = 1,
        Right = 2,
        UTurn = 3,
    }

    public enum LaneKind
    {
        Road,
        Connector,
    }

    /// <summary>Where two paths through an intersection come too close to share.</summary>
    public sealed class Conflict
    {
        /// <summary>Lane id of the other connector.</summary>
        public int Foe;
        /// <summary>Shared stretch, measured along this connector.</summary>
        public double SIn, SOut;
        /// <summary>The same stretch, measured along the other connector.</summary>
        public double FoeIn, FoeOut;
    }

    public struct Sibling
    {
        public int Id;
        public double SDiverge;
    }

    public sealed class Lane
    {
        public const int Turns = 4;

        public int Id;
        public LaneKind Kind;
        public LanePath Path;
        public double Length;
        /// <summary>Meters per second.</summary>
        public double SpeedLimit;
        /// <summary>Sideways position on its road: 0 is next to the centerline, counting up to the right.</summary>
        public int Slot;
        /// <summary>Neighbor lanes going the same way. A lane can have several along its length.</summary>
        public List<int> Lefts = new List<int>();
        public List<int> Rights = new List<int>();
        /// <summary>Lanes that feed into the start of this one.</summary>
        public List<int> Preds = new List<int>();

        /// <summary>The intersection this road lane is heading for, and the arm it arrives on.</summary>
        public int ApproachNode = -1;
        public int ApproachArm = -1;
        /// <summary>Turns that can be made from some lane of this approach, indexed by Turn.</summary>
        public bool[] ApproachTurns;
        /// <summary>For each turn, which way to change lanes to reach a lane that allows it (-1 left, 1 right, 0 here or impossible).</summary>
        public int[] TurnSide = new int[Turns];
        /// <summary>For a lane that ends before the intersection: the side to merge to (-1 left, 1 right).</summary>
        public int MergeSide;

        /// <summary>The lane runs off the edge of the map: vehicles that reach its end leave.</summary>
        public bool IsExit;

        /// <summary>The intersection a lane belongs to: set for road lanes that reach a stop line, and for connectors.</summary>
        public int Node = -1;
        /// <summary>Road lanes that reach the stop line: which arm of the intersection.</summary>
        public int Arm = -1;
        /// <summary>Turns allowed from the end of this lane, indexed by Turn; -1 where not allowed. Null if the lane ends before the intersection.</summary>
        public int[] Connectors;

        // Connector lanes (paths through the intersection).
        public int From = -1;
        public int Next = -1;
        public Turn Turn;
        public int FromArm = -1;
        public List<Conflict> Conflicts;
        /// <summary>Connectors that start from the same lane, and where they split apart.</summary>
        public List<Sibling> Siblings;

        public bool Allows(Turn turn)
        {
            return Connectors != null && Connectors[(int)turn] >= 0;
        }

        /// <summary>The connector for a turn, falling back to whatever this lane allows.</summary>
        public int ConnectorFor(Turn turn)
        {
            if (Connectors[(int)turn] >= 0) return Connectors[(int)turn];
            if (Connectors[(int)Turn.Straight] >= 0) return Connectors[(int)Turn.Straight];
            if (Connectors[(int)Turn.Left] >= 0) return Connectors[(int)Turn.Left];
            if (Connectors[(int)Turn.Right] >= 0) return Connectors[(int)Turn.Right];
            return Connectors[(int)Turn.UTurn];
        }

        public Conflict ConflictWith(int otherId)
        {
            if (Conflicts == null) return null;
            foreach (var c in Conflicts) if (c.Foe == otherId) return c;
            return null;
        }
    }

    /// <summary>One road arriving at (and leaving) an intersection.</summary>
    public sealed class Approach
    {
        public int Node;
        public int Arm;
        /// <summary>Direction pointing away from the intersection along this road, radians.</summary>
        public double Angle;
        /// <summary>0 for the north-south street, 1 for east-west. Opposite arms share a street and a green.</summary>
        public int Street;
        /// <summary>Lanes arriving at the stop line and lanes leaving, each left to right.</summary>
        public List<int> Inbound = new List<int>();
        public List<int> Outbound = new List<int>();
        public bool AllowUTurn;
        /// <summary>Heading of arriving traffic, and a roadside spot for the signal or stop sign.</summary>
        public double H;
        public Vec Post;
        /// <summary>
        /// Where the paint sits as this road meets the intersection, measured to the right of
        /// the road's centerline in the direction of travel: the yellow-line side and the
        /// outer edge, for the arriving lanes and for the leaving lanes.
        /// </summary>
        public double InLeft, InRight, OutLeft, OutRight;
        /// <summary>How far this road's centerline is moved sideways where it meets the node, toward the clockwise side looking outward. Used to hang a ramp off the edge of a highway.</summary>
        public double Shift;
    }

    public sealed class Node
    {
        public int Id;
        public Vec Pos;
        /// <summary>Distance from the center to the stop lines.</summary>
        public double Radius;
        public List<Approach> Arms = new List<Approach>();
        public JunctionMode DefaultMode;
        /// <summary>Outline of the pavement inside the intersection.</summary>
        public List<Vec> Box = new List<Vec>();

        /// <summary>
        /// A real intersection with a light or stop signs, as opposed to a bend, a place where a
        /// ramp leaves or joins a highway, or the edge of the map.
        /// </summary>
        public bool Controlled => Arms.Count > 2 && DefaultMode != JunctionMode.Free;

        /// <summary>The edge of the map: vehicles leave here and new ones arrive.</summary>
        public bool Terminal => Arms.Count == 1;
    }

    public sealed class Strip
    {
        public Vec[] Left;
        public Vec[] Right;
    }

    public sealed class Marking
    {
        public Vec[] Pts;
        public double Width;
        public bool Yellow;
        /// <summary>Meters painted and meters skipped; both 0 for a solid line.</summary>
        public double DashOn, DashOff;
    }

    /// <summary>The stop line across one lane.</summary>
    public sealed class StopLine
    {
        public int Lane;
        public Vec A, B;
        /// <summary>Heading of traffic arriving at this line.</summary>
        public double H;
    }

    /// <summary>Arrows painted in a lane before the stop line.</summary>
    public sealed class ArrowMark
    {
        /// <summary>End of the lane at the stop line; the paint goes just before it.</summary>
        public PathPose At;
        public bool[] Turns;
        /// <summary>Paint a "no U-turn" symbol.</summary>
        public bool NoUTurn;
    }

    public sealed class World
    {
        public List<Lane> Lanes = new List<Lane>();
        public List<Node> Nodes = new List<Node>();
        public double LaneWidth;
        public List<Strip> Surfaces = new List<Strip>();
        public List<Marking> Markings = new List<Marking>();
        public List<StopLine> StopLines = new List<StopLine>();
        public List<ArrowMark> Arrows = new List<ArrowMark>();
        /// <summary>Roads that come in from the edge of the map, where new vehicles arrive.</summary>
        public List<Approach> Entries = new List<Approach>();
        /// <summary>Half the width of the square that contains everything.</summary>
        public double Extent;
    }

    public static class Connectors
    {
        /// <summary>Paths closer than this (centerline to centerline) cannot be used by two cars at once.</summary>
        private const double ConflictDist = 2.6;
        private const double DivergeDist = 2.2;

        /// <summary>Fill in conflicts and siblings for the connectors of one intersection.</summary>
        public static void Link(List<Lane> conns)
        {
            foreach (var a in conns)
            {
                a.Conflicts = new List<Conflict>();
                a.Siblings = new List<Sibling>();
            }
            foreach (var a in conns)
            {
                foreach (var b in conns)
                {
                    if (a.Id == b.Id) continue;
                    if (a.From == b.From)
                    {
                        a.Siblings.Add(new Sibling { Id = b.Id, SDiverge = DivergeAt(a.Path, b.Path) });
                        continue;
                    }
                    if (a.FromArm == b.FromArm) continue;
                    double mineIn, mineOut, theirIn, theirOut;
                    if (CloseRange(a.Path, b.Path, out mineIn, out mineOut) && CloseRange(b.Path, a.Path, out theirIn, out theirOut))
                    {
                        a.Conflicts.Add(new Conflict { Foe = b.Id, SIn = mineIn, SOut = mineOut, FoeIn = theirIn, FoeOut = theirOut });
                    }
                }
                // Stable order by where each conflict starts.
                var original = a.Conflicts;
                var sorted = new List<Conflict>(original);
                sorted.Sort((p, q) => p.SIn != q.SIn ? p.SIn.CompareTo(q.SIn) : original.IndexOf(p).CompareTo(original.IndexOf(q)));
                a.Conflicts = sorted;
            }
        }

        /// <summary>The stretch of path a that lies within ConflictDist of path b.</summary>
        private static bool CloseRange(LanePath a, LanePath b, out double from, out double to)
        {
            double first = -1, last = -1;
            const double step = 0.25;
            for (double s = 0; s <= a.Length + 1e-6; s += step)
            {
                PathPose p = a.At(Math.Min(s, a.Length));
                if (b.Project(p.X, p.Y).D < ConflictDist)
                {
                    if (first < 0) first = s;
                    last = s;
                }
            }
            from = Math.Max(0, first - step);
            to = Math.Min(a.Length, last + step);
            return first >= 0;
        }

        private static double DivergeAt(LanePath a, LanePath b)
        {
            for (double s = 0; s <= a.Length; s += 0.25)
            {
                PathPose p = a.At(s);
                if (b.Project(p.X, p.Y).D > DivergeDist) return s;
            }
            return a.Length;
        }
    }
}
