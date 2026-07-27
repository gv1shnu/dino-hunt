using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Whether one point can see another. An injectable seam (like IPathfinder) so combat is
    /// testable headless — the real match uses RaycastLineOfSight (blocked by cover), while
    /// tests supply a scripted implementation. Full FOV perception is a later milestone (M7);
    /// this is just the geometric blocking combat needs.
    /// </summary>
    public interface ILineOfSight
    {
        bool CanSee(Vector3 from, Vector3 to);
    }

    /// <summary>Default fallback: everything is visible. Used when no LOS is supplied.</summary>
    public sealed class AlwaysVisibleLineOfSight : ILineOfSight
    {
        public bool CanSee(Vector3 from, Vector3 to) => true;
    }
}
