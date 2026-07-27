using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    public class UtilityBrainTests
    {
        private static Personality P(float ag, float gr, float ca, float tp, float pa) =>
            new Personality { aggression = ag, greed = gr, caution = ca, teamplay = tp, patience = pa };

        private static Agent MakeAgent(int id, Team team, Vector3 pos, Personality p)
        {
            var weapon = new WeaponState(new WeaponSpec(), new WeaponSpec { name = "sidearm", magazineSize = 0, infinite = true, range = 45f });
            var a = new Agent(id, team, pos, 7f, new DeterministicRandom((ulong)id), 100f, weapon) { Personality = p };
            return a;
        }

        private static ArenaPoints Points() => new ArenaPoints
        {
            Nest = Vector3.zero,
            BlueBase = new Vector3(-50f, 0f, 0f),
            RedBase = new Vector3(50f, 0f, 0f),
            NestRadius = 8f,
            DeliveryRadius = 12f
        };

        [Test]
        public void CautiousAgent_FleesNearbyRaptor()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-40f, 0f, 0f), P(0f, 0f, 1f, 0f, 0f));
            var raptor = new Raptor(0, new Vector3(-38f, 0f, 0f), 500f);
            var world = new WorldView(new List<Agent> { a }, new List<Raptor> { raptor }, new List<Egg>(), Points());

            Assert.AreEqual(ActionType.FleeRaptor, brain.Decide(a, world).Type);
        }

        [Test]
        public void Carrier_HeadsHomeToDeliver()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-45f, 0f, 0f), P(0.5f, 0.5f, 0.5f, 0.5f, 0.5f));
            var egg = new Egg(0, Vector3.zero) { State = EggState.Carried, Carrier = a };
            a.CarriedEgg = egg;
            var world = new WorldView(new List<Agent> { a }, new List<Raptor>(), new List<Egg> { egg }, Points());

            Assert.AreEqual(ActionType.DeliverEgg, brain.Decide(a, world).Type);
        }

        [Test]
        public void AggressiveAgent_EngagesNearbyEnemy()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-10f, 0f, 0f), P(1f, 0f, 0f, 0f, 0f));
            var enemy = MakeAgent(1, Team.Red, new Vector3(10f, 0f, 0f), P(0f, 0f, 0f, 0f, 0f));
            var world = new WorldView(new List<Agent> { a, enemy }, new List<Raptor>(), new List<Egg>(), Points());

            var d = brain.Decide(a, world);
            Assert.AreEqual(ActionType.Engage, d.Type);
            Assert.AreSame(enemy, d.TargetEnemy);
        }

        [Test]
        public void GreedyAgent_GoesForTheEgg()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-20f, 0f, 0f), P(0f, 1f, 0f, 0f, 0f));
            var egg = new Egg(0, Vector3.zero);
            var world = new WorldView(new List<Agent> { a }, new List<Raptor>(), new List<Egg> { egg }, Points());

            Assert.AreEqual(ActionType.GrabEgg, brain.Decide(a, world).Type);
        }

        [Test]
        public void FacingAwayFromEnemy_DoesNotEngage()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, Vector3.zero, P(1f, 0f, 0f, 0f, 0f)); // Facing defaults to +Z
            var enemy = MakeAgent(1, Team.Red, new Vector3(0f, 0f, -15f), P(0f, 0f, 0f, 0f, 0f)); // directly behind
            var world = new WorldView(new List<Agent> { a, enemy }, new List<Raptor>(), new List<Egg>(), Points(),
                new AlwaysVisibleLineOfSight(), sightRange: 80f, fovDegrees: 140f);

            Assert.AreNotEqual(ActionType.Engage, brain.Decide(a, world).Type,
                "an enemy directly behind the facing cone shouldn't be perceived, let alone engaged");
        }

        [Test]
        public void RaptorAlerted_ButNotVisible_StillPullsBackTowardBase()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-40f, 0f, 0f), P(0f, 0f, 1f, 0f, 0f));
            var world = new WorldView(new List<Agent> { a }, new List<Raptor>(), new List<Egg>(), Points(),
                new AlwaysVisibleLineOfSight(), sightRange: 80f, fovDegrees: 140f);
            world.BeginTick(10.0);
            // Screech heard nearby, but no raptor in the world to directly see.
            world.RaiseRaptorAlert(a.Position, 10.0, 5f, radius: 200f);

            var d = brain.Decide(a, world);
            Assert.AreEqual(ActionType.FleeRaptor, d.Type, "hearing the screech alone should still trigger a cautious pull back");
            Assert.AreEqual(Points().BlueBase, d.Destination, "without a visible raptor position, fall back on the known-safe own base");
        }

        // Perceiving WorldView helper (raptor near enough to see, wide cone, clear LOS).
        private static WorldView Seeing(List<Agent> agents, List<Raptor> raptors) =>
            new WorldView(agents, raptors, new List<Egg>(), Points(), new AlwaysVisibleLineOfSight(), sightRange: 80f, fovDegrees: 360f);

        [Test]
        public void AggressiveWithTeammateNear_HuntsTheRaptor()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-10f, 0f, 0f), P(1f, 0f, 0f, 1f, 0f));
            var mate = MakeAgent(2, Team.Blue, new Vector3(-12f, 0f, 0f), P(1f, 0f, 0f, 1f, 0f)); // within IsolationRadius
            var raptor = new Raptor(0, new Vector3(-18f, 0f, 0f), 208f);
            var d = brain.Decide(a, Seeing(new List<Agent> { a, mate }, new List<Raptor> { raptor }));

            Assert.AreEqual(ActionType.HuntRaptor, d.Type);
            Assert.AreSame(raptor, d.TargetRaptor);
        }

        [Test]
        public void CautiousAgent_FleesRatherThanHunts_EvenWithTeammate()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-10f, 0f, 0f), P(0f, 0f, 1f, 1f, 0f));
            var mate = MakeAgent(2, Team.Blue, new Vector3(-12f, 0f, 0f), P(0f, 0f, 1f, 1f, 0f));
            var raptor = new Raptor(0, new Vector3(-18f, 0f, 0f), 208f);
            var d = brain.Decide(a, Seeing(new List<Agent> { a, mate }, new List<Raptor> { raptor }));

            Assert.AreEqual(ActionType.FleeRaptor, d.Type, "high caution should pick flight over the fight");
        }

        [Test]
        public void Carrier_NeverHuntsTheRaptor()
        {
            var brain = new UtilityBrain();
            var a = MakeAgent(0, Team.Blue, new Vector3(-10f, 0f, 0f), P(1f, 0f, 0f, 1f, 0f));
            var mate = MakeAgent(2, Team.Blue, new Vector3(-12f, 0f, 0f), P(1f, 0f, 0f, 1f, 0f));
            var egg = new Egg(0, Vector3.zero) { State = EggState.Carried, Carrier = a };
            a.CarriedEgg = egg;
            var raptor = new Raptor(0, new Vector3(-18f, 0f, 0f), 208f);
            var world = new WorldView(new List<Agent> { a, mate }, new List<Raptor> { raptor }, new List<Egg> { egg },
                Points(), new AlwaysVisibleLineOfSight(), sightRange: 80f, fovDegrees: 360f);

            Assert.AreNotEqual(ActionType.HuntRaptor, brain.Decide(a, world).Type, "a carrier runs the egg home, never brawls the raptor");
        }

        private sealed class StraightPathfinder : IPathfinder
        {
            public bool TryFindPath(Vector3 s, Vector3 e, List<Vector3> o) { o.Clear(); o.Add(s); o.Add(e); return true; }
        }

        [Test]
        public void FullMatch_WithUtilityBrain_IsDeterministic()
        {
            var pts = new ArenaPoints { Nest = Vector3.zero, BlueBase = new Vector3(-40f, 0f, 0f), RedBase = new Vector3(40f, 0f, 0f), NestRadius = 8f, DeliveryRadius = 12f };
            var sinkA = new InMemoryEventSink();
            var sinkB = new InMemoryEventSink();
            var a = new Simulation(new MatchConfig { seed = 321, teamSize = 3, raptorCount = 1, eggCount = 5 }, pts, new StraightPathfinder(), new AlwaysVisibleLineOfSight(), sinkA);
            var b = new Simulation(new MatchConfig { seed = 321, teamSize = 3, raptorCount = 1, eggCount = 5 }, pts, new StraightPathfinder(), new AlwaysVisibleLineOfSight(), sinkB);
            a.Start();
            b.Start();
            for (int i = 0; i < 8000 && (a.IsRunning || b.IsRunning); i++) { a.Step(); b.Step(); }

            Assert.AreEqual(sinkA.Lines.Count, sinkB.Lines.Count);
            for (int i = 0; i < sinkA.Lines.Count; i++)
                Assert.AreEqual(sinkA.Lines[i], sinkB.Lines[i], $"diverged at line {i}");
        }
    }
}
