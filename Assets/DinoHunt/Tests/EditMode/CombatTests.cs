using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    public class CombatTests
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

        // Force fighting: max aggression, no greed, so the Utility AI always chooses Engage.
        private static Personality Aggressive => new Personality
        {
            aggression = 1f, greed = 0f, caution = 0f, teamplay = 0f, patience = 0f
        };

        // Bases close together so the two agents are inside weapon range at spawn and engage immediately.
        private static ArenaPoints ClosePoints() => new ArenaPoints
        {
            Nest = Vector3.zero,
            BlueBase = new Vector3(-5f, 0f, 0f),
            RedBase = new Vector3(5f, 0f, 0f)
        };

        private static Simulation NewCombatSim(MatchConfig config, out InMemoryEventSink sink)
        {
            sink = new InMemoryEventSink();
            return new Simulation(config, ClosePoints(), new StraightPathfinder(), new AlwaysVisibleLineOfSight(), sink);
        }

        [Test]
        public void MatchStartAndSpawns_AreLogged()
        {
            var sim = NewCombatSim(new MatchConfig { raptorCount = 0, eggCount = 0, personality = Aggressive, perAgentPersonalities = false,seed = 1, teamSize = 1 }, out var sink);
            sim.Start();
            Assert.AreEqual(1, sink.Count("match_start"));
            Assert.AreEqual(2, sink.Count("spawn"));
        }

        [Test]
        public void Engaging_ProducesShotsAndDamage()
        {
            var sim = NewCombatSim(new MatchConfig { raptorCount = 0, eggCount = 0, personality = Aggressive, perAgentPersonalities = false,seed = 1, teamSize = 1 }, out var sink);
            sim.Start();
            for (int i = 0; i < 60; i++) sim.Step();

            Assert.Greater(sink.Count("shot_fired"), 0, "agents in range with LOS should fire");
            Assert.Greater(sink.Count("damage"), 0, "shots should land (perfect aim)");
        }

        [Test]
        public void CombatRunsToDeath_AndMatchEnds()
        {
            var sim = NewCombatSim(new MatchConfig { raptorCount = 0, eggCount = 0, personality = Aggressive, perAgentPersonalities = false,seed = 1, teamSize = 1 }, out var sink);
            sim.Start();
            for (int i = 0; i < 6000 && sim.IsRunning; i++) sim.Step();

            Assert.IsFalse(sim.IsRunning, "match should end once a team is wiped");
            Assert.Greater(sink.Count("death"), 0);
            Assert.AreEqual(1, sink.Count("match_end"));
        }

        [Test]
        public void RifleDepletes_ThenFallsBackToInfiniteSidearm()
        {
            // Tiny rifle pool, unkillable agents (huge HP) so we can observe the weapon switch.
            var config = new MatchConfig
            {
                seed = 1,
                teamSize = 1,
                raptorCount = 0,
                eggCount = 0,
                personality = Aggressive, perAgentPersonalities = false,
                agentHealth = 100000f,
                rifle = new WeaponSpec { name = "rifle", damage = 1f, range = 45f, fireInterval = 0.05f, magazineSize = 2, reloadTime = 0.2f, startingReserve = 2 },
                sidearm = new WeaponSpec { name = "sidearm", damage = 1f, range = 45f, fireInterval = 0.05f, magazineSize = 0, reloadTime = 0f, startingReserve = 0, infinite = true }
            };
            var sim = NewCombatSim(config, out var sink);
            sim.Start();
            for (int i = 0; i < 400; i++) sim.Step();

            int sidearmShots = 0;
            foreach (var line in sink.Lines)
                if (line.Contains("\"type\":\"shot_fired\"") && line.Contains("\"weapon\":\"sidearm\"")) sidearmShots++;

            Assert.Greater(sidearmShots, 0, "once the 4-round rifle pool is spent, the infinite sidearm should take over");
        }

        [Test]
        public void SameSeed_ProducesIdenticalEventStream()
        {
            var a = NewCombatSim(new MatchConfig { raptorCount = 0, eggCount = 0, personality = Aggressive, perAgentPersonalities = false,seed = 777, teamSize = 2 }, out var sinkA);
            var b = NewCombatSim(new MatchConfig { raptorCount = 0, eggCount = 0, personality = Aggressive, perAgentPersonalities = false,seed = 777, teamSize = 2 }, out var sinkB);
            a.Start();
            b.Start();
            for (int i = 0; i < 4000 && (a.IsRunning || b.IsRunning); i++) { a.Step(); b.Step(); }

            Assert.AreEqual(sinkA.Lines.Count, sinkB.Lines.Count, "same seed must yield the same number of events");
            for (int i = 0; i < sinkA.Lines.Count; i++)
                Assert.AreEqual(sinkA.Lines[i], sinkB.Lines[i], $"event stream diverged at line {i}");
        }
    }
}
