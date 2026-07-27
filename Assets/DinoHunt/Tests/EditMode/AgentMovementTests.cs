using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    public class AgentMovementTests
    {
        /// <summary>Trivial pathfinder: walk straight from start to end. Keeps the sim testable without a baked NavMesh.</summary>
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

        /// <summary>Nobody can see anybody — isolates pure wandering from combat.</summary>
        private sealed class NeverSee : ILineOfSight
        {
            public bool CanSee(Vector3 from, Vector3 to) => false;
        }

        private static ArenaPoints Points() => new ArenaPoints
        {
            Nest = Vector3.zero,
            BlueBase = new Vector3(-45f, 0f, 0f),
            RedBase = new Vector3(45f, 0f, 0f)
        };

        [Test]
        public void Spawns_TwoAgentsPerTeam_AtCorrectBases()
        {
            var sim = new Simulation(new MatchConfig { raptorCount = 0,seed = 1, teamSize = 2 }, Points(), new StraightPathfinder(), new NeverSee(), brain: new StubBrain());
            Assert.AreEqual(4, sim.Agents.Count);
            Assert.AreEqual(2, CountTeam(sim, Team.Blue));
            Assert.AreEqual(2, CountTeam(sim, Team.Red));
        }

        [Test]
        public void Agent_MovesFromSpawn_WhenStepped()
        {
            var sim = new Simulation(new MatchConfig { raptorCount = 0,seed = 5, teamSize = 1, agentSpeed = 6f }, Points(), new StraightPathfinder(), new NeverSee(), brain: new StubBrain());
            sim.Start();
            var agent = sim.Agents[0];
            Vector3 spawn = agent.Position;

            for (int i = 0; i < 30; i++) sim.Step();

            Assert.AreNotEqual(spawn, agent.Position, "agent should have moved after stepping");
            Assert.IsFalse(string.IsNullOrEmpty(agent.Intent));
            Assert.AreNotEqual("spawning", agent.Intent, "agent should have made a real decision");
        }

        [Test]
        public void Movement_NeverExceedsSpeedTimesDt_PerStep()
        {
            var config = new MatchConfig { raptorCount = 0,seed = 9, teamSize = 2, agentSpeed = 6f, fixedDeltaTime = 1f / 60f };
            var sim = new Simulation(config, Points(), new StraightPathfinder(), new NeverSee(), brain: new StubBrain());
            sim.Start();

            float maxStep = config.agentSpeed * config.fixedDeltaTime + 1e-4f;
            var prev = new Dictionary<int, Vector3>();
            foreach (var a in sim.Agents) prev[a.Id] = a.Position;

            for (int step = 0; step < 500; step++)
            {
                sim.Step();
                foreach (var a in sim.Agents)
                {
                    float moved = (a.Position - prev[a.Id]).magnitude;
                    Assert.LessOrEqual(moved, maxStep, $"agent {a.Id} moved {moved} in one step (cap {maxStep})");
                    prev[a.Id] = a.Position;
                }
            }
        }

        [Test]
        public void SameSeed_ProducesIdenticalAgentTrajectories()
        {
            var a = new Simulation(new MatchConfig { raptorCount = 0,seed = 4242, teamSize = 2 }, Points(), new StraightPathfinder(), new NeverSee(), brain: new StubBrain());
            var b = new Simulation(new MatchConfig { raptorCount = 0,seed = 4242, teamSize = 2 }, Points(), new StraightPathfinder(), new NeverSee(), brain: new StubBrain());
            a.Start();
            b.Start();

            for (int i = 0; i < 1000; i++)
            {
                a.Step();
                b.Step();
                for (int k = 0; k < a.Agents.Count; k++)
                {
                    Assert.AreEqual(a.Agents[k].Position, b.Agents[k].Position, $"pos diverged, agent {k} step {i}");
                    Assert.AreEqual(a.Agents[k].Intent, b.Agents[k].Intent, $"intent diverged, agent {k} step {i}");
                }
            }
        }

        private static int CountTeam(Simulation sim, Team team)
        {
            int n = 0;
            foreach (var a in sim.Agents) if (a.Team == team) n++;
            return n;
        }
    }
}
