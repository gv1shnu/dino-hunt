namespace DinoHunt.Core
{
    /// <summary>
    /// Fixed-timestep clock for the simulation. Time is derived from the tick count
    /// (tick * dt), never accumulated from per-frame deltas — accumulation drifts and
    /// breaks determinism. Same tick count always yields the same time, exactly.
    /// </summary>
    public sealed class SimClock
    {
        /// <summary>Seconds per simulation step. Fixed for the whole match.</summary>
        public float FixedDeltaTime { get; }

        /// <summary>Number of steps taken since the match began.</summary>
        public long Tick { get; private set; }

        public SimClock(float fixedDeltaTime)
        {
            FixedDeltaTime = fixedDeltaTime;
            Tick = 0;
        }

        /// <summary>Elapsed simulated time in seconds. Computed, never accumulated.</summary>
        public double Time => Tick * (double)FixedDeltaTime;

        /// <summary>Advance one fixed step.</summary>
        public void Step() => Tick++;

        /// <summary>Reset to the start of a match.</summary>
        public void Reset() => Tick = 0;
    }
}
