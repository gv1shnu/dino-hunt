using System;
using UnityEngine;

namespace DinoHunt.Arena
{
    /// <summary>
    /// Serializable description of the symmetric greybox arena. All distances in world units.
    /// Mirrored across the arena center (the nest), so only one side's parameters are specified
    /// and the builder reflects them.
    ///
    /// Nest-to-base distance is the primary tuning dial for the whole game (GDD §9.3). The field
    /// is deliberately large with dense cover so fights involve repositioning (break LOS behind a
    /// structure -> re-acquire) rather than two lines colliding head-on.
    /// </summary>
    [Serializable]
    public sealed class ArenaLayout
    {
        [Header("Overall")]
        [Tooltip("Distance from arena center (nest) to each base center along the X axis. GDD §9.3: the primary tuning dial. 350 chosen from a headless distance sweep (250..1600) verified at n=120: 350 is the only value healthy on ALL six MatchHealth checks (skew 20%, 156s, 4.83 eggs). 600 gives more raptor combat but ships a real ~40% blue team-skew (the §4 race-to-nest ordering artifact) and is gated on fixing that first.")]
        public float nestToBaseDistance = 350f;

        [Tooltip("Half-width of the playfield along Z (the route spread). Scaled with nestToBaseDistance (x0.21875 from the authored 1600-scale 567) so the arena keeps its shape, only its size.")]
        public float halfWidth = 124f;

        [Header("Nest (center danger zone)")]
        public float nestRadius = 16f;

        [Header("Bases / delivery zones")]
        public Vector2 baseSize = new Vector2(28f, 38f);

        [Header("Routes")]
        [Tooltip("Z offset of the north and south flank routes from the mid route. Scaled with nestToBaseDistance (x0.21875 from the authored 1600-scale 383) to preserve arena shape.")]
        public float flankRouteOffset = 84f;

        [Tooltip("Cover blocks per HALF of each flank route (mirrored to the other half). Heavy cover.")]
        public int flankCoverCount = 8;

        [Tooltip("Cover blocks per HALF of the mid route (mirrored). Sparse / exposed sniping lane.")]
        public int midCoverCount = 3;

        [Tooltip("Size range for cover cubes (x = min, y = max footprint). Kept larger than agents so cover reads as cover.")]
        public Vector2 coverSizeRange = new Vector2(16f, 30f);

        [Tooltip("Wall height for bases and cover.")]
        public float blockHeight = 10f;

        [Header("Scatter structures (light mini-open-world greybox)")]
        [Tooltip("Abandoned-building blocks per HALF of the field (mirrored to the other half for symmetry). Deterministic from the match seed. Dense = more repositioning in fights.")]
        public int structureCountPerHalf = 45;

        [Tooltip("Footprint size range for scattered structures (x = min, y = max).")]
        public Vector2 structureSizeRange = new Vector2(16f, 48f);

        [Tooltip("Height range for scattered structures (tall = buildings, short = rubble/walls).")]
        public Vector2 structureHeightRange = new Vector2(10f, 44f);

        [Header("Dynamic generation")]
        [Tooltip("Vary the arena per match seed instead of using the fixed values below as-is. Route shape, cover density and structure density are all re-rolled, so agents can never be tuned to one map. Mirroring is always preserved.")]
        public bool proceduralVariation;

        [Tooltip("How far generated values may stray from the authored ones, 0..1. 0.35 = ±35%. Mild values keep matches comparable; high values make each arena a genuinely different problem.")]
        [Range(0f, 1f)] public float variationAmount = 0.35f;

        [Header("Nest scatter (ruins ringing the danger zone)")]
        [Tooltip("Extra structures scattered in a band just outside the nest, per HALF (mirrored). Reuses structureSizeRange/structureHeightRange.")]
        public int nestScatterCount = 14;

        [Tooltip("Distance band for nest-scatter structures, measured out from the nest edge (x = min, y = max).")]
        public Vector2 nestScatterBand = new Vector2(8f, 70f);
    }
}
