using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Real line-of-sight: a physics raycast blocked by cover colliders. Agent capsules have
    /// no colliders (visualization only), and bases/nest are flush to the ground, so only the
    /// tall cover cubes block sight — which is exactly what we want. Deterministic on a given
    /// machine for static geometry (same cross-machine caveat as NavMesh).
    /// </summary>
    public sealed class RaycastLineOfSight : ILineOfSight
    {
        private readonly int _mask;

        public RaycastLineOfSight(int layerMask = Physics.DefaultRaycastLayers)
        {
            _mask = layerMask;
        }

        public bool CanSee(Vector3 from, Vector3 to)
        {
            Vector3 d = to - from;
            float dist = d.magnitude;
            if (dist < 0.001f) return true;
            return !Physics.Raycast(from, d / dist, dist, _mask, QueryTriggerInteraction.Ignore);
        }
    }
}
