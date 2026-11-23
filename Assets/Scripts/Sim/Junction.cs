using System.Collections.Generic;

namespace Traffic.Sim
{
    public enum JunctionMode
    {
        Signal,
        AllWay,
    }

    public enum SignalLight
    {
        Green,
        Yellow,
        Red,
        Stop,
    }

    /// <summary>
    /// Controls the intersection. In signal mode it cycles: north-south green,
    /// yellow, all red, east-west green, yellow, all red. In all-way mode every
    /// arm has a stop sign.
    /// </summary>
    public sealed class Junction
    {
        public JunctionMode Mode = JunctionMode.Signal;
        public double Green = 14;
        public double Yellow = 3.5;
        public double AllRed = 2;
        public int Phase;
        /// <summary>Seconds spent in the current phase.</summary>
        public double PhaseT;
        /// <summary>When true the phase never advances (used by tests).</summary>
        public bool Hold;
        /// <summary>Order in which cars were let through in all-way mode.</summary>
        public int Seq;

        private readonly List<KeyValuePair<double, int>> log = new List<KeyValuePair<double, int>> { new KeyValuePair<double, int>(-1e9, 0) };

        private double Duration(int phase)
        {
            int k = phase % 3;
            return k == 0 ? Green : k == 1 ? Yellow : AllRed;
        }

        public void Update(double dt, double time)
        {
            if (Hold) return;
            PhaseT += dt;
            if (PhaseT >= Duration(Phase)) SetPhase((Phase + 1) % 6, time);
        }

        public void SetPhase(int phase, double time)
        {
            Phase = phase;
            PhaseT = 0;
            log.Add(new KeyValuePair<double, int>(time, phase));
            if (log.Count > 12) log.RemoveAt(0);
        }

        /// <summary>The light an arm shows right now (0 north, 1 east, 2 south, 3 west).</summary>
        public SignalLight ArmState(int arm)
        {
            return Mode == JunctionMode.AllWay ? SignalLight.Stop : StateFor(arm, Phase);
        }

        /// <summary>The light an arm showed at an earlier time.</summary>
        public SignalLight ArmState(int arm, double at)
        {
            if (Mode == JunctionMode.AllWay) return SignalLight.Stop;
            int phase = Phase;
            for (int i = log.Count - 1; i >= 0; i--)
            {
                if (log[i].Key <= at)
                {
                    phase = log[i].Value;
                    break;
                }
            }
            return StateFor(arm, phase);
        }

        private static SignalLight StateFor(int arm, int phase)
        {
            bool northSouth = arm % 2 == 0;
            bool mine = northSouth ? phase < 3 : phase >= 3;
            if (!mine) return SignalLight.Red;
            int k = phase % 3;
            return k == 0 ? SignalLight.Green : k == 1 ? SignalLight.Yellow : SignalLight.Red;
        }
    }
}
