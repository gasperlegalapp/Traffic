using System;
using System.Collections.Generic;

namespace Traffic.Sim
{
    public enum CarState
    {
        Cruise,
        Follow,
        Brake,
        Stopping,
        Wait,
        Yield,
        LettingIn,
        Merge,
        LaneChange,
        Startled,
        Crash,
    }

    /// <summary>The thing that is currently limiting a car's acceleration.</summary>
    public enum Binding
    {
        Free,
        Leader,
        Line,
        Yield,
        Coop,
        End,
        Startle,
    }

    public enum YellowChoice
    {
        Undecided,
        Go,
        Stop,
    }

    /// <summary>What a driver has decided about the intersection it is approaching.</summary>
    public sealed class JunctionMemo
    {
        public YellowChoice Yellow;
        public bool RedRoll;
        /// <summary>Decided to run the red light.</summary>
        public bool Run;
        /// <summary>All-way stop: when the car came to rest at the line (negative if it has not), and whether it has been let go.</summary>
        public double Arrival = -1;
        public bool Released;
        public int Order = -1;
    }

    public sealed class Car
    {
        public int Id;
        /// <summary>Paint color as 0xRRGGBB.</summary>
        public int Color;
        public double Len = 4.5;
        public double Wid = 1.9;
        public DriverParams Drv;

        public int Lane;
        /// <summary>Distance of the car's center along its lane.</summary>
        public double S;
        public double V;
        public double A;
        public int PrevLane = -1;
        public Turn Turn;

        /// <summary>Sideways offset from the lane center during a lane change (positive is right).</summary>
        public double Lat;
        public double LcFrom;
        public double LcT = 1;
        public double LcDur = 1;
        /// <summary>Lane the car is still partly in, and its position there relative to S.</summary>
        public int GhostLane = -1;
        public double GhostOff;
        public double LcCooldownUntil;
        public double NextLcCheck;
        /// <summary>Side the driver needs to move to (-1 left, 1 right), and how badly (0 to 1).</summary>
        public int WantSide;
        public double Urgency;

        public JunctionMemo Junc = new JunctionMemo();
        public double StartleAt;
        public double StartleUntil;
        public bool Crashed;
        public double CrashedAt;

        /// <summary>World pose, refreshed every step.</summary>
        public double X, Y, H;
        /// <summary>Position on the lane centerline (ignores the sideways offset).</summary>
        public double Cx, Cy;

        // Readouts for the overlay and inspector.
        public CarState State;
        public int Blinker;
        public double V0;
        public int LeaderId = -1;
        public double Gap = double.PositiveInfinity;
        public double StopDist = double.PositiveInfinity;
        public Binding StopKind;

        public double ANext;
        public double ALead;
        public readonly float[] ObsGap = new float[Simulation.ObsN];
        public readonly float[] ObsV = new float[Simulation.ObsN];

        // Recent history for graphs, 10 samples per second, as a ring.
        public readonly float[] HistV = new float[Simulation.HistN];
        public readonly float[] HistGap = new float[Simulation.HistN];
        public readonly float[] HistA = new float[Simulation.HistN];
        public int HistCount;
        public int HistIndex;

        public bool ChangingLanes => LcT < LcDur;
    }

    public struct RoutePoint
    {
        public Lane Lane;
        public double S;
    }

    public sealed class Simulation
    {
        /// <summary>Length of one simulation step in seconds. The sim always advances in these steps.</summary>
        public const double Dt = 1.0 / 30;
        public const int ObsN = 64;
        public const int HistN = 300;
        /// <summary>Ticks between history samples (10 per second).</summary>
        private const int HistEvery = 3;
        private const double Lookahead = 150;
        private static readonly double[] CurveLook = { 0, 8, 16, 28, 42, 60 };
        /// <summary>Distance over which a driver who must change lanes gets steadily more insistent.</summary>
        private const double MandatoryDist = 150;

        private static readonly int[] CarColors = { 0xf25f5c, 0xffe066, 0x70c1b3, 0x4ea8de, 0xf4a261, 0xe9ecef, 0xb388eb, 0x8ac926, 0xff8fab, 0x90e0ef };

        public readonly World World;
        public readonly List<Lane> Lanes;
        public readonly Junction Junction = new Junction();
        public List<Car> Cars = new List<Car>();
        public double Time;
        public int Tick;
        public Rng Rng;
        public int Crashes;
        public int Passes;
        /// <summary>Remove crashed cars after CrashClearAfter seconds.</summary>
        public bool AutoClear = true;
        public double CrashClearAfter = 12;
        public Action<Car, Car> OnCrash;

        private int nextId = 1;
        private readonly List<Entry>[] laneCars;
        private readonly List<ActiveCar> active = new List<ActiveCar>();
        private readonly double[] minRad;

        private struct Entry
        {
            public Car Car;
            public double S;
            public bool Ghost;
        }

        private struct ActiveCar
        {
            public Car Car;
            public Lane Conn;
            public double U;
        }

        private sealed class Neighbor
        {
            public Car Car;
            /// <summary>Bumper-to-bumper gap.</summary>
            public double Gap;
        }

        private struct Target
        {
            public Lane Lane;
            public double S;
            public double D;
        }

        public Simulation(World world, uint seed = 1)
        {
            World = world;
            Lanes = world.Lanes;
            Rng = new Rng(seed);
            laneCars = new List<Entry>[Lanes.Count];
            minRad = new double[Lanes.Count];
            for (int i = 0; i < Lanes.Count; i++)
            {
                laneCars[i] = new List<Entry>();
                minRad[i] = Lanes[i].Path.MinRadius();
            }
        }

        // ---------------------------------------------------------------- cars

        /// <summary>Place a car. A null turn picks one at random.</summary>
        public Car AddCar(int lane, double s, double v = 0, DriverParams drv = null, Turn? turn = null)
        {
            int id = nextId++;
            var car = new Car
            {
                Id = id,
                Color = CarColors[id % CarColors.Length],
                Drv = (drv ?? DriverParams.Normal).Clone(),
                Lane = lane,
                S = s,
                V = v,
            };
            car.Turn = turn ?? PickTurn();
            car.NextLcCheck = Time + Rng.Next() * 0.4;
            for (int i = 0; i < ObsN; i++) car.ObsGap[i] = 1e6f;
            Cars.Add(car);
            UpdatePose(car);
            return car;
        }

        /// <summary>Drop a car onto a free stretch of road. Returns null when the track is full.</summary>
        public Car Spawn(DriverParams baseDriver = null, double variation = 0)
        {
            if (baseDriver == null) baseDriver = DriverParams.Normal;
            var roads = Lanes.FindAll(l => l.Kind == LaneKind.Road && l.Length > 60);
            for (int attempt = 0; attempt < 60; attempt++)
            {
                Lane lane = Rng.Pick(roads);
                double s = Rng.Range(15, lane.Length - 30);
                bool free = true;
                foreach (var c in Cars)
                {
                    if (c.Lane == lane.Id && Math.Abs(c.S - s) < 16) free = false;
                    if (c.GhostLane == lane.Id && Math.Abs(c.S + c.GhostOff - s) < 16) free = false;
                }
                if (!free) continue;
                DriverParams drv = baseDriver.Clone();
                if (variation > 0)
                {
                    for (int k = 0; k < DriverParams.Count; k++)
                    {
                        double lo = DriverParams.Ranges[k, 0];
                        double hi = DriverParams.Ranges[k, 1];
                        drv.Set(k, Geo.Clamp(drv.Get(k) + (Rng.Next() - 0.5) * variation * (hi - lo), lo, hi));
                    }
                }
                return AddCar(lane.Id, s, 6, drv);
            }
            return null;
        }

        public void RemoveCar(int id)
        {
            Cars.RemoveAll(c => c.Id == id);
        }

        public void Clear()
        {
            Cars.Clear();
        }

        public void ClearCrashes()
        {
            Cars.RemoveAll(c => c.Crashed);
        }

        public Car CarById(int id)
        {
            foreach (var c in Cars) if (c.Id == id) return c;
            return null;
        }

        public void SetMode(JunctionMode mode)
        {
            if (Junction.Mode == mode) return;
            Junction.Mode = mode;
            foreach (var c in Cars) c.Junc = new JunctionMemo();
        }

        private Turn PickTurn()
        {
            double r = Rng.Next();
            return r < 0.25 ? Turn.Left : r < 0.75 ? Turn.Straight : Turn.Right;
        }

        // ---------------------------------------------------------------- step

        public void Step()
        {
            Junction.Update(Dt, Time);
            BuildLists();
            foreach (var car in Cars) if (!car.Crashed) Perceive(car);
            LaneChanges();
            Integrate();
            DetectCrashes();
            Tick++;
            Time = Tick * Dt;
        }

        private void BuildLists()
        {
            foreach (var list in laneCars) list.Clear();
            foreach (var car in Cars)
            {
                laneCars[car.Lane].Add(new Entry { Car = car, S = car.S });
                if (car.GhostLane >= 0)
                {
                    double gs = car.S + car.GhostOff;
                    if (gs > 0 && gs < Lanes[car.GhostLane].Length) laneCars[car.GhostLane].Add(new Entry { Car = car, S = gs, Ghost = true });
                }
            }
            foreach (var list in laneCars)
            {
                list.Sort((p, q) => p.S != q.S ? p.S.CompareTo(q.S) : p.Car.Id.CompareTo(q.Car.Id));
            }
            active.Clear();
            foreach (var car in Cars)
            {
                Lane conn;
                double u;
                if (JunctionCoord(car, out conn, out u) && (u + car.Len / 2 > 0 || car.Junc.Released))
                {
                    active.Add(new ActiveCar { Car = car, Conn = conn, U = u });
                }
            }
        }

        private void UpdatePose(Car car)
        {
            PathPose p = Lanes[car.Lane].Path.At(car.S);
            car.Cx = p.X;
            car.Cy = p.Y;
            car.X = p.X + Math.Sin(p.H) * car.Lat;
            car.Y = p.Y - Math.Cos(p.H) * car.Lat;
            car.H = p.H;
        }

        // ---------------------------------------------------------------- routes

        /// <summary>The path through the intersection this car will take from an inbound lane.</summary>
        private Lane PlannedConn(Car car, Lane lane)
        {
            return Lanes[lane.ConnectorFor(car.Turn)];
        }

        private int RouteNext(Car car, Lane lane)
        {
            if (lane.Kind == LaneKind.Connector) return lane.Next;
            if (lane.Connectors != null && lane.Id == car.Lane) return PlannedConn(car, lane).Id;
            return -1;
        }

        /// <summary>The lane and position a given distance ahead of the car along its route. False if the route ends first.</summary>
        public bool Ahead(Car car, double dist, out RoutePoint point)
        {
            Lane lane = Lanes[car.Lane];
            double s = car.S + dist;
            while (s > lane.Length)
            {
                int next = RouteNext(car, lane);
                if (next < 0)
                {
                    point = default(RoutePoint);
                    return false;
                }
                s -= lane.Length;
                lane = Lanes[next];
            }
            point = new RoutePoint { Lane = lane, S = s };
            return true;
        }

        /// <summary>
        /// Where a car is relative to its path through the intersection: negative
        /// before the stop line, 0 at the line, past the path's length once clear.
        /// </summary>
        private bool JunctionCoord(Car car, out Lane conn, out double u)
        {
            Lane lane = Lanes[car.Lane];
            if (lane.Kind == LaneKind.Connector)
            {
                conn = lane;
                u = car.S;
                return true;
            }
            if (lane.Connectors != null)
            {
                conn = PlannedConn(car, lane);
                u = car.S - lane.Length;
                return true;
            }
            if (car.PrevLane >= 0 && car.S - car.Len / 2 < 0)
            {
                Lane prev = Lanes[car.PrevLane];
                if (prev.Kind == LaneKind.Connector)
                {
                    conn = prev;
                    u = prev.Length + car.S;
                    return true;
                }
            }
            conn = null;
            u = 0;
            return false;
        }

        // ---------------------------------------------------------------- seeing

        /// <summary>The nearest car ahead along this car's route.</summary>
        private void FindLeader(Car car, out double gap, out double v, out int id)
        {
            double half = car.Len / 2;
            Lane lane = Lanes[car.Lane];
            double bestGap = double.PositiveInfinity;
            double bestV = 0;
            int bestId = -1;
            void Consider(Entry e, double g)
            {
                if (g < bestGap)
                {
                    bestGap = g;
                    bestV = e.Car.V;
                    bestId = e.Car.Id;
                }
            }

            foreach (var e in laneCars[car.Lane])
            {
                if (e.Car == car || e.S < car.S || (e.S == car.S && e.Car.Id < car.Id)) continue;
                Consider(e, e.S - e.Car.Len / 2 - car.S - half);
                break;
            }
            if (lane.Siblings != null)
            {
                foreach (var sib in lane.Siblings)
                {
                    foreach (var e in laneCars[sib.Id])
                    {
                        if (e.S > car.S && e.S - e.Car.Len / 2 < sib.SDiverge) Consider(e, e.S - e.Car.Len / 2 - car.S - half);
                    }
                }
            }
            if (bestId < 0)
            {
                double dist = lane.Length - car.S;
                Lane cur = lane;
                for (int depth = 0; depth < 3 && dist < Lookahead; depth++)
                {
                    int nid = RouteNext(car, cur);
                    if (nid < 0) break;
                    Lane nl = Lanes[nid];
                    if (laneCars[nid].Count > 0)
                    {
                        Entry first = laneCars[nid][0];
                        Consider(first, dist + first.S - first.Car.Len / 2 - half);
                    }
                    if (nl.Siblings != null)
                    {
                        foreach (var sib in nl.Siblings)
                        {
                            foreach (var e in laneCars[sib.Id])
                            {
                                if (e.S - e.Car.Len / 2 < sib.SDiverge) Consider(e, dist + e.S - e.Car.Len / 2 - half);
                            }
                        }
                    }
                    if (bestId >= 0) break;
                    dist += nl.Length;
                    cur = nl;
                }
            }
            gap = bestGap;
            v = bestV;
            id = bestId;
        }

        private double ConnSpeed(Lane conn, Derived d)
        {
            double r = minRad[conn.Id];
            return r < 400 ? Math.Sqrt(d.ALat * r) : double.PositiveInfinity;
        }

        /// <summary>Decide this car's acceleration for the step from what its driver can see.</summary>
        private void Perceive(Car car)
        {
            Derived d = DriverModel.Derive(car.Drv);
            DriverParams drv = car.Drv;
            Lane lane = Lanes[car.Lane];
            double half = car.Len / 2;

            // Desired speed: the limit, scaled by the driver, slowed for curves ahead.
            double v0 = lane.SpeedLimit * drv.Speed;
            foreach (double look in CurveLook)
            {
                RoutePoint pt;
                if (!Ahead(car, look, out pt)) break;
                double r = pt.Lane.Kind == LaneKind.Connector ? minRad[pt.Lane.Id] : pt.Lane.Path.RadiusAt(pt.S);
                if (r < 400)
                {
                    double vc = Math.Sqrt(d.ALat * r);
                    v0 = Math.Min(v0, Math.Sqrt(vc * vc + 2 * d.B * 0.6 * look));
                }
            }
            car.V0 = v0;

            double free = DriverModel.FreeAccel(car.V, v0, d);
            double acc = free;
            Binding binding = Binding.Free;
            void Use(double a, Binding kind)
            {
                if (a < acc)
                {
                    acc = a;
                    binding = kind;
                }
            }

            // The car ahead, as the driver saw it one reaction time ago.
            double leadGap, leadV;
            int leadId;
            FindLeader(car, out leadGap, out leadV, out leadId);
            car.LeaderId = leadId;
            car.Gap = leadGap;
            int i = Tick % ObsN;
            car.ObsGap[i] = (float)Math.Min(leadGap, 1e6);
            car.ObsV[i] = (float)leadV;
            int delay = Math.Min(ObsN - 1, (int)Math.Floor(drv.Reaction / Dt + 0.5));
            int j = ((Tick - delay) % ObsN + ObsN) % ObsN;
            if (car.ObsGap[j] < Lookahead)
            {
                // Drivers know their own speed, so they extrapolate the old gap forward;
                // what arrives late is news of the other car changing speed.
                double closing = car.V - car.ObsV[j];
                double gapNow = car.ObsGap[j] - closing * delay * Dt;
                Use(DriverModel.Idm(car.V, v0, gapNow, closing, drv.Headway, d), Binding.Leader);
            }
            car.ALead = leadGap < Lookahead ? Math.Min(free, DriverModel.Idm(car.V, v0, leadGap, car.V - leadV, drv.Headway, d)) : free;

            // Fixed things to stop for: the end of the lane, the stop line, a gap to wait for.
            car.StopDist = double.PositiveInfinity;
            car.StopKind = Binding.Free;
            double StopFor(double dist, Binding kind)
            {
                double a = DriverModel.Idm(car.V, v0, Math.Max(dist, 0) + d.S0, car.V, drv.Headway, d);
                Use(a, kind);
                if (dist < car.StopDist)
                {
                    car.StopDist = dist;
                    car.StopKind = kind;
                }
                return a;
            }
            if (lane.Kind == LaneKind.Road && lane.Connectors == null)
            {
                double a = StopFor(lane.Length - car.S - half - 0.5, Binding.End);
                car.ALead = Math.Min(car.ALead, a);
            }
            double jsDist;
            Binding jsKind;
            if (JunctionStop(car, lane, d, v0, out jsDist, out jsKind)) StopFor(jsDist, jsKind);

            // Courtesy: ease off for a car ahead in the next lane that needs to merge in.
            if (drv.Politeness > 0.15 && lane.Kind == LaneKind.Road)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (int id in pass == 0 ? lane.Lefts : lane.Rights)
                    {
                        foreach (var e in laneCars[id])
                        {
                            Car m = e.Car;
                            if (e.Ghost || m.Crashed || m.WantSide == 0 || m.Urgency < 0.4) continue;
                            Lane mLane = Lanes[m.Lane];
                            bool wantsMine = (m.WantSide < 0 ? mLane.Lefts : mLane.Rights).Contains(lane.Id);
                            if (!wantsMine) continue;
                            Projection pr = lane.Path.Project(m.Cx, m.Cy);
                            if (pr.D > World.LaneWidth * 1.4) continue;
                            double g = pr.S - m.Len / 2 - (car.S + half);
                            if (g < 0.5 || g > 40) continue;
                            Use(Math.Max(DriverModel.Idm(car.V, v0, g, car.V - m.V, drv.Headway, d), -d.B * drv.Politeness), Binding.Coop);
                        }
                    }
                }
            }

            // Startled by a cut-in: brake harder than needed for a moment.
            if (car.StartleAt > 0 && Time >= car.StartleAt)
            {
                car.StartleUntil = Time + 0.5 + d.Nerves;
                car.StartleAt = 0;
            }
            if (Time < car.StartleUntil)
            {
                if (leadGap > 1.3 * (d.S0 + car.V * drv.Headway) || car.V < 0.5) car.StartleUntil = 0;
                else Use(-d.B * (1.3 + 1.2 * d.Nerves), Binding.Startle);
            }

            car.ANext = Geo.Clamp(acc, -DriverModel.MaxBrake, d.A);

            if (binding == Binding.Startle) car.State = CarState.Startled;
            else if (car.ChangingLanes) car.State = CarState.LaneChange;
            else if (binding == Binding.Leader) car.State = acc < -1.5 ? CarState.Brake : CarState.Follow;
            else if (binding == Binding.Line) car.State = car.V < 0.5 ? CarState.Wait : CarState.Stopping;
            else if (binding == Binding.Yield) car.State = CarState.Yield;
            else if (binding == Binding.Coop) car.State = CarState.LettingIn;
            else if (binding == Binding.End) car.State = CarState.Merge;
            else car.State = CarState.Cruise;

            // Turn signals.
            if (car.ChangingLanes) car.Blinker = car.LcFrom > 0 ? -1 : 1;
            else if (car.WantSide != 0 && car.Urgency > 0.1) car.Blinker = car.WantSide;
            else
            {
                Turn turn = Turn.Straight;
                if (lane.Kind == LaneKind.Connector) turn = lane.Turn;
                else if (lane.Connectors != null && lane.Length - car.S < 55) turn = PlannedConn(car, lane).Turn;
                car.Blinker = turn == Turn.Left ? -1 : turn == Turn.Right ? 1 : 0;
            }
        }

        // ---------------------------------------------------------------- intersection

        /// <summary>Where, if anywhere, this car has to stop for the intersection.</summary>
        private bool JunctionStop(Car car, Lane lane, Derived d, double v0, out double stopDist, out Binding stopKind)
        {
            stopDist = 0;
            stopKind = Binding.Free;
            double half = car.Len / 2;
            Lane conn;
            double u;
            if (lane.Kind == LaneKind.Connector)
            {
                conn = lane;
                u = car.S;
            }
            else if (lane.Connectors != null)
            {
                double dLine = lane.Length - car.S - half;
                if (dLine > 90) return false;
                conn = PlannedConn(car, lane);
                u = car.S - lane.Length;
                if (dLine > -0.5 && !MayEnter(car, lane, conn, dLine, d))
                {
                    stopDist = dLine - 0.6;
                    stopKind = Binding.Line;
                    return true;
                }
            }
            else
            {
                return false;
            }

            // Inside (or cleared to enter): wait short of any shared stretch another car needs first.
            bool found = false;
            double myFront = u + half;
            double vc = Math.Min(v0, ConnSpeed(conn, d));
            double margin = 0.8 + 1.2 * d.Nerves;
            double myRank = Rank(car, conn, u);
            foreach (var cz in conn.Conflicts)
            {
                if (myFront > cz.SIn) continue;
                double dIn = cz.SIn - myFront;
                if (dIn > 60) continue;
                double tIn = DriverModel.TravelTime(dIn, car.V, d.A, vc);
                double tOut = DriverModel.TravelTime(cz.SOut - (u - half), car.V, d.A, vc);
                Lane foeConn = Lanes[cz.Foe];
                bool wait = false;
                ForFoes(foeConn, (foe, uf) =>
                {
                    if (wait || foe == car) return;
                    double fFront = uf + foe.Len / 2;
                    double fRear = uf - foe.Len / 2;
                    if (fRear > cz.FoeOut) return;
                    bool inZone = fFront > cz.FoeIn;
                    if (foe.Crashed)
                    {
                        if (inZone) wait = true;
                        return;
                    }
                    if (!inZone && !IsActive(foe, foeConn, uf)) return;
                    Derived fd = DriverModel.Derive(foe.Drv);
                    double fvc = Math.Min(foeConn.SpeedLimit * foe.Drv.Speed, ConnSpeed(foeConn, fd));
                    double fOut = DriverModel.TravelTime(cz.FoeOut - fRear, foe.V, fd.A, fvc);
                    if (inZone)
                    {
                        if (tIn - margin * 0.5 < fOut) wait = true;
                        return;
                    }
                    double fIn = DriverModel.TravelTime(cz.FoeIn - fFront, foe.V, fd.A, fvc);
                    if (tIn - margin < fOut && fIn < tOut + margin)
                    {
                        double fr = Rank(foe, foeConn, uf);
                        if (fr > myRank || (fr == myRank && (fIn < tIn || (fIn == tIn && foe.Id < car.Id)))) wait = true;
                    }
                });
                if (wait)
                {
                    double dist = dIn - 1.0;
                    if (!found || dist < stopDist)
                    {
                        stopDist = dist;
                        stopKind = Binding.Yield;
                        found = true;
                    }
                }
            }
            return found;
        }

        /// <summary>May this car cross the stop line now?</summary>
        private bool MayEnter(Car car, Lane lane, Lane conn, double dLine, Derived d)
        {
            JunctionMemo j = car.Junc;
            Junction jn = Junction;

            if (jn.Mode == JunctionMode.AllWay)
            {
                if (j.Released) return true;
                double rolling = 0.4 + (1 - car.Drv.Compliance) * 4;
                if (dLine > 3.2 || car.V > rolling) return false;
                if (j.Arrival < 0) j.Arrival = Time;
                if (Time - j.Arrival < (0.4 + 0.6 * d.Nerves) * car.Drv.Compliance) return false;
                foreach (var o in active)
                {
                    if (o.Car == car || o.Conn.Id == conn.Id) continue;
                    Conflict cz = conn.ConflictWith(o.Conn.Id);
                    if (cz != null && o.U - o.Car.Len / 2 < cz.FoeOut) return false;
                }
                foreach (var o in Cars)
                {
                    JunctionMemo oj = o.Junc;
                    if (o == car || o.Crashed || oj.Arrival < 0 || oj.Released) continue;
                    Lane ol = Lanes[o.Lane];
                    if (ol.Connectors == null) continue;
                    if (oj.Arrival < j.Arrival || (oj.Arrival == j.Arrival && o.Id < car.Id))
                    {
                        if (conn.ConflictWith(PlannedConn(o, ol).Id) != null) return false;
                    }
                }
                if (!ExitHasSpace(car, conn)) return false;
                j.Released = true;
                j.Order = jn.Seq++;
                active.Add(new ActiveCar { Car = car, Conn = conn, U = car.S - lane.Length });
                return true;
            }

            if (j.Run) return true;
            SignalLight light = jn.ArmState(lane.Arm, Time - car.Drv.Reaction);
            bool ok;
            if (light == SignalLight.Green)
            {
                j.Yellow = YellowChoice.Undecided;
                ok = true;
            }
            else if (light == SignalLight.Yellow)
            {
                if (j.Yellow == YellowChoice.Undecided)
                {
                    double brakingNeeded = car.V * car.V / (2 * Math.Max(dLine, 0.5));
                    j.Yellow = brakingNeeded > 1.8 + 2.2 * car.Drv.Aggression ? YellowChoice.Go : YellowChoice.Stop;
                }
                if (j.Yellow == YellowChoice.Go && car.V < 2 && dLine > 2) j.Yellow = YellowChoice.Stop;
                ok = j.Yellow == YellowChoice.Go;
            }
            else if (j.Yellow == YellowChoice.Go && car.V > 2)
            {
                ok = true;
            }
            else
            {
                if (!j.RedRoll && dLine < 70)
                {
                    j.RedRoll = true;
                    j.Run = Rng.Next() >= car.Drv.Compliance;
                }
                ok = j.Run;
            }
            return ok && ExitHasSpace(car, conn);
        }

        /// <summary>Don't block the box: is there room on the far side?</summary>
        private bool ExitHasSpace(Car car, Lane conn)
        {
            Lane exit = Lanes[conn.Next];
            if (laneCars[exit.Id].Count == 0) return true;
            Entry first = laneCars[exit.Id][0];
            if (first.Car.V > 2.5 && !first.Car.Crashed) return true;
            int inside = 0;
            foreach (int p in exit.Preds) inside += laneCars[p].Count;
            return first.S - first.Car.Len / 2 > (inside + 1) * (car.Len + 2.5);
        }

        /// <summary>Is this car going to go through, so that others must plan around it?</summary>
        private bool IsActive(Car foe, Lane foeConn, double uf)
        {
            if (uf + foe.Len / 2 > 0) return true;
            if (Junction.Mode == JunctionMode.AllWay) return foe.Junc.Released;
            if (foe.Junc.Run) return true;
            SignalLight light = Junction.ArmState(foeConn.FromArm);
            if (light == SignalLight.Green) return true;
            if (light == SignalLight.Yellow) return foe.Junc.Yellow == YellowChoice.Go || (foe.Junc.Yellow == YellowChoice.Undecided && -uf - foe.Len / 2 < 40);
            return foe.Junc.Yellow == YellowChoice.Go && foe.V > 2;
        }

        /// <summary>Right of way: the higher rank goes first.</summary>
        private double Rank(Car car, Lane conn, double u)
        {
            if (Junction.Mode == JunctionMode.AllWay) return 1e6 - (car.Junc.Order >= 0 ? car.Junc.Order : 1e5);
            bool crossed = u + car.Len / 2 > 0;
            if (crossed && Junction.ArmState(conn.FromArm) != SignalLight.Green) return 4; // clearing the box
            if (car.Junc.Run) return 0; // red-light runner
            return conn.Turn == Turn.Left ? 2 : 3; // left turns give way to oncoming traffic
        }

        /// <summary>Every car using, approaching or just leaving a path through the intersection.</summary>
        private void ForFoes(Lane conn, Action<Car, double> fn)
        {
            foreach (var e in laneCars[conn.Id]) fn(e.Car, e.S);
            Lane from = Lanes[conn.From];
            foreach (var e in laneCars[from.Id])
            {
                if (e.Ghost || from.Length - e.S > 80) continue;
                if (PlannedConn(e.Car, from) == conn) fn(e.Car, e.S - from.Length);
            }
            foreach (var e in laneCars[conn.Next])
            {
                if (e.S - e.Car.Len / 2 >= 0) break;
                if (!e.Ghost && e.Car.PrevLane == conn.Id) fn(e.Car, conn.Length + e.S);
            }
        }

        // ---------------------------------------------------------------- lane changes

        private bool TargetLane(Car car, int side, out Target target)
        {
            Lane lane = Lanes[car.Lane];
            foreach (int id in side < 0 ? lane.Lefts : lane.Rights)
            {
                Lane t = Lanes[id];
                Projection pr = t.Path.Project(car.Cx, car.Cy);
                if (pr.D < World.LaneWidth * 1.4 && pr.S > 2 && pr.S < t.Length - 2)
                {
                    target = new Target { Lane = t, S = pr.S, D = pr.D };
                    return true;
                }
            }
            target = default(Target);
            return false;
        }

        private Neighbor LeaderIn(Lane t, double s, Car self)
        {
            foreach (var e in laneCars[t.Id])
            {
                if (e.Car == self || e.S <= s) continue;
                return new Neighbor { Car = e.Car, Gap = e.S - e.Car.Len / 2 - (s + self.Len / 2) };
            }
            int nextId = -1;
            if (t.Kind == LaneKind.Connector) nextId = t.Next;
            else if (t.Connectors != null) nextId = t.ConnectorFor(self.Turn);
            if (nextId >= 0 && laneCars[nextId].Count > 0)
            {
                Entry first = laneCars[nextId][0];
                if (first.Car != self) return new Neighbor { Car = first.Car, Gap = t.Length - s + first.S - first.Car.Len / 2 - self.Len / 2 };
            }
            return null;
        }

        private Neighbor FollowerIn(Lane t, double s, Car self)
        {
            var list = laneCars[t.Id];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Entry e = list[i];
                if (e.Car == self || e.S >= s) continue;
                return new Neighbor { Car = e.Car, Gap = s - self.Len / 2 - (e.S + e.Car.Len / 2) };
            }
            Neighbor best = null;
            foreach (int p in t.Preds)
            {
                var pl = laneCars[p];
                if (pl.Count == 0) continue;
                Entry e = pl[pl.Count - 1];
                if (e.Car == self) continue;
                double gap = s - self.Len / 2 + (Lanes[p].Length - e.S) - e.Car.Len / 2;
                if (best == null || gap < best.Gap) best = new Neighbor { Car = e.Car, Gap = gap };
            }
            return best;
        }

        private bool AnyAllows(List<int> ids, Turn turn)
        {
            foreach (int id in ids) if (Lanes[id].Allows(turn)) return true;
            return false;
        }

        private static readonly int[] Sides = { -1, 1 };

        private void LaneChanges()
        {
            // Changing lanes rebuilds the per-lane lists but never adds or removes cars.
            for (int ci = 0; ci < Cars.Count; ci++)
            {
                Car car = Cars[ci];
                if (car.Crashed || car.ChangingLanes) continue;
                Lane lane = Lanes[car.Lane];
                if (lane.Kind != LaneKind.Road)
                {
                    car.WantSide = 0;
                    car.Urgency = 0;
                    continue;
                }
                if (Time < car.NextLcCheck) continue;
                car.NextLcCheck = Time + 0.4;
                Derived d = DriverModel.Derive(car.Drv);
                double half = car.Len / 2;
                double remaining = lane.Length - car.S - half;

                // Does the driver have to leave this lane?
                int need = 0;
                double urgency = 0;
                if (lane.Connectors == null)
                {
                    need = lane.Lefts.Count > 0 ? -1 : 1;
                    urgency = Geo.Clamp(1 - (remaining - 5) / MandatoryDist, 0, 1);
                }
                else if (!lane.Allows(car.Turn))
                {
                    double toSolid = remaining - LabWorld.SolidLine;
                    if (toSolid < 5)
                    {
                        car.Turn = PlannedConn(car, lane).Turn; // too late: take what this lane allows
                    }
                    else
                    {
                        if (AnyAllows(lane.Lefts, car.Turn)) need = -1;
                        else if (AnyAllows(lane.Rights, car.Turn)) need = 1;
                        urgency = Geo.Clamp(1 - toSolid / MandatoryDist, 0, 1);
                    }
                }
                car.WantSide = urgency > 0.05 ? need : 0;
                car.Urgency = need != 0 ? urgency : 0;

                if (lane.Connectors != null && remaining < LabWorld.SolidLine) continue;
                if (car.S < 12) continue;
                if (Time < car.LcCooldownUntil && urgency < 0.5) continue;

                bool have = false;
                int bestSide = 0;
                Target bestTarget = default(Target);
                Neighbor bestFol = null;
                double bestScore = 0;
                foreach (int side in Sides)
                {
                    if (need != 0 && side != need && urgency > 0) continue;
                    Target tgt;
                    if (!TargetLane(car, side, out tgt)) continue;
                    Lane t = tgt.Lane;
                    if (side == need)
                    {
                        if (lane.Connectors != null && !t.Allows(car.Turn)) continue;
                    }
                    else
                    {
                        if (t.Connectors == null && t.Length - tgt.S < 220) continue; // don't hop into a lane that is about to end
                        if (t.Connectors != null && !t.Allows(car.Turn) && remaining < 170) continue;
                    }
                    Neighbor lead = LeaderIn(t, tgt.S, car);
                    Neighbor fol = FollowerIn(t, tgt.S, car);
                    if (lead != null && lead.Gap < 1.0) continue;
                    if (fol != null && fol.Gap < 1.5) continue;

                    double v0 = car.V0;
                    double aNew = DriverModel.FreeAccel(car.V, v0, d);
                    if (lead != null) aNew = Math.Min(aNew, DriverModel.Idm(car.V, v0, lead.Gap, car.V - lead.Car.V, car.Drv.Headway, d));
                    double folCost = 0;
                    double aFol = 0;
                    if (fol != null && !fol.Car.Crashed)
                    {
                        Car f = fol.Car;
                        aFol = DriverModel.Idm(f.V, Math.Max(f.V0, 1), fol.Gap, f.V - car.V, f.Drv.Headway, DriverModel.Derive(f.Drv));
                        folCost = Math.Min(0, aFol - f.ANext);
                    }
                    double bSafe = d.BSafe + urgency * 2.5;
                    if (aFol < -bSafe || aNew < -bSafe) continue;
                    double bias = side == need ? 0.3 + urgency * 3 : 0;
                    double score = aNew - car.ALead + car.Drv.Politeness * folCost + bias;
                    if (score > d.LcThreshold && (!have || score > bestScore))
                    {
                        have = true;
                        bestSide = side;
                        bestTarget = tgt;
                        bestFol = fol;
                        bestScore = score;
                    }
                }
                if (have) ChangeLane(car, bestSide, bestTarget, bestFol);
            }
        }

        private void ChangeLane(Car car, int side, Target target, Neighbor fol)
        {
            Derived d = DriverModel.Derive(car.Drv);
            Lane t = target.Lane;
            double s = target.S;
            car.GhostLane = car.Lane;
            car.GhostOff = car.S - s;
            car.Lane = t.Id;
            car.S = s;
            car.PrevLane = -1;
            car.Lat = side < 0 ? target.D : -target.D;
            car.LcFrom = car.Lat;
            car.LcT = 0;
            car.LcDur = d.LcTime;
            car.LcCooldownUntil = Time + 2 + 3 * d.Nerves;
            car.WantSide = 0;
            car.Urgency = 0;
            Neighbor lead = LeaderIn(t, s, car);
            for (int i = 0; i < ObsN; i++)
            {
                car.ObsGap[i] = lead != null ? (float)lead.Gap : 1e6f;
                if (lead != null) car.ObsV[i] = (float)lead.Car.V;
            }
            // Did that cut someone off?
            if (fol != null && !fol.Car.Crashed && fol.Car.V > 2)
            {
                Car f = fol.Car;
                double closing = f.V - car.V;
                double timeGap = fol.Gap / f.V;
                double ttc = closing > 0.1 ? fol.Gap / closing : double.PositiveInfinity;
                if ((timeGap < 0.9 || ttc < 3.5) && f.StartleAt == 0 && Time >= f.StartleUntil)
                {
                    f.StartleAt = Time + f.Drv.Reaction;
                }
            }
            BuildLists();
        }

        /// <summary>
        /// Make a car change lanes right now, safe or not. With a negative id, picks
        /// the pair of cars best lined up for a cut-off. Returns the car that cut in,
        /// or null if no car could.
        /// </summary>
        public Car ForceCutIn(int id = -1)
        {
            BuildLists();
            Car bestCar = null;
            int bestSide = 0;
            Target bestTarget = default(Target);
            Neighbor bestFol = null;
            double bestScore = 0;
            foreach (var car in Cars)
            {
                if (id >= 0 && car.Id != id) continue;
                if (car.Crashed || car.ChangingLanes || Lanes[car.Lane].Kind != LaneKind.Road) continue;
                foreach (int side in Sides)
                {
                    Target tgt;
                    if (!TargetLane(car, side, out tgt)) continue;
                    Neighbor lead = LeaderIn(tgt.Lane, tgt.S, car);
                    Neighbor fol = FollowerIn(tgt.Lane, tgt.S, car);
                    if (lead != null && lead.Gap < 0.5) continue;
                    if (fol != null && fol.Gap < 0.8) continue;
                    double score;
                    if (id >= 0) score = fol != null ? 1000 - fol.Gap : 0;
                    else if (fol != null && fol.Gap < 25 && fol.Car.V > 3) score = 1000 - fol.Gap;
                    else continue;
                    if (bestCar == null || score > bestScore)
                    {
                        bestCar = car;
                        bestSide = side;
                        bestTarget = tgt;
                        bestFol = fol;
                        bestScore = score;
                    }
                }
            }
            if (bestCar == null) return null;
            ChangeLane(bestCar, bestSide, bestTarget, bestFol);
            return bestCar;
        }

        // ---------------------------------------------------------------- moving

        private void Integrate()
        {
            bool removed = false;
            foreach (var car in Cars)
            {
                if (car.Crashed)
                {
                    if (AutoClear && Time - car.CrashedAt > CrashClearAfter) removed = true;
                    continue;
                }
                car.A = car.ANext;
                car.V = Math.Max(0, car.V + car.A * Dt);
                car.S += car.V * Dt;

                double yaw = 0;
                if (car.ChangingLanes)
                {
                    car.LcT += Dt;
                    double f = Geo.Clamp(car.LcT / car.LcDur, 0, 1);
                    double before = car.Lat;
                    car.Lat = car.LcFrom * (1 - f * f * (3 - 2 * f));
                    yaw = Math.Atan2(-(car.Lat - before) / Dt, Math.Max(car.V, 3));
                    if (f >= 0.5) car.GhostLane = -1;
                    if (f >= 1) car.Lat = 0;
                }

                Lane lane = Lanes[car.Lane];
                while (car.S > lane.Length)
                {
                    int next = RouteNext(car, lane);
                    if (next < 0)
                    {
                        car.S = lane.Length;
                        car.V = 0;
                        break;
                    }
                    car.S -= lane.Length;
                    car.PrevLane = lane.Id;
                    car.Lane = next;
                    if (lane.Kind == LaneKind.Connector)
                    {
                        car.Junc = new JunctionMemo();
                        car.Turn = PickTurn();
                        Passes++;
                    }
                    lane = Lanes[next];
                }
                UpdatePose(car);
                car.H += yaw;

                if (Tick % HistEvery == 0)
                {
                    car.HistV[car.HistIndex] = (float)car.V;
                    car.HistGap[car.HistIndex] = (float)Math.Min(car.Gap, 99);
                    car.HistA[car.HistIndex] = (float)car.A;
                    car.HistIndex = (car.HistIndex + 1) % HistN;
                    car.HistCount = Math.Min(car.HistCount + 1, HistN);
                }
            }
            if (removed) Cars.RemoveAll(c => c.Crashed && Time - c.CrashedAt > CrashClearAfter);
        }

        private void DetectCrashes()
        {
            for (int i = 0; i < Cars.Count; i++)
            {
                Car a = Cars[i];
                for (int k = i + 1; k < Cars.Count; k++)
                {
                    Car b = Cars[k];
                    if (a.Crashed && b.Crashed) continue;
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    if (dx * dx + dy * dy > 36) continue;
                    if (!BoxesOverlap(a, b)) continue;
                    MarkCrashed(a);
                    MarkCrashed(b);
                    Crashes++;
                    if (OnCrash != null) OnCrash(a, b);
                }
            }
        }

        private void MarkCrashed(Car c)
        {
            if (c.Crashed) return;
            c.Crashed = true;
            c.CrashedAt = Time;
            c.V = 0;
            c.A = 0;
            c.State = CarState.Crash;
            c.Blinker = 0;
            c.GhostLane = -1;
            c.LcT = c.LcDur;
        }

        /// <summary>Separating-axis test on the two car rectangles, shrunk a little so a near miss is not a crash.</summary>
        private static bool BoxesOverlap(Car a, Car b)
        {
            for (int n = 0; n < 4; n++)
            {
                double ang = (n < 2 ? a.H : b.H) + (n % 2 == 1 ? Math.PI / 2 : 0);
                double ax = Math.Cos(ang);
                double ay = Math.Sin(ang);
                double dist = Math.Abs((b.X - a.X) * ax + (b.Y - a.Y) * ay);
                if (dist > Extent(a, ax, ay) + Extent(b, ax, ay)) return false;
            }
            return true;
        }

        private static double Extent(Car c, double ax, double ay)
        {
            double hl = c.Len / 2 - 0.15;
            double hw = c.Wid / 2 - 0.1;
            double fx = Math.Cos(c.H);
            double fy = Math.Sin(c.H);
            return hl * Math.Abs(fx * ax + fy * ay) + hw * Math.Abs(-fy * ax + fx * ay);
        }
    }
}
