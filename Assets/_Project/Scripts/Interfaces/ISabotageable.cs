namespace ToyFactory.Interfaces
{
    /// <summary>
    /// A gameplay object the Saboteur can act on (a door, a trap, a battery pickup). The
    /// Saboteur's brain only ever outputs an intent naming a target; the runtime looks up
    /// that target and calls Execute, so the brain never touches a GameObject directly.
    /// </summary>
    public interface ISabotageable
    {
        /// <summary>Carries out this object's one sabotage action (e.g. a door closes).</summary>
        void Execute();
    }
}
