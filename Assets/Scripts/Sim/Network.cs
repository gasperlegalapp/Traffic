using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    public enum Turn
    {
        Left = 0,
        Straight = 1,
        Right = 2,
    }

    public enum LaneKind
    {
        Road,
        Connector,
    }

    /// <summary>Where two paths through the intersection come too close to share.</summary>
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
        public int Id;
        public LaneKind Kind;
        public LanePath Path;
        public double Length;
        /// <summary>Meters per second.</summary>
        public double SpeedLimit;
        /// <summary>Neighbor lanes going the same way. A lane can have several along its length.</summary>
        public List<int> Lefts = new List<int>();
        public List<int> Rights = new List<int>();
        /// <summary>Lanes that feed into the start of this one.</summary>
        public List<int> Preds = new List<int>();

        /// <summary>Road lanes that end at the stop line: which arm (-1 if none).</summary>
        public int Arm = -1;
        /// <summary>Turns allowed from the end of this lane, indexed by Turn; -1 where not allowed. Null if the lane does not reach the intersection.</summary>
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
            return Connectors[(int)Turn.Right];
        }

        public Conflict ConflictWith(int otherId)
        {
            if (Conflicts == null) return null;
            foreach (var c in Conflicts) if (c.Foe == otherId) return c;
            return null;
        }
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

    public sealed class StopLine
    {
        public int Arm;
        public Vec A, B;
        /// <summary>Roadside spot for the signal head or stop sign.</summary>
        public Vec Post;
        /// <summary>Heading of traffic arriving at this line.</summary>
        public double H;
    }

    public sealed class World
    {
        public List<Lane> Lanes = new List<Lane>();
        public double LaneWidth;
        /// <summary>Inbound and outbound lane ids for each arm (0 north, 1 east, 2 south, 3 west), left lane first.</summary>
        public int[][] Inbound = new int[4][];
        public int[][] Outbound = new int[4][];
        public List<Strip> Surfaces = new List<Strip>();
        public List<Vec> Box = new List<Vec>();
        public List<Marking> Markings = new List<Marking>();
        public List<StopLine> StopLines = new List<StopLine>();
        /// <summary>Half the width of the square that contains everything.</summary>
        public double Extent;
    }

    public static class Connectors
    {
        /// <summary>Paths closer than this (centerline to centerline) cannot be used by two cars at once.</summary>
        private const double ConflictDist = 2.6;
        private const double DivergeDist = 2.2;

        /// <summary>Fill in conflicts and siblings for every connector.</summary>
        public static void Link(List<Lane> lanes)
        {
            var conns = lanes.FindAll(l => l.Kind == LaneKind.Connector);
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
                var sorted = new List<Conflict>(a.Conflicts);
                sorted.Sort((p, q) => p.SIn != q.SIn ? p.SIn.CompareTo(q.SIn) : a.Conflicts.IndexOf(p).CompareTo(a.Conflicts.IndexOf(q)));
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
                Pose p = a.At(Math.Min(s, a.Length));
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
                Pose p = a.At(s);
                if (b.Project(p.X, p.Y).D > DivergeDist) return s;
            }
            return a.Length;
        }
    }
}
