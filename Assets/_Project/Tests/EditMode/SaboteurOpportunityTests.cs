using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;
using ToyFactory.AI.Core.Search;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Battery theft, trap arming and door closing as the game runs them, with the brain's awareness and
    /// pathfinder bound: each needs the Saboteur to know the player is about, and a target it can really walk to.
    /// </summary>
    public class SaboteurOpportunityTests
    {
        sealed class FakeSight : IPlayerSight
        {
            public bool Visible = true;
            public bool CanSeeCell(Vector2Int cell) => Visible;
        }

        GridGraph _grid;
        AStarSearch _pathfinder;
        FakeSight _sight;
        WorldBlackboard _world;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(60, 60, Vector3.zero);
            _pathfinder = new AStarSearch(_grid);
            _sight = new FakeSight();
            _world = new WorldBlackboard();
            _world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, new Vector2Int(55, 30), ObjectiveTargetKind.Task) });
        }

        static SaboteurIdentity A => new SaboteurIdentity(1, SaboteurLetter.A);

        void SetPlayer(Vector2Int cell, float ammo = 0.5f) =>
            _world.SetPlayer(new PlayerSnapshot(true, cell, _grid.CellToWorld(cell), Vector3.zero, Vector3.right,
                5f, true, 1f, ammo, false, 0f, 0f));

        AgentContext Ctx(Vector2Int cell, Vector3 facing, float time = 0f, SensorSnapshot senses = default) =>
            new AgentContext(cell, _grid.CellToWorld(cell), facing, time, _world, senses);

        SensorSnapshot PlayerNoise(float time) =>
            new SensorSnapshot(_world.Player.Position, 60f, SaboteurAwareness.PlayerNoiseSourceId, time);

        T Bound<T>(T source) where T : ISaboteurAwarenessUser
        {
            source.Bind(new SaboteurAwareness(_sight), _pathfinder);
            return source;
        }

        List<ActionCandidate> Batteries(StealBatterySource source, Vector2Int sabo, Vector3 facing, params SabotageFact[] facts)
        {
            _world.SetSabotageFacts(facts);
            var list = new List<ActionCandidate>();
            source.AddCandidates(Ctx(sabo, facing), A, list);
            return list;
        }

        static SabotageFact Battery(int id, int x, int y) => new SabotageFact(id, new Vector2Int(x, y), SabotageFactKind.Battery, true);
        static SabotageFact Trap(int id, int x, int y) => new SabotageFact(id, new Vector2Int(x, y), SabotageFactKind.Trap, true);

        // ---- Batteries

        [Test]
        public void ABatteryIsOnlyTakenFromAPlayerTheSaboteurHasSpotted()
        {
            SetPlayer(new Vector2Int(30, 30));
            var sabo = new Vector2Int(40, 30);
            Assert.AreEqual(1, Batteries(Bound(new StealBatterySource(_grid)), sabo, Vector3.left, Battery(21, 44, 30)).Count,
                "Facing the player 5 m off: seen, and the battery is 2 m away.");
            Assert.IsEmpty(Batteries(Bound(new StealBatterySource(_grid)), sabo, Vector3.right, Battery(21, 44, 30)),
                "Facing away, the player 5 m behind: not noticed, so no theft.");
        }

        [Test]
        public void TheBatteryMustBeCloserToTheSaboteurThanToThePlayer()
        {
            SetPlayer(new Vector2Int(30, 30));
            Assert.IsEmpty(Batteries(Bound(new StealBatterySource(_grid)), new Vector2Int(40, 30), Vector3.left, Battery(21, 32, 30)),
                "1 m from the player, 4 m from the Saboteur.");
        }

        [Test]
        public void TheBatteryMustBeWithinReachByTheWalkNotTheStraightLine()
        {
            // A wall right next to the battery, with the only way round far to the north.
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int y = 0; y < 56; y++)
                    batch.SetWalkable(new Vector2Int(42, y), false);
                batch.Commit();
            }

            SetPlayer(new Vector2Int(30, 30));
            Assert.IsEmpty(Batteries(Bound(new StealBatterySource(_grid)), new Vector2Int(40, 30), Vector3.left, Battery(21, 44, 30)),
                "2 m in a straight line, but some 27 m by the walk round the wall.");
        }

        [Test]
        public void AnUnreachableBatteryIsNotOffered()
        {
            using (GridGraph.Batch batch = _grid.BeginBatch())
            {
                for (int x = 43; x <= 45; x++)
                for (int y = 29; y <= 31; y++)
                    if (x != 44 || y != 30)
                        batch.SetWalkable(new Vector2Int(x, y), false);
                batch.Commit();
            }

            SetPlayer(new Vector2Int(30, 30));
            Assert.IsEmpty(Batteries(Bound(new StealBatterySource(_grid)), new Vector2Int(40, 30), Vector3.left, Battery(21, 44, 30)),
                "Walled in on every side.");
        }

        [Test]
        public void APlayerWithAFullBatteryIsNeverRobbed()
        {
            SetPlayer(new Vector2Int(30, 30), ammo: 1f);
            Assert.IsEmpty(Batteries(Bound(new StealBatterySource(_grid)), new Vector2Int(40, 30), Vector3.left, Battery(21, 44, 30)));
        }

        [Test]
        public void ABatteryBeyondTheInteractionRangeIsNotOffered()
        {
            SetPlayer(new Vector2Int(22, 30));   // 9 m off, in the Saboteur's cone
            Assert.IsEmpty(Batteries(Bound(new StealBatterySource(_grid)), new Vector2Int(40, 30), Vector3.left, Battery(21, 40, 57)),
                "13.5 m from the Saboteur: past the 12 m reach.");
        }

        // ---- Traps

        List<ActionCandidate> Traps(ArmTrapSource source, Vector2Int sabo, Vector3 facing, SensorSnapshot senses = default)
        {
            _world.SetSabotageFacts(new[] { Trap(11, 40, 30) });
            var list = new List<ActionCandidate>();
            source.AddCandidates(Ctx(sabo, facing, 0f, senses), A, list);
            return list;
        }

        [Test]
        public void ATrapIsOnlyArmedForAPlayerTheSaboteurKnowsIsAbout()
        {
            SetPlayer(new Vector2Int(10, 30), ammo: 1f);
            var cache = new DetourCache(_grid, _pathfinder);

            Assert.IsEmpty(Traps(Bound(new ArmTrapSource(_grid, cache)), new Vector2Int(40, 26), Vector3.forward),
                "The player is 15 m off and behind: the Saboteur has no reason to expect them.");
            Assert.AreEqual(1, Traps(Bound(new ArmTrapSource(_grid, cache)), new Vector2Int(40, 26), Vector3.forward, PlayerNoise(0f)).Count,
                "Heard them: it arms the trap on their way.");
        }

        [Test]
        public void ATrapThePlayerWouldReachFirstOrUnderTheirFeetIsNotArmed()
        {
            var cache = new DetourCache(_grid, _pathfinder);
            SetPlayer(new Vector2Int(33, 30), ammo: 1f);   // 3.5 m from the trap
            Assert.IsEmpty(Traps(Bound(new ArmTrapSource(_grid, cache)), new Vector2Int(40, 26), Vector3.left, PlayerNoise(0f)));
        }

        // ---- Doors

        [Test]
        public void ADoorIsOnlyClosedOnAPlayerTheSaboteurKnowsIsAbout()
        {
            var grid = new GridGraph(40, 20, Vector3.zero);
            using (GridGraph.Batch batch = grid.BeginBatch())
            {
                for (int y = 0; y < grid.Height; y++)
                {
                    if (y != 10 && y != 0)
                        batch.SetWalkable(new Vector2Int(20, y), false);
                }

                batch.SetDoor(new Vector2Int(20, 10), 1, false);
                batch.SetDoor(new Vector2Int(20, 0), 2, false);
                batch.Commit();
            }

            var pathfinder = new AStarSearch(grid);
            var world = new WorldBlackboard();
            world.SetPlayer(new PlayerSnapshot(true, new Vector2Int(5, 10), grid.CellToWorld(new Vector2Int(5, 10)), Vector3.zero, Vector3.right,
                5f, true, 1f, 1f, false, 0f, 0f));
            world.SetObjectiveTargets(new[] { new ObjectiveTarget(7, new Vector2Int(35, 10), ObjectiveTargetKind.Task) });
            var cache = new DetourCache(grid, pathfinder);
            var sabo = new Vector2Int(15, 10);

            var unaware = new CloseDoorSource(cache);
            unaware.Bind(new SaboteurAwareness(_sight), pathfinder);
            var list = new List<ActionCandidate>();
            unaware.AddCandidates(new AgentContext(sabo, grid.CellToWorld(sabo), Vector3.forward, 0f, world, default), A, list);
            Assert.IsEmpty(list, "The player is 5 m off to the side and behind its cone: no door.");

            var aware = new CloseDoorSource(cache);
            aware.Bind(new SaboteurAwareness(_sight), pathfinder);
            var noise = new SensorSnapshot(world.Player.Position, 40f, SaboteurAwareness.PlayerNoiseSourceId, 0f);
            aware.AddCandidates(new AgentContext(sabo, grid.CellToWorld(sabo), Vector3.forward, 0f, world, noise), A, list);
            Assert.AreEqual(1, list.Count, "Heard the player: door 1 lengthens their route.");
            Assert.AreEqual(new ActionKey(SaboteurActionKind.CloseDoor, 1), list[0].Key);
        }
    }
}
