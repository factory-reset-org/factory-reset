using System.Collections.Generic;
using ToyFactory.AI.Core;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// Supplies a Saboteur's sabotage and combat candidates (CloseDoor, ArmTrap, StealBattery,
    /// AttackPlayer, Flee) to its brain's decision loop. Each action implements this once its
    /// world facts exist; until then the brain only has Idle/Patrol.
    /// </summary>
    /// <remarks>
    /// A source reports what this one instance could do and how much it wants to, from its own
    /// considerations only. It never looks at the rest of the squad: the brain applies the
    /// squad layer afterwards (the "not claimed" veto and attack saturation, through
    /// <see cref="SquadCoordinator"/>), then selects and claims. Pure C#, so it can be faked in tests.
    /// </remarks>
    public interface ICandidateSource
    {
        /// <summary>
        /// Appends this instance's eligible candidates for the current decision. Eligibility
        /// (invalid target, player in the doorway, lockout) is decided here, before scoring: a
        /// pair that is not added is not a candidate. Each <see cref="ActionCandidate.BaseScore"/>
        /// is the compensated score in [0, 1] before the squad layer. Do not add Idle; the brain
        /// always adds it.
        /// </summary>
        void AddCandidates(in AgentContext ctx, SaboteurIdentity identity, List<ActionCandidate> candidates);
    }
}
