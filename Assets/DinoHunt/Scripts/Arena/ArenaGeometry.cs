using System.Collections.Generic;
using DinoHunt.Core;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Arena
{
    public enum ArenaBoxKind { Cover, Structure, NestScatter }

    /// <summary>One solid, axis-aligned block of the arena. Pure data — no GameObject, no material.</summary>
    public struct ArenaBox
    {
        public Vector3 Center;
        public Vector3 Size;
        public ArenaBoxKind Kind;
        /// <summary>Index into the building palette, or -1 for the flat cover colour.</summary>
        public int ColorIndex;

        public Vector3 Min => Center - Size * 0.5f;
        public Vector3 Max => Center + Size * 0.5f;
    }

    /// <summary>
    /// The arena as pure geometry, generated deterministically from an ArenaLayout + match seed.
    ///
    /// This exists so the rendered game and the headless batch runner build *the same world*.
    /// ArenaBuilder turns these boxes into GameObjects with colliders; the batch runner feeds the
    /// identical list to an analytic line-of-sight test and a grid pathfinder. Same seed in, same
    /// obstacles out, no Unity subsystems required.
    ///
    /// Only solid, sight-blocking blocks are produced. The ground, nest disc, and base pads are
    /// flat to the floor and never block a sightline at eye height, so they are the view's problem.
    /// </summary>
    public static class ArenaGeometry
    {
        public static Vector3 NestCenter => Vector3.zero;
        public static Vector3 BlueBaseCenter(ArenaLayout l) => new Vector3(-l.nestToBaseDistance, 0f, 0f);
        public static Vector3 RedBaseCenter(ArenaLayout l) => new Vector3(l.nestToBaseDistance, 0f, 0f);

        public static float HalfX(ArenaLayout l) => l.nestToBaseDistance + l.baseSize.x * 0.5f;
        public static float HalfZ(ArenaLayout l) => Mathf.Max(l.halfWidth, l.flankRouteOffset + 6f);
        public static float Extent(ArenaLayout l) => Mathf.Max(HalfX(l), HalfZ(l));

        /// <summary>The waypoints the simulation reasons about. Shared so play and batch agree exactly.</summary>
        public static ArenaPoints MakePoints(ArenaLayout l) => new ArenaPoints
        {
            Nest = NestCenter,
            BlueBase = BlueBaseCenter(l),
            RedBase = RedBaseCenter(l),
            NestRadius = l.nestRadius,
            DeliveryRadius = Mathf.Max(l.baseSize.x, l.baseSize.y) * 0.5f + 2f
        };

        /// <summary>
        /// Resolve the layout actually used for a match. With procedural variation off this is the
        /// authored layout unchanged; with it on, the tunable dimensions are re-rolled from the match
        /// seed so every match is a different arena.
        ///
        /// What is NEVER varied: left/right mirroring, and the fact that there are three routes.
        /// Symmetry is non-negotiable for fair AI-vs-AI (GDD §9.1) — an asymmetric arena makes every
        /// result ambiguous, because you can no longer tell skill from map advantage.
        ///
        /// Why this matters beyond variety: agents tuned or evolved against a single fixed arena
        /// learn that arena, not the game. Varying it is what keeps a strategy general.
        /// </summary>
        public static ArenaLayout Resolve(ArenaLayout authored, ulong seed)
        {
            if (authored == null || !authored.proceduralVariation) return authored;

            var rng = new DeterministicRandom(seed ^ 0x5CA1EUL); // separate stream from block scatter
            float v = Mathf.Clamp01(authored.variationAmount);

            var g = new ArenaLayout
            {
                // Shape of the field.
                nestToBaseDistance = Mathf.Max(80f, Vary(authored.nestToBaseDistance, v, ref rng)),
                halfWidth = Mathf.Max(40f, Vary(authored.halfWidth, v, ref rng)),
                flankRouteOffset = Mathf.Max(25f, Vary(authored.flankRouteOffset, v, ref rng)),
                nestRadius = Mathf.Max(6f, Vary(authored.nestRadius, v, ref rng)),
                baseSize = authored.baseSize,

                // Density of things to hide behind. This is the dial that most changes how a match
                // plays: sparse arenas favour long sightlines and interception, dense ones favour
                // flanking and repositioning.
                flankCoverCount = Mathf.Max(1, Mathf.RoundToInt(Vary(authored.flankCoverCount, v, ref rng))),
                midCoverCount = Mathf.Max(0, Mathf.RoundToInt(Vary(authored.midCoverCount, v, ref rng))),
                coverSizeRange = authored.coverSizeRange,
                blockHeight = authored.blockHeight,
                structureCountPerHalf = Mathf.Max(0, Mathf.RoundToInt(Vary(authored.structureCountPerHalf, v, ref rng))),
                structureSizeRange = authored.structureSizeRange,
                structureHeightRange = authored.structureHeightRange,
                nestScatterCount = Mathf.Max(0, Mathf.RoundToInt(Vary(authored.nestScatterCount, v, ref rng))),
                nestScatterBand = authored.nestScatterBand,

                // Cleared so Resolve is idempotent: resolving an already-resolved layout returns it
                // unchanged rather than rolling the dice a second time. Callers can resolve freely.
                proceduralVariation = false,
                variationAmount = authored.variationAmount
            };

            // Keep the flanks inside the field however the dice fell.
            g.flankRouteOffset = Mathf.Min(g.flankRouteOffset, g.halfWidth * 0.9f);
            return g;
        }

        /// <summary>Scale a value by ±amount. Separate method rather than a local function so the sim compiles under mcs for headless runs.</summary>
        private static float Vary(float value, float amount, ref DeterministicRandom rng) => value * (1f + rng.NextFloat(-amount, amount));

        /// <summary>
        /// Generate every solid block for a given layout + seed. The RNG call order here is the
        /// contract — changing it changes every existing seed's arena, so append rather than reorder.
        /// </summary>
        public static List<ArenaBox> Build(ArenaLayout layout, ulong seed, int buildingPaletteSize)
        {
            ArenaLayout l = Resolve(layout, seed);
            var boxes = new List<ArenaBox>(256);
            var rng = new DeterministicRandom(seed ^ 0xA11EA5UL); // distinct stream for arena layout

            // Three routes per side: mid (short, exposed) and two flanks (long, covered).
            AddRouteCover(boxes, l, 0f, l.midCoverCount, ref rng);
            AddRouteCover(boxes, l, l.flankRouteOffset, l.flankCoverCount, ref rng);
            AddRouteCover(boxes, l, -l.flankRouteOffset, l.flankCoverCount, ref rng);

            AddScatter(boxes, l, ArenaBoxKind.Structure, l.structureCountPerHalf,
                       l.nestRadius + 12f, l.nestToBaseDistance - l.baseSize.x - 6f, HalfZ(l) - 4f,
                       buildingPaletteSize, ref rng);

            // A band of ruins hugging the nest, just outside its clear disc.
            AddScatter(boxes, l, ArenaBoxKind.NestScatter, l.nestScatterCount,
                       l.nestRadius + l.nestScatterBand.x, l.nestRadius + l.nestScatterBand.y,
                       l.nestRadius + l.nestScatterBand.y,
                       buildingPaletteSize, ref rng);

            return boxes;
        }

        // Cover is spaced evenly along a route and MIRRORED across the centre line. Map symmetry is
        // non-negotiable for fair AI-vs-AI (GDD §9.1) — a piece must never land on only one side.
        private static void AddRouteCover(List<ArenaBox> boxes, ArenaLayout l, float zOffset, int count, ref DeterministicRandom rng)
        {
            if (count <= 0) return;

            float innerX = l.nestRadius + 4f;
            float outerX = l.nestToBaseDistance - l.baseSize.x;

            for (int i = 0; i < count; i++)
            {
                float t = count == 1 ? 0.5f : i / (float)(count - 1);
                float x = Mathf.Lerp(innerX, outerX, t);
                float z = zOffset + rng.NextFloat(-3f, 3f);
                float size = rng.NextFloat(l.coverSizeRange.x, l.coverSizeRange.y);

                var scale = new Vector3(size, l.blockHeight, size);
                float y = l.blockHeight * 0.5f;
                boxes.Add(new ArenaBox { Center = new Vector3(x, y, z), Size = scale, Kind = ArenaBoxKind.Cover, ColorIndex = -1 });
                boxes.Add(new ArenaBox { Center = new Vector3(-x, y, z), Size = scale, Kind = ArenaBoxKind.Cover, ColorIndex = -1 });
            }
        }

        private static void AddScatter(List<ArenaBox> boxes, ArenaLayout l, ArenaBoxKind kind, int count,
                                       float innerX, float outerX, float zLimit, int paletteSize, ref DeterministicRandom rng)
        {
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                float x = rng.NextFloat(innerX, outerX);
                float z = rng.NextFloat(-zLimit, zLimit);
                float w = rng.NextFloat(l.structureSizeRange.x, l.structureSizeRange.y);
                float d = rng.NextFloat(l.structureSizeRange.x, l.structureSizeRange.y);
                float h = rng.NextFloat(l.structureHeightRange.x, l.structureHeightRange.y);
                int color = rng.NextInt(0, Mathf.Max(1, paletteSize));

                var scale = new Vector3(w, h, d);
                float y = h * 0.5f;
                boxes.Add(new ArenaBox { Center = new Vector3(x, y, z), Size = scale, Kind = kind, ColorIndex = color });
                boxes.Add(new ArenaBox { Center = new Vector3(-x, y, z), Size = scale, Kind = kind, ColorIndex = color });
            }
        }
    }
}
