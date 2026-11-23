using System.Collections.Generic;

namespace Traffic.Sim
{
    public enum JunctionMode
    {
        /// <summary>No control: a bend where two roads meet.</summary>
        Free,
        /// <summary>Plain traffic light: on green everyone goes, and left turns give way to oncoming traffic.</summary>
        Signal,
        /// <summary>Traffic light with a left-turn arrow: left turns go only on the arrow.</summary>
        ProtectedLeft,
        /// <summary>Stop sign on every arm.</summary>
        AllWay,
    }

    public enum SignalLight
    {
        Green,
        Yellow,
        Red,
        Stop,
    }

    /// <summary>Controls one intersection: runs its signal cycle and answers what light each movement sees.</summary>
    public sealed class Junction
    {
        private const int All = 0, LeftOnly = 1, ThroughOnly = 2;

        private struct Stage
        {
            public double Duration;
            public int Street;
            public int Moves;
            public SignalLight Light;
        }

        public readonly int Id;
        public JunctionMode Mode { get; private set; }
        public double Green = 14;
        public double Yellow = 3.5;
        public double AllRed = 2;
        public double ArrowGreen = 7;
        public int Phase;
        /// <summary>Seconds spent in the current phase.</summary>
        public double PhaseT;
        /// <summary>When true the phase never advances (used by tests).</summary>
        public bool Hold;
        /// <summary>Order in which cars were let through in all-way mode.</summary>
        public int Seq;

        private readonly int[] armStreet;
        private readonly bool[] streetHasLeft = new bool[2];
        private readonly bool[] streetUsed = new bool[2];
        private readonly List<Stage> phases = new List<Stage>();
        private readonly List<KeyValuePair<double, int>> log = new List<KeyValuePair<double, int>>();

        public Junction(Node node, List<Lane> lanes)
        {
            Id = node.Id;
            armStreet = new int[node.Arms.Count];
            foreach (Approach arm in node.Arms)
            {
                armStreet[arm.Arm] = arm.Street;
                streetUsed[arm.Street] = true;
                foreach (int id in arm.Inbound)
                {
                    if (lanes[id].Allows(Turn.Left) || lanes[id].Allows(Turn.UTurn)) streetHasLeft[arm.Street] = true;
                }
            }
            SetMode(node.DefaultMode, 0);
        }

        public void SetMode(JunctionMode mode, double time)
        {
            Mode = mode;
            phases.Clear();
            if (mode == JunctionMode.Signal || mode == JunctionMode.ProtectedLeft)
            {
                for (int street = 0; street < 2; street++)
                {
                    if (!streetUsed[street]) continue;
                    int moves = All;
                    if (mode == JunctionMode.ProtectedLeft && streetHasLeft[street])
                    {
                        Add(ArrowGreen, street, LeftOnly, SignalLight.Green);
                        Add(3, street, LeftOnly, SignalLight.Yellow);
                        Add(1.5, street, All, SignalLight.Red);
                        moves = ThroughOnly;
                    }
                    Add(Green, street, moves, SignalLight.Green);
                    Add(Yellow, street, moves, SignalLight.Yellow);
                    Add(AllRed, street, All, SignalLight.Red);
                }
            }
            Phase = 0;
            PhaseT = 0;
            Seq = 0;
            log.Clear();
            log.Add(new KeyValuePair<double, int>(-1e9, 0));
            log.Add(new KeyValuePair<double, int>(time, 0));
        }

        private void Add(double duration, int street, int moves, SignalLight light)
        {
            phases.Add(new Stage { Duration = duration, Street = street, Moves = moves, Light = light });
        }

        public int PhaseCount => phases.Count;

        public void Update(double dt, double time)
        {
            if (Hold || phases.Count == 0) return;
            PhaseT += dt;
            if (PhaseT >= phases[Phase].Duration) SetPhase((Phase + 1) % phases.Count, time);
        }

        public void SetPhase(int phase, double time)
        {
            Phase = phase;
            PhaseT = 0;
            log.Add(new KeyValuePair<double, int>(time, phase));
            if (log.Count > 12) log.RemoveAt(0);
        }

        /// <summary>Jump to the first green phase for a street (0 north-south, 1 east-west). With arrow set, the left-arrow phase.</summary>
        public void ShowGreen(int street, bool arrow, double time)
        {
            for (int i = 0; i < phases.Count; i++)
            {
                Stage p = phases[i];
                if (p.Street != street || p.Light != SignalLight.Green) continue;
                if (arrow != (p.Moves == LeftOnly)) continue;
                SetPhase(i, time);
                return;
            }
        }

        /// <summary>The light a movement sees right now.</summary>
        public SignalLight State(int arm, Turn turn)
        {
            return StateIn(arm, turn, Phase);
        }

        /// <summary>The light a movement saw at an earlier time.</summary>
        public SignalLight State(int arm, Turn turn, double at)
        {
            int phase = Phase;
            for (int i = log.Count - 1; i >= 0; i--)
            {
                if (log[i].Key <= at)
                {
                    phase = log[i].Value;
                    break;
                }
            }
            return StateIn(arm, turn, phase);
        }

        /// <summary>True while this turn has a green arrow: nobody else may cross its path.</summary>
        public bool IsProtected(int arm, Turn turn)
        {
            if (phases.Count == 0) return false;
            Stage p = phases[Phase];
            return p.Moves == LeftOnly && p.Street == armStreet[arm] && IsLeft(turn);
        }

        /// <summary>True when this turn is controlled by its own arrow rather than the main light.</summary>
        public bool HasArrow(int arm)
        {
            return Mode == JunctionMode.ProtectedLeft && streetHasLeft[armStreet[arm]];
        }

        private static bool IsLeft(Turn turn)
        {
            return turn == Turn.Left || turn == Turn.UTurn;
        }

        private SignalLight StateIn(int arm, Turn turn, int phase)
        {
            if (Mode == JunctionMode.AllWay) return SignalLight.Stop;
            if (Mode == JunctionMode.Free || phases.Count == 0) return SignalLight.Green;
            Stage p = phases[phase];
            if (p.Street != armStreet[arm]) return SignalLight.Red;
            if (p.Moves == LeftOnly && !IsLeft(turn)) return SignalLight.Red;
            if (p.Moves == ThroughOnly && IsLeft(turn)) return SignalLight.Red;
            return p.Light;
        }
    }
}
