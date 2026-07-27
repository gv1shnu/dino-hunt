using UnityEngine;

namespace DinoHunt.Sim
{
    public enum EggState { InNest, Carried, Dropped, Delivered }

    /// <summary>
    /// A single egg. Finite (no respawn), starts in the nest. Possession is a liability — the
    /// carrier gets nothing for holding it; all reward is paid on delivery. An egg delivered by
    /// the team that took it from an enemy carrier is "stolen" (worth less); otherwise "fresh".
    /// </summary>
    public sealed class Egg
    {
        public readonly int Id;
        public EggState State;

        /// <summary>World position when InNest or Dropped. When Carried, read the carrier.</summary>
        public Vector3 Position;
        public Agent Carrier;

        /// <summary>True once taken from an enemy carrier: delivers for 2 instead of 3.</summary>
        public bool Stolen;

        /// <summary>Team of the last carrier when the egg was dropped (for steal detection on pickup).</summary>
        public Team? DroppedByTeam;

        public Team? DeliveredBy;

        public Egg(int id, Vector3 nestPos)
        {
            Id = id;
            State = EggState.InNest;
            Position = nestPos;
        }

        public string LogId => "e_" + Id.ToString("00");
    }
}
