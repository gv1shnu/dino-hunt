using DinoHunt.Arena;
using DinoHunt.Core;
using DinoHunt.Logging;
using DinoHunt.Sim;

namespace DinoHunt.Batch
{
    /// <summary>
    /// Runs one match to completion with no rendering, no scene, and no Unity subsystems.
    ///
    /// This is the payoff of the sim/render separation rule: Simulation already had no visual
    /// dependencies, and its two couplings to Unity (NavMesh pathfinding, physics raycasts) were
    /// already injectable seams. Supplying headless implementations of those two is the entire
    /// difference between a played match and a batch match — the AI, combat, eggs, raptors,
    /// perception, and event log are all literally the same code.
    /// </summary>
    public static class MatchRunner
    {
        /// <summary>Safety cap so a stalemate can never hang a batch. 60Hz × 1800s = 30 minutes of sim.</summary>
        public const int DefaultMaxTicks = 108000;

        /// <summary>Run a match and return its derived statistics. Optionally also hand back the raw event lines.</summary>
        public static MatchResult Run(MatchConfig config, ArenaLayout layout, int maxTicks = DefaultMaxTicks,
                                      InMemoryEventSink captureSink = null, IAgentBrain brain = null)
        {
            // Resolve once: with procedural variation on, this is where the match's arena is decided.
            // Geometry, waypoints, and pathfinding must all agree, so they share one resolved layout.
            ArenaLayout resolved = ArenaGeometry.Resolve(layout, config.seed);

            var boxes = ArenaGeometry.Build(resolved, config.seed, buildingPaletteSize: 4);
            var points = ArenaGeometry.MakePoints(resolved);

            var los = new AnalyticLineOfSight(boxes);
            var pathfinder = new GridPathfinder(boxes, resolved);
            var sink = captureSink ?? new InMemoryEventSink();

            var sim = new Simulation(config, points, pathfinder, los, sink, null, brain);
            sim.Start();

            int ticks = 0;
            while (sim.IsRunning && ticks < maxTicks)
            {
                sim.Step();
                ticks++;
            }

            return MatchStats.Derive(sink.Lines, config.seed, ticks);
        }
    }
}
