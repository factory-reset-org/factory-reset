using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// ArmTrap and StealBattery: when each source offers a trap or battery and how it scores, and what
    /// the brain does once one is selected (walk there, act within reach, end on the runtime's answer).
    /// The level is an open 60 x 60 grid; the player walks east along row 30 to an objective at x = 55.
    /// </summary>
    public class SaboteurTrapBatteryTests
    {
        static readonly ActionKey Trap11 = new ActionKey(SaboteurActionKind.ArmTrap, 11);
        static readonly ActionKey Battery21 = new ActionKey(SaboteurActionKind.StealBattery, 21);

        // Exactly representable, like the other brain tests, so slots line up with whole frames.
        const float Frame = 1f / 64f;

        GridGraph _grid;
        AStarSearch _pathfinder;
        WorldBlackboard _world;
        TargetClaims _claims;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(60, 60, Vector3.zero);
            _pathfinder = new AStarSearch(_grid);
            _claims = new TargetClaims();
            _world = new WorldBlackboard();
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, new Vector2Int(55, 30), ObjectiveTargetKind.Task) });
            SetPlayer(new Vector2Int(10, 30));
        }

        void SetPlayer(Vector2Int cell, float sprint = 5f, float ammo = 1f, bool alive = true, bool known = true) =>
            _world.SetPlayer(new PlayerSnapshot(known, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                sprint, alive, 1f, ammo, false, 0f, 0f));

        static SabotageFact Trap(int id, int x, int y, bool available = true) =>
            new SabotageFact(id, new Vector2Int(x, y), SabotageFactKind.Trap, available);

        static SabotageFact Battery(int id, int x, int y, bool available = true) =>
            new SabotageFact(id, new Vector2Int(x, y), SabotageFactKind.Battery, available);

        static SaboteurIdentity Identity(SaboteurLetter letter) => new SaboteurIdentity((int)letter + 1, letter);

        AgentContext Ctx(Vector2Int cell, float time = 0f) =>
            new AgentContext(cell, _grid.CellToWorld(cell), Vector3.forward, time, _world, default);

        DetourCache Cache() => new DetourCache(_grid, _pathfinder);

        List<ActionCandidate> Trapped(Vector2Int sabo, params SabotageFact[] facts)
        {
            _world.SetSabotageFacts(facts);
            var list = new List<ActionCandidate>();
            new ArmTrapSource(_grid, Cache()).AddCandidates(Ctx(sabo), Identity(SaboteurLetter.A), list);
            return list;
        }

        List<ActionCandidate> Stolen(Vector2Int sabo, params SabotageFact[] facts)
        {
            _world.SetSabotageFacts(facts);
            var list = new List<ActionCandidate>();
            new StealBatterySource(_grid).AddCandidates(Ctx(sabo), Identity(SaboteurLetter.A), list);
            return list;
        }

        // ---- The helpers both sources use

        [Test]
        public void SnapKeepsAWalkableCellAndMovesABlockedOneToANeighbour()
        {
            Assert.IsTrue(SabotageTargetCells.TrySnap(_grid, new Vector2Int(20, 20), out Vector2Int same));
            Assert.AreEqual(new Vector2Int(20, 20), same);

            _grid.SetWalkable(new Vector2Int(20, 20), false);
            Assert.IsTrue(SabotageTargetCells.TrySnap(_grid, new Vector2Int(20, 20), out Vector2Int moved));
            Assert.AreNotEqual(new Vector2Int(20, 20), moved);
            Assert.LessOrEqual(Mathf.Abs(moved.x - 20), 1);
            Assert.LessOrEqual(Mathf.Abs(moved.y - 20), 1);
        }

        [Test]
        public void SnapSkipsACellOutsideTheGridAndOneWithNothingWalkableNearby()
        {
            Assert.IsFalse(SabotageTargetCells.TrySnap(_grid, new Vector2Int(-3, 5), out _));
            Assert.IsFalse(SabotageTargetCells.TrySnap(_grid, new Vector2Int(5, 99), out _));

            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 10; x <= 30; x++)
                for (int y = 10; y <= 30; y++)
                    batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            Assert.IsFalse(SabotageTargetCells.TrySnap(_grid, new Vector2Int(20, 20), out _), "Nothing walkable within the snap radius.");
        }

        [Test]
        public void MetresAlongCountsStraightAndDiagonalStepsUntilTheRouteComesWithinClearance()
        {
            var straight = new List<Vector2Int>();
            for (int x = 0; x <= 10; x++)
                straight.Add(new Vector2Int(x, 0));

            Assert.IsTrue(SabotageTargetCells.TryMetresAlong(straight, new Vector2Int(5, 1), 2, out float metres));
            Assert.AreEqual(3 * GridGraph.CellSize, metres, 1e-4f, "Cell 3 is the first within 2 cells of cell 5.");

            var diagonal = new List<Vector2Int> { new Vector2Int(0, 0), new Vector2Int(1, 1), new Vector2Int(2, 1) };
            Assert.IsTrue(SabotageTargetCells.TryMetresAlong(diagonal, new Vector2Int(2, 1), 0, out metres));
            Assert.AreEqual((Mathf.Sqrt(2f) + 1f) * GridGraph.CellSize, metres, 1e-3f);

            Assert.IsFalse(SabotageTargetCells.TryMetresAlong(straight, new Vector2Int(5, 9), 2, out _), "Never close enough.");
            Assert.IsFalse(SabotageTargetCells.TryMetresAlong(null, new Vector2Int(5, 0), 2, out _));
        }

        // ---- ArmTrap source

        [Test]
        public void TrapOnThePlayersRouteInTimeIsACandidate()
        {
            // The player is 2.8 s from it at 5 m/s; the Saboteur is 2 m (0.47 s) away.
            List<ActionCandidate> list = Trapped(new Vector2Int(40, 26), Trap(11, 40, 30));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(Trap11, list[0].Key);
            Assert.Greater(list[0].BaseScore, SaboteurBrain.IdleScore);
            Assert.LessOrEqual(list[0].BaseScore, 1f);
        }

        [Test]
        public void TrapOffTheRouteIsNotACandidate()
        {
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), Trap(11, 40, 45)));
        }

        [Test]
        public void ArmedOrMissingOrBatteryFactsAreNotTraps()
        {
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), Trap(11, 40, 30, available: false)), "Already armed.");
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26)), "No facts: nothing to arm.");
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), Battery(21, 40, 30)), "A battery is not a trap.");
        }

        [Test]
        public void TrapTheSlowPlayerReachesAfterTheHorizonIsNotACandidate()
        {
            // 2 m/s: the trap at 14 m along the route is 7 s away, then moved to the far end, 12 s away.
            SetPlayer(new Vector2Int(2, 30), sprint: 2f);
            Assert.AreEqual(1, Trapped(new Vector2Int(20, 26), Trap(11, 20, 30)).Count, "9 m at 2 m/s is 4.5 s: inside the horizon.");

            SetPlayer(new Vector2Int(2, 30), sprint: 2f);
            Assert.IsEmpty(Trapped(new Vector2Int(54, 26), Trap(11, 54, 30)), "26 m at 2 m/s is 13 s: beyond it.");
        }

        [Test]
        public void TrapThePlayerWillHavePassedBeforeTheSaboteurArrivesIsNotACandidate()
        {
            // The player is about 1 m (0.2 s) from it, the Saboteur 15 m (3.5 s) away.
            Assert.IsEmpty(Trapped(new Vector2Int(14, 15), Trap(11, 14, 30)));
        }

        [Test]
        public void NearerSaboteurAndSoonerPassScoreHigher()
        {
            float near = Trapped(new Vector2Int(40, 26), Trap(11, 40, 30))[0].BaseScore;
            float far = Trapped(new Vector2Int(40, 12), Trap(11, 40, 30))[0].BaseScore;
            Assert.Greater(near, far, "The nearer Saboteur wants it more.");

            SetPlayer(new Vector2Int(30, 30));
            float soon = Trapped(new Vector2Int(40, 26), Trap(11, 40, 30))[0].BaseScore;
            SetPlayer(new Vector2Int(10, 30));
            float later = Trapped(new Vector2Int(40, 26), Trap(11, 40, 30))[0].BaseScore;
            Assert.Greater(soon, later, "A player about to pass makes arming it more urgent.");
        }

        [Test]
        public void TrapInsideAPropIsMovedToAWalkableCellAndOneOffTheGridIsSkipped()
        {
            _grid.SetWalkable(new Vector2Int(40, 30), false);

            Assert.AreEqual(1, Trapped(new Vector2Int(40, 26), Trap(11, 40, 30)).Count, "Snapped onto a walkable neighbour.");
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), Trap(11, -4, 30)), "Outside the grid.");
        }

        [Test]
        public void NoCandidateWithoutAPlayerARouteOrAWorld()
        {
            SabotageFact[] facts = { Trap(11, 40, 30) };

            SetPlayer(new Vector2Int(10, 30), alive: false);
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), facts), "Dead player.");

            SetPlayer(new Vector2Int(10, 30), known: false);
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), facts), "Unknown player.");

            SetPlayer(new Vector2Int(10, 30));
            _world.SetObjectiveTargets(new ObjectiveTarget[0]);
            Assert.IsEmpty(Trapped(new Vector2Int(40, 26), facts), "No objective, so no predicted route.");

            var list = new List<ActionCandidate>();
            var noWorld = new AgentContext(new Vector2Int(40, 26), Vector3.zero, Vector3.forward, 0f, null, default);
            new ArmTrapSource(_grid, Cache()).AddCandidates(noWorld, Identity(SaboteurLetter.A), list);
            Assert.IsEmpty(list);
        }

        [Test]
        public void SourcesNeedTheirGridAndCache()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ArmTrapSource(null, Cache()));
            Assert.Throws<System.ArgumentNullException>(() => new ArmTrapSource(_grid, null));
            Assert.Throws<System.ArgumentNullException>(() => new StealBatterySource(null));
        }

        // ---- StealBattery source

        [Test]
        public void BatteryTheSaboteurReachesFirstIsACandidateWhenThePlayerIsShortOfAmmo()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f);

            List<ActionCandidate> list = Stolen(new Vector2Int(30, 36), Battery(21, 30, 40));

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual(Battery21, list[0].Key);
            Assert.Greater(list[0].BaseScore, SaboteurBrain.IdleScore);
        }

        [Test]
        public void PlayerWithAFullBatteryVetoesTheTheft()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 1f);

            Assert.IsEmpty(Stolen(new Vector2Int(30, 36), Battery(21, 30, 40)));
        }

        [Test]
        public void LessAmmunitionAndANearerBatteryScoreHigher()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f);
            float low = Stolen(new Vector2Int(30, 36), Battery(21, 30, 40))[0].BaseScore;
            SetPlayer(new Vector2Int(10, 30), ammo: 0.6f);
            float high = Stolen(new Vector2Int(30, 36), Battery(21, 30, 40))[0].BaseScore;
            Assert.Greater(low, high, "The emptier the player, the more it hurts.");

            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f);
            float near = Stolen(new Vector2Int(30, 38), Battery(21, 30, 40))[0].BaseScore;
            float far = Stolen(new Vector2Int(30, 20), Battery(21, 30, 40))[0].BaseScore;
            Assert.Greater(near, far);
        }

        [Test]
        public void BatteryThePlayerWouldReachFirstIsNotACandidate()
        {
            // The player is 1 m (0.2 s) from it; the Saboteur is 10 m away.
            SetPlayer(new Vector2Int(30, 38), ammo: 0.2f);

            Assert.IsEmpty(Stolen(new Vector2Int(30, 20), Battery(21, 30, 40)));
        }

        [Test]
        public void TakenOrMissingOrTrapFactsAreNotBatteries()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f);

            Assert.IsEmpty(Stolen(new Vector2Int(30, 36), Battery(21, 30, 40, available: false)));
            Assert.IsEmpty(Stolen(new Vector2Int(30, 36)));
            Assert.IsEmpty(Stolen(new Vector2Int(30, 36), Trap(11, 30, 40)));
        }

        [Test]
        public void BatteryOnAShelfIsSnappedAndOneOffTheGridIsSkipped()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f);
            _grid.SetWalkable(new Vector2Int(30, 40), false);

            Assert.AreEqual(1, Stolen(new Vector2Int(30, 36), Battery(21, 30, 40)).Count);
            Assert.IsEmpty(Stolen(new Vector2Int(30, 36), Battery(21, 70, 40)));
        }

        [Test]
        public void NoBatteryCandidateForADeadOrUnknownPlayerOrWithoutAWorld()
        {
            SabotageFact[] facts = { Battery(21, 30, 40) };

            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f, alive: false);
            Assert.IsEmpty(Stolen(new Vector2Int(30, 36), facts));

            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f, known: false);
            Assert.IsEmpty(Stolen(new Vector2Int(30, 36), facts));

            var list = new List<ActionCandidate>();
            var noWorld = new AgentContext(new Vector2Int(30, 36), Vector3.zero, Vector3.forward, 0f, null, default);
            new StealBatterySource(_grid).AddCandidates(noWorld, Identity(SaboteurLetter.A), list);
            Assert.IsEmpty(list);
        }

        [Test]
        public void DetourCacheKeepsThePlayersRouteFromTheLatestRefresh()
        {
            DetourCache cache = Cache();
            Assert.AreEqual(0, cache.PlayerRoute.Count, "Nothing before the first refresh.");

            cache.Refresh(_world, 0f);

            Assert.IsTrue(cache.HasObjective);
            Assert.AreEqual(new Vector2Int(10, 30), cache.PlayerRoute[0]);
            Assert.AreEqual(new Vector2Int(55, 30), cache.PlayerRoute[cache.PlayerRoute.Count - 1]);

            _world.SetObjectiveTargets(new ObjectiveTarget[0]);
            cache.Refresh(_world, 1f);
            Assert.IsFalse(cache.HasObjective);
            Assert.AreEqual(0, cache.PlayerRoute.Count, "No objective, no route.");
        }

        // ---- The brain

        SaboteurBrain Brain(SaboteurLetter letter, DetourCache cache = null, TargetClaims claims = null)
        {
            cache = cache ?? Cache();
            var source = new CompositeCandidateSource(new ArmTrapSource(_grid, cache), new StealBatterySource(_grid));
            return new SaboteurBrain(Identity(letter), _grid, _pathfinder, claims ?? _claims,
                new List<Vector3> { _grid.CellToWorld(new Vector2Int(5, 5)) }, null, 0, source, cache);
        }

        [Test]
        public void SelectedTrapIsWalkedToThenArmedAndAskedForUntilTheRuntimeAnswers()
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);

            AgentIntent walk = brain.Tick(Ctx(new Vector2Int(40, 20)));
            Assert.AreEqual(Trap11, brain.CurrentAction);
            Assert.AreEqual("ArmTrap", walk.DebugState);
            Assert.AreEqual(AgentAction.None, walk.Action, "Not in reach yet.");
            Assert.IsNotNull(walk.Path);
            Assert.Greater(walk.Path.Count, 0);
            Assert.Less(Vector3.Distance(walk.Path[walk.Path.Count - 1], _grid.CellToWorld(new Vector2Int(40, 30))), 0.6f);
            Assert.IsNull(brain.Tick(Ctx(new Vector2Int(40, 21), Frame)).Path, "The route already sent is still being walked.");

            AgentIntent arm = brain.Tick(Ctx(new Vector2Int(40, 29), 2 * Frame));
            Assert.AreEqual(AgentAction.ArmTrap, arm.Action);
            Assert.AreEqual(11, arm.ActionTargetId);
            Assert.AreEqual(_grid.CellToWorld(new Vector2Int(40, 30)), arm.LookTarget);
            Assert.IsNotNull(arm.Path, "The first tick in reach stops the body.");
            Assert.AreEqual(0, arm.Path.Count);

            AgentIntent again = brain.Tick(Ctx(new Vector2Int(40, 29), 3 * Frame));
            Assert.AreEqual(AgentAction.ArmTrap, again.Action, "The request stays open until the runtime answers.");
            Assert.IsNull(again.Path);
        }

        [Test]
        public void TheRuntimesAnswerEndsTheTrapPlanAndTheTrapIsNotChosenAgainForTheCooldown()
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            brain.Tick(Ctx(new Vector2Int(40, 29)));

            brain.OnActionResolved(AgentAction.ArmTrap, 11, true);
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30, available: false) });
            AgentIntent after = brain.Tick(Ctx(new Vector2Int(40, 29), Frame));

            Assert.AreNotEqual("ArmTrap", after.DebugState);
            Assert.AreEqual(AgentAction.None, after.Action);

            // Even if the trap were offered again, the cooldown holds it back.
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            for (float t = 2 * Frame; t < 5f; t += 0.25f)
            {
                AgentIntent intent = brain.Tick(Ctx(new Vector2Int(40, 29), t));
                Assert.AreNotEqual("ArmTrap", intent.DebugState, $"t = {t}");
                Assert.AreEqual(AgentAction.None, intent.Action, $"t = {t}");
            }
        }

        [Test]
        public void TrapThatCannotBeReachedIsGivenUpAndNotRetriedAtOnce()
        {
            // A one-cell island: the source offers it (the trap's own cell is walkable), no route leads there.
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 39; x <= 41; x++)
                for (int y = 29; y <= 31; y++)
                    if (x != 40 || y != 30)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);

            AgentIntent intent = brain.Tick(Ctx(new Vector2Int(40, 20)));
            Assert.AreNotEqual("ArmTrap", intent.DebugState, "No route, so the plan ends and the Saboteur patrols.");
            Assert.AreNotEqual(AgentAction.ArmTrap, intent.Action);

            for (float t = 0.25f; t < 4f; t += 0.25f)
            {
                AgentIntent later = brain.Tick(Ctx(new Vector2Int(40, 20), t));
                Assert.AreNotEqual("ArmTrap", later.DebugState, $"The failure cooldown holds at t = {t}.");
            }
        }

        [Test]
        public void TrapArmedOrRemovedMidWalkEndsThePlanWithoutACooldownPenalty()
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            Assert.AreEqual("ArmTrap", brain.Tick(Ctx(new Vector2Int(40, 20))).DebugState);

            _world.SetSabotageFacts(new SabotageFact[0]);
            AgentIntent intent = brain.Tick(Ctx(new Vector2Int(40, 22), Frame));

            Assert.AreNotEqual("ArmTrap", intent.DebugState);
            Assert.AreEqual(AgentAction.None, intent.Action);
        }

        [Test]
        public void RequestThatStaysUnansweredInReachIsGivenUpAfterTheTimeout()
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            Assert.AreEqual(AgentAction.ArmTrap, brain.Tick(Ctx(new Vector2Int(40, 29))).Action);

            AgentIntent before = brain.Tick(Ctx(new Vector2Int(40, 29), SaboteurBrain.FactTimeoutSeconds - Frame));
            Assert.AreEqual(AgentAction.ArmTrap, before.Action);

            AgentIntent after = brain.Tick(Ctx(new Vector2Int(40, 29), SaboteurBrain.FactTimeoutSeconds + Frame));
            Assert.AreNotEqual(AgentAction.ArmTrap, after.Action);
            Assert.AreNotEqual("ArmTrap", after.DebugState);
        }

        [Test]
        public void SelectedBatteryIsWalkedToThenTakenAndTheAnswerEndsThePlan()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 0.2f);
            _world.SetSabotageFacts(new[] { Battery(21, 30, 40) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);

            AgentIntent walk = brain.Tick(Ctx(new Vector2Int(30, 34)));
            Assert.AreEqual(Battery21, brain.CurrentAction);
            Assert.AreEqual("StealBattery", walk.DebugState);
            Assert.AreEqual(AgentAction.None, walk.Action);
            Assert.Greater(walk.Path.Count, 0);

            AgentIntent take = brain.Tick(Ctx(new Vector2Int(30, 39), Frame));
            Assert.AreEqual(AgentAction.StealBattery, take.Action);
            Assert.AreEqual(21, take.ActionTargetId);

            brain.OnActionResolved(AgentAction.StealBattery, 21, true);
            _world.SetSabotageFacts(new SabotageFact[0]);
            AgentIntent after = brain.Tick(Ctx(new Vector2Int(30, 39), 2 * Frame));
            Assert.AreNotEqual("StealBattery", after.DebugState);
            Assert.AreEqual(AgentAction.None, after.Action);
        }

        [Test]
        public void TwoSaboteursNeverGoForTheSameTrap()
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain a = Brain(SaboteurLetter.A);
            SaboteurBrain b = Brain(SaboteurLetter.B);

            a.Tick(Ctx(new Vector2Int(40, 26)));
            b.Tick(Ctx(new Vector2Int(40, 25)));
            b.Tick(Ctx(new Vector2Int(40, 25), SquadCoordinator.StaggerStep));

            Assert.AreEqual(Trap11, a.CurrentAction);
            Assert.AreNotEqual(Trap11, b.CurrentAction, "A holds the claim, so B's candidate is vetoed.");
        }

        [Test]
        public void StunDropsTheTrapPlanAndTheNextTickRoutesAgain()
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            SaboteurBrain brain = Brain(SaboteurLetter.A);
            brain.Tick(Ctx(new Vector2Int(40, 29)));

            brain.OnStunned(2f);
            _world.SetSabotageFacts(new SabotageFact[0]);
            AgentIntent intent = brain.Tick(Ctx(new Vector2Int(40, 29), 5f));

            Assert.AreEqual("Patrol", intent.DebugState);
            Assert.IsNotNull(intent.Path);
        }

        [Test]
        public void FactCooldownIsDefinedForTrapsAndBatteriesOnly()
        {
            var settings = new SelectorSettings();

            Assert.AreEqual(10f, settings.CooldownSeconds(SaboteurActionKind.ArmTrap));
            Assert.AreEqual(10f, settings.CooldownSeconds(SaboteurActionKind.StealBattery));
            Assert.AreEqual(settings.DoorCooldownSeconds, settings.CooldownSeconds(SaboteurActionKind.CloseDoor));
            Assert.AreEqual(0f, settings.CooldownSeconds(SaboteurActionKind.AttackPlayer));
            Assert.AreEqual(0f, settings.CooldownSeconds(SaboteurActionKind.Flee));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new SelectorSettings(factCooldownSeconds: -1f));
        }
    }
}
