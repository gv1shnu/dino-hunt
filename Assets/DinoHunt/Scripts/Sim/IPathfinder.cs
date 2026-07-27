using System.Collections.Generic;
using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Turns a start/end pair into a list of corner points to walk through. The seam exists
    /// so the Simulation can be stepped headless in tests with a trivial straight-line
    /// pathfinder, while the real match uses NavMeshPathfinder (Unity NavMesh as an oracle).
    /// </summary>
    public interface IPathfinder
    {
        /// <summary>
        /// Fill <paramref name="cornersOut"/> with the path from start to end (cleared first).
        /// Returns false if no path exists; corners typically include the start point.
        /// </summary>
        bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> cornersOut);
    }
}
