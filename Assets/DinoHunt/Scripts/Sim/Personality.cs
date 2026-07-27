using System;
using UnityEngine;

namespace DinoHunt.Sim
{
    /// <summary>
    /// An agent's personality: a plain weight vector, 0..1 per trait. This is the ONLY thing that
    /// differs between agents of identical logic. The simulation must never know or care whether
    /// these weights were hand-tuned, mutated, or produced by a learning algorithm — that keeps
    /// evolution a separate, swappable concern (GDD architecture principle).
    ///
    /// In M6 all agents share one default vector. Per-agent variation + stats is M8.
    /// </summary>
    [Serializable]
    public sealed class Personality
    {
        [Range(0f, 1f)] public float aggression = 0.5f;      // seek fights
        [Range(0f, 1f)] public float greed = 0.5f;           // chase eggs / steals
        [Range(0f, 1f)] public float caution = 0.5f;         // self-preservation: flee / retreat
        [Range(0f, 1f)] public float teamplay = 0.5f;        // regroup / stay near teammates
        [Range(0f, 1f)] public float patience = 0.5f;        // hold and wait vs act now

        public Personality Clone() => new Personality
        {
            aggression = aggression, greed = greed, caution = caution, teamplay = teamplay, patience = patience
        };
    }
}
