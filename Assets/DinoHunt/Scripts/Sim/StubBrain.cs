namespace DinoHunt.Sim
{
    /// <summary>
    /// Simple objective-only brain: carry an egg home, otherwise go rob the nest. No combat, no
    /// threat reaction. Kept as the M2–M5 baseline and used by tests that want to isolate a
    /// mechanic (egg flow, raptor lethality) without utility-driven fleeing or fighting.
    /// The real decision-making is UtilityBrain.
    /// </summary>
    public sealed class StubBrain : IAgentBrain
    {
        public Decision Decide(Agent agent, WorldView world)
        {
            if (agent.CarriedEgg != null)
                return new Decision { Type = ActionType.DeliverEgg, Destination = world.OwnBase(agent), Intent = "carrying egg home" };

            return new Decision { Type = ActionType.GrabEgg, Destination = world.Points.Nest, Intent = "going for an egg" };
        }
    }
}
