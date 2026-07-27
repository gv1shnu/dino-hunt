namespace DinoHunt.Sim
{
    /// <summary>
    /// Decides what an agent wants each tick, returning a full Decision (action + target +
    /// destination). UtilityBrain is the real implementation (scores actions against world state
    /// weighted by personality); StubBrain is a simple objective-only fallback used by tests.
    ///
    /// A brain must be pure/deterministic: read agent + world + personality, draw only from the
    /// agent's own RNG, and never touch anything visual or global.
    /// </summary>
    public interface IAgentBrain
    {
        Decision Decide(Agent agent, WorldView world);
    }
}
