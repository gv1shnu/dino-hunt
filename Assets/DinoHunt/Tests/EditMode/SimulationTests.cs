using DinoHunt.Core;
using NUnit.Framework;

namespace DinoHunt.Tests
{
    public class SimulationTests
    {
        [Test]
        public void SimClock_TimeIsTickTimesDt_NotAccumulated()
        {
            var clock = new SimClock(1f / 60f);
            for (int i = 0; i < 600; i++) clock.Step();

            Assert.AreEqual(600, clock.Tick);
            // Exactly tick * dt, computed from the runtime dt — no accumulation drift.
            // (Expected is derived from clock.FixedDeltaTime, not a literal, so compiler
            // constant-folding of 1f/60f in double precision can't skew the comparison.)
            Assert.AreEqual(600 * (double)clock.FixedDeltaTime, clock.Time);
        }

        [Test]
        public void SimClock_Reset_ReturnsToStart()
        {
            var clock = new SimClock(0.02f);
            clock.Step();
            clock.Step();
            clock.Reset();
            Assert.AreEqual(0, clock.Tick);
            Assert.AreEqual(0.0, clock.Time);
        }

        [Test]
        public void Simulation_DoesNotStepUntilStarted()
        {
            var sim = new Simulation(new MatchConfig { seed = 1 });
            sim.Step();
            Assert.AreEqual(0, sim.Clock.Tick, "Step before Start should be a no-op");
        }

        [Test]
        public void Simulation_StepsWhileRunning()
        {
            var sim = new Simulation(new MatchConfig { seed = 1 });
            sim.Start();
            for (int i = 0; i < 100; i++) sim.Step();
            Assert.AreEqual(100, sim.Clock.Tick);
            Assert.IsTrue(sim.IsRunning);
        }

        [Test]
        public void Simulation_StopsAtTimerExpiry()
        {
            var config = new MatchConfig { seed = 1, fixedDeltaTime = 0.1f, matchTimerSeconds = 1f };
            var sim = new Simulation(config);
            sim.Start();

            // 10 steps of 0.1s reaches exactly 1.0s -> should stop.
            for (int i = 0; i < 50; i++) sim.Step();

            Assert.IsFalse(sim.IsRunning);
            Assert.AreEqual(10, sim.Clock.Tick, "should stop the tick it reaches the timer, not overrun");
        }

        [Test]
        public void Simulation_IsReproducible_SameSeedSameRngStream()
        {
            var a = new Simulation(new MatchConfig { seed = 12345 });
            var b = new Simulation(new MatchConfig { seed = 12345 });

            for (int i = 0; i < 500; i++)
                Assert.AreEqual(a.Rng.NextULong(), b.Rng.NextULong(), $"RNG diverged at {i}");
        }
    }
}
