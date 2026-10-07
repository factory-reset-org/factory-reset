using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.FSM;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.AI.Agents.Tracker
{
    /// <summary>
    /// The Tracker Toy: a wind-up scout that hunts by ear. Greedy best-first search for every
    /// route, and a hierarchical, data-driven FSM for behaviour.
    /// </summary>
    /// <remarks>
    /// <para><b>Hierarchy.</b> The top machine holds two parent states and two interrupts:
    /// <c>Calm</c> (Patrol, Investigate, Distracted), <c>Hunting</c> (Chase, WaitAtDoor, Search),
    /// <c>Rewind</c> and <c>Stunned</c>. Calm and Hunting are <see cref="CompositeState{T}"/>s
    /// with their own child transitions, so "the player is visible" is one Calm-to-Hunting
    /// transition instead of one per calm state, and an interrupt beats every child.
    /// Every transition is data with a numeric priority; <see cref="DescribeTransitions"/>
    /// prints the table.</para>
    /// <para><b>Senses.</b> Vision is pure C#: S2's <see cref="VisionQuery"/> cone with line of
    /// sight traced on the grid (<see cref="GridLineCheck"/>). Hearing reads the propagated
    /// noise the runtime puts in <see cref="SensorSnapshot"/> into a <see cref="NoiseMemory"/>.</para>
    /// <para><b>Closed doors.</b> The toy cannot open doors. When only a closed door stands
    /// between it and its goal it walks to the near side of the door (a second GBFS that treats
    /// closed doors as open shows which door, and the part of that route before it is walkable).
    /// Hunting, it then waits there in WaitAtDoor before searching its own side.</para>
    /// <para><b>Time.</b> Every timer uses <c>ctx.Time</c> (game time), so cutscenes and pause
    /// freeze them. Stuns are owned by the controller: see <see cref="OnStunned"/>.</para>
    /// </remarks>
    public sealed partial class TrackerBrain : IAgentBrain, IWindUpState
    {
        // Speeds in m/s, from the prototype's 3.5 m/s base speed.
        public const float PatrolSpeed = 1.9f;
        public const float InvestigateSpeed = 3.2f;
        public const float DistractedSpeed = 3.6f;
        public const float ChaseSpeed = 4.6f;
        public const float SearchSpeed = 3.3f;

        // Vision.
        public const float VisionRange = 12f;
        public const float VisionHalfAngle = 60f;
        public const float ProximityRange = 2.5f;   // heard/felt all round at close range
        public const float SightEndInset = 0.8f;    // see the GridSight remark

        // Timers, seconds of game time.
        public const float ChaseRepathInterval = 0.5f;
        public const float LoseSightDelay = 0.7f;
        public const float SearchDuration = 8f;
        public const float InvestigateLookTime = 2.4f;
        public const float CircleStepInterval = 1.2f;
        public const float DoorWaitTime = 2.5f;     // staring at the door the player escaped through

        // Distances, metres.
        public const float ArrivalRadius = 0.5f;
        public const float CircleRadius = 1.5f;
        public const int NearestCellRadius = 6;     // cells searched for a walkable stand-in

        readonly GridGraph _grid;
        readonly WorldBlackboard _blackboard;
        readonly GreedyBestFirstSearch _search;
        readonly WindUpEnergy _energy = new WindUpEnergy();
        readonly NoiseMemory _noises = new NoiseMemory();
        readonly Vector2Int[] _patrolCells;
        int _patrolIndex;

        readonly StateMachine<TrackerBrain> _machine;
        readonly CompositeState<TrackerBrain> _calm;
        readonly CompositeState<TrackerBrain> _hunting;
        readonly IState<TrackerBrain> _rewindFromCalm;
        readonly IState<TrackerBrain> _rewindFromHunting;
        readonly IState<TrackerBrain> _stunned;
        readonly List<string> _labels = new List<string>();

        // This tick's input.
        AgentContext _ctx;
        float Now => _ctx.Time;

        // Perception.
        bool _seesPlayer;
        bool _hasLastKnown;
        Vector3 _lastKnown;
        Vector3 _lastKnownVelocity;
        float _lastSeenTime = float.NegativeInfinity;

        // Flags the states raise for the transition table.
        bool _stunPending;
        bool _huntingWhenStunned;
        bool _inChase;
        bool _investigationDone;
        bool _distractionOver;
        bool _searchTimedOut;
        bool _doorWaitOver;

        // The closed door between the Tracker and its goal, found by MoveToOrDoor.
        bool _doorBlocked;
        Vector2Int _doorCell;
        Vector2Int _doorApproach;
        Vector3? _searchCentre;   // Search rings round this instead of the last known position, once
        Vector3 _activeSearchCentre;

        // Current route and this tick's output.
        List<Vector2Int> _routeCells;
        Vector2Int _routeGoal;
        bool _routeViaDoors;
        bool _replanRequested;
        List<Vector3> _outPath;
        float _outSpeed;
        Vector3? _outLook;
        AgentAction _outAction;
        string _debugState = "Patrol";
        AlertLevel _alert = AlertLevel.None;

        public TrackerBrain(GridGraph grid, WorldBlackboard blackboard, IReadOnlyList<Vector3> patrolPoints)
        {
            _grid = grid ?? throw new ArgumentNullException(nameof(grid));
            _blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            if (patrolPoints == null || patrolPoints.Count == 0)
                throw new ArgumentException("The Tracker needs at least one patrol point.", nameof(patrolPoints));

            _search = new GreedyBestFirstSearch(grid);
            _patrolCells = new Vector2Int[patrolPoints.Count];
            for (int i = 0; i < patrolPoints.Count; i++)
                _patrolCells[i] = grid.WorldToCell(patrolPoints[i]);

            // Leaf states.
            var patrol = new PatrolState();
            var investigate = new InvestigateState();
            var distracted = new DistractedState();
            var chase = new ChaseState();
            var search = new SearchState();
            var waitAtDoor = new WaitAtDoorState();

            // Calm children: a repeating lure beats a one-off noise.
            var calmRules = new List<Transition<TrackerBrain>>();
            Rule(calmRules, "Calm", null, distracted, 30, "a repeating source (toy, terminal) is the best noise",
                b => b.BestNoiseIsRepeating() && !(b.CalmChild is DistractedState));
            Rule(calmRules, "Calm", distracted, patrol, 25, "the lure has been silent for 1.5 s", b => b._distractionOver);
            Rule(calmRules, "Calm", patrol, investigate, 20, "a one-off noise is remembered", b => b.HasNoiseToFollow());
            Rule(calmRules, "Calm", investigate, patrol, 15, "arrived and looked around for 2.4 s", b => b._investigationDone);
            _calm = new CompositeState<TrackerBrain>("Calm", patrol, calmRules);

            // Hunting children.
            var huntRules = new List<Transition<TrackerBrain>>();
            Rule(huntRules, "Hunting", search, chase, 30, "sees the player", b => b._seesPlayer);
            Rule(huntRules, "Hunting", waitAtDoor, chase, 30, "sees the player", b => b._seesPlayer);
            Rule(huntRules, "Hunting", waitAtDoor, chase, 28, "the door opened", b => !b._grid.GetNode(b._doorCell).IsDoorClosed);
            Rule(huntRules, "Hunting", chase, waitAtDoor, 25, "a closed door stands between it and the player",
                b => b._doorBlocked);
            Rule(huntRules, "Hunting", waitAtDoor, search, 22, "waited 2.5 s at the shut door", b => b._doorWaitOver);
            Rule(huntRules, "Hunting", chase, search, 20, "lost sight for 0.7 s", b => b.LostSightFor(LoseSightDelay));
            _hunting = new CompositeState<TrackerBrain>("Hunting", chase, huntRules);

            // Interrupts. Two Rewind states so each knows where to return to.
            _rewindFromCalm = new RewindState("Rewind");
            _rewindFromHunting = new RewindState("Rewind");
            _stunned = new StunnedState();

            var top = new List<Transition<TrackerBrain>>();
            Rule(top, "Top", null, _stunned, 100, "stunned (first tick after the reboot)",
                b => b._stunPending && b._machine.Current != b._stunned);
            // A stun does not finish a rewind: go straight back to winding up.
            Rule(top, "Top", _stunned, _rewindFromHunting, 92, "still rewinding, was hunting",
                b => !b._stunPending && b._energy.IsRewinding && b._huntingWhenStunned);
            Rule(top, "Top", _stunned, _rewindFromCalm, 91, "still rewinding",
                b => !b._stunPending && b._energy.IsRewinding);
            Rule(top, "Top", _stunned, _hunting, 90, "was hunting when stunned: resume from the last known position",
                b => !b._stunPending && b._huntingWhenStunned && b._hasLastKnown && b.PlayerAvailable());
            Rule(top, "Top", _stunned, _calm, 89, "otherwise", b => !b._stunPending);
            Rule(top, "Top", _calm, _rewindFromCalm, 80, "wind-up energy reached 0", b => b._energy.IsRewinding);
            Rule(top, "Top", _hunting, _rewindFromHunting, 80, "wind-up energy reached 0", b => b._energy.IsRewinding);
            Rule(top, "Top", _rewindFromHunting, _hunting, 70, "energy full again", b => !b._energy.IsRewinding);
            Rule(top, "Top", _rewindFromCalm, _calm, 70, "energy full again", b => !b._energy.IsRewinding);
            Rule(top, "Top", _calm, _hunting, 50, "sees the player", b => b._seesPlayer);
            Rule(top, "Top", _hunting, _calm, 40, "search timed out after 8 s, or the player is gone",
                b => b._searchTimedOut || !b.PlayerAvailable());
            _machine = new StateMachine<TrackerBrain>(_calm, top);
        }

        // ---- IAgentBrain ---------------------------------------------------------------

        public AgentIntent Tick(in AgentContext ctx)
        {
            _ctx = ctx;
            _outPath = null;
            _outLook = null;
            _outAction = AgentAction.None;

            // The brain was not ticked while stunned but game time ran on: restart the energy
            // clock so the stun does not count as drain (a 7 s stun would cost 14, or 70).
            if (_stunPending)
                _energy.Resume(Now);
            _energy.Tick(Now, chasing: _inChase);

            Perceive();
            _machine.Tick(this);
            // Stunned is a pass-through: it is entered and left on the same first tick after
            // the reboot, so the agent plans a fresh route straight away.
            if (_machine.Current == _stunned)
                _machine.Tick(this);

            if (_replanRequested && _outPath == null && _routeCells != null)
            {
                if (_routeViaDoors)
                    MoveToOrDoor(_routeGoal);
                else
                    MoveTo(_routeGoal);
            }
            _replanRequested = false;

            return new AgentIntent
            {
                Path = _outPath,
                DesiredSpeed = _outSpeed,
                LookTarget = _outLook,
                Action = _outAction,
                ActionTargetId = 0,
                DebugState = _debugState,
                Alert = _alert
            };
        }

        /// <summary>
        /// Replans only if a changed cell lies on the remaining route, or is the closed door
        /// the route stops at (it may have opened).
        /// </summary>
        public void OnGraphChanged(IReadOnlyList<Vector2Int> changedCells)
        {
            if (_routeCells == null || changedCells == null)
                return;
            for (int i = 0; i < changedCells.Count; i++)
            {
                if (_routeCells.Contains(changedCells[i]) || (_doorBlocked && changedCells[i] == _doorCell))
                {
                    _replanRequested = true;
                    return;
                }
            }
        }

        /// <summary>
        /// The controller owns the stun: the body has stopped and this brain is not ticked
        /// until the reboot. Drop the route and remember whether it was hunting, so the first
        /// tick after the reboot can search from the last known position.
        /// </summary>
        public void OnStunned(float duration)
        {
            _stunPending = true;
            IState<TrackerBrain> current = _machine.Current;
            _huntingWhenStunned = current == _hunting || current == _rewindFromHunting;
            _routeCells = null;
        }

        /// <summary>
        /// Nothing to release: the Tracker holds no blackboard claims or cell reservations,
        /// and its noise memory and route are its own. It is never scrapped, only stunned.
        /// </summary>
        public void OnDestroyed() { }

        // ---- Read-only state for the body, debug overlay and tests ---------------------

        /// <summary>Leaf state name, e.g. "Chase"; also the intent's DebugState.</summary>
        public string StateName => _debugState;

        /// <summary>Top-level state: Calm, Hunting, Rewind or Stunned.</summary>
        public string TopStateName =>
            _machine.Current == _calm ? "Calm" :
            _machine.Current == _hunting ? "Hunting" :
            _machine.Current == _stunned ? "Stunned" : "Rewind";

        /// <summary>The "?"/"!" level this tick: Alert while chasing or waiting at a door, Suspicious while following a noise or searching.</summary>
        public AlertLevel Alert => _alert;

        /// <summary>True while the last route stops at a closed door instead of reaching its goal.</summary>
        public bool IsBlockedByDoor => _doorBlocked;

        /// <summary>The closed door the route stops at; meaningful while <see cref="IsBlockedByDoor"/>.</summary>
        public Vector2Int BlockingDoorCell => _doorCell;

        /// <summary>The cells of the last GBFS route still being followed, or null.</summary>
        public IReadOnlyList<Vector2Int> RouteCells => _routeCells;

        /// <summary>Whether the player has been seen at least once (the last known position, LKP).</summary>
        public bool HasLastKnownPosition => _hasLastKnown;

        /// <summary>Where the player was last seen.</summary>
        public Vector3 LastKnownPosition => _lastKnown;

        /// <summary>True in Search; <see cref="SearchCentre"/> is then the middle of its rings.</summary>
        public bool IsSearching => _debugState == "Search";

        /// <summary>The point Search rings round: the LKP, or the near side of a shut door.</summary>
        public Vector3 SearchCentre => _activeSearchCentre;

        /// <summary>Adds every noise still remembered at the last tick to <paramref name="into"/> (read-only, for the overlay).</summary>
        public void GetRememberedNoises(List<RememberedNoise> into) => _noises.CopyTo(Now, into);

        /// <summary>Wind-up energy 0..1, for the key-spin animation.</summary>
        public float Energy01 => _energy.Energy01;

        /// <summary>True while rewinding: the toy stands still and is vulnerable.</summary>
        public bool IsRewinding => _energy.IsRewinding;

        /// <summary>Hits count double while rewinding (applied by the runtime's damage code).</summary>
        public float DamageMultiplier => _energy.IsRewinding ? 2f : 1f;

        /// <summary>The full transition table, grouped by machine, highest priority first: for the debug overlay and the viva.</summary>
        public string DescribeTransitions()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Machine | Priority | From -> To | When");
            foreach (string label in _labels)
                sb.AppendLine(label);
            return sb.ToString();
        }

        // ---- Transition helpers ---------------------------------------------------------

        void Rule(List<Transition<TrackerBrain>> list, string machine, IState<TrackerBrain> from,
            IState<TrackerBrain> to, int priority, string when, Func<TrackerBrain, bool> condition)
        {
            list.Add(new Transition<TrackerBrain>(from, to, condition, priority));
            _labels.Add($"{machine} | {priority} | {Name(from)} -> {Name(to)} | {when}");
        }

        static string Name(IState<TrackerBrain> state) => state == null ? "any" : state.ToString();

        IState<TrackerBrain> CalmChild => _calm.CurrentChild;

        // The context's blackboard is the live one; the constructor's is the fallback for a
        // context built without one.
        PlayerSnapshot Player => (_ctx.World ?? _blackboard).Player;

        bool PlayerAvailable()
        {
            PlayerSnapshot player = Player;
            return player.IsKnown && player.IsAlive;
        }

        bool LostSightFor(float seconds) => !_seesPlayer && Now - _lastSeenTime >= seconds;

        bool HasNoiseToFollow() => _noises.TryGetBest(Now, _ctx.Position, out NoiseTarget best) && !best.IsRepeating;

        bool BestNoiseIsRepeating() => _noises.TryGetBest(Now, _ctx.Position, out NoiseTarget best) && best.IsRepeating;

        // ---- Perception -----------------------------------------------------------------

        void Perceive()
        {
            SensorSnapshot senses = _ctx.Senses;
            if (senses.HasNoise)
                _noises.Remember(senses.NoiseSourceId, senses.NoisePosition, senses.NoiseLevel, senses.NoiseTime);

            _seesPlayer = CanSeePlayer();
            if (_seesPlayer)
            {
                _lastKnown = Player.Position;
                _lastKnownVelocity = Player.Velocity;
                _hasLastKnown = true;
                _lastSeenTime = Now;
            }
        }

        bool CanSeePlayer()
        {
            if (!PlayerAvailable())
                return false;
            Vector3 eye = _ctx.Position;
            Vector3 target = Player.Position;
            if (!GridSight(eye, target))
                return false;
            Vector3 flat = target - eye;
            flat.y = 0f;
            if (flat.magnitude <= ProximityRange)
                return true;
            Vector3 forward = _ctx.Forward;
            forward.y = 0f;
            return VisionQuery.CanSee(eye, forward, new Vector3(target.x, eye.y, target.z),
                VisionRange, VisionHalfAngle, lineOfSightClear: true);
        }

        /// <remarks>
        /// Line of sight traced on the grid. Cells within the 0.55 m agent clearance of a wall
        /// are unwalkable even though nothing solid is there, so a player standing against a
        /// wall would always be "behind" it. The line therefore stops 0.8 m short of the target,
        /// past that clearance band; a real wall between them still blocks it.
        /// </remarks>
        bool GridSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance <= SightEndInset)
                return true;
            Vector3 end = from + delta * ((distance - SightEndInset) / distance);
            return GridLineCheck.IsWalkable(_grid, from, end);
        }

        // ---- Movement -------------------------------------------------------------------

        /// <summary>Plans a GBFS route to <paramref name="goal"/> (or the nearest walkable cell) and outputs it.</summary>
        bool MoveTo(Vector2Int goal)
        {
            _routeGoal = goal;
            _routeViaDoors = false;
            _doorBlocked = false;
            if (!TryWalkable(_ctx.Cell, out Vector2Int start) || !TryWalkable(goal, out Vector2Int end))
            {
                StopMoving();
                return false;
            }

            PathResult result = _search.FindPath(start, end, BaseCostModel.Instance);
            if (!result.Found)
            {
                StopMoving();
                return false;
            }

            Follow(result.Cells, result.Cells.Count);
            return true;
        }

        bool MoveTo(Vector3 worldGoal) => MoveTo(_grid.WorldToCell(worldGoal));

        /// <summary>
        /// Like <see cref="MoveTo(Vector2Int)"/>, but when the only thing in the way is a closed
        /// door, walks to the near side of that door and sets <c>_doorBlocked</c>. A second GBFS
        /// that treats closed doors as open finds the route; the part before its first closed
        /// door is a real, walkable route. Walls and boxes still block both searches.
        /// </summary>
        bool MoveToOrDoor(Vector2Int goal)
        {
            bool moving = MoveTo(goal);
            _routeViaDoors = true;   // replans after a graph change keep checking the door
            if (moving)
                return true;
            if (!TryWalkable(_ctx.Cell, out Vector2Int start) || !TryWalkable(goal, out Vector2Int end))
                return false;

            PathResult through = _search.FindPath(start, end, BaseCostModel.Instance, throughClosedDoors: true);
            if (!through.Found)
                return false;
            for (int i = 1; i < through.Cells.Count; i++)
            {
                if (!_grid.GetNode(through.Cells[i]).IsDoorClosed)
                    continue;
                _doorBlocked = true;
                _doorCell = through.Cells[i];
                _doorApproach = through.Cells[i - 1];
                Follow(through.Cells, i);
                return true;
            }
            return false;
        }

        bool MoveToOrDoor(Vector3 worldGoal) => MoveToOrDoor(_grid.WorldToCell(worldGoal));

        // Outputs the first <paramref name="count"/> cells of a planned route as this tick's path.
        void Follow(List<Vector2Int> cells, int count)
        {
            _routeCells = count == cells.Count ? cells : cells.GetRange(0, count);
            var world = new List<Vector3>(count);
            for (int i = 0; i < count; i++)
                world.Add(_grid.CellToWorld(_routeCells[i]));
            _outPath = world;
        }

        void StopMoving()
        {
            _routeCells = null;
            _outPath = new List<Vector3>();   // empty = stop where you are
        }

        bool TryWalkable(Vector2Int cell, out Vector2Int walkable) =>
            _grid.TryFindNearestTraversable(cell, NearestCellRadius, out walkable);

        /// <summary>Arrival by distance to the last waypoint, never by cell (the body stops short).</summary>
        bool Arrived()
        {
            if (_routeCells == null || _routeCells.Count == 0)
                return false;
            Vector3 end = _grid.CellToWorld(_routeCells[_routeCells.Count - 1]);
            float dx = end.x - _ctx.Position.x, dz = end.z - _ctx.Position.z;
            return dx * dx + dz * dz <= ArrivalRadius * ArrivalRadius;
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
