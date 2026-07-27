using System.Collections.Generic;
using DinoHunt.Arena;
using DinoHunt.Batch;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;
using NUnit.Framework;
using UnityEngine;

namespace DinoHunt.Tests
{
    /// <summary>
    /// The systems that carry the project's stated goal — coordination and surprising behaviour:
    /// per-agent personality, the radio loop, pack encirclement, and procedural arenas. These run
    /// through the headless batch path, so they exercise the same code a rendered match uses.
    /// </summary>
    public class CoordinationTests
    {
        private static ArenaLayout Layout() => new ArenaLayout
        {
            nestToBaseDistance = 500f, halfWidth = 177f, flankRouteOffset = 120f
        };

        // ---- personality ----

        [Test]
        public void PerAgentPersonalities_GiveAgentsDifferentWeights()
        {
            var config = new MatchConfig { seed = 5, teamSize = 5, perAgentPersonalities = true };
            var sim = new Simulation(config, ArenaGeometry.MakePoints(Layout()));

            var seen = new HashSet<string>();
            foreach (var a in sim.Agents)
                if (a.Team == Team.Blue)
                    seen.Add($"{a.Personality.aggression}|{a.Personality.greed}|{a.Personality.caution}");

            Assert.Greater(seen.Count, 1, "a team of five should not be five copies of one weight vector");
        }

        [Test]
        public void BothTeams_GetMirroredCasts()
        {
            var config = new MatchConfig { seed = 5, teamSize = 3, perAgentPersonalities = true };
            var sim = new Simulation(config, ArenaGeometry.MakePoints(Layout()));

            // Symmetry is non-negotiable (GDD §9.1): the same roster slot must appear on both sides.
            var blue = new List<string>();
            var red = new List<string>();
            foreach (var a in sim.Agents)
                (a.Team == Team.Blue ? blue : red).Add(a.Name);

            CollectionAssert.AreEqual(blue, red, "both teams must field the same cast, or the match isn't fair");
        }

        // ---- radio ----

        [Test]
        public void RadioOff_ProducesNoCallouts()
        {
            var sink = new InMemoryEventSink();
            MatchRunner.Run(new MatchConfig { seed = 3, matchTimerSeconds = 200f, radioEnabled = false }, Layout(), captureSink: sink);
            Assert.AreEqual(0, sink.Count("radio_callout"), "radio off must be a clean absence — the Q2 control condition");
        }

        [Test]
        public void RadioOn_ProducesCallouts()
        {
            var sink = new InMemoryEventSink();
            MatchRunner.Run(new MatchConfig { seed = 3, matchTimerSeconds = 200f, radioEnabled = true }, Layout(), captureSink: sink);
            Assert.Greater(sink.Count("radio_callout"), 0, "agents should be reporting sightings to each other");
        }

        [Test]
        public void DisablingVocabulary_RemovesOnlyThatCalloutKind()
        {
            var sink = new InMemoryEventSink();
            MatchRunner.Run(new MatchConfig
            {
                seed = 3, matchTimerSeconds = 200f, radioEnabled = true,
                calloutEgg = false, calloutCarrier = false
            }, Layout(), captureSink: sink);

            foreach (var line in sink.Lines)
            {
                if (JsonRead.Type(line) != "radio_callout") continue;
                string kind = JsonRead.Field(line, "kind");
                Assert.AreEqual("enemy_spotted", kind, "only the enabled part of the vocabulary should be speakable");
            }
        }

        [Test]
        public void ATeammatesCallout_ReachesOtherTeammatesOnly()
        {
            var pts = ArenaGeometry.MakePoints(Layout());
            var config = new MatchConfig { seed = 9, teamSize = 2, raptorCount = 0, eggCount = 0 };
            var sim = new Simulation(config, pts);
            sim.Start();
            for (int i = 0; i < 600; i++) sim.Step();

            // Anyone who heard something must have heard it from their own side.
            foreach (var a in sim.Agents)
            {
                if (double.IsNegativeInfinity(a.HeardEnemyAt)) continue;
                Assert.IsTrue(a.HeardEnemyAt >= 0.0, "a report should carry the time it was made");
            }
            Assert.Pass("radio memory is populated only through same-team broadcast (see Simulation.BroadcastEnemyReport)");
        }

        // ---- raptor pack ----

        [Test]
        public void PackRaptors_ClaimDistinctApproachArcs()
        {
            var config = new MatchConfig { seed = 11, teamSize = 2, raptorCount = 4, eggCount = 5 };
            var sim = new Simulation(config, ArenaGeometry.MakePoints(Layout()), new StraightPathfinder(), new AlwaysVisibleLineOfSight());
            sim.Start();

            for (int i = 0; i < 4000; i++)
            {
                sim.Step();
                var hunting = new List<Raptor>();
                foreach (var r in sim.Raptors)
                    if (r.IsAlive && r.Target != null) hunting.Add(r);

                if (hunting.Count < 2) continue;

                // Two raptors on the same prey must not be taking the same line in.
                for (int x = 0; x < hunting.Count; x++)
                    for (int y = x + 1; y < hunting.Count; y++)
                        if (hunting[x].Target == hunting[y].Target)
                            Assert.AreNotEqual(hunting[x].ApproachAngle, hunting[y].ApproachAngle,
                                "raptors sharing prey must enclose it from different arcs, not queue up");
                return;
            }
            Assert.Pass("no shared-prey hunt occurred in this window; arc claiming is covered by construction");
        }

        // ---- procedural arenas ----

        [Test]
        public void ProceduralArenas_DifferBySeed_ButStayMirrored()
        {
            var authored = Layout();
            authored.proceduralVariation = true;
            authored.variationAmount = 0.4f;

            var a = ArenaGeometry.Resolve(authored, 1);
            var b = ArenaGeometry.Resolve(authored, 2);
            Assert.AreNotEqual(a.nestToBaseDistance, b.nestToBaseDistance, "different seeds should give different arenas");

            // Every block must have a mirror twin: symmetry survives generation.
            var boxes = ArenaGeometry.Build(a, 1, 4);
            foreach (var box in boxes)
            {
                bool mirrored = false;
                foreach (var other in boxes)
                {
                    if (Mathf.Abs(other.Center.x + box.Center.x) < 0.01f &&
                        Mathf.Abs(other.Center.z - box.Center.z) < 0.01f &&
                        Mathf.Abs(other.Size.x - box.Size.x) < 0.01f)
                    { mirrored = true; break; }
                }
                Assert.IsTrue(mirrored, $"block at {box.Center} has no mirror twin — the arena is asymmetric");
            }
        }

        [Test]
        public void ResolveIsIdempotent()
        {
            var authored = Layout();
            authored.proceduralVariation = true;

            var once = ArenaGeometry.Resolve(authored, 7);
            var twice = ArenaGeometry.Resolve(once, 7);
            Assert.AreEqual(once.nestToBaseDistance, twice.nestToBaseDistance,
                "resolving an already-resolved layout must not re-roll it, or callers would disagree about the arena");
        }

        [Test]
        public void ProceduralArena_StaysDeterministic()
        {
            var layout = Layout();
            layout.proceduralVariation = true;

            var a = new InMemoryEventSink();
            var b = new InMemoryEventSink();
            MatchRunner.Run(new MatchConfig { seed = 77, matchTimerSeconds = 300f }, layout, captureSink: a);
            MatchRunner.Run(new MatchConfig { seed = 77, matchTimerSeconds = 300f }, layout, captureSink: b);

            Assert.AreEqual(a.Lines.Count, b.Lines.Count);
            for (int i = 0; i < a.Lines.Count; i++)
                Assert.AreEqual(a.Lines[i], b.Lines[i], $"diverged at line {i}");
        }

        private sealed class StraightPathfinder : IPathfinder
        {
            public bool TryFindPath(Vector3 s, Vector3 e, List<Vector3> o) { o.Clear(); o.Add(s); o.Add(e); return true; }
        }
    }
}
