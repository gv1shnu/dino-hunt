using System;
using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Immutable description of a weapon. Two per agent: the finite-ammo rifle and the
    /// infinite low-damage sidearm floor (so an agent who empties the rifle still has a
    /// last-resort option — the GDD "empty magazine" fix). Data only; tunable in the Inspector.
    /// </summary>
    [Serializable]
    public sealed class WeaponSpec
    {
        public string name = "rifle";

        [Tooltip("Damage per hit (body). Headshots are deferred until an aim model exists.")]
        public float damage = 8f;

        [Tooltip("Max engagement distance in units.")]
        public float range = 45f;

        [Tooltip("Seconds between shots.")]
        public float fireInterval = 0.18f;

        [Tooltip("Rounds per magazine. 0 = no magazine (fires continuously, used by the infinite sidearm).")]
        public int magazineSize = 30;

        [Tooltip("Seconds to reload a magazine.")]
        public float reloadTime = 2f;

        [Tooltip("Rounds held in reserve beyond the first magazine. Rifle: 60 (+30 mag = 90 total).")]
        public int startingReserve = 60;

        [Tooltip("If true, never runs out of ammo (the sidearm floor).")]
        public bool infinite = false;
    }
}
