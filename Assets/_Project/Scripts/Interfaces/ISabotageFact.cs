namespace ToyFactory.Interfaces
{
    /// <summary>
    /// What a Saboteur's brain may know about a sabotage target before it chooses it: which
    /// kind it is, the id it is named by, and whether the action would still do anything. A
    /// trap and a battery implement this next to <see cref="ISabotageable"/>; the runtime reads
    /// it (with the target's position) and writes the blackboard, so the brain never touches a
    /// GameObject.
    /// </summary>
    public interface ISabotageFact
    {
        /// <summary>The kind this target is registered under in <see cref="SabotageTargets"/>.</summary>
        SabotageKind FactKind { get; }

        /// <summary>The id the Saboteur names this target by. Unique across doors, traps and batteries.</summary>
        int SabotageId { get; }

        /// <summary>True while the action would do something: a trap that is not armed yet, a battery still there.</summary>
        bool Available { get; }
    }
}
