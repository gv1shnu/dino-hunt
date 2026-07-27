using System.Collections.Generic;
using DinoHunt.Arena;
using DinoHunt.Sim;
using UnityEngine;

namespace DinoHunt.Batch
{
    /// <summary>
    /// Line of sight computed directly against the arena's boxes, with no Unity physics.
    /// The headless counterpart to RaycastLineOfSight.
    ///
    /// This is not an approximation: every sight-blocking object in the arena is an
    /// axis-aligned box (ArenaGeometry), and Unity's raycast against those same box colliders
    /// answers the same question. A segment/AABB slab test gives the identical result while
    /// running anywhere — no colliders, no scene, no editor.
    /// </summary>
    public sealed class AnalyticLineOfSight : ILineOfSight
    {
        private readonly Vector3[] _min;
        private readonly Vector3[] _max;
        private readonly int _count;

        public AnalyticLineOfSight(IReadOnlyList<ArenaBox> boxes)
        {
            _count = boxes.Count;
            _min = new Vector3[_count];
            _max = new Vector3[_count];
            for (int i = 0; i < _count; i++)
            {
                _min[i] = boxes[i].Min;
                _max[i] = boxes[i].Max;
            }
        }

        public bool CanSee(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;

            // Segment bounding box, for a cheap reject before the per-axis slab test.
            float sMinX = from.x < to.x ? from.x : to.x, sMaxX = from.x < to.x ? to.x : from.x;
            float sMinY = from.y < to.y ? from.y : to.y, sMaxY = from.y < to.y ? to.y : from.y;
            float sMinZ = from.z < to.z ? from.z : to.z, sMaxZ = from.z < to.z ? to.z : from.z;

            for (int i = 0; i < _count; i++)
            {
                Vector3 mn = _min[i], mx = _max[i];
                if (mx.x < sMinX || mn.x > sMaxX) continue;
                if (mx.y < sMinY || mn.y > sMaxY) continue;
                if (mx.z < sMinZ || mn.z > sMaxZ) continue;

                if (SegmentHitsBox(from, d, mn, mx)) return false;
            }
            return true;
        }

        /// <summary>Slab test over the segment from + t*d, t in [0,1].</summary>
        private static bool SegmentHitsBox(Vector3 from, Vector3 d, Vector3 mn, Vector3 mx)
        {
            float tMin = 0f, tMax = 1f;

            if (!Slab(from.x, d.x, mn.x, mx.x, ref tMin, ref tMax)) return false;
            if (!Slab(from.y, d.y, mn.y, mx.y, ref tMin, ref tMax)) return false;
            if (!Slab(from.z, d.z, mn.z, mx.z, ref tMin, ref tMax)) return false;

            return true;
        }

        private static bool Slab(float origin, float dir, float mn, float mx, ref float tMin, ref float tMax)
        {
            const float Epsilon = 1e-8f;

            if (dir > -Epsilon && dir < Epsilon)
                return origin >= mn && origin <= mx; // parallel: only intersects if already inside the slab

            float inv = 1f / dir;
            float t1 = (mn - origin) * inv;
            float t2 = (mx - origin) * inv;
            if (t1 > t2) { float tmp = t1; t1 = t2; t2 = tmp; }

            if (t1 > tMin) tMin = t1;
            if (t2 < tMax) tMax = t2;
            return tMin <= tMax;
        }
    }
}
