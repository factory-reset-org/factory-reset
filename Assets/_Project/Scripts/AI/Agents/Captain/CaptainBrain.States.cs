using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.FSM;

namespace ToyFactory.AI.Agents.Captain
{
    // The states. Each one only writes this tick's output (route, speed, look target,
    // action) and raises the flags the transition table reads; no state changes state
    // itself. The prediction and the intercept plan come from Decide (2 Hz).
    public sealed partial class CaptainBrain
    {
        abstract class CaptainState : IState<CaptainBrain>
        {
            readonly string _name;

            protected CaptainState(string name) { _name = name; }

            public virtual void Enter(CaptainBrain b) { b._debugState = _name; }
            public abstract void Tick(CaptainBrain b);
            public virtual void Exit(CaptainBrain b) { }

            public override string ToString() => _name;
        }

        /// <summary>Powered down in the Control Room until the Chapter 3 wake. Ignores everything.</summary>
        sealed class DormantState : CaptainState
        {
            public DormantState() : base("Dormant") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b.StopMoving();
                b._prediction = default;
            }

            public override void Tick(CaptainBrain b) { b._outSpeed = 0f; }
        }

        /// <summary>
        /// Not confident enough to commit. Watches the player from a distance and backs off
        /// if they come within 8 m, so it is not simply chasing while the prediction settles.
        /// </summary>
        sealed class ObserveState : CaptainState
        {
            public ObserveState() : base("Observe") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b.StopMoving();
            }

            public override void Tick(CaptainBrain b)
            {
                b._outSpeed = ObserveSpeed;
                if (!b.PlayerAvailable())
                    return;

                Vector3 player = b.Player.Position;
                b._outLook = player;

                // Back off at most once per decision, and only once the last step is done.
                bool stepping = b._routeCells != null && !b.ArrivedAt(b._routeGoal);
                if (!b._decidedThisTick || stepping || FlatDistance(b._ctx.Position, player) >= ObserveDistance)
                    return;

                Vector3 away = b._ctx.Position - player;
                away.y = 0f;
                if (away.sqrMagnitude < 1e-4f)
                    away = -b._ctx.Forward;
                Vector3 target = b._ctx.Position + away.normalized * RetreatStep;
                b.MoveTo(b.ClampedCell(target));
            }
        }

        /// <summary>
        /// Confident about g*: walks with A* to the planned cell, the first chokepoint (or
        /// route cell) it reaches 1 s before the player. Follows the plan as it is refreshed,
        /// and gives it up if the goal changes or the plan disappears.
        /// </summary>
        sealed class InterceptState : CaptainState
        {
            public InterceptState() : base("Intercept") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b._planInvalid = false;
                b._targetCell = b._plan.Cell;
                b._targetGoalId = b._planGoalId;
                b.MoveTo(b._targetCell);
            }

            public override void Tick(CaptainBrain b)
            {
                b._outSpeed = InterceptSpeed;
                if (!b._decidedThisTick)
                    return;

                if (!b._plan.HasPlan || b._planGoalId != b._targetGoalId)
                {
                    b._planInvalid = true;
                    return;
                }
                if (b._plan.Cell != b._targetCell)
                {
                    // The player moved on: the earliest cell that still beats them has changed.
                    b._targetCell = b._plan.Cell;
                    b.MoveTo(b._targetCell);
                }
            }
        }

        /// <summary>
        /// At the intercept cell: stands still facing the way the player will come. Holds its
        /// ground while the cell is still ahead of the player on their predicted route.
        /// </summary>
        sealed class AmbushState : CaptainState
        {
            public AmbushState() : base("Ambush") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b.StopMoving();
            }

            public override void Tick(CaptainBrain b)
            {
                b._outSpeed = 0f;
                b._outLook = b._grid.CellToWorld(b._approachCell);
                if (!b._decidedThisTick)
                    return;

                if (!b._plan.HasPlan || b._planGoalId != b._targetGoalId || !b.IsAheadOfPlayer(b._targetCell))
                    b._planInvalid = true;
            }
        }

        /// <summary>
        /// A fight: stands, faces the player and asks for a shot every 1.2 s while in contact.
        /// The body's weapon aims for 0.3 s (the telegraph) before each shot, so the player can
        /// react. Starts when the player is seen within 10 m and lasts at least 2 s; contact
        /// (line of sight within 14 m) keeps it going, so stepping in and out of 10 m does not
        /// switch it on and off.
        /// </summary>
        sealed class EngageState : CaptainState
        {
            float _lastShotAt;

            public EngageState() : base("Engage") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b.StopMoving();
                b._engagedAt = b.Now;
                _lastShotAt = float.NegativeInfinity;
            }

            public override void Tick(CaptainBrain b)
            {
                b._outSpeed = 0f;
                if (!b.PlayerAvailable())
                    return;

                // Out of contact it faces where it last saw the player, not where they are now.
                b._outLook = b._inContact ? b.Player.Position : b._lastContactPosition;
                if (b._inContact && b.Now - _lastShotAt >= FireInterval)
                {
                    b._outAction = AgentAction.Shoot;
                    _lastShotAt = b.Now;
                }
            }
        }

        /// <summary>
        /// Lost the player mid-fight: walks at intercept speed to where it last had contact,
        /// then stands there for 1 s looking the way the player was heading. Contact again
        /// returns it to Engage; otherwise, after that look or 5 s, it predicts again.
        /// </summary>
        sealed class PursueState : CaptainState
        {
            float _enteredAt;
            float _arrivedAt;
            bool _arrived;

            public PursueState() : base("Pursue") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b._pursueOver = false;
                _enteredAt = b.Now;
                _arrived = false;
                if (!b.MoveTo(b.ClampedCell(b._lastContactPosition)))
                    b._pursueOver = true;   // no route there: give up and predict again
            }

            public override void Tick(CaptainBrain b)
            {
                b._outSpeed = InterceptSpeed;
                if (b.Now - _enteredAt >= PursueTimeout)
                {
                    b._pursueOver = true;
                    return;
                }

                if (!_arrived && b.ArrivedAt(b._routeGoal))
                {
                    _arrived = true;
                    _arrivedAt = b.Now;
                    b.StopMoving();
                }
                if (!_arrived)
                    return;

                b._outSpeed = 0f;
                b._outLook = b._lastContactPosition + b._lastContactHeading * 3f;
                if (b.Now - _arrivedAt >= LookAroundTime)
                    b._pursueOver = true;
            }
        }

        /// <summary>
        /// The plan was invalidated: forget it and decide again now. Lasts one tick; the table
        /// then picks Intercept or Observe from the fresh decision.
        /// </summary>
        sealed class ReassessState : CaptainState
        {
            public ReassessState() : base("Reassess") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b._planInvalid = false;
                b._targetGoalId = NoGoal;
                if (!b._decidedThisTick)
                    b.Decide();
            }

            public override void Tick(CaptainBrain b) { }
        }

        /// <summary>The first tick after the reboot. Passes straight through to Reassess.</summary>
        sealed class StunnedState : CaptainState
        {
            public StunnedState() : base("Stunned") { }

            public override void Enter(CaptainBrain b)
            {
                base.Enter(b);
                b._stunPending = false;
            }

            public override void Tick(CaptainBrain b) { b._outSpeed = 0f; }
        }
    }
}
