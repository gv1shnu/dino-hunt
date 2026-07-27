using System;
using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>A named personality. The name is for the observer; only the weights affect the simulation.</summary>
    [Serializable]
    public sealed class RosterEntry
    {
        public string name = "agent";
        public Personality personality = new Personality();

        public RosterEntry() { }

        public RosterEntry(string n, float aggression, float greed, float caution, float teamplay, float patience)
        {
            name = n;
            personality = new Personality
            {
                aggression = aggression, greed = greed, caution = caution, teamplay = teamplay, patience = patience
            };
        }

        public RosterEntry Clone() => new RosterEntry { name = name, personality = personality.Clone() };
    }

    /// <summary>
    /// The cast. Each agent gets its own weight vector, which is the entire mechanism behind
    /// distinct behaviour (GDD §11.2) — identical logic, different weights.
    ///
    /// These archetypes are a starting distribution, not a claim. They exist to spread agents across
    /// the weight space so behaviour is visibly varied and so research question Q1 has something to
    /// cluster. Evolution (docs/RESEARCH.md) searches this same space and will happily find vectors
    /// no one would have written by hand — that is the point of keeping personality plain data.
    /// </summary>
    public static class Roster
    {
        /// <summary>Default cast, ordered so a team of any size still gets a spread of temperaments.</summary>
        public static RosterEntry[] Default() => new[]
        {
            //                        aggression greed caution teamplay patience
            new RosterEntry("Viktor",     0.85f, 0.30f,  0.20f,   0.35f,   0.20f), // brawler: starts fights, finishes few
            new RosterEntry("Mara",       0.25f, 0.90f,  0.55f,   0.30f,   0.35f), // thief: lives at the nest
            new RosterEntry("Sable",      0.45f, 0.45f,  0.50f,   0.90f,   0.45f), // escort: sticks to whoever carries
            new RosterEntry("Rook",       0.70f, 0.75f,  0.35f,   0.40f,   0.30f), // interceptor: hunts carriers
            new RosterEntry("Juno",       0.30f, 0.40f,  0.85f,   0.65f,   0.80f), // survivor: patient, hard to kill
            new RosterEntry("Cass",       0.60f, 0.60f,  0.45f,   0.55f,   0.40f), // all-rounder
            new RosterEntry("Bram",       0.90f, 0.20f,  0.15f,   0.20f,   0.15f), // reckless: the one who solos raptors
            new RosterEntry("Wren",       0.20f, 0.65f,  0.75f,   0.75f,   0.65f), // cautious support
        };

        /// <summary>Personality for one agent, deterministic by team slot. Wraps if the team is larger than the cast.</summary>
        public static RosterEntry For(RosterEntry[] roster, int indexInTeam)
        {
            if (roster == null || roster.Length == 0) return new RosterEntry("agent", 0.5f, 0.5f, 0.5f, 0.5f, 0.5f);
            return roster[indexInTeam % roster.Length];
        }
    }
}
