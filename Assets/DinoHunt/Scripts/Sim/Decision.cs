using UnityEngine;

namespace DinoHunt.Sim
{
    public enum ActionType { Hold, GrabEgg, DeliverEgg, Engage, Retreat, FleeRaptor, Regroup, HuntRaptor, Escort, Intercept, Investigate }

    /// <summary>
    /// The output of a brain for one tick: what the agent wants to do, where, and who (Engage → an
    /// enemy agent, HuntRaptor → a raptor). The simulation executes it with existing primitives
    /// (move / shoot / grab / deliver).
    /// </summary>
    public struct Decision
    {
        public ActionType Type;
        public Agent TargetEnemy;   // for Engage
        public Raptor TargetRaptor; // for HuntRaptor
        public Vector3 Destination; // for movement-based actions
        public string Intent;       // human-readable, drives the overlay + intent_change log
    }
}
