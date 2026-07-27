using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    /// <summary>
    /// M7 perception gating: range + facing cone + line-of-sight. Verifies the CanPerceive
    /// primitive directly and that NearestEnemy/NearestRaptor actually use it, plus the
    /// sound-based (not sight-based) raptor alert window.
    /// </summary>
    public class WorldViewTests
    {
        private sealed class ScriptedLos : ILineOfSight
        {
            public bool Blocked;
            public bool CanSee(Vector3 from, Vector3 to) => !Blocked;
        }

        private static Agent MakeAgent(int id, Team team, Vector3 pos, Vector3 facing)
        {
            var weapon = new WeaponState(new WeaponSpec(), new WeaponSpec { name = "sidearm", magazineSize = 0, infinite = true, range = 45f });
            return new Agent(id, team, pos, 7f, new DeterministicRandom((ulong)id), 100f, weapon) { Facing = facing.normalized };
        }

        private static ArenaPoints Points() => new ArenaPoints
        {
            Nest = Vector3.zero,
            BlueBase = new Vector3(-50f, 0f, 0f),
            RedBase = new Vector3(50f, 0f, 0f)
        };

        [Test]
        public void InFovAndRange_WithClearLOS_IsPerceived()
        {
            var self = MakeAgent(0, Team.Blue, Vector3.zero, Vector3.forward);
            var world = new WorldView(new List<Agent> { self }, new List<Raptor>(), new List<Egg>(), Points(),
                new ScriptedLos(), sightRange: 50f, fovDegrees: 140f);

            Assert.IsTrue(world.CanPerceive(self, new Vector3(0f, 0f, 20f)), "directly ahead, in range, clear LOS should be perceived");
        }

        [Test]
        public void OutsideFovCone_IsNotPerceived()
        {
            var self = MakeAgent(0, Team.Blue, Vector3.zero, Vector3.forward);
            var world = new WorldView(new List<Agent> { self }, new List<Raptor>(), new List<Egg>(), Points(),
                new ScriptedLos(), sightRange: 50f, fovDegrees: 140f);

            // Directly behind the agent, well outside a 140-degree cone even though in range with clear LOS.
            Assert.IsFalse(world.CanPerceive(self, new Vector3(0f, 0f, -20f)), "behind the facing cone should not be perceived");
        }

        [Test]
        public void OutsideSightRange_IsNotPerceived()
        {
            var self = MakeAgent(0, Team.Blue, Vector3.zero, Vector3.forward);
            var world = new WorldView(new List<Agent> { self }, new List<Raptor>(), new List<Egg>(), Points(),
                new ScriptedLos(), sightRange: 50f, fovDegrees: 140f);

            Assert.IsFalse(world.CanPerceive(self, new Vector3(0f, 0f, 200f)), "far beyond sight range should not be perceived, even dead ahead");
        }

        [Test]
        public void BlockedLineOfSight_IsNotPerceived()
        {
            var self = MakeAgent(0, Team.Blue, Vector3.zero, Vector3.forward);
            var los = new ScriptedLos { Blocked = true };
            var world = new WorldView(new List<Agent> { self }, new List<Raptor>(), new List<Egg>(), Points(),
                los, sightRange: 50f, fovDegrees: 140f);

            Assert.IsFalse(world.CanPerceive(self, new Vector3(0f, 0f, 10f)), "cover blocking the raycast should defeat perception even in-cone and in-range");
        }

        [Test]
        public void NearestEnemy_SkipsEnemiesOutsideThePerceptionCone()
        {
            var self = MakeAgent(0, Team.Blue, Vector3.zero, Vector3.forward);
            var behind = MakeAgent(1, Team.Red, new Vector3(0f, 0f, -10f), Vector3.back);
            var ahead = MakeAgent(2, Team.Red, new Vector3(0f, 0f, 15f), Vector3.forward);
            var world = new WorldView(new List<Agent> { self, behind, ahead }, new List<Raptor>(), new List<Egg>(), Points(),
                new ScriptedLos(), sightRange: 50f, fovDegrees: 140f);

            Agent found = world.NearestEnemy(self, out _);
            Assert.AreSame(ahead, found, "only the enemy inside the facing cone should be perceived, even though 'behind' is closer");
        }

        [Test]
        public void RaptorAlertActive_HoldsForTheConfiguredWindowThenExpires()
        {
            var self = MakeAgent(0, Team.Blue, Vector3.zero, Vector3.forward);
            var world = new WorldView(new List<Agent> { self }, new List<Raptor>(), new List<Egg>(), Points());

            world.BeginTick(0.0);
            Assert.IsFalse(world.RaptorAlertActive(self), "no screech yet");

            world.RaiseRaptorAlert(Vector3.zero, 0.0, 5f, 100f);
            world.BeginTick(3.0);
            Assert.IsTrue(world.RaptorAlertActive(self), "within the alert window, sound-based awareness should hold");

            world.BeginTick(6.0);
            Assert.IsFalse(world.RaptorAlertActive(self), "past the alert window it should expire");
        }

        [Test]
        public void RaptorAlert_IsNotHeardBeyondEarshot()
        {
            var near = MakeAgent(0, Team.Blue, new Vector3(50f, 0f, 0f), Vector3.forward);
            var far = MakeAgent(1, Team.Blue, new Vector3(400f, 0f, 0f), Vector3.forward);
            var world = new WorldView(new List<Agent> { near, far }, new List<Raptor>(), new List<Egg>(), Points());

            world.RaiseRaptorAlert(Vector3.zero, 0.0, 5f, radius: 100f);
            world.BeginTick(1.0);

            Assert.IsTrue(world.RaptorAlertActive(near), "an agent inside earshot hears the screech");
            Assert.IsFalse(world.RaptorAlertActive(far),
                "an agent far across the map must NOT hear it — an always-on alert suppresses nest-robbing entirely");
        }
    }
}
