using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Real pathfinder: uses Unity's baked NavMesh purely as an oracle (NavMesh.CalculatePath),
    /// never as a mover. Deterministic on a given machine for a given baked mesh. The returned
    /// corners are handed back to the Simulation, which does the actual fixed-step movement.
    /// </summary>
    public sealed class NavMeshPathfinder : IPathfinder
    {
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly float _sampleRadius;

        public NavMeshPathfinder(float sampleRadius = 4f)
        {
            _sampleRadius = sampleRadius;
        }

        public bool TryFindPath(Vector3 start, Vector3 end, List<Vector3> cornersOut)
        {
            cornersOut.Clear();

            // Snap both endpoints onto the navmesh (spawns/waypoints may sit slightly off it).
            if (!NavMesh.SamplePosition(start, out var s, _sampleRadius, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(end, out var e, _sampleRadius, NavMesh.AllAreas)) return false;

            if (!NavMesh.CalculatePath(s.position, e.position, NavMesh.AllAreas, _path)) return false;
            if (_path.status == NavMeshPathStatus.PathInvalid) return false;

            var corners = _path.corners;
            for (int i = 0; i < corners.Length; i++)
                cornersOut.Add(corners[i]);

            return cornersOut.Count > 0;
        }
    }
}
