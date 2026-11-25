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
        /// <summary>Braking hard because someone is breaking the rules in front of them.</summary>
        Scared,
        /// <summary>Hanging back from a car that is all over the road.</summary>
        Wary,
        /// <summary>Not looking at the road.</summary>
        Distracted,
        /// <summary>Slowing down to look at a crash.</summary>
        Gawking,
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
        Scare,
        Hazard,
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
        /// <summary>Stopped at a red light and is now turning right through it.</summary>
        public bool RightOnRed;
        public int Order = -1;
    }

    public enum CrashKind
    {
        RearEnd,
        Sideswipe,
        Broadside,
        HeadOn,
    }

    /// <summary>A record of one collision.</summary>
    public sealed class Accident
    {
        public double Time;
        public CrashKind Kind;
        /// <summary>A hard hit that wrecks the vehicles, as opposed to a fender bender they drive away from.</summary>
        public bool Major;
        /// <summary>How fast the two were closing, meters per second.</summary>
        public double ClosingSpeed;
        public double X, Y;
        public Car A, B;
    }

    public sealed class Car
    {
        public int Id;
        /// <summary>Paint color as 0xRRGGBB.</summary>
        public int Color;
        public VehicleSpec Vehicle = VehicleSpec.Of(VehicleKind.Car);
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
        /// <summary>Frightened by a rule-breaker: braking to a stop until this time.</summary>
        public double ScareUntil;
        public double NextScare;

        /// <summary>Name shown over a special driver's car; null for ordinary drivers.</summary>
        public string Special;
        /// <summary>The car's paint flashes.</summary>
        public bool Flash;
        /// <summary>Sideways drift of a driver who cannot hold the lane (positive is right).</summary>
        public double Wobble;
        public double WobblePhase;
        /// <summary>Looking away from the road until this time.</summary>
        public double LapseUntil;
        public double NextLapse;
        public double LeadV;
        public double NextBump;
        /// <summary>Seconds spent stopped inside an intersection.</summary>
        public double BoxWait;
        /// <summary>Has driven off the edge of the map.</summary>
        public bool Gone;
        public bool Crashed;
        public double CrashedAt;
        /// <summary>Wrecked and waiting to be towed, as opposed to stopped after a fender bender.</summary>
        public bool CrashMajor;
        /// <summary>When the wreck is towed away, or the driver gets going again after a fender bender.</summary>
        public double CrashClearAt;
        /// <summary>Just pulled away from a fender bender: cannot crash again for a moment.</summary>
        public double CrashImmuneUntil;
        /// <summary>The car it last hit. The two cannot crash into each other again until they have come apart.</summary>
        public int CrashPartner = -1;
        /// <summary>The car ahead is a wreck: get round it.</summary>
        public bool BlockedAhead;

        /// <summary>World pose, refreshed every step.</summary>
        public double X, Y, H;
        /// <summary>Height above the ground: more than 0 on a bridge.</summary>
        public double Height;
        /// <summary>The node at the edge of the map this driver is heading for, or -1 to wander, turning at random.</summary>
        public int Dest = -1;
        /// <summary>When the journey began, and how long it would take on empty roads.</summary>
        public double Born, Ideal;
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
        /// <summary>A car counts as inside an intersection once its nose is this far past the stop line. Overshooting by less does not.</summary>
        private const double Committed = 0.5;


        public readonly World World;
        public readonly List<Lane> Lanes;
        /// <summary>One controller per intersection, in the same order as World.Nodes.</summary>
        public readonly List<Junction> Junctions = new List<Junction>();
        public List<Car> Cars = new List<Car>();
        public double Time;
        public int Tick;
        public Rng Rng;
        public int Crashes;
        /// <summary>With crashes off: times a car ran into the back of another and was simply held behind it.</summary>
        public int Bumps;
        /// <summary>When false, cars that touch are not wrecked: a car is held behind the one it hit, and cars that cross paths pass through each other.</summary>
        public bool CrashesEnabled = true;
        /// <summary>Vehicles that have driven off the edge of the map.</summary>
        public int Left;
        /// <summary>New vehicles per minute on each city-street lane that enters the map; faster roads get proportionally more.</summary>
        public double ArrivalsPerMinute = 6;
        /// <summary>The kind of driver that arrives, how much they vary, and the share of trucks and motorcycles.</summary>
        public DriverParams ArrivalDriver;
        public double ArrivalVariation = 0.3;
        public double ArrivalTrucks = 0.12;
        public double ArrivalMotorcycles = 0.06;
        /// <summary>No new arrivals while this many vehicles are on the map.</summary>
        public int MaxCars = 320;
        /// <summary>
        /// When true, vehicles that arrive are going somewhere: each picks a way off the map
        /// that it can reach and takes the quickest route. When false they turn at random.
        /// </summary>
        public bool UseRoutes;
        /// <summary>The journeys traffic wants to make. Empty means every arrival may head for any other way off the map.</summary>
        public List<Trip> Demand = new List<Trip>();
        /// <summary>Journeys finished at the place they were heading for, and vehicles that left somewhere else.</summary>
        public int Arrived, Strays;
        /// <summary>For finished journeys: the seconds they took in all, and the seconds they would have taken on empty roads.</summary>
        public double TripSeconds, TripIdeal;
        private Routes routes;
        /// <summary>How to get to each way off the map.</summary>
        public Routes Routes => routes ?? (routes = new Routes(World));
        /// <summary>Allow turning right at a red light after stopping.</summary>
        public bool RightOnRed = true;
        /// <summary>Cars that have made it through an intersection.</summary>
        public int Passes;
        /// <summary>Clear crashes after a while: wrecks are towed away and fender benders drive off.</summary>
        public bool AutoClear = true;
        /// <summary>Closing speed, in meters per second, above which a collision wrecks the vehicles (about 11 mph).</summary>
        public double MajorCrashSpeed = 5;
        /// <summary>Seconds before a wreck is towed away, and before the drivers in a fender bender move on. Each varies a little.</summary>
        public double TowAfter = 50;
        public double FenderBenderDelay = 14;
        public readonly List<Accident> Accidents = new List<Accident>();
        public Action<Accident> OnAccident;
        private readonly List<Car> wrecks = new List<Car>();

        private int nextId = 1;
        private readonly List<Entry>[] laneCars;
        private readonly List<ActiveCar> active = new List<ActiveCar>();
        private readonly double[] minRad;
        private Car foundLeader;
        private readonly double[] nextArrival;
        private bool scareSeen;

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
            nextArrival = new double[Lanes.Count];
            for (int i = 0; i < Lanes.Count; i++)
            {
                laneCars[i] = new List<Entry>();
                minRad[i] = Lanes[i].Path.MinRadius();
            }
            foreach (Node node in world.Nodes)
            {
                var junction = new Junction(node, Lanes);
                // Start the lights out of step with each other.
                if (junction.PhaseCount > 0) junction.PhaseT = node.Id * 3 % 11;
                Junctions.Add(junction);
            }
        }

        // ---------------------------------------------------------------- cars

        /// <summary>Place a car. A null turn picks one at random.</summary>
        public Car AddCar(int lane, double s, double v = 0, DriverParams drv = null, Turn? turn = null, VehicleKind kind = VehicleKind.Car)
        {
            int id = nextId++;
            VehicleSpec spec = VehicleSpec.Of(kind);
            var car = new Car
            {
                Id = id,
                Vehicle = spec,
                Len = spec.Len,
                Wid = spec.Wid,
                Color = spec.Colors[id % spec.Colors.Length],
                Drv = (drv ?? DriverParams.Normal).Clone(),
                Lane = lane,
                S = s,
                V = v,
            };
            car.Turn = turn ?? PickTurn(car, Lanes[lane]);
            car.NextLcCheck = Time + Rng.Next() * 0.4;
            for (int i = 0; i < ObsN; i++) car.ObsGap[i] = 1e6f;
            Cars.Add(car);
            UpdatePose(car);
            return car;
        }

        /// <summary>Drop a car onto a free stretch of road. Returns null when the track is full.</summary>
        public Car Spawn(DriverParams baseDriver = null, double variation = 0, VehicleKind kind = VehicleKind.Car)
        {
            if (baseDriver == null) baseDriver = DriverParams.Normal;
            var roads = Lanes.FindAll(l => l.Kind == LaneKind.Road && l.Length > 50);
            if (roads.Count == 0) return null;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                Lane lane = Rng.Pick(roads);
                double s = Rng.Range(15, lane.Length - 30);
                double length = VehicleSpec.Of(kind).Len;
                bool free = true;
                foreach (var c in Cars)
                {
                    double room = 11.5 + (c.Len + length) / 2;
                    if (c.Lane == lane.Id && Math.Abs(c.S - s) < room) free = false;
                    if (c.GhostLane == lane.Id && Math.Abs(c.S + c.GhostOff - s) < room) free = false;
                }
                if (!free) continue;
                return AddCar(lane.Id, s, 6, Vary(baseDriver, variation), null, kind);
            }
            return null;
        }

        /// <summary>A copy of a driver with each main dial nudged at random.</summary>
        private DriverParams Vary(DriverParams baseDriver, double variation)
        {
            DriverParams drv = baseDriver.Clone();
            if (variation <= 0) return drv;
            for (int k = 0; k < DriverParams.Count; k++)
            {
                double lo = DriverParams.Ranges[k, 0];
                double hi = DriverParams.Ranges[k, 1];
                drv.Set(k, Geo.Clamp(drv.Get(k) + (Rng.Next() - 0.5) * variation * (hi - lo), lo, hi));
            }
            return drv;
        }

        /// <summary>
        /// Bring new vehicles in on the roads that enter from the edge of the map. Each lane has
        /// its own clock: arrivals come at random, and the rate itself rises and falls over a
        /// minute or so, so traffic comes in waves.
        /// </summary>
        private void Arrivals()
        {
            if (World.Entries.Count == 0 || ArrivalsPerMinute <= 0) return;
            foreach (Approach entry in World.Entries)
            {
                foreach (int id in entry.Outbound)
                {
                    if (Time < nextArrival[id]) continue;
                    Lane lane = Lanes[id];
                    VehicleKind kind = VehicleKind.Car;
                    double pick = Rng.Next();
                    if (pick < ArrivalTrucks) kind = VehicleKind.Truck;
                    else if (pick < ArrivalTrucks + ArrivalMotorcycles) kind = VehicleKind.Motorcycle;
                    double length = VehicleSpec.Of(kind).Len;
                    // Wait for the mouth of the lane to clear.
                    Car ahead = null;
                    foreach (Car c in Cars) if (c.Lane == id && (ahead == null || c.S < ahead.S)) ahead = c;
                    // A vehicle still halfway out of this lane is in the way too.
                    bool leaving = false;
                    foreach (Car c in Cars) if (c.GhostLane == id && c.S + c.GhostOff - c.Len / 2 < length + 8) leaving = true;
                    if (leaving || Cars.Count >= MaxCars || (ahead != null && ahead.S - ahead.Len / 2 < length + 8))
                    {
                        nextArrival[id] = Time + 0.5;
                        continue;
                    }
                    double speed = lane.SpeedLimit * 0.9;
                    // On a road that reaches a junction almost at once, come in slowly enough to stop for it.
                    if (lane.Length < 45) speed = Math.Min(speed, Math.Sqrt(2 * 2.0 * Math.Max(lane.Length - length - 3, 1)));
                    if (ahead != null)
                    {
                        // Come in no faster than can be stopped, gently, in the room there is.
                        double room = ahead.S - ahead.Len / 2 - length - 1;
                        speed = Math.Min(speed, ahead.V * 0.8 + Math.Sqrt(2 * 1.5 * Math.Max(room - 6, 0)));
                    }
                    // Where is this one going? With nowhere it can get to, it does not set out.
                    int dest = -1;
                    if (UseRoutes)
                    {
                        dest = PickDestination(entry.Node, id);
                        if (dest < 0)
                        {
                            nextArrival[id] = Time + 2;
                            continue;
                        }
                    }
                    Car arrival = AddCar(id, length / 2 + 0.5, speed, Vary(ArrivalDriver ?? DriverParams.Normal, ArrivalVariation), null, kind);
                    if (dest >= 0) SetDestination(arrival, dest);

                    // Faster roads carry more traffic per lane.
                    double perSecond = ArrivalsPerMinute / 60 * (lane.SpeedLimit / NetworkBuilder.CitySpeed);
                    perSecond *= 1 + 0.6 * Math.Sin(Time / 19 + id * 1.3);
                    perSecond = Math.Min(perSecond, 0.5); // about as much as one lane can carry
                    double wait = -Math.Log(1 - Rng.Next()) / Math.Max(perSecond, 1e-3);
                    nextArrival[id] = Time + Geo.Clamp(wait, 1.2, 120);
                }
            }
        }

        /// <summary>Drop one special driver onto the road. Returns null when the track is full.</summary>
        public Car SpawnSpecial(SpecialDriver kind, VehicleKind vehicle = VehicleKind.Car)
        {
            Car car = Spawn(kind.Make(), 0, vehicle);
            if (car == null) return null;
            car.Special = kind.Name;
            car.Color = kind.Color;
            car.Flash = kind.Flash;
            return car;
        }

        /// <summary>What this driver can do in this vehicle.</summary>
        private static Derived DeriveFor(Car car)
        {
            Derived d = DriverModel.Derive(car.Drv);
            VehicleSpec v = car.Vehicle;
            d.A *= v.Accel;
            d.B *= v.Brake;
            d.ALat *= v.Corner;
            d.LcTime *= v.LaneChange;
            return d;
        }

        /// <summary>Pushy enough to force a way through once committed to breaking a rule.</summary>
        private static bool IsBully(Car car)
        {
            return car.Drv.Aggression > 0.7;
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

        /// <summary>Change how one intersection is controlled.</summary>
        /// <summary>
        /// Run an intersection by signs: one kind of sign on its side street and none on the
        /// through street, or, with Sign.None, no signs anywhere.
        /// </summary>
        public void SetSigns(int node, Sign sign)
        {
            Junction junction = Junctions[node];
            if (junction.IsSideStreet(sign) || !World.Nodes[node].Controlled) return;
            junction.SignSideStreet(sign, Time);
            foreach (var c in Cars)
            {
                Lane lane = Lanes[c.Lane];
                if (lane.Node == node || lane.ApproachNode == node) c.Junc = new JunctionMemo();
            }
        }

        public void SetMode(int node, JunctionMode mode)
        {
            Junction junction = Junctions[node];
            if (junction.Mode == mode || !World.Nodes[node].Controlled) return;
            junction.SetMode(mode, Time);
            foreach (var c in Cars)
            {
                Lane lane = Lanes[c.Lane];
                if (lane.Node == node || lane.ApproachNode == node) c.Junc = new JunctionMemo();
            }
        }

        /// <summary>Change every real intersection at once.</summary>
        public void SetAllModes(JunctionMode mode)
        {
            foreach (Node node in World.Nodes) SetMode(node.Id, mode);
        }

        private static readonly double[] TurnWeights = { 0.22, 0.5, 0.22, 0.06 };

        /// <summary>Choose the turn a car will make at the intersection its lane leads to.</summary>
        /// <summary>Choose where a vehicle setting out from a lane at the edge of the map is going. -1 if it can get nowhere.</summary>
        private int PickDestination(int fromNode, int laneId)
        {
            var options = new List<Trip>();
            if (Demand.Count > 0)
            {
                foreach (Trip trip in Demand) if (trip.From == fromNode) options.Add(trip);
            }
            else
            {
                foreach (int exit in Routes.Exits) if (exit != fromNode) options.Add(new Trip { From = fromNode, To = exit, Weight = 1 });
            }
            double total = 0;
            for (int i = options.Count - 1; i >= 0; i--)
            {
                if (double.IsInfinity(Routes.CostTo(options[i].To)[laneId]) || options[i].Weight <= 0) options.RemoveAt(i);
                else total += options[i].Weight;
            }
            if (options.Count == 0) return -1;
            double r = Rng.Next() * total;
            foreach (Trip trip in options)
            {
                r -= trip.Weight;
                if (r < 0) return trip.To;
            }
            return options[options.Count - 1].To;
        }

        /// <summary>Send a vehicle to a way off the map by the quickest route from where it is.</summary>
        public void SetDestination(Car car, int exitNode)
        {
            car.Dest = exitNode;
            car.Born = Time;
            car.Ideal = Routes.CostTo(exitNode)[car.Lane];
            car.Turn = PickTurn(car, Lanes[car.Lane]);
        }

        private Turn PickTurn(Car car, Lane lane)
        {
            bool[] offered = lane.ApproachTurns;
            if (offered == null) return Turn.Straight;
            // A driver who is going somewhere takes the turn that gets there soonest.
            Turn toward;
            if (car.Dest >= 0 && Routes.TurnToward(car.Dest, lane, car.Vehicle.CanUTurn, out toward)) return toward;
            double total = 0;
            // A truck cannot get round a U-turn.
            bool noUTurn = !car.Vehicle.CanUTurn;
            for (int t = 0; t < Lane.Turns; t++) if (offered[t] && !(noUTurn && t == (int)Turn.UTurn)) total += TurnWeights[t];
            double r = Rng.Next() * total;
            Turn last = Turn.Straight;
            for (int t = 0; t < Lane.Turns; t++)
            {
                if (!offered[t] || (noUTurn && t == (int)Turn.UTurn)) continue;
                last = (Turn)t;
                r -= TurnWeights[t];
                if (r < 0) break;
            }
            return last;
        }

        // ---------------------------------------------------------------- step

        public void Step()
        {
            foreach (Junction junction in Junctions) junction.Update(Dt, Time);
            Arrivals();
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
            wrecks.Clear();
            foreach (var car in Cars) if (car.Crashed) wrecks.Add(car);
            foreach (var car in Cars)
            {
                Lane conn;
                double u;
                if (JunctionCoord(car, out conn, out u) && (u + car.Len / 2 > Committed || car.Junc.Released))
                {
                    active.Add(new ActiveCar { Car = car, Conn = conn, U = u });
                }
            }
        }

        private void UpdatePose(Car car)
        {
            PathPose p = Lanes[car.Lane].Path.At(car.S);
            car.Height = Lanes[car.Lane].HeightAt(car.S);
            car.Cx = p.X;
            car.Cy = p.Y;
            double side = car.Lat + car.Wobble;
            if (car.Len > 6)
            {
                // A long vehicle keeps both ends on its path: the body lies along the line
                // from its tail to its nose, so it cuts the inside of a turn instead of
                // swinging its ends into the next lane.
                double half = car.Len / 2;
                RoutePoint ahead;
                PathPose nose = Ahead(car, half, out ahead) ? ahead.Lane.Path.At(ahead.S) : Lanes[car.Lane].Path.At(car.S + half);
                PathPose tail;
                if (car.S >= half || car.PrevLane < 0) tail = Lanes[car.Lane].Path.At(car.S - half);
                else tail = Lanes[car.PrevLane].Path.At(Lanes[car.PrevLane].Length + car.S - half);
                double h = Math.Atan2(nose.Y - tail.Y, nose.X - tail.X);
                car.X = (nose.X + tail.X) / 2 + Math.Sin(h) * side;
                car.Y = (nose.Y + tail.Y) / 2 - Math.Cos(h) * side;
                car.H = h;
                return;
            }
            car.X = p.X + Math.Sin(p.H) * side;
            car.Y = p.Y - Math.Cos(p.H) * side;
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
            foundLeader = null;
            void Consider(Entry e, double g)
            {
                if (g < bestGap)
                {
                    foundLeader = e.Car;
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
            Derived d = DeriveFor(car);
            DriverParams drv = car.Drv;
            Lane lane = Lanes[car.Lane];
            double half = car.Len / 2;

            // Desired speed: the limit, scaled by the driver, slowed for curves ahead.
            double v0 = lane.SpeedLimit * drv.Speed * car.Vehicle.Speed;
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
            // Rubbernecking: slow right down to look at a crash nearby, even one on the other side of the road.
            bool gawking = false;
            foreach (Car w in wrecks)
            {
                if (w.Lane == car.Lane || Math.Abs(w.Height - car.Height) > 2.5) continue;
                double wx = w.X - car.X, wy = w.Y - car.Y;
                if (wx * wx + wy * wy > 35 * 35) continue;
                gawking = true;
                v0 = Math.Min(v0, Math.Max(4, lane.SpeedLimit * 0.45));
                break;
            }

            // A driver who cannot hold a speed drifts above and below what they meant to do.
            if (drv.Swerve > 0) v0 *= 1 + 0.25 * drv.Swerve * Math.Sin(car.WobblePhase * 0.37);
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
            car.LeadV = leadV;
            car.BlockedAhead = foundLeader != null && foundLeader.Crashed && leadGap < 30;
            // Nobody wants to sit right behind a car that is all over the road.
            double headway = drv.Headway;
            if (foundLeader != null && foundLeader.Drv.Swerve > 0.3 && drv.Swerve <= 0.3 && d.Nerves > 0.15) headway *= 1.6;
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
                Use(DriverModel.Idm(car.V, v0, gapNow, closing, headway, d), Binding.Leader);
            }
            car.ALead = leadGap < Lookahead ? Math.Min(free, DriverModel.Idm(car.V, v0, leadGap, car.V - leadV, drv.Headway, d)) : free;

            // Halfway through a lane change the car is still partly in the lane it is leaving,
            // so it must not run into whoever is ahead of it there.
            if (car.GhostLane >= 0)
            {
                double ghostS = car.S + car.GhostOff;
                foreach (var e in laneCars[car.GhostLane])
                {
                    if (e.Car == car || e.S <= ghostS) continue;
                    double g = e.S - e.Car.Len / 2 - ghostS - half;
                    Use(DriverModel.Idm(car.V, v0, g, car.V - e.Car.V, drv.Headway, d), Binding.Leader);
                    break;
                }
            }

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
            if (lane.Kind == LaneKind.Road && lane.Connectors == null && !lane.IsExit)
            {
                // The lane ends ahead: be ready to stop at the end if nobody lets us in.
                double a = StopFor(lane.Length - car.S - half - 0.5, Binding.End);
                car.ALead = Math.Min(car.ALead, a);
            }
            double jsDist;
            Binding jsKind;
            scareSeen = false;
            if (JunctionStop(car, lane, d, v0, out jsDist, out jsKind)) StopFor(jsDist, jsKind);

            // Someone pushing through a red light inches into the intersection rather than charging it.
            if (car.Junc.Run && (lane.Kind == LaneKind.Connector || (lane.Connectors != null && lane.Length - car.S - half < 12)))
            {
                Use(DriverModel.FreeAccel(car.V, Math.Min(v0, 3.5), d), Binding.Free);
            }

            // A rule-breaker is forcing through ahead: ordinary drivers slam on the brakes and sit there a moment.
            if (scareSeen && d.Nerves > 0.15 && Time > car.NextScare && lane.Kind != LaneKind.Connector)
            {
                car.ScareUntil = Time + 1.5 + 1.5 * d.Nerves;
                car.NextScare = car.ScareUntil + 4;
            }
            // Never freeze in the middle of the intersection: once in, keep going.
            if (Time < car.ScareUntil && lane.Kind != LaneKind.Connector) Use(car.V > 0.3 ? -d.B * (1.3 + 1.2 * d.Nerves) : -1, Binding.Scare);

            // Hang back from a car in the next lane that keeps drifting out of its own.
            if (lane.Kind == LaneKind.Road && drv.Swerve <= 0.3 && d.Nerves > 0.15)
            {
                for (int pass = 0; pass < 2; pass++)
                {
                    foreach (int id in pass == 0 ? lane.Lefts : lane.Rights)
                    {
                        foreach (var e in laneCars[id])
                        {
                            Car m = e.Car;
                            if (e.Ghost || m.Crashed || m.Drv.Swerve <= 0.3 || m.V < 1) continue;
                            Projection pr = lane.Path.Project(m.Cx, m.Cy);
                            if (pr.D > World.LaneWidth * 1.4) continue;
                            double g = pr.S - m.Len / 2 - (car.S + half);
                            if (g < -6 || g > 30) continue;
                            Use(Math.Max(DriverModel.Idm(car.V, v0, Math.Max(g, 0.5), car.V - m.V, drv.Headway, d), -d.B), Binding.Hazard);
                        }
                    }
                }
            }

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

            // Looking at the phone: the driver just keeps doing what they were doing.
            bool lapsing = false;
            if (drv.Distraction > 0.02)
            {
                if (Time >= car.NextLapse && Time >= car.LapseUntil)
                {
                    car.LapseUntil = Time + 1 + 3.5 * drv.Distraction * Rng.Next();
                    car.NextLapse = car.LapseUntil + (2 + 10 * (1 - drv.Distraction)) * (0.5 + Rng.Next());
                }
                lapsing = Time < car.LapseUntil;
                if (lapsing) car.ANext = 0;
            }

            if (lapsing) car.State = CarState.Distracted;
            else if (binding == Binding.Scare) car.State = CarState.Scared;
            else if (binding == Binding.Hazard) car.State = CarState.Wary;
            else if (binding == Binding.Startle) car.State = CarState.Startled;
            else if (car.ChangingLanes) car.State = CarState.LaneChange;
            else if (binding == Binding.Leader) car.State = acc < -1.5 ? CarState.Brake : CarState.Follow;
            else if (binding == Binding.Line) car.State = car.V < 0.5 ? CarState.Wait : CarState.Stopping;
            else if (binding == Binding.Yield) car.State = CarState.Yield;
            else if (binding == Binding.Coop) car.State = CarState.LettingIn;
            else if (binding == Binding.End) car.State = CarState.Merge;
            else car.State = gawking ? CarState.Gawking : CarState.Cruise;

            // Turn signals.
            if (car.ChangingLanes) car.Blinker = car.LcFrom > 0 ? -1 : 1;
            else if (car.WantSide != 0 && car.Urgency > 0.1) car.Blinker = car.WantSide;
            else
            {
                Turn turn = Turn.Straight;
                if (lane.Kind == LaneKind.Connector) turn = lane.Turn;
                else if (lane.Connectors != null && lane.Length - car.S < 55) turn = PlannedConn(car, lane).Turn;
                car.Blinker = turn == Turn.Left || turn == Turn.UTurn ? -1 : turn == Turn.Right ? 1 : 0;
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
                if (dLine > -Committed && !MayEnter(car, lane, conn, dLine, d))
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
            bool outside = myFront <= Committed;
            double vc = Math.Min(v0, ConnSpeed(conn, d));
            double margin = 0.8 + 1.2 * d.Nerves;
            double myRank = Rank(car, conn, u);
            // Facing a stop or yield sign, the whole way across has to be clear before setting off: the car waits at
            // the line, not out among the traffic going by, and looks far enough up the road to judge fast traffic.
            bool bySigns = Junctions[conn.Node].Mode == JunctionMode.Signs;
            bool atSign = outside && Junctions[conn.Node].SignAt(conn.FromArm) != Sign.None;
            foreach (var cz in conn.Conflicts)
            {
                if (myFront > cz.SIn) continue;
                double dIn = cz.SIn - myFront;
                if (dIn > 60) continue;
                double tIn = DriverModel.TravelTime(dIn, car.V, d.A, vc);
                double tOut = DriverModel.TravelTime(cz.SOut - (u - half), car.V, d.A, vc);
                Lane foeConn = Lanes[cz.Foe];
                bool wait = false;
                bool blocked = false;
                double look = bySigns ? Math.Max(80, Lanes[foeConn.From].SpeedLimit * 7) : 80;
                ForFoes(foeConn, look, (foe, uf) =>
                {
                    if (wait || foe == car) return;
                    double fFront = uf + foe.Len / 2;
                    double fRear = uf - foe.Len / 2;
                    if (fRear > cz.FoeOut) return;
                    bool inZone = fFront > cz.FoeIn;
                    if (!foe.Crashed && foe.Junc.Run && IsBully(foe) && fFront > -4) scareSeen = true;
                    if (foe.Crashed)
                    {
                        if (inZone) wait = blocked = true;
                        return;
                    }
                    if (!outside)
                    {
                        // Already in the intersection. Don't wait for someone standing behind their
                        // stop line, and don't sit here for ever: after a while only give way to a
                        // car physically in the way, and in the end just go.
                        // (Only one that is being held there. One that is free to go is about to move.)
                        bool held = foe.StopKind == Binding.Line || foe.StopKind == Binding.Yield;
                        if (!inZone && fFront <= Committed && foe.V < 1 && held) return;
                        // Not at a junction without signs, though: there, waiting just short of the
                        // traffic going by is how giving way works, however long it takes.
                        // The same goes for the through street of an intersection run by signs: a car turning left off it
                        // waits for its gap in the middle, as long as that takes.
                        bool giveWay = Junctions[conn.Node].Mode == JunctionMode.Free || (bySigns && !Junctions[conn.Node].NoSigns);
                        // Giving up on waiting is for getting out of a standstill, so it only ever applies to a car that
                        // is itself stopped or crawling. However long the wait, nobody pulls out in front of traffic at speed.
                        bool crawling = foe.V < 4;
                        if (!giveWay && !inZone && car.BoxWait > 10 && crawling) return;
                        if (!giveWay && car.BoxWait > 25 && crawling) return;
                    }
                    // A car sitting in the intersection: stay out until it has gone, rather than nosing in after it.
                    bool sitting = fFront > Committed && foe.V < 1.5;
                    if (!inZone && !IsActive(foe, foeConn, uf)) return;
                    Derived fd = DeriveFor(foe);
                    double fvc = Math.Min(foeConn.SpeedLimit * foe.Drv.Speed, ConnSpeed(foeConn, fd));
                    // When will the other car be clear? Allow for it not speeding up at all: it may
                    // be held back by something this driver cannot see.
                    double fOut = Math.Max(DriverModel.TravelTime(cz.FoeOut - fRear, foe.V, fd.A, fvc), (cz.FoeOut - fRear) / Math.Max(foe.V, 2));
                    if (inZone)
                    {
                        if (tIn - margin * 0.5 < fOut)
                        {
                            wait = true;
                            blocked |= sitting;
                        }
                        return;
                    }
                    double fIn = DriverModel.TravelTime(cz.FoeIn - fFront, foe.V, fd.A, fvc);
                    if (tIn - margin < fOut && fIn < tOut + margin)
                    {
                        double fr = Rank(foe, foeConn, uf);
                        if (fr > myRank || (fr == myRank && (fIn < tIn || (fIn == tIn && foe.Id < car.Id))))
                        {
                            wait = true;
                            blocked |= sitting;
                        }
                    }
                });
                if (wait)
                {
                    double dist = (blocked || atSign) && outside ? -myFront - 0.6 : dIn - 1.0;
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
            Junction jn = Junctions[conn.Node];
            if (jn.Mode == JunctionMode.Free) return true;

            if (jn.Mode == JunctionMode.Signs && !jn.NoSigns)
            {
                Sign sign = jn.SignAt(lane.Arm);
                if (sign == Sign.Stop && !j.Released)
                {
                    // A stop sign: come to a stop at the line (ORC 4511.43(A)). After that the car goes when
                    // the way is clear, which the giving way in JunctionStop sees to. The less a driver
                    // cares for the rules, the faster they roll through it.
                    double rolling = 0.4 + (1 - car.Drv.Compliance) * 4;
                    if (dLine > 3.2 || car.V > rolling) return false;
                    if (j.Arrival < 0) j.Arrival = Time;
                    if (Time - j.Arrival < (0.4 + 0.6 * d.Nerves) * car.Drv.Compliance) return false;
                    j.Released = true;
                }
                // A yield sign: slow to a speed that is easy to stop from, and stop only if someone is coming (4511.43(B)).
                if (sign == Sign.Yield && car.V > YieldSpeed && dLine > 1) return false;
                return ExitHasSpace(car, conn);
            }

            if (jn.Mode == JunctionMode.AllWay || jn.NoSigns)
            {
                // Stop signs all round: everyone stops, and whoever stopped first goes first. With no signs at all it is the
                // same, except that nobody has to stop: each comes up slowly, and goes straight through if the way is free.
                bool open = jn.NoSigns;
                if (j.Released) return true;
                double rolling = 0.4 + (1 - car.Drv.Compliance) * 4;
                if (open)
                {
                    List<Entry> queue = laneCars[lane.Id];
                    if (dLine > 14 || car.V > YieldSpeed || queue.Count == 0 || queue[queue.Count - 1].Car != car) return false;
                }
                else if (dLine > 3.2 || car.V > rolling) return false;
                if (j.Arrival < 0) j.Arrival = Time;
                if (!open && Time - j.Arrival < (0.4 + 0.6 * d.Nerves) * car.Drv.Compliance) return false;
                foreach (var o in active)
                {
                    if (o.Car == car || o.Conn.Id == conn.Id || o.Conn.Node != conn.Node) continue;
                    Conflict cz = conn.ConflictWith(o.Conn.Id);
                    if (cz != null && o.U - o.Car.Len / 2 < cz.FoeOut) return false;
                }
                bool jumpsQueue = car.Drv.Compliance < 0.3;
                foreach (var o in Cars)
                {
                    JunctionMemo oj = o.Junc;
                    if (jumpsQueue) break;
                    if (o == car || o.Crashed || oj.Released) continue;
                    Lane ol = Lanes[o.Lane];
                    if (ol.Connectors == null || ol.Node != conn.Node) continue;
                    double theirArrival = oj.Arrival;
                    if (theirArrival < 0)
                    {
                        // Not there yet. With no signs, a car coming up to its line as this one reaches its own is
                        // as good as arriving together with it.
                        List<Entry> theirQueue = laneCars[ol.Id];
                        bool coming = open && ol.Length - o.S - o.Len / 2 < 22 && o.V > 0.5 && theirQueue.Count > 0 && theirQueue[theirQueue.Count - 1].Car == o;
                        if (!coming) continue;
                        theirArrival = Time;
                    }
                    bool theyFirst = theirArrival < j.Arrival || (theirArrival == j.Arrival && o.Id < car.Id);
                    if (open && ol.Arm != lane.Arm && Math.Abs(theirArrival - j.Arrival) < 1.5 && Time - j.Arrival < 4)
                    {
                        // Two who come to an intersection with no signs from different streets at about the same time:
                        // the one on the left gives way to the one on the right (ORC 4511.41). The rule lapses after a
                        // few seconds, so that four who arrive together do not each wait for the next for ever.
                        List<Approach> arms = World.Nodes[conn.Node].Arms;
                        double onRight = Math.Sin(arms[ol.Arm].Angle - arms[lane.Arm].Angle);
                        if (Math.Abs(onRight) > 0.3) theyFirst = onRight > 0;
                    }
                    if (theyFirst)
                    {
                        // Someone who got here first goes first, unless they have nowhere to go.
                        Lane theirs = PlannedConn(o, ol);
                        if (conn.ConflictWith(theirs.Id) != null && ExitHasSpace(o, theirs)) return false;
                    }
                }
                if (!ExitHasSpace(car, conn)) return false;
                j.Released = true;
                j.Order = jn.Seq++;
                active.Add(new ActiveCar { Car = car, Conn = conn, U = car.S - lane.Length });
                return true;
            }

            if (j.Run) return true;
            SignalLight light = jn.State(lane.Arm, conn.Turn, Time - car.Drv.Reaction);
            bool ok;
            if (light == SignalLight.Green)
            {
                j.Yellow = YellowChoice.Undecided;
                j.RightOnRed = false;
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
                if (!ok && RightOnRed && (conn.Turn == Turn.Right || (conn.Turn == Turn.Left && OneWayToOneWay(conn))))
                {
                    // Turn on red: right, or left from a one-way street into a one-way street (ORC 4511.13(C)(1)(b)).
                    // Come to a stop at the line first, then go when there is a gap.
                    if (!j.RightOnRed && dLine < 3.2 && car.V < 0.4)
                    {
                        if (j.Arrival < 0) j.Arrival = Time;
                        if (Time - j.Arrival >= 0.5 + 0.7 * d.Nerves) j.RightOnRed = true;
                    }
                    ok = j.RightOnRed;
                }
            }
            return ok && ExitHasSpace(car, conn);
        }

        /// <summary>The speed a driver slows to for a yield sign, meters per second (about 15 mph).</summary>
        private const double YieldSpeed = 7;

        /// <summary>Whether a path through an intersection leads from a one-way road into a one-way road.</summary>
        private bool OneWayToOneWay(Lane conn)
        {
            Node node = World.Nodes[conn.Node];
            if (node.Arms[conn.FromArm].Outbound.Count > 0) return false;
            foreach (Approach arm in node.Arms) if (arm.Outbound.Contains(conn.Next)) return arm.Inbound.Count == 0;
            return false;
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
            if (uf + foe.Len / 2 > Committed) return true;
            Junction jn = Junctions[foeConn.Node];
            if (jn.Mode == JunctionMode.Free) return true;
            if (jn.Mode == JunctionMode.AllWay || jn.NoSigns) return foe.Junc.Released;
            // Run by signs: everyone is on their way except a car that has yet to make its stop.
            if (jn.Mode == JunctionMode.Signs) return jn.SignAt(foeConn.FromArm) != Sign.Stop || foe.Junc.Released;
            if (foe.Junc.Run) return true;
            SignalLight light = jn.State(foeConn.FromArm, foeConn.Turn);
            if (light == SignalLight.Green) return true;
            if (light == SignalLight.Yellow) return foe.Junc.Yellow == YellowChoice.Go || (foe.Junc.Yellow == YellowChoice.Undecided && -uf - foe.Len / 2 < 40);
            return foe.Junc.Yellow == YellowChoice.Go && foe.V > 2;
        }

        /// <summary>Right of way: the higher rank goes first.</summary>
        private double Rank(Car car, Lane conn, double u)
        {
            Junction jn = Junctions[conn.Node];
            // No light or signs (a way on to a roundabout): traffic carrying straight on goes first, and whoever is turning in gives way.
            // Where two ways straight on end in one lane, the straighter of them goes first.
            if (jn.Mode == JunctionMode.Free) return conn.Turn == Turn.Straight && !conn.GiveWay ? 3 : 2;
            if (jn.Mode == JunctionMode.AllWay || jn.NoSigns) return 1e6 - (car.Junc.Order >= 0 ? car.Junc.Order : 1e5);
            if (jn.Mode == JunctionMode.Signs)
            {
                // Traffic with no sign to face has the right of way over traffic facing a stop or yield sign
                // (ORC 4511.43), and either way a left turn gives way to traffic coming the other way (4511.42).
                double among = conn.Turn == Turn.UTurn ? 0 : conn.Turn == Turn.Left ? 0.5 : 1;
                return (jn.SignAt(conn.FromArm) == Sign.None ? 3 : 1) + among;
            }
            bool crossed = u + car.Len / 2 > Committed;
            if (car.Junc.RightOnRed) return 0.5; // turning right on red: gives way to everyone with a green
            if (car.Junc.Run) return crossed && IsBully(car) ? 5 : 0; // red-light runner: waits for a gap, unless pushy enough to force it
            if (crossed && jn.State(conn.FromArm, conn.Turn) != SignalLight.Green) return 4; // clearing the box
            if (jn.IsProtected(conn.FromArm, conn.Turn)) return 3; // green arrow
            if (conn.Turn == Turn.UTurn) return 1; // U-turns give way to everyone
            return conn.Turn == Turn.Left ? 2 : 3; // left turns give way to oncoming traffic
        }

        /// <summary>Every car using, approaching or just leaving a path through the intersection.</summary>
        private void ForFoes(Lane conn, double look, Action<Car, double> fn)
        {
            foreach (var e in laneCars[conn.Id]) fn(e.Car, e.S);
            Lane from = Lanes[conn.From];
            foreach (var e in laneCars[from.Id])
            {
                if (e.Ghost || from.Length - e.S > look) continue;
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
            // Nobody ahead in this lane: look on through the junction at its end, and into the road
            // beyond it, so as not to pull out into a lane whose traffic is stopped just past it.
            double toEnd = t.Length - s - self.Len / 2;
            int nextId = -1;
            if (t.Kind == LaneKind.Connector) nextId = t.Next;
            else if (t.Connectors != null) nextId = t.ConnectorFor(self.Turn);
            if (nextId < 0) return null;
            Entry? first = FirstIn(nextId, self);
            if (first.HasValue) return new Neighbor { Car = first.Value.Car, Gap = toEnd + first.Value.S - first.Value.Car.Len / 2 };
            Lane next = Lanes[nextId];
            if (next.Kind != LaneKind.Connector || toEnd + next.Length > 160) return null;
            first = FirstIn(next.Next, self);
            if (!first.HasValue) return null;
            double gap = toEnd + next.Length + first.Value.S - first.Value.Car.Len / 2;
            return gap < 160 ? new Neighbor { Car = first.Value.Car, Gap = gap } : null;
        }

        /// <summary>The car furthest back in a lane, other than one given.</summary>
        private Entry? FirstIn(int laneId, Car self)
        {
            foreach (Entry e in laneCars[laneId]) if (e.Car != self) return e;
            return null;
        }

        private Neighbor FollowerIn(Lane t, double s, Car self)
        {
            var list = laneCars[t.Id];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Entry e = list[i];
                if (e.Car == self || e.S >= s) continue;
                double gap = s - self.Len / 2 - (e.S + e.Car.Len / 2);
                // A car that is already moving out of this lane will not be the one behind for long:
                // unless it is right on our tail, the one to worry about is whoever is behind it.
                if (e.Ghost && gap > 2) continue;
                return new Neighbor { Car = e.Car, Gap = gap };
            }
            // Nobody behind in this lane: look back up the road, through the junction it comes out of,
            // far enough to see a fast car that is still a few seconds away.
            Neighbor best = null;
            foreach (int p in t.Preds) LookBack(p, s - self.Len / 2, self, 2, ref best);
            return best;
        }

        /// <summary>The last car in a lane that leads to where we are, or failing that in the lanes that lead to it.</summary>
        private void LookBack(int laneId, double ahead, Car self, int further, ref Neighbor best)
        {
            Lane lane = Lanes[laneId];
            var list = laneCars[laneId];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Entry e = list[i];
                if (e.Car == self) continue;
                double gap = ahead + (lane.Length - e.S) - e.Car.Len / 2;
                if (best == null || gap < best.Gap) best = new Neighbor { Car = e.Car, Gap = gap };
                return;
            }
            if (further <= 0 || ahead + lane.Length > 160) return;
            foreach (int p in lane.Preds) LookBack(p, ahead + lane.Length, self, further - 1, ref best);
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
                car.NextLcCheck = Time + 0.4 * (1 - 0.5 * car.Drv.Weave);
                Derived d = DeriveFor(car);
                double half = car.Len / 2;
                double remaining = lane.Length - car.S - half;

                // Does the driver have to leave this lane?
                int need = 0;
                double urgency = 0;
                double insistOver = Math.Min(MandatoryDist, lane.Length * 0.8);
                if (lane.IsExit)
                {
                    // Running off the edge of the map: any lane will do.
                }
                else if (lane.Connectors == null)
                {
                    need = lane.MergeSide;
                    urgency = Geo.Clamp(1 - (remaining - 5) / insistOver, 0, 1);
                }
                else if (!lane.Allows(car.Turn))
                {
                    double toSolid = remaining - NetworkBuilder.SolidLine;
                    need = lane.TurnSide[(int)car.Turn];
                    if (toSolid < 5 || need == 0)
                    {
                        car.Turn = PlannedConn(car, lane).Turn; // too late, or no such lane: take what this one allows
                        need = 0;
                    }
                    else urgency = Geo.Clamp(1 - toSolid / insistOver, 0, 1);
                }
                // Stuck behind a wreck: any way round will do, even across a solid line or into a lane for a different turn.
                bool blocked = car.BlockedAhead;
                if (blocked)
                {
                    need = 0;
                    urgency = Math.Max(urgency, 0.8);
                }
                car.WantSide = urgency > 0.05 ? need : 0;
                car.Urgency = need != 0 ? urgency : 0;

                if (!blocked && lane.Connectors != null && World.Nodes[lane.Node].Controlled && remaining < NetworkBuilder.SolidLine) continue;
                if (car.S < 12) continue;
                if (lane.IsEntry && car.S < 60) continue; // settle in before changing lanes: traffic is still arriving behind
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
                        // Moving over for a turn: the next lane must allow it or be on the way to one that does.
                        if (lane.Connectors != null && (t.Connectors == null || !(t.Allows(car.Turn) || t.TurnSide[(int)car.Turn] == need))) continue;
                    }
                    else if (!blocked)
                    {
                        if (t.Connectors == null && !t.IsExit && t.Length - tgt.S < 220) continue; // don't hop into a lane that is about to end
                        if (t.Connectors != null && !t.Allows(car.Turn) && remaining < 170) continue;
                    }
                    Neighbor lead = LeaderIn(t, tgt.S, car);
                    Neighbor fol = FollowerIn(t, tgt.S, car);
                    // Room needed at each end: less when crawling, so stopped traffic can still zip together.
                    if (lead != null && lead.Gap < Math.Min(1.0, 0.4 + car.V * 0.3)) continue;
                    if (fol != null && fol.Gap < Math.Min(1.5, 0.4 + fol.Car.V * 0.5)) continue;

                    double v0 = car.V0;
                    double aNew = DriverModel.FreeAccel(car.V, v0, d);
                    if (lead != null) aNew = Math.Min(aNew, DriverModel.Idm(car.V, v0, lead.Gap, car.V - lead.Car.V, car.Drv.Headway, d));
                    double folCost = 0;
                    double aFol = 0;
                    if (fol != null && !fol.Car.Crashed)
                    {
                        Car f = fol.Car;
                        aFol = DriverModel.Idm(f.V, Math.Max(f.V0, 1), fol.Gap, f.V - car.V, f.Drv.Headway, DeriveFor(f));
                        // Slowing the car behind counts against the move, but not if it is already stopped.
                        folCost = f.V < 0.5 ? 0 : Math.Min(0, aFol - f.ANext);
                    }
                    // A car that is already stopped cannot be made to brake any harder.
                    double bSafe = d.BSafe + urgency * 2.5;
                    bool folStopped = fol != null && fol.Car.V < 0.5;
                    if ((!folStopped && aFol < -bSafe) || (car.V >= 0.5 && aNew < -bSafe)) continue;
                    double bias = (side == need ? 0.3 + urgency * 3 : 0) + 0.25 * car.Drv.Weave + (blocked ? 2.5 : 0);
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
                if (have)
                {
                    // Going round a wreck may mean giving up the turn that was planned.
                    Lane into = bestTarget.Lane;
                    if (blocked && into.Connectors != null && !into.Allows(car.Turn)) car.Turn = Lanes[into.ConnectorFor(car.Turn)].Turn;
                    ChangeLane(car, bestSide, bestTarget, bestFol);
                }
            }
        }

        private void ChangeLane(Car car, int side, Target target, Neighbor fol)
        {
            Derived d = DeriveFor(car);
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
            car.LcCooldownUntil = Time + d.LcCooldown;
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
                    if (AutoClear && Time >= car.CrashClearAt)
                    {
                        if (car.CrashMajor)
                        {
                            // Towed away.
                            car.Gone = true;
                            removed = true;
                        }
                        else
                        {
                            // A fender bender: the drivers have swapped details and move on.
                            car.Crashed = false;
                            car.CrashImmuneUntil = Time + 8;
                            car.State = CarState.Cruise;
                            UpdatePose(car);
                        }
                    }
                    continue;
                }
                car.A = car.ANext;
                car.V = Math.Max(0, car.V + car.A * Dt);
                if (!CrashesEnabled && car.Gap < 0.3 && car.V > car.LeadV)
                {
                    // Ran into the car ahead. With crashes off, just hold it there.
                    if (car.V - car.LeadV > 1.5 && Time >= car.NextBump)
                    {
                        Bumps++;
                        car.NextBump = Time + 3;
                    }
                    car.V = car.Gap < 0.05 ? Math.Min(car.V, car.LeadV * 0.8) : car.LeadV;
                }
                car.S += car.V * Dt;
                if (Lanes[car.Lane].Kind == LaneKind.Connector && car.V < 0.3) car.BoxWait += Dt;
                else if (car.V > 1 || Lanes[car.Lane].Kind != LaneKind.Connector) car.BoxWait = 0;

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
                        if (lane.IsExit)
                        {
                            // Drove off the edge of the map.
                            car.Gone = true;
                            Left++;
                            if (car.Dest >= 0 && lane.ApproachNode == car.Dest)
                            {
                                Arrived++;
                                TripSeconds += Time - car.Born;
                                TripIdeal += car.Ideal;
                            }
                            else if (car.Dest >= 0) Strays++;
                            removed = true;
                            break;
                        }
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
                        car.Turn = PickTurn(car, Lanes[next]);
                        if (World.Nodes[lane.Node].Controlled) Passes++; // bends don't count
                    }
                    lane = Lanes[next];
                }
                if (car.Drv.Swerve > 0)
                {
                    // Slow, uneven drift from side to side, fading out as the car stops.
                    car.WobblePhase += Dt * (0.9 + 0.5 * Math.Sin(car.Id * 1.7 + Time * 0.13));
                    double reach = 1.3 * car.Drv.Swerve * Math.Min(1, car.V / 4) * (lane.Kind == LaneKind.Connector ? 0.4 : 1);
                    double before = car.Wobble;
                    car.Wobble = reach * (0.65 * Math.Sin(car.WobblePhase) + 0.35 * Math.Sin(2.3 * car.WobblePhase + 1));
                    yaw += Math.Atan2(-(car.Wobble - before) / Dt, Math.Max(car.V, 3));
                }
                else car.Wobble = 0;
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
            if (removed) Cars.RemoveAll(c => c.Gone);
        }

        private void DetectCrashes()
        {
            if (!CrashesEnabled) return;
            for (int i = 0; i < Cars.Count; i++)
            {
                Car a = Cars[i];
                for (int k = i + 1; k < Cars.Count; k++)
                {
                    Car b = Cars[k];
                    if (a.Crashed && b.Crashed) continue;
                    if (Time < a.CrashImmuneUntil || Time < b.CrashImmuneUntil) continue;
                    double dx = b.X - a.X;
                    double dy = b.Y - a.Y;
                    double reach = (a.Len + b.Len) / 2 + 1.5;
                    bool partners = a.CrashPartner == b.Id || b.CrashPartner == a.Id;
                    bool touching = dx * dx + dy * dy <= reach * reach && Math.Abs(a.Height - b.Height) <= 2.5 && BoxesOverlap(a, b);
                    if (partners)
                    {
                        // Still tangled from the last crash: that is not a new one. Once apart, they can crash again.
                        if (!touching)
                        {
                            if (a.CrashPartner == b.Id) a.CrashPartner = -1;
                            if (b.CrashPartner == a.Id) b.CrashPartner = -1;
                        }
                        continue;
                    }
                    if (touching) Collide(a, b);
                }
            }
        }

        /// <summary>Two vehicles have hit: work out what kind of crash it was and how bad, and stop them where they are.</summary>
        private void Collide(Car a, Car b)
        {
            double avx = a.V * Math.Cos(a.H), avy = a.V * Math.Sin(a.H);
            double bvx = b.V * Math.Cos(b.H), bvy = b.V * Math.Sin(b.H);
            double closing = Geo.Hypot(avx - bvx, avy - bvy);
            bool major = closing >= MajorCrashSpeed;

            // The angle between the two says what kind of crash it was.
            double between = Math.Abs(Math.Atan2(Math.Sin(a.H - b.H), Math.Cos(a.H - b.H)));
            CrashKind kind;
            if (between > 2.6) kind = CrashKind.HeadOn;
            else if (between > 0.5) kind = CrashKind.Broadside;
            else
            {
                // Going the same way: one behind the other, or side by side?
                double along = Math.Abs((b.X - a.X) * Math.Cos(a.H) + (b.Y - a.Y) * Math.Sin(a.H));
                kind = along > (a.Len + b.Len) / 2 - 1.5 ? CrashKind.RearEnd : CrashKind.Sideswipe;
            }

            // Hitting a wreck that is already there joins that crash: everything is cleared together.
            double wait = major ? TowAfter * (0.8 + 0.5 * Rng.Next()) : FenderBenderDelay * (0.8 + 0.5 * Rng.Next());
            double clearAt = Time + wait;
            if (a.Crashed) clearAt = Math.Max(clearAt, a.CrashClearAt);
            if (b.Crashed) clearAt = Math.Max(clearAt, b.CrashClearAt);
            bool wrecked = major || (a.Crashed && a.CrashMajor) || (b.Crashed && b.CrashMajor);
            // A hard hit knocks the vehicles askew.
            double knock = major ? Math.Min(0.6, closing / 25) : 0.05;
            MarkCrashed(a, wrecked, clearAt, (Rng.Next() - 0.5) * 2 * knock);
            MarkCrashed(b, wrecked, clearAt, (Rng.Next() - 0.5) * 2 * knock);

            a.CrashPartner = b.Id;
            b.CrashPartner = a.Id;
            Crashes++;
            var accident = new Accident { Time = Time, Kind = kind, Major = major, ClosingSpeed = closing, X = (a.X + b.X) / 2, Y = (a.Y + b.Y) / 2, A = a, B = b };
            Accidents.Add(accident);
            if (Accidents.Count > 50) Accidents.RemoveAt(0);
            if (OnAccident != null) OnAccident(accident);
        }

        private void MarkCrashed(Car c, bool major, double clearAt, double spin)
        {
            c.CrashMajor = c.CrashMajor && c.Crashed || major;
            c.CrashClearAt = clearAt;
            if (c.Crashed) return;
            c.Crashed = true;
            c.CrashedAt = Time;
            c.V = 0;
            c.A = 0;
            c.H += spin;
            c.State = CarState.Crash;
            c.Blinker = 0;
            c.GhostLane = -1;
            c.LcT = c.LcDur;
            c.Wobble = 0;
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
