using ToyFactory.Runtime.Agents;

namespace ToyFactory.Runtime.Debugging
{
    /// <summary>
    /// One layer of the debug overlay. Each agent's owner can add a layer that draws what their
    /// brain is thinking, in their own file, without touching the overlay:
    /// <code>
    /// public sealed class TrackerOverlayLayer : IAgentOverlayLayer
    /// {
    ///     public string Name => "Tracker";
    ///     public bool Handles(AgentController agent) => agent.Brain is TrackerBrain;
    ///     public void Draw(AgentController agent, OverlayCanvas canvas)
    ///     {
    ///         var brain = (TrackerBrain)agent.Brain;
    ///         canvas.DrawLabel(agent.transform.position + Vector3.up * 2f, brain.TopStateName, Color.cyan);
    ///     }
    ///
    ///     [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    ///     static void Register() => AgentDebugOverlay.Register(new TrackerOverlayLayer());
    /// }
    /// </code>
    /// </summary>
    public interface IAgentOverlayLayer
    {
        /// <summary>Shown in the overlay's header, so you can see which layers are active.</summary>
        string Name { get; }

        /// <summary>True for the agents this layer draws (for example, those with a given brain).</summary>
        bool Handles(AgentController agent);

        /// <summary>Draws this frame's view of <paramref name="agent"/>. Called only while the overlay is shown.</summary>
        void Draw(AgentController agent, OverlayCanvas canvas);
    }
}
