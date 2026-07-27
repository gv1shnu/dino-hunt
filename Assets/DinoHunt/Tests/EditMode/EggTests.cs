using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    public class EggTests
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

        private sealed class NeverSee : ILineOfSight
        {
            public bool CanSee(Vector3 from, Vector3 to) => false;
        }

        private sealed class ToggleLineOfSight : ILineOfSight
        {
            public bool On;
            public bool CanSee(Vector3 from, Vector3 to) => On;
        }

        private static ArenaPoints Points(Vector3 blueBase, Vector3 redBase, float deliveryRadius = 12f) => new ArenaPoints
        {
            Nest = Vector3.zero,
            BlueBase = blueBase,
            RedBase = redBase,
            NestRadius = 8f,
            DeliveryRadius = deliveryRadius
        };

        private static WeaponSpec FastRifle() => new WeaponSpec
        {
            name = "rifle", damage = 60f, range = 45f, fireInterval = 0.1f,
            magazineSize = 30, reloadTime = 2.5f, startingReserve = 60
        };

        [Test]
        public void Agent_GrabsEggFromNest()
        {
            var sim = new Simulation(new MatchConfig { raptorCount = 0,seed = 1, teamSize = 1 },
                Points(new Vector3(-10, 0, 0), new Vector3(200, 0, 0)), new StraightPathfinder(), new NeverSee(), null, brain: new StubBrain());
            sim.Start();
            for (int i = 0; i < 300; i++) sim.Step();

            Assert.IsNotNull(sim.Agents[0].CarriedEgg, "blue should have grabbed an egg at the nest");
            Assert.AreEqual(EggState.Carried, sim.Agents[0].CarriedEgg.State);
        }

        [Test]
        public void Carrying_SlowsMovementToPenaltySpeed()
        {
            var config = new MatchConfig { raptorCount = 0,seed = 1, teamSize = 1, agentSpeed = 9f, fixedDeltaTime = 1f / 60f, carrySpeedMultiplier = 0.65f };
            var sim = new Simulation(config, Points(new Vector3(-30, 0, 0), new Vector3(200, 0, 0)), new StraightPathfinder(), new NeverSee(), null, brain: new StubBrain());
            sim.Start();

            var blue = sim.Agents[0];
            float cap = config.agentSpeed * config.carrySpeedMultiplier * config.fixedDeltaTime + 1e-4f;
            bool sawCarryMovement = false;
            Vector3 prev = blue.Position;

            for (int i = 0; i < 1200; i++)
            {
                sim.Step();
                if (blue.CarriedEgg != null)
                {
                    float moved = (blue.Position - prev).magnitude;
                    Assert.LessOrEqual(moved, cap, "a carrier must not move faster than the carry-penalty speed");
                    if (moved > 1e-4f) sawCarryMovement = true;
                }
                prev = blue.Position;
            }
            Assert.IsTrue(sawCarryMovement, "the carrier should have moved while carrying (so the cap check is meaningful)");
        }

        [Test]
        public void Delivery_HealsCarrierToFull_AndLogsDelivered()
        {
            var sink = new InMemoryEventSink();
            var sim = new Simulation(new MatchConfig { raptorCount = 0,seed = 1, teamSize = 1, eggCount = 5, agentHealth = 100f },
                Points(new Vector3(-30, 0, 0), new Vector3(200, 0, 0)), new StraightPathfinder(), new NeverSee(), sink, brain: new StubBrain());
            sim.Start();
            sim.Agents[0].Health = 5f; // wounded on the way in

            for (int i = 0; i < 2500 && sink.Count("egg_delivered") == 0; i++) sim.Step();

            Assert.GreaterOrEqual(sink.Count("egg_delivered"), 1, "the egg should have been delivered");
            Assert.AreEqual(100f, sim.Agents[0].Health, 0.001f, "delivery is the only heal — it should restore full health");
        }

        [Test]
        public void AllEggsDelivered_EndsMatch()
        {
            var sink = new InMemoryEventSink();
            var sim = new Simulation(new MatchConfig { raptorCount = 0,seed = 1, teamSize = 1, eggCount = 1 },
                Points(new Vector3(-30, 0, 0), new Vector3(200, 0, 0)), new StraightPathfinder(), new NeverSee(), sink, brain: new StubBrain());
            sim.Start();
            for (int i = 0; i < 3000 && sim.IsRunning; i++) sim.Step();

            Assert.IsFalse(sim.IsRunning);
            Assert.AreEqual(1, sink.Count("match_end"));
            bool byAllEggs = false;
            foreach (var line in sink.Lines) if (line.Contains("all_eggs_delivered")) byAllEggs = true;
            Assert.IsTrue(byAllEggs, "match should end with reason all_eggs_delivered");
        }

        [Test]
        public void KilledCarrier_DropsEggInPlace()
        {
            var sink = new InMemoryEventSink();
            var los = new ToggleLineOfSight { On = false };
            var config = new MatchConfig { raptorCount = 0,seed = 1, teamSize = 1, eggCount = 5, rifle = FastRifle() };
            var sim = new Simulation(config, Points(new Vector3(40, 0, 0), new Vector3(-20, 0, 0)), new StraightPathfinder(), los, sink, brain: new StubBrain());
            sim.Start();

            for (int i = 0; i < 180; i++) sim.Step(); // red grabs and starts carrying
            los.On = true;                             // blue can now see and kill the red carrier
            for (int i = 0; i < 400 && sim.IsRunning; i++) sim.Step();

            Assert.GreaterOrEqual(sink.Count("egg_drop"), 1, "a carrier killed by an enemy should drop its egg");
        }

        [Test]
        public void EnemyPicksUpDroppedEgg_CountsAsSteal()
        {
            var sink = new InMemoryEventSink();
            var los = new ToggleLineOfSight { On = false };
            var config = new MatchConfig { raptorCount = 0,seed = 2, teamSize = 2, eggCount = 2, pickupRadius = 10f, rifle = FastRifle() };
            var sim = new Simulation(config, Points(new Vector3(40, 0, 0), new Vector3(-16, 0, 0), 10f), new StraightPathfinder(), los, sink, brain: new StubBrain());
            sim.Start();

            for (int i = 0; i < 160; i++) sim.Step();                                           // reds grab both eggs
            los.On = true;
            for (int i = 0; i < 400 && sim.IsRunning && sink.Count("egg_drop") == 0; i++) sim.Step(); // kill one red carrier
            los.On = false;                                                                     // stop before a full wipe
            for (int i = 0; i < 2000 && sim.IsRunning; i++) sim.Step();                          // a free blue takes the drop

            Assert.GreaterOrEqual(sink.Count("egg_stolen"), 1, "an enemy taking a dropped egg should register as a steal");
        }

        [Test]
        public void SameSeed_ProducesIdenticalEggAndCombatStream()
        {
            InMemoryEventSink sinkA, sinkB;
            var a = MakeSkirmish(555, out sinkA);
            var b = MakeSkirmish(555, out sinkB);
            a.Start();
            b.Start();
            for (int i = 0; i < 8000 && (a.IsRunning || b.IsRunning); i++) { a.Step(); b.Step(); }

            Assert.AreEqual(sinkA.Lines.Count, sinkB.Lines.Count, "same seed must yield the same number of events");
            for (int i = 0; i < sinkA.Lines.Count; i++)
                Assert.AreEqual(sinkA.Lines[i], sinkB.Lines[i], $"event stream diverged at line {i}");
        }

        private static Simulation MakeSkirmish(ulong seed, out InMemoryEventSink sink)
        {
            sink = new InMemoryEventSink();
            return new Simulation(new MatchConfig { raptorCount = 0,seed = seed, teamSize = 2, eggCount = 5 },
                Points(new Vector3(-40, 0, 0), new Vector3(40, 0, 0)), new StraightPathfinder(), new AlwaysVisibleLineOfSight(), sink, brain: new StubBrain());
        }
    }
}
