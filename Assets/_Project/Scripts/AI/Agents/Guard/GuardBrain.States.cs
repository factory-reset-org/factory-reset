using UnityEngine;
using ToyFactory.AI.Core.FSM;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Guard
{
    // The states. Each one only writes this tick's output (route, speed, look target,
    // action) and sets its own timers; no state changes state itself. Cover is chosen by
    // EvaluateCover before the machine runs, and the transition table reads its flags.
    public sealed partial class GuardBrain
    {
        abstract class GuardState : IState<GuardBrain>
        {
            readonly string _name;

            protected GuardState(string name) { _name = name; }

            public virtual void Enter(GuardBrain b) { b._debugState = _name; }
            public abstract void Tick(GuardBrain b);
            public virtual void Exit(GuardBrain b) { }

            public override string ToString() => _name;
        }

        /// <summary>
        /// No living player in range. Walks its patrol points, or holds position if it has
        /// none. Perceive has already given its cover back, so other agents can use it.
        /// </summary>
        sealed class PatrolState : GuardState
        {
            public PatrolState() : base("Patrol") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b._nextPatrolTime = float.NegativeInfinity;
                b.StopMoving();
            }

            public override void Tick(GuardBrain b)
            {
                if (b._patrolPoints.Count == 0)
                {
                    b._outSpeed = 0f;
                    return;
                }

                b._outSpeed = PatrolSpeed;
                bool walking = b._routeCells != null && !b.ArrivedAt(b._routeGoal);
                if (walking || b.Now < b._nextPatrolTime)
                    return;

                b._nextPatrolTime = b.Now + PatrolRetryInterval;
                b.MoveTo(b.ClampedCell(b._patrolPoints[b._patrolIndex]), BaseCostModel.Instance);
                b._patrolIndex = (b._patrolIndex + 1) % b._patrolPoints.Count;
            }
        }

        /// <summary>Walking to the reserved cover with tactical A*. Fires if the player is visible on the way.</summary>
        sealed class TakeCoverState : GuardState
        {
            public TakeCoverState() : base("TakeCover") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b.MoveTo(b._coverCell, b._tactical);
            }

            public override void Tick(GuardBrain b)
            {
                b._outSpeed = WalkSpeed;
                b._outLook = b.Player.Position;
                b.TryShoot();
            }
        }

        /// <summary>Settled behind cover, facing the player, waiting out the hold timer before the next peek.</summary>
        sealed class InCoverState : GuardState
        {
            public InCoverState() : base("InCover") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b._holdUntil = b.Now + b._holdTime;
                // Arriving counts from half a metre out, a whole cell: stopping there left it a cell short of
                // the cover, on the open side, in view 39% of the time in the Painting Room. It walks on to the
                // cover's centre, where the cover was judged, and only then stands still.
                if (b.SettledOn(b._coverCell))
                    b.StopMoving();
                else
                    b.MoveTo(b._coverCell, BaseCostModel.Instance);
            }

            public override void Tick(GuardBrain b)
            {
                b._outSpeed = WalkSpeed;
                b._outLook = b.Player.Position;
            }
        }

        /// <summary>Steps out to the peek cell and fires while the peek timer runs.</summary>
        sealed class PeekAndShootState : GuardState
        {
            public PeekAndShootState() : base("PeekAndShoot") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b._peekUntil = b.Now + PeekDuration;
                b._peekStartedAt = b.Now;
                b._hasPeekCell = b.TryFindPeekCell(out Vector2Int peekCell);
                b._peekCell = peekCell;
                if (b._hasPeekCell)
                    b.MoveTo(peekCell, BaseCostModel.Instance);
            }

            // It fires only once out on the peek cell: stepping out, its cannon is still behind the cover.
            public override void Tick(GuardBrain b)
            {
                b._outSpeed = WalkSpeed;
                b._outLook = b.Player.Position;
                if (b.AtPeekCell())
                    b.TryShoot();
            }
        }

        /// <summary>
        /// The cover choice changed. Lasts one tick: the table then picks TakeCover, Advance
        /// or Retreat from the fresh choice.
        /// </summary>
        sealed class RelocateState : GuardState
        {
            public RelocateState() : base("Relocate") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b._reconsider = false;
            }

            public override void Tick(GuardBrain b) { }
        }

        /// <summary>
        /// The player is low on battery or reloading. Pushes to the closer cover chosen for
        /// that tier, or straight at the player down to the ideal range if there is none.
        /// </summary>
        sealed class AdvanceState : GuardState
        {
            public AdvanceState() : base("Advance") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b._nextChaseTime = float.NegativeInfinity;
                if (b._hasCover)
                    b.MoveTo(b._coverCell, b._tactical);
            }

            public override void Tick(GuardBrain b)
            {
                b._outSpeed = AdvanceSpeed;
                b._outLook = b.Player.Position;
                b.TryShoot();
                if (b._hasCover)
                    return;

                // In range is enough, unless it came out to get a shot: then it walks on until it sees the player.
                if (FlatDistance(b._ctx.Position, b.Player.Position) <= b._idealRange && (!b._pushForShot || b._seesPlayer))
                {
                    if (b._routeCells != null)
                        b.StopMoving();
                    return;
                }
                if (b.Now >= b._nextChaseTime)
                {
                    b._nextChaseTime = b.Now + ChaseRepathInterval;
                    b.MoveTo(b.Player.Cell, b._tactical);
                }
            }
        }

        /// <summary>
        /// No valid cover. Falls back to the reachable hidden cell farthest from the player;
        /// if every nearby cell is exposed it fights from where it stands.
        /// </summary>
        sealed class RetreatState : GuardState
        {
            public RetreatState() : base("Retreat") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                if (b.TryFindRetreatCell(out Vector2Int cell))
                    b.MoveTo(cell, b._tactical);
                else
                    b.StopMoving();
            }

            public override void Tick(GuardBrain b)
            {
                b._outSpeed = WalkSpeed;
                b._outLook = b.Player.Position;
                b.TryShoot();
            }
        }

        /// <summary>The first tick after the reboot. Passes straight through to Relocate.</summary>
        sealed class StunnedState : GuardState
        {
            public StunnedState() : base("Stunned") { }

            public override void Enter(GuardBrain b)
            {
                base.Enter(b);
                b._stunPending = false;
            }

            public override void Tick(GuardBrain b) { b._outSpeed = 0f; }
        }
    }
}
