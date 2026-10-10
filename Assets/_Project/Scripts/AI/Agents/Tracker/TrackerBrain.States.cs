using System.Collections.Generic;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.FSM;

namespace ToyFactory.AI.Agents.Tracker
{
    // The leaf and interrupt states. Each one only writes this tick's output (route, speed,
    // look target, action) and raises the flags the transition table reads; no state
    // changes state itself.
    public sealed partial class TrackerBrain
    {
        abstract class TrackerState : IState<TrackerBrain>
        {
            readonly string _name;
            readonly AlertLevel _alert;

            protected TrackerState(string name, AlertLevel alert)
            {
                _name = name;
                _alert = alert;
            }

            public virtual void Enter(TrackerBrain b)
            {
                b._debugState = _name;
                b._alert = _alert;
            }
            public abstract void Tick(TrackerBrain b);
            public virtual void Exit(TrackerBrain b) { }

            public override string ToString() => _name;
        }

        /// <summary>Walks the patrol loop with GBFS, one point at a time.</summary>
        sealed class PatrolState : TrackerState
        {
            public PatrolState() : base("Patrol", AlertLevel.None) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._routeCells = null;   // whatever the last state was doing, head back to the loop
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = PatrolSpeed;
                int count = b._patrolCells.Length;
                bool arrived = b.Arrived();
                if (b._routeCells != null && !arrived)
                    return;
                if (arrived)
                {
                    if (count == 1)
                        return;   // a one-point "loop": stay put
                    b._patrolIndex = (b._patrolIndex + 1) % count;
                }
                if (!b.MoveTo(b._patrolCells[b._patrolIndex]))
                    b._patrolIndex = (b._patrolIndex + 1) % count;   // unreachable: try the next one
            }
        }

        /// <summary>
        /// Goes to the best one-off noise, looks around for 2.4 s, then marks it handled. A noise
        /// heard through a closed door is checked from the near side of that door.
        /// </summary>
        sealed class InvestigateState : TrackerState
        {
            int _sourceId;
            Vector3 _target;
            float _arrivedAt;

            public InvestigateState() : base("Investigate", AlertLevel.Suspicious) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._investigationDone = false;
                _arrivedAt = float.NegativeInfinity;
                b._noises.TryGetBest(b.Now, b._ctx.Position, out NoiseTarget best);
                GoTo(b, best);
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = InvestigateSpeed;

                // A louder (or newer) noise elsewhere takes over before arrival.
                if (float.IsNegativeInfinity(_arrivedAt) &&
                    b._noises.TryGetBest(b.Now, b._ctx.Position, out NoiseTarget best) && !best.IsRepeating &&
                    (best.SourceId != _sourceId || FlatDistance(best.Position, _target) > CircleRadius))
                    GoTo(b, best);

                if (float.IsNegativeInfinity(_arrivedAt))
                {
                    if (b._routeCells != null && !b.Arrived())
                    {
                        b._outLook = _target;
                        return;
                    }
                    _arrivedAt = b.Now;   // arrived, or the spot is unreachable: look from here
                    b.StopMoving();
                }

                // Sweep the head round at 150 deg/s while looking.
                float angle = (b.Now - _arrivedAt) * 150f * Mathf.Deg2Rad;
                b._outLook = b._ctx.Position + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * 2f;
                if (b.Now - _arrivedAt >= InvestigateLookTime)
                {
                    b._noises.MarkHandled(_sourceId);
                    b._investigationDone = true;
                }
            }

            public override void Exit(TrackerBrain b) { b._investigationDone = false; }

            void GoTo(TrackerBrain b, NoiseTarget noise)
            {
                _sourceId = noise.SourceId;
                _target = noise.Position;
                b.MoveToOrDoor(_target);
            }
        }

        /// <summary>
        /// Follows a repeating source (a thrown toy walks) and watches it, as in the prototype:
        /// walks towards it, replanning every 0.5 s, stops 1.3 m away facing it, and sets off
        /// again only once it is 2 m away, so it neither turns its back on the toy nor stops and
        /// starts at one distance. Lasts until the source has been silent for 1.5 s.
        /// </summary>
        sealed class DistractedState : TrackerState
        {
            int _sourceId;
            bool _following;
            float _nextRepathAt;

            public DistractedState() : base("Distracted", AlertLevel.Suspicious) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._distractionOver = false;
                b._noises.TryGetBestRepeating(b.Now, b._ctx.Position, out NoiseTarget best);
                _sourceId = best.SourceId;
                _following = true;
                _nextRepathAt = b.Now;
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = DistractedSpeed;
                if (!b._noises.IsStillRepeating(_sourceId, b.Now))
                    b._distractionOver = true;
                if (!b._noises.TryGetPosition(_sourceId, out Vector3 source))
                    return;

                b._outLook = source;
                float distance = FlatDistance(b._ctx.Position, source);
                if (_following && distance <= WatchDistance)
                {
                    _following = false;
                    b.StopMoving();   // stands still, and the body turns to the look target
                    return;
                }
                if (!_following && distance > FollowAgainDistance)
                {
                    _following = true;
                    _nextRepathAt = b.Now;
                }
                if (!_following || b.Now < _nextRepathAt)
                    return;

                _nextRepathAt = b.Now + DistractedRepathInterval;
                // The toy is on a cell it has already reached (it stops short of a toy on a prop
                // or against a wall): stay put rather than send a fresh one-cell route.
                if (b._routeCells != null && b._grid.WorldToCell(source) == b._routeGoal && b.Arrived())
                    return;
                b.MoveTo(source);
            }

            public override void Exit(TrackerBrain b)
            {
                // The lure has gone quiet: don't then walk over to investigate it.
                b._noises.MarkHandled(_sourceId);
                b._distractionOver = false;
            }
        }

        /// <summary>
        /// Runs at the player, replanning every 0.5 s to where it sees them (or last saw them).
        /// If a closed door is in the way it runs to the door, and the table moves it to WaitAtDoor.
        /// </summary>
        sealed class ChaseState : TrackerState
        {
            float _nextRepathAt;

            public ChaseState() : base("Chase", AlertLevel.Alert) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._inChase = true;
                b._doorBlocked = false;
                b._searchCentre = null;
                _nextRepathAt = b.Now;
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = ChaseSpeed;
                Vector3 target = b._seesPlayer ? b.Player.Position : b._lastKnown;
                b._outLook = target;
                if (b.Now < _nextRepathAt)
                    return;
                b.MoveToOrDoor(target);
                _nextRepathAt = b.Now + ChaseRepathInterval;
            }

            public override void Exit(TrackerBrain b) { b._inChase = false; }
        }

        /// <summary>
        /// Sweeps rings round the last known position (1.5, 3 and 4.5 m, 8 points each, starting
        /// in the direction the player was heading) for 8 s, then gives up. After waiting at a shut
        /// door it sweeps round the near side of the door instead, the side it can reach.
        /// </summary>
        sealed class SearchState : TrackerState
        {
            const int PointsPerRing = 8;
            const int Rings = 3;
            const int MaxPlansPerTick = 3;   // bounds the GBFS calls when points are unreachable

            float _enteredAt;
            int _point;
            float _headingDeg;
            Vector3 _centre;

            public SearchState() : base("Search", AlertLevel.Suspicious) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._searchTimedOut = false;
                _enteredAt = b.Now;
                _point = -1;
                _centre = b._searchCentre ?? b._lastKnown;
                b._searchCentre = null;
                b._activeSearchCentre = _centre;
                Vector3 v = b._lastKnownVelocity;
                _headingDeg = v.x * v.x + v.z * v.z > 0.01f ? Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg : 0f;
                b.StopMoving();
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = SearchSpeed;
                if (b.Now - _enteredAt >= SearchDuration)
                {
                    b._searchTimedOut = true;
                    return;
                }
                if (b._routeCells != null && !b.Arrived())
                    return;

                for (int tries = 0; tries < MaxPlansPerTick; tries++)
                {
                    _point = (_point + 1) % (PointsPerRing * Rings);
                    if (b.MoveTo(PointAt(b, _point)))
                        return;
                }
            }

            public override void Exit(TrackerBrain b) { b._searchTimedOut = false; }

            Vector3 PointAt(TrackerBrain b, int index)
            {
                int ring = index / PointsPerRing;
                float radius = CircleRadius * (ring + 1);
                float angle = (_headingDeg + (index % PointsPerRing) * 45f) * Mathf.Deg2Rad;
                return _centre + new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * radius;
            }
        }

        /// <summary>
        /// The player got away through a door and shut it. Runs to the near side of the door,
        /// stares at it for 2.5 s (the toy has no hands to open it), then the table hands over to
        /// Search round this side. If the door opens, or the player shows up, it chases again.
        /// </summary>
        sealed class WaitAtDoorState : TrackerState
        {
            float _arrivedAt;

            public WaitAtDoorState() : base("WaitAtDoor", AlertLevel.Alert) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._doorWaitOver = false;
                _arrivedAt = float.NegativeInfinity;   // keeps the route Chase planned to the door
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = ChaseSpeed;
                b._outLook = b._grid.CellToWorld(b._doorCell);
                if (float.IsNegativeInfinity(_arrivedAt))
                {
                    if (b._routeCells != null && !b.Arrived())
                        return;
                    _arrivedAt = b.Now;
                    b.StopMoving();
                }
                if (b.Now - _arrivedAt >= DoorWaitTime)
                    b._doorWaitOver = true;
            }

            public override void Exit(TrackerBrain b)
            {
                if (b._doorWaitOver)
                    b._searchCentre = b._grid.CellToWorld(b._doorApproach);
                b._doorWaitOver = false;
            }
        }

        /// <summary>Energy ran out: stand still and wind back up for 3 s. Hits count double meanwhile.</summary>
        sealed class RewindState : TrackerState
        {
            public RewindState(string name) : base(name, AlertLevel.None) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b.StopMoving();
            }

            public override void Tick(TrackerBrain b)
            {
                b._outSpeed = 0f;
                b._outAction = AgentAction.Rewind;
            }
        }

        /// <summary>
        /// Pass-through state for the first tick after a stun's reboot. The controller held the
        /// body for the whole stun and did not tick this brain, so all that is left is to clear
        /// the stun and pick Calm or Hunting, which plan a fresh route on the same tick.
        /// </summary>
        sealed class StunnedState : TrackerState
        {
            public StunnedState() : base("Stunned", AlertLevel.None) { }

            public override void Enter(TrackerBrain b)
            {
                base.Enter(b);
                b._stunPending = false;
                b._routeCells = null;
            }

            public override void Tick(TrackerBrain b) { b._outSpeed = 0f; }

            public override void Exit(TrackerBrain b) { b._huntingWhenStunned = false; }
        }
    }
}
