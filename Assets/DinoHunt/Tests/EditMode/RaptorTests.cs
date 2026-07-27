using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    public class RaptorTests
    {
        private sealed class StraightPathfinder : IPathfinder
        {
            public bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> cornersOut)
            {
                cornersOut.Clear();
                cornersOut.Add(start);
                cornersOut.Add(end);
                return true;
            }
        }

        // Nobody sees anybody via LOS — isolates the raptor (which also aggros on nest proximity)
        // and keeps agents from shooting each other, so the raptor is the only killer.
        private sealed class NeverSee : ILineOfSight
        {
            public bool CanSee(Vector3 from, Vector3 to) => false;
        }

        private static ArenaPoints Points(Vector3 blueBase) => new ArenaPoints
        {
            Nest = Vector3.zero,
            BlueBase = blueBase,
            RedBase = new Vector3(300f, 0f, 0f), // far away and irrelevant
            NestRadius = 8f,
            DeliveryRadius = 12f
        };

        private static Simulation Sim(MatchConfig config, Vector3 blueBase, out InMemoryEventSink sink)
        {
            sink = new InMemoryEventSink();
            return new Simulation(config, Points(blueBase), new StraightPathfinder(), new NeverSee(), sink, brain: new StubBrain());
        }

        [Test]
        public void Raptor_Spawns_AndIsLogged()
        {
            var sim = Sim(new MatchConfig { seed = 1, teamSize = 1, raptorCount = 1 }, new Vector3(-8f, 0f, 0f), out var sink);
            sim.Start();
            Assert.AreEqual(1, sim.Raptors.Count);
            Assert.AreEqual(1, sink.Count("raptor_spawn"));
        }

        [Test]
        public void Raptor_AggrosAndScreeches_WhenAgentNearsNest()
        {
            var sim = Sim(new MatchConfig { seed = 1, teamSize = 1, raptorCount = 1, eggCount = 0 }, new Vector3(-30f, 0f, 0f), out var sink);
            sim.Start();
            for (int i = 0; i < 400 && sink.Count("raptor_aggro") == 0; i++) sim.Step();

            Assert.GreaterOrEqual(sink.Count("raptor_aggro"), 1, "the raptor should wake as an agent approaches the nest");
            Assert.GreaterOrEqual(sink.Count("raptor_screech"), 1, "waking should screech");
        }

        [Test]
        public void Raptor_Kills_WithCauseRaptor()
        {
            var sim = Sim(new MatchConfig { seed = 1, teamSize = 1, raptorCount = 1, eggCount = 0 }, new Vector3(-30f, 0f, 0f), out var sink);
            sim.Start();
            for (int i = 0; i < 1500 && sim.IsRunning; i++) sim.Step();

            bool raptorKill = false;
            foreach (var line in sink.Lines)
                if (line.Contains("\"type\":\"death\"") && line.Contains("\"cause\":\"raptor\"")) raptorKill = true;
            Assert.IsTrue(raptorKill, "the raptor should have clawed an agent to death");
        }

        [Test]
        public void RaptorKillOfCarrier_ReturnsEggToNest_NoDrop()
        {
            // raptorSpeed 6 lets the runner reach the nest and grab before the raptor catches the
            // slower carrier on the way out.
            var sim = Sim(new MatchConfig { seed = 1, teamSize = 1, raptorCount = 1, raptorSpeed = 6f, eggCount = 5 }, new Vector3(-30f, 0f, 0f), out var sink);
            sim.Start();

            var blue = sim.Agents[0];
            Egg carried = null;
            for (int i = 0; i < 1200 && carried == null; i++) { sim.Step(); if (blue.CarriedEgg != null) carried = blue.CarriedEgg; }
            Assert.IsNotNull(carried, "the runner should grab an egg before the raptor catches it");

            for (int i = 0; i < 2000 && blue.IsAlive; i++) sim.Step();
            Assert.IsFalse(blue.IsAlive, "the raptor should kill the carrier");
            Assert.AreEqual(EggState.InNest, carried.State, "a raptor kill drags the egg back to the nest");
            Assert.AreEqual(0, sink.Count("egg_drop"), "raptor kills never drop an egg in place");
        }

        [Test]
        public void TeamWipe_DoesNotEndMatch_WhileRaptorAlive()
        {
            // Near agent (blue) gets clawed to death; the far agent (red) is still alive and a raptor
            // remains — the survivors haven't "won", so the match must keep running.
            var sim = Sim(new MatchConfig { seed = 1, teamSize = 1, raptorCount = 1, eggCount = 0 }, new Vector3(-30f, 0f, 0f), out var sink);
            sim.Start();
            var blue = sim.Agents[0];
            for (int i = 0; i < 1500 && blue.IsAlive; i++) sim.Step();

            Assert.IsFalse(blue.IsAlive, "the raptor should have killed the near agent");
            Assert.IsTrue(sim.IsRunning, "a team is wiped but a raptor lives — the match must continue");
            foreach (var line in sink.Lines)
                Assert.IsFalse(line.Contains("\"reason\":\"team_wipe\""), "team_wipe must not fire while a raptor is alive");
        }

        // --- raptor killing (post-M7) ---

        // Blue sits right next to the nest and is aggressive; red is parked far away and unseen so
        // it never distracts. UtilityBrain (default) so agents actually choose to hunt.
        private static Simulation HuntSim(MatchConfig config, out InMemoryEventSink sink)
        {
            sink = new InMemoryEventSink();
            var pts = new ArenaPoints
            {
                Nest = Vector3.zero,
                BlueBase = new Vector3(-14f, 0f, 0f),   // hard by the nest
                RedBase = new Vector3(400f, 0f, 0f),    // far, out of perception
                NestRadius = 8f,
                DeliveryRadius = 12f
            };
            // AlwaysVisible so the raptor can actually be perceived (hunting requires sight).
            return new Simulation(config, pts, new StraightPathfinder(), new AlwaysVisibleLineOfSight(), sink);
        }

        private static Personality Aggressive => new Personality { aggression = 1f, greed = 0f, caution = 0f, teamplay = 1f, patience = 0f };

        [Test]
        public void AggressiveTeam_BringsDownRaptor_AndLogsDeath()
        {
            var sim = HuntSim(new MatchConfig { seed = 7, teamSize = 3, raptorCount = 1, eggCount = 0, personality = Aggressive, perAgentPersonalities = false }, out var sink);
            sim.Start();
            for (int i = 0; i < 3000 && sink.Count("raptor_death") == 0; i++) sim.Step();

            Assert.GreaterOrEqual(sink.Count("raptor_death"), 1, "three aggressive agents focus-firing should kill the raptor");
            Assert.Greater(sink.Count("raptor_damage"), 0, "raptor hits should be logged");
        }

        [Test]
        public void KilledRaptor_StopsThreatening_AndLeavesTheField()
        {
            var sim = HuntSim(new MatchConfig { seed = 7, teamSize = 3, raptorCount = 1, eggCount = 0, personality = Aggressive, perAgentPersonalities = false }, out var sink);
            sim.Start();
            for (int i = 0; i < 3000 && sink.Count("raptor_death") == 0; i++) sim.Step();

            Assert.IsFalse(sim.Raptors[0].IsAlive, "the raptor should be dead");
        }

        [Test]
        public void SameSeed_ProducesIdenticalStream_WithRaptorKilling()
        {
            var a = HuntSim(new MatchConfig { seed = 55, teamSize = 3, raptorCount = 1, eggCount = 0, personality = Aggressive, perAgentPersonalities = false }, out var sinkA);
            var b = HuntSim(new MatchConfig { seed = 55, teamSize = 3, raptorCount = 1, eggCount = 0, personality = Aggressive, perAgentPersonalities = false }, out var sinkB);
            a.Start();
            b.Start();
            for (int i = 0; i < 4000 && (a.IsRunning || b.IsRunning); i++) { a.Step(); b.Step(); }

            Assert.AreEqual(sinkA.Lines.Count, sinkB.Lines.Count);
            for (int i = 0; i < sinkA.Lines.Count; i++)
                Assert.AreEqual(sinkA.Lines[i], sinkB.Lines[i], $"diverged at line {i}");
        }

        [Test]
        public void SameSeed_ProducesIdenticalStream_WithRaptor()
        {
            var a = Sim(new MatchConfig { seed = 99, teamSize = 2, raptorCount = 1, eggCount = 5 }, new Vector3(-40f, 0f, 0f), out var sinkA);
            var b = Sim(new MatchConfig { seed = 99, teamSize = 2, raptorCount = 1, eggCount = 5 }, new Vector3(-40f, 0f, 0f), out var sinkB);
            a.Start();
            b.Start();
            for (int i = 0; i < 6000 && (a.IsRunning || b.IsRunning); i++) { a.Step(); b.Step(); }

            Assert.AreEqual(sinkA.Lines.Count, sinkB.Lines.Count);
            for (int i = 0; i < sinkA.Lines.Count; i++)
                Assert.AreEqual(sinkA.Lines[i], sinkB.Lines[i], $"diverged at line {i}");
        }
    }
}
