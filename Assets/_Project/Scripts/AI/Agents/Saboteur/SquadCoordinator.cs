using System;
using System.Collections.Generic;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.AI.Agents.Saboteur
{
    /// <summary>
    /// The squad layer shared by the four Saboteur instances: target claims through S2's
    /// <see cref="TargetClaims"/>, the "not claimed" veto, attack saturation and the
    /// decision stagger. It adds no decision logic of its own; every instance still
    /// chooses with its own scores.
    /// </summary>
    /// <remarks>
    /// Claim ownership lives only in <see cref="TargetClaims"/>. The coordinator remembers
    /// each member's committed action so it can count attackers and check whether a
    /// holder still owns its target.
    /// </remarks>
    public sealed class SquadCoordinator
    {
        /// <summary>Seconds between one instance's decisions and the next one's (62.5 ms).</summary>
        public const float StaggerStep = SaboteurBrain.DecisionInterval / 4f;

        /// <summary>Other live attackers at which an AttackPlayer score is saturated.</summary>
        public const int SaturationAttackers = 2;

        /// <summary>AttackPlayer multiplier once <see cref="SaturationAttackers"/> others attack.</summary>
        public const float SaturatedAttackMultiplier = 0.45f;

        sealed class Member
        {
            public bool Destroyed;
            public bool HasAction;
            public ActionKey Action;
        }

        readonly TargetClaims _claims;
        readonly Dictionary<int, Member> _members = new Dictionary<int, Member>(4);

        /// <summary>Creates a coordinator over the squad's one shared claims instance.</summary>
        public SquadCoordinator(TargetClaims claims)
        {
            _claims = claims ?? throw new ArgumentNullException(nameof(claims));
        }

        /// <summary>
        /// Seconds after the squad's first decision at which <paramref name="letter"/> decides:
        /// A = 0, B = 62.5, C = 125 and D = 187.5 ms, so one frame never holds four decisions.
        /// </summary>
        public static float DecisionOffset(SaboteurLetter letter) => (int)letter * StaggerStep;

        /// <summary>True for the actions that claim their target: CloseDoor, ArmTrap and StealBattery.</summary>
        public static bool IsClaimable(SaboteurActionKind kind) =>
            kind == SaboteurActionKind.CloseDoor
            || kind == SaboteurActionKind.ArmTrap
            || kind == SaboteurActionKind.StealBattery;

        /// <summary>Adds an instance to the squad. Each agent id may join once.</summary>
        public void Register(SaboteurIdentity identity)
        {
            if (!identity.IsAssigned)
                throw new ArgumentException("The Saboteur needs an identity from the spawner.", nameof(identity));
            if (_members.ContainsKey(identity.AgentId))
                throw new ArgumentException($"{identity} is already registered.", nameof(identity));

            _members.Add(identity.AgentId, new Member());
        }

        /// <summary>True once <see cref="OnDestroyed"/> has run for the instance.</summary>
        public bool IsDestroyed(int agentId) => Get(agentId).Destroyed;

        /// <summary>
        /// The "not claimed" consideration: 0 when another instance holds the target of a
        /// claimable action, which vetoes the candidate; 1 when the target is free, held by
        /// this instance, or the action claims nothing.
        /// </summary>
        public float NotClaimedFactor(int agentId, ActionKey key)
        {
            Get(agentId);
            if (!IsClaimable(key.Kind))
                return 1f;

            int? holder = _claims.ClaimedBy(key.TargetId);
            return holder.HasValue && holder.Value != agentId ? 0f : 1f;
        }

        /// <summary>
        /// The AttackPlayer multiplier for this instance: 1.0 while 0-1 other live instances
        /// are attacking, <see cref="SaturatedAttackMultiplier"/> at 2 or more.
        /// </summary>
        public float AttackSaturation(int agentId)
        {
            Get(agentId);
            int others = 0;
            foreach (KeyValuePair<int, Member> entry in _members)
            {
                Member member = entry.Value;
                if (entry.Key != agentId && !member.Destroyed && member.HasAction
                    && member.Action.Kind == SaboteurActionKind.AttackPlayer)
                    others++;
            }

            return others >= SaturationAttackers ? SaturatedAttackMultiplier : 1f;
        }

        /// <summary>
        /// Records that the instance commits to <paramref name="key"/> with its final score.
        /// A claimable action claims its target through <see cref="TargetClaims.TryClaim"/>:
        /// a higher score takes the claim and an exact tie goes to the lower agent id.
        /// Recommitting to the same pair refreshes the claim's score. Returns false, with no
        /// plan recorded, when the claim is lost or the instance is destroyed.
        /// </summary>
        public bool TryCommit(int agentId, ActionKey key, float finalScore)
        {
            Member member = Get(agentId);
            if (float.IsNaN(finalScore) || finalScore < 0f || finalScore > 1f)
                throw new ArgumentOutOfRangeException(nameof(finalScore));
            if (member.Destroyed)
                return false;

            // One plan holds at most one claim, so a new plan first gives up the old one.
            if (!member.HasAction || !member.Action.Equals(key))
                _claims.Release(agentId);

            if (IsClaimable(key.Kind) && !_claims.TryClaim(key.TargetId, agentId, finalScore))
            {
                member.HasAction = false;
                return false;
            }

            member.Action = key;
            member.HasAction = true;
            return true;
        }

        /// <summary>
        /// Call on each of the instance's ticks before selecting. Returns true when another
        /// instance has outscored it for the target it committed to; the plan is then dropped
        /// here and the caller cancels its own selection.
        /// </summary>
        public bool CheckOutscored(int agentId)
        {
            Member member = Get(agentId);
            if (member.Destroyed || !member.HasAction || !IsClaimable(member.Action.Kind))
                return false;

            int? holder = _claims.ClaimedBy(member.Action.TargetId);
            if (holder.HasValue && holder.Value == agentId)
                return false;

            member.HasAction = false;
            return true;
        }

        /// <summary>
        /// Ends the instance's plan and releases its claims. Call on action end, on
        /// invalidation (door closed first, battery collected, trap armed) and on stun.
        /// </summary>
        public void EndPlan(int agentId)
        {
            Member member = Get(agentId);
            member.HasAction = false;
            _claims.Release(agentId);
        }

        /// <summary>
        /// Permanently removes the instance from the squad: every claim it holds is released
        /// and it no longer counts as an attacker. Later calls do nothing.
        /// </summary>
        public void OnDestroyed(int agentId)
        {
            Member member = Get(agentId);
            if (member.Destroyed)
                return;

            member.Destroyed = true;
            member.HasAction = false;
            _claims.Release(agentId);
        }

        Member Get(int agentId)
        {
            if (!_members.TryGetValue(agentId, out Member member))
                throw new ArgumentException($"Agent {agentId} is not in the squad.", nameof(agentId));
            return member;
        }
    }
}
