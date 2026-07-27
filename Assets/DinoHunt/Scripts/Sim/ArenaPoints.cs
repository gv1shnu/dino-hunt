using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// The arena's key world positions as plain data, handed to the Simulation so it never
    /// has to know about the ArenaBuilder or anything visual. These are the waypoints the
    /// (stub) AI reasons about: the nest and each team's base.
    /// </summary>
    public sealed class ArenaPoints
    {
        public Vector3 Nest;
        public Vector3 BlueBase;
        public Vector3 RedBase;

        /// <summary>Radius of the nest (eggs spawn within it; agents grab when inside grab range of the center).</summary>
        public float NestRadius = 8f;

        /// <summary>Radius of each base's delivery zone. Stand inside your own for the delivery time to bank an egg.</summary>
        public float DeliveryRadius = 12f;

        /// <summary>Lateral spacing between teammates spawned at the same base.</summary>
        public float SpawnSpacing = 3f;
    }
}
