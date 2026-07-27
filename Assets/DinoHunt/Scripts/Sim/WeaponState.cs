using System;

namespace DinoHunt.Sim
{
    /// <summary>
    /// Per-agent runtime weapon state and firing/reload logic. Deterministic: driven purely
    /// by the fixed timestep, no RNG (perfect aim). The agent fires the rifle until its finite
    /// pool is exhausted, then falls back to the sidearm permanently.
    ///
    /// Both weapons carry magazines and reload. The difference is the reserve: the rifle draws
    /// from a finite pool and can be spent for good, while the sidearm reloads forever — it is the
    /// floor that guarantees an agent is never left with no options at all.
    /// </summary>
    public sealed class WeaponState
    {
        private readonly WeaponSpec _rifle;
        private readonly WeaponSpec _sidearm;

        public int RifleMag;
        public int RifleReserve;
        public int SidearmMag;
        public float Cooldown;      // time until the next shot is allowed
        public float ReloadTimer;   // > 0 while reloading
        public bool IsReloading;

        /// <summary>True when the in-progress reload is refilling the sidearm rather than the rifle.</summary>
        private bool _reloadingSidearm;

        public WeaponState(WeaponSpec rifle, WeaponSpec sidearm)
        {
            _rifle = rifle;
            _sidearm = sidearm;
            RifleMag = rifle.magazineSize;
            RifleReserve = rifle.startingReserve;
            SidearmMag = sidearm.magazineSize;
        }

        /// <summary>Rifle has nothing left to fire and isn't mid-reload.</summary>
        public bool RifleDepleted => RifleMag <= 0 && RifleReserve <= 0 && !(IsReloading && !_reloadingSidearm);

        /// <summary>Currently selected weapon: rifle until fully depleted, then the sidearm.</summary>
        public WeaponSpec Active => RifleDepleted ? _sidearm : _rifle;

        public bool ReadyToFire => !IsReloading && Cooldown <= 0f;

        public int RifleAmmoTotal => RifleMag + RifleReserve;

        /// <summary>Rounds left in the current magazine of whichever weapon is active.</summary>
        public int ActiveMag => RifleDepleted ? SidearmMag : RifleMag;

        /// <summary>Advance timers by one fixed step.</summary>
        public void Tick(float dt)
        {
            if (IsReloading)
            {
                ReloadTimer -= dt;
                if (ReloadTimer <= 0f)
                {
                    if (_reloadingSidearm)
                    {
                        SidearmMag = _sidearm.magazineSize; // infinite reserve: always a full magazine
                    }
                    else
                    {
                        int load = Math.Min(_rifle.magazineSize, RifleReserve);
                        RifleMag = load;
                        RifleReserve -= load;
                    }
                    IsReloading = false;
                    Cooldown = 0f;
                }
            }
            else if (Cooldown > 0f)
            {
                Cooldown -= dt;
            }
        }

        /// <summary>
        /// Attempt to fire. Returns the weapon spec used (for damage/logging), or null if the
        /// shot couldn't happen this tick (cooling down, or a reload had to start instead).
        /// </summary>
        public WeaponSpec Fire()
        {
            if (!ReadyToFire) return null;

            if (!RifleDepleted)
            {
                if (RifleMag <= 0)
                {
                    // Empty magazine but reserve remains: reload instead of firing.
                    if (RifleReserve > 0) StartRifleReload();
                    return null;
                }

                RifleMag--;
                Cooldown = _rifle.fireInterval;
                if (RifleMag <= 0 && RifleReserve > 0) StartRifleReload();
                return _rifle;
            }

            // Sidearm: magazine-fed but never runs dry, so an agent always has something.
            if (_sidearm.magazineSize > 0)
            {
                if (SidearmMag <= 0)
                {
                    StartSidearmReload();
                    return null;
                }

                SidearmMag--;
                Cooldown = _sidearm.fireInterval;
                if (SidearmMag <= 0) StartSidearmReload();
                return _sidearm;
            }

            Cooldown = _sidearm.fireInterval;
            return _sidearm;
        }

        public void StartReload() => StartRifleReload();

        private void StartRifleReload()
        {
            if (IsReloading || RifleReserve <= 0) return;
            IsReloading = true;
            _reloadingSidearm = false;
            ReloadTimer = _rifle.reloadTime;
        }

        private void StartSidearmReload()
        {
            if (IsReloading || _sidearm.reloadTime <= 0f) return;
            IsReloading = true;
            _reloadingSidearm = true;
            ReloadTimer = _sidearm.reloadTime;
        }
    }
}
