using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    public class GuardBrainTests
    {
        // A 30 x 11 open room with one wall column at x = 15, y = 3..7. The player stands to
        // the west, so the cells just east of the wall (x = 16..19, y = 3..7) are in its shadow.
        const int Width = 30;
        const int Height = 11;
        const int WallX = 15;
        const int GuardId = 7;

        static readonly Vector2Int PlayerCell = new Vector2Int(5, 5);
        static readonly Vector2Int GuardStart = new Vector2Int(22, 5);

        sealed class FakeVisibility : ICoverVisibility
        {
            public bool WallCastsShadow = true;
            public bool HalfHeightOnly;
            public readonly HashSet<Vector2Int> Exposed = new HashSet<Vector2Int>();

            // Cells the player has no line of sight to at any height (the far side of a wall).
            public readonly HashSet<Vector2Int> Blocked = new HashSet<Vector2Int>();

            public bool IsBlocked(Vector2Int cell, float height)
            {
                if (Blocked.Contains(cell))
                    return true;
                if (!WallCastsShadow || Exposed.Contains(cell))
                    return false;
                bool inShadow = cell.x > WallX && cell.x <= WallX + 4 && cell.y >= 3 && cell.y <= 7;
                if (!inShadow)
                    return false;
                return !HalfHeightOnly || height < 1f;
            }
        }

        // A room that spans the world x range [MinX, MaxX], all of z.
        sealed class FakeHome : IGuardHome
        {
            public float MinX, MaxX;

            public bool Contains(Vector3 position, float margin) =>
                position.x >= MinX - margin && position.x <= MaxX + margin;
        }

        GridGraph _grid;
        WorldBlackboard _world;
        FakeVisibility _visibility;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(Width, Height, Vector3.zero);
            for (int y = 3; y <= 7; y++)
                _grid.SetWalkable(new Vector2Int(WallX, y), false);
            _world = new WorldBlackboard();
            _visibility = new FakeVisibility();
        }

        GuardBrain Guard(int id = GuardId, IReadOnlyList<Vector3> patrol = null, IGuardHome home = null) =>
            new GuardBrain(_grid, new AStarSearch(_grid), _world, _visibility, id, patrol, home);

        void SetPlayer(float ammo = 1f, float overcharge = 0f, bool alive = true, bool reloading = false,
            Vector2Int? at = null)
        {
            Vector2Int cell = at ?? PlayerCell;
            _world.SetPlayer(new PlayerSnapshot(
                isKnown: true,
                cell: cell,
                position: _grid.CellToWorld(cell),
                velocity: Vector3.zero,
                forward: Vector3.right,
                sprintSpeed: 7f,
                isAlive: alive,
                healthFraction: 1f,
                ammoFraction: ammo,
                isReloading: reloading,
                overchargeTimeLeft: overcharge,
                lastShotTime: -1f));
        }

        AgentIntent TickAt(GuardBrain brain, Vector3 position, float time) =>
            brain.Tick(new AgentContext(_grid.WorldToCell(position), position, Vector3.left, time,
                _world, default(SensorSnapshot)));

        AgentIntent TickAt(GuardBrain brain, Vector2Int cell, float time) =>
            TickAt(brain, _grid.CellToWorld(cell), time);

        AgentIntent TickAt(GuardBrain brain, Vector2Int cell, float time, SensorSnapshot senses) =>
            brain.Tick(new AgentContext(cell, _grid.CellToWorld(cell), Vector3.left, time, _world, senses));

        // Walks the Guard into its cover: one tick to choose it, one tick standing on it.
        Vector2Int SettleInCover(GuardBrain brain)
        {
            TickAt(brain, GuardStart, 0f);
            Vector2Int cover = brain.CoverCell;
            TickAt(brain, cover, 0.5f);
            Assert.AreEqual("InCover", brain.StateName);
            return cover;
        }

        [Test]
        public void Guard_NoPlayer_PatrolsOrHolds()
        {
            GuardBrain brain = Guard();

            AgentIntent intent = TickAt(brain, GuardStart, 0f);

            Assert.AreEqual("Patrol", brain.StateName);
            Assert.AreEqual(AgentAction.None, intent.Action);
            Assert.IsFalse(brain.HasCover);
        }

        [Test]
        public void Guard_DeadPlayer_PatrolsOrHolds()
        {
            SetPlayer(alive: false);
            GuardBrain brain = Guard();

            TickAt(brain, GuardStart, 0f);

            Assert.AreEqual("Patrol", brain.StateName);
        }

        [Test]
        public void Guard_NoPlayerWithPatrolPoints_WalksToThem()
        {
            var patrol = new List<Vector3> { _grid.CellToWorld(new Vector2Int(25, 2)) };
            GuardBrain brain = Guard(patrol: patrol);

            AgentIntent intent = TickAt(brain, GuardStart, 0f);

            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 0);
            Assert.AreEqual(patrol[0], intent.Path[intent.Path.Count - 1]);
        }

        [Test]
        public void Guard_PlayerInRange_TakesCoverAndReservesIt()
        {
            SetPlayer();
            GuardBrain brain = Guard();

            AgentIntent intent = TickAt(brain, GuardStart, 0f);

            Assert.AreEqual("TakeCover", brain.StateName);
            Assert.IsTrue(brain.HasCover);
            Assert.AreEqual(GuardId, _world.Reservations.ReservedBy(brain.CoverCell));
            Assert.AreEqual(_grid.CellToWorld(brain.CoverCell), intent.Path[intent.Path.Count - 1]);
        }

        [Test]
        public void Guard_PlayerBeyondAlertRange_StaysOnPatrol()
        {
            _grid = new GridGraph(100, Height, Vector3.zero);
            SetPlayer();
            GuardBrain brain = Guard();

            TickAt(brain, new Vector2Int(90, 5), 0f);

            Assert.AreEqual("Patrol", brain.StateName);
        }

        [Test]
        public void Guard_ArrivalUsesDistanceNotCell()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);
            Vector2Int cover = brain.CoverCell;

            // 0.4 m east of the cover centre: a neighbouring cell, but within the 0.5 m radius.
            Vector3 nearby = _grid.CellToWorld(cover) + new Vector3(0.4f, 0f, 0f);
            TickAt(brain, nearby, 0.5f);

            Assert.AreNotEqual(cover, _grid.WorldToCell(nearby));
            Assert.AreEqual("InCover", brain.StateName);
        }

        [Test]
        public void Guard_HoldTimerElapses_PeeksAndShoots()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            AgentIntent peek = TickAt(brain, cover, 2f);
            Assert.AreEqual("PeekAndShoot", brain.StateName);

            Vector3 peekPosition = peek.Path[peek.Path.Count - 1];
            AgentIntent shot = TickAt(brain, peekPosition, 2.2f);

            Assert.AreEqual(AgentAction.Shoot, shot.Action);
            Assert.AreEqual(_grid.CellToWorld(PlayerCell), shot.LookTarget);
        }

        [Test]
        public void Guard_PeekTimerElapses_ReturnsToCover()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);
            TickAt(brain, cover, 2f);

            TickAt(brain, cover, 2f + GuardBrain.PeekDuration + 0.1f);

            Assert.AreEqual("InCover", brain.StateName);
        }

        [Test]
        public void Guard_OverchargeActive_UsesTwelveMetresAndDoesNotPeek()
        {
            SetPlayer(overcharge: 6f);
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            TickAt(brain, cover, 4f);

            Assert.AreEqual(GuardBrain.OverchargeRange, brain.IdealRange);
            Assert.AreEqual("InCover", brain.StateName);
        }

        [Test]
        public void Guard_OverchargeStartsMidPeek_ReturnsToCover()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);
            TickAt(brain, cover, 2f);
            Assert.AreEqual("PeekAndShoot", brain.StateName);

            SetPlayer(overcharge: 6f);
            TickAt(brain, cover, 2.1f);

            Assert.AreEqual("InCover", brain.StateName);
        }

        [Test]
        public void Guard_OverchargeEndsMidWait_ReevaluatesWithBatteryRow()
        {
            SetPlayer(overcharge: 6f);
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            SetPlayer(ammo: 0.4f);
            TickAt(brain, cover, 4f);

            Assert.AreEqual(GuardBrain.MidBatteryRange, brain.IdealRange);
        }

        [Test]
        public void Guard_OverchargeWithOnlyHalfCover_HasNoCover()
        {
            _visibility.HalfHeightOnly = true;
            SetPlayer(overcharge: 6f);
            GuardBrain brain = Guard();

            TickAt(brain, GuardStart, 0f);

            Assert.IsFalse(brain.HasCover);
            Assert.AreEqual("Retreat", brain.StateName);
        }

        [Test]
        public void Guard_BatteryBelow25_ReducesIdealDistanceAndAdvances()
        {
            SetPlayer(ammo: 0.2f);
            GuardBrain brain = Guard();

            TickAt(brain, GuardStart, 0f);

            Assert.AreEqual(GuardBrain.LowBatteryRange, brain.IdealRange);
            Assert.AreEqual("Advance", brain.StateName);
        }

        [Test]
        public void Guard_PlayerReloading_Advances()
        {
            SetPlayer(reloading: true);
            GuardBrain brain = Guard();

            TickAt(brain, GuardStart, 0f);

            Assert.AreEqual("Advance", brain.StateName);
        }

        [Test]
        public void Guard_BatteryTiers_SetTheIdealDistance()
        {
            SetPlayer(ammo: 0.9f);
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);
            Assert.AreEqual(GuardBrain.HighBatteryRange, brain.IdealRange);

            SetPlayer(ammo: 0.4f);
            TickAt(brain, GuardStart, 0.1f);
            Assert.AreEqual(GuardBrain.MidBatteryRange, brain.IdealRange);
        }

        [Test]
        public void Guard_NoCoverAvailable_FallsBackToRetreat()
        {
            _visibility.WallCastsShadow = false;
            SetPlayer();
            GuardBrain brain = Guard();

            AgentIntent intent = TickAt(brain, GuardStart, 0f);

            // Every cell is exposed, so there is nowhere to retreat to: it stands and fights.
            Assert.AreEqual("Retreat", brain.StateName);
            Assert.IsFalse(brain.HasCover);
            Assert.AreEqual(0, intent.Path.Count);
            Assert.AreEqual(AgentAction.Shoot, intent.Action);
        }

        [Test]
        public void Guard_ExposedMidCover_TransitionsToRelocate()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            _visibility.Exposed.Add(cover);   // the player flanked it
            AgentIntent intent = TickAt(brain, cover, 0.6f);

            Assert.IsTrue(brain.HasCover);
            Assert.AreNotEqual(cover, brain.CoverCell);
            Assert.AreEqual("TakeCover", brain.StateName);
            Assert.IsNull(_world.Reservations.ReservedBy(cover));
            Assert.AreEqual(_grid.CellToWorld(brain.CoverCell), intent.Path[intent.Path.Count - 1]);
        }

        [Test]
        public void Cover_ReservedByOtherAgent_SkipsToNextBest()
        {
            SetPlayer();
            GuardBrain first = Guard(id: 1);
            GuardBrain second = Guard(id: 2);

            TickAt(first, GuardStart, 0f);
            TickAt(second, GuardStart, 0f);

            Assert.IsTrue(second.HasCover);
            Assert.AreNotEqual(first.CoverCell, second.CoverCell);
        }

        [Test]
        public void Cover_TargetBlockedByBox_Reselects()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);
            Vector2Int cover = brain.CoverCell;

            _grid.AddBlocker(cover);
            brain.OnGraphChanged(new[] { cover });
            TickAt(brain, GuardStart, 0.1f);

            Assert.IsTrue(brain.HasCover);
            Assert.AreNotEqual(cover, brain.CoverCell);
        }

        [Test]
        public void Guard_GraphChangeOnRoute_Replans()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            AgentIntent first = TickAt(brain, GuardStart, 0f);
            Vector3 blockedPoint = first.Path[1];
            Vector2Int blocked = _grid.WorldToCell(blockedPoint);

            _grid.AddBlocker(blocked);
            brain.OnGraphChanged(new[] { blocked });
            AgentIntent second = TickAt(brain, GuardStart, 0.1f);

            Assert.IsNotNull(second.Path);
            CollectionAssert.DoesNotContain(second.Path, blockedPoint);
        }

        [Test]
        public void Guard_GraphChangeOffRoute_KeepsItsPath()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);

            brain.OnGraphChanged(new[] { new Vector2Int(28, 9) });
            AgentIntent intent = TickAt(brain, GuardStart, 0.1f);

            Assert.IsNull(intent.Path);   // null = keep following the current path
        }

        [Test]
        public void Guard_OnStunned_ReleasesCoverReservation()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            brain.OnStunned(8f);

            Assert.IsFalse(brain.HasCover);
            Assert.IsNull(_world.Reservations.ReservedBy(cover));
        }

        [Test]
        public void Guard_AfterReboot_PlansFreshRoute()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            SettleInCover(brain);
            brain.OnStunned(8f);

            AgentIntent intent = TickAt(brain, GuardStart, 9f);

            Assert.AreEqual("TakeCover", brain.StateName);
            Assert.IsNotNull(intent.Path);
            Assert.Greater(intent.Path.Count, 0);
            Assert.AreEqual(GuardId, _world.Reservations.ReservedBy(brain.CoverCell));
        }

        [Test]
        public void Guard_OnDestroyed_ReleasesCoverReservation()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            brain.OnDestroyed();

            Assert.IsNull(_world.Reservations.ReservedBy(cover));
        }

        [Test]
        public void Guard_PlayerLost_ReturnsToPatrolAndReleasesCover()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            _world.SetPlayer(default);
            TickAt(brain, cover, 1f);

            Assert.AreEqual("Patrol", brain.StateName);
            Assert.IsNull(_world.Reservations.ReservedBy(cover));
        }

        [Test]
        public void Guard_Transitions_PickHighestPriorityValidOne()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            Vector2Int cover = SettleInCover(brain);

            // Stunned (100) and exposed cover (60) are both true: the stun wins, and the
            // reboot tick then plans from scratch.
            _visibility.Exposed.Add(cover);
            brain.OnStunned(8f);
            TickAt(brain, cover, 9f);

            Assert.AreEqual("TakeCover", brain.StateName);
            Assert.AreNotEqual(cover, brain.CoverCell);
        }

        // ---- Engagement: sight or sound, in its own room, from its chapter -------------

        [Test]
        public void Guard_PlayerInRangeBehindAWall_StaysOnPatrol()
        {
            _visibility.Blocked.Add(GuardStart);
            SetPlayer();
            GuardBrain brain = Guard();

            TickAt(brain, GuardStart, 0f);

            Assert.IsFalse(brain.IsEngaged);
            Assert.AreEqual("Patrol", brain.StateName);
            Assert.IsFalse(brain.HasCover);
        }

        [Test]
        public void Guard_PlayerInRangeInPlainSight_Engages()
        {
            SetPlayer();
            GuardBrain brain = Guard();

            TickAt(brain, GuardStart, 0f);

            Assert.IsTrue(brain.IsEngaged);
        }

        [Test]
        public void Guard_HearsThePlayerShootBehindAWall_Engages()
        {
            _visibility.Blocked.Add(GuardStart);
            SetPlayer();
            GuardBrain brain = Guard();
            var shot = new SensorSnapshot(_grid.CellToWorld(PlayerCell), 60f, GuardBrain.PlayerNoiseSourceId, 0f);

            TickAt(brain, GuardStart, 0f, shot);

            Assert.IsTrue(brain.IsEngaged);
        }

        [Test]
        public void Guard_OtherNoises_DoNotAlertIt()
        {
            _visibility.Blocked.Add(GuardStart);
            SetPlayer();
            GuardBrain brain = Guard();
            Vector3 at = _grid.CellToWorld(PlayerCell);

            // A door slam (another source), a quiet player noise, and a shot that is five seconds old.
            TickAt(brain, GuardStart, 0f, new SensorSnapshot(at, 60f, 12345, 0f));
            Assert.IsFalse(brain.IsEngaged, "Not the player's noise.");

            TickAt(brain, GuardStart, 0.1f, new SensorSnapshot(at, 20f, GuardBrain.PlayerNoiseSourceId, 0.1f));
            Assert.IsFalse(brain.IsEngaged, "Too quiet to be a shot.");

            TickAt(brain, GuardStart, 5f, new SensorSnapshot(at, 60f, GuardBrain.PlayerNoiseSourceId, 0f));
            Assert.IsFalse(brain.IsEngaged, "Too old.");
        }

        [Test]
        public void Guard_PlayerOutsideItsRoom_IsNotFought()
        {
            SetPlayer();
            GuardBrain brain = Guard(home: new FakeHome { MinX = 8f, MaxX = 14f });   // the player is at x 2.75

            TickAt(brain, GuardStart, 0f);

            Assert.IsFalse(brain.IsEngaged);
            Assert.AreEqual("Patrol", brain.StateName);
        }

        [Test]
        public void Guard_PlayerJustOutsideItsDoor_IsFought()
        {
            SetPlayer();
            GuardBrain brain = Guard(home: new FakeHome { MinX = 5f, MaxX = 14f });   // within the 3 m margin

            TickAt(brain, GuardStart, 0f);

            Assert.IsTrue(brain.IsEngaged);
        }

        [Test]
        public void Guard_PlayerLeavesItsRoomMidFight_ReturnsToPatrolAndReleasesCover()
        {
            SetPlayer();
            GuardBrain brain = Guard(home: new FakeHome { MinX = 5f, MaxX = 14f });
            Vector2Int cover = SettleInCover(brain);
            Assert.AreEqual(GuardId, _world.Reservations.ReservedBy(cover));

            SetPlayer(at: new Vector2Int(0, 5));   // x 0.25, more than 3 m outside the room
            TickAt(brain, cover, 1f);

            Assert.IsFalse(brain.IsEngaged);
            Assert.AreEqual("Patrol", brain.StateName);
            Assert.IsNull(_world.Reservations.ReservedBy(cover));
        }

        [Test]
        public void Guard_PlayerStepsIntoItsRoom_IsDetectedWithoutALineOfSight()
        {
            _visibility.Blocked.Add(GuardStart);   // it cannot see the player
            SetPlayer();
            GuardBrain brain = Guard(home: new FakeHome { MinX = 1f, MaxX = 14f });   // the player is at x 2.75, inside

            TickAt(brain, GuardStart, 0f);

            Assert.IsTrue(brain.IsEngaged);
        }

        [Test]
        public void Guard_PlayerJustOutsideItsDoor_NeedsASightOrASoundToBeDetected()
        {
            _visibility.Blocked.Add(GuardStart);
            SetPlayer();
            GuardBrain brain = Guard(home: new FakeHome { MinX = 5f, MaxX = 14f });   // within the margin, not in the room

            TickAt(brain, GuardStart, 0f);
            Assert.IsFalse(brain.IsEngaged, "Not in the room, not seen, nothing heard.");

            var shot = new SensorSnapshot(_grid.CellToWorld(PlayerCell), 60f, GuardBrain.PlayerNoiseSourceId, 0.1f);
            TickAt(brain, GuardStart, 0.1f, shot);
            Assert.IsTrue(brain.IsEngaged, "A shot gives it away.");
        }

        [Test]
        public void Guard_IsNotHeldBackByTheChapter()
        {
            SetPlayer();
            GuardBrain brain = Guard(home: new FakeHome { MinX = 1f, MaxX = 14f });
            _world.SetChapterIndex(1);

            TickAt(brain, GuardStart, 0f);

            Assert.IsTrue(brain.IsEngaged, "The room is the Guard's from the start.");
        }

        [Test]
        public void Guard_TakesCoverOnlyInsideItsRoom()
        {
            SetPlayer();
            // The shadow cells are at world x 8.25 to 9.75. Cells up to 8.5 + 1 m of slack are on offer.
            var home = new FakeHome { MinX = 5f, MaxX = 8.5f };
            GuardBrain brain = Guard(home: home);

            TickAt(brain, GuardStart, 0f);

            Assert.IsTrue(brain.HasCover);
            Assert.LessOrEqual(_grid.CellToWorld(brain.CoverCell).x, home.MaxX + GuardBrain.CoverMargin + 0.01f);
        }

        [Test]
        public void Guard_NoCoverInsideItsRoom_DoesNotLeaveToFindSome()
        {
            SetPlayer();
            var home = new FakeHome { MinX = 5f, MaxX = 6f };   // every shadow cell is outside, even with the slack
            GuardBrain brain = Guard(home: home);

            TickAt(brain, GuardStart, 0f);

            Assert.IsTrue(brain.IsEngaged);
            Assert.IsFalse(brain.HasCover);
        }

        [Test]
        public void Guard_TransitionTable_IsDataAndPrintable()
        {
            string table = Guard().DescribeTransitions();

            StringAssert.Contains("100 | any -> Stunned", table);
            StringAssert.Contains("80 | TakeCover -> InCover", table);
            StringAssert.Contains("20 | Relocate -> Retreat", table);
        }

        // ---- Read-only state for the debug overlay ------------------------------------

        [Test]
        public void Guard_Engaged_ReportsTheCoverItScoredAndItsRoute()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);

            var scored = new List<ScoredCover>();
            brain.GetScoredCover(scored);

            Assert.IsTrue(brain.IsEngaged);
            Assert.AreEqual(_grid.CellToWorld(PlayerCell), brain.PlayerPosition);
            Assert.AreEqual("High", brain.BatteryTierName);
            Assert.That(scored.Count, Is.InRange(1, GuardBrain.TopCandidates + 1));
            Assert.IsTrue(scored.Exists(c => c.Cell == brain.CoverCell), "The chosen cover is one of the scored ones.");
            foreach (ScoredCover cover in scored)
            {
                Assert.That(cover.Score, Is.InRange(0f, 1f));
                Assert.That(cover.PathCost, Is.InRange(0f, GuardBrain.MaxPathCost));
            }
            Assert.Greater(brain.RouteCells.Count, 1);
            Assert.AreEqual(brain.CoverCell, brain.RouteCells[brain.RouteCells.Count - 1]);
        }

        [Test]
        public void Guard_ScoredCover_IsAppendedAndNeverGrowsBetweenEvaluations()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);
            var scored = new List<ScoredCover>();
            brain.GetScoredCover(scored);
            int first = scored.Count;

            // A second evaluation a second later replaces the list instead of adding to it.
            TickAt(brain, GuardStart, GuardBrain.CoverInterval + 0.1f);
            brain.GetScoredCover(scored);

            Assert.That(scored.Count - first, Is.InRange(1, GuardBrain.TopCandidates + 1));
        }

        [Test]
        public void Guard_NotEngaged_ReportsNoCoverAndNoRoute()
        {
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);

            var scored = new List<ScoredCover>();
            brain.GetScoredCover(scored);

            Assert.IsFalse(brain.IsEngaged);
            Assert.AreEqual(0, scored.Count);
            Assert.AreEqual(0, brain.RouteCells.Count);
        }

        [Test]
        public void Guard_PlayerLeaves_ClearsTheScoredCover()
        {
            SetPlayer();
            GuardBrain brain = Guard();
            TickAt(brain, GuardStart, 0f);

            SetPlayer(alive: false);
            TickAt(brain, GuardStart, 0.5f);

            var scored = new List<ScoredCover>();
            brain.GetScoredCover(scored);
            Assert.AreEqual(0, scored.Count);
        }

        [Test]
        public void Guard_BatteryTierName_FollowsThePlayersBattery()
        {
            GuardBrain brain = Guard();

            SetPlayer(ammo: 0.4f);
            TickAt(brain, GuardStart, 0f);
            Assert.AreEqual("Mid", brain.BatteryTierName);

            SetPlayer(ammo: 0.1f);
            TickAt(brain, GuardStart, 0.1f);
            Assert.AreEqual("Low", brain.BatteryTierName);

            SetPlayer(overcharge: 5f);
            TickAt(brain, GuardStart, 0.2f);
            Assert.AreEqual("Overcharge", brain.BatteryTierName);
        }
    }
}
