using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Guard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    public sealed class PhysicsCoverVisibilityTests
    {
        sealed class FakePlayer : IPlayerState
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity { get; set; }
            public Vector3 Forward { get; set; } = Vector3.forward;
            public float SprintSpeed { get; set; } = 7f;
            public bool IsAlive { get; set; } = true;
            public float HealthFraction { get; set; } = 1f;
            public float AmmoFraction { get; set; } = 1f;
            public bool IsReloading { get; set; }
            public float OverchargeTimeLeft { get; set; }
            public float LastShotTime { get; set; } = -1f;
            public void TakeDamage(float amount, int sourceAgentId) { }
        }

        // The player stands at the origin looking along +X. Obstacles sit at x = 5, and the
        // cell under test is just behind them, centred on (5.75, 0, 0.25).
        static readonly Vector3 BehindObstacle = new Vector3(5.75f, 0f, 0.25f);

        readonly List<GameObject> _spawned = new List<GameObject>();
        GridGraph _grid;
        PhysicsCoverVisibility _visibility;
        Vector2Int _cell;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(40, 40, new Vector3(-10f, 0f, -10f));
            _visibility = new PhysicsCoverVisibility(_grid);
            _cell = _grid.WorldToCell(BehindObstacle);
            PlayerState.Publish(new FakePlayer { Position = Vector3.zero });
        }

        [TearDown]
        public void TearDown()
        {
            PlayerState.Publish(null);
            foreach (GameObject go in _spawned)
                Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        void SpawnBox(float height)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.position = new Vector3(5f, height * 0.5f, 0f);
            box.transform.localScale = new Vector3(0.5f, height, 4f);
            _spawned.Add(box);
            Physics.SyncTransforms();
        }

        [Test]
        public void OpenGroundIsNotBlocked()
        {
            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.LowCoverHeight));
            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.ChestHeight));
        }

        [Test]
        public void TallWallBlocksBothHeights()
        {
            SpawnBox(height: 3f);

            Assert.IsTrue(_visibility.IsBlocked(_cell, CoverEvaluator.LowCoverHeight));
            Assert.IsTrue(_visibility.IsBlocked(_cell, CoverEvaluator.ChestHeight));
        }

        [Test]
        public void LowBoxBlocksOnlyTheLowHeight()
        {
            SpawnBox(height: 0.8f);

            Assert.IsTrue(_visibility.IsBlocked(_cell, CoverEvaluator.LowCoverHeight));
            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.ChestHeight));
        }

        [Test]
        public void CharactersAreNotCover()
        {
            var body = new GameObject("Agent");
            body.transform.position = new Vector3(5f, 0f, 0f);
            CharacterController controller = body.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.radius = 0.4f;
            controller.center = new Vector3(0f, 1f, 0f);
            _spawned.Add(body);
            Physics.SyncTransforms();

            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.LowCoverHeight));
            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.ChestHeight));
        }

        [Test]
        public void TriggersAreNotCover()
        {
            SpawnBox(height: 3f);
            _spawned[0].GetComponent<Collider>().isTrigger = true;

            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.ChestHeight));
        }

        [Test]
        public void NothingIsBlockedWithoutAPlayer()
        {
            SpawnBox(height: 3f);
            PlayerState.Publish(null);

            Assert.IsFalse(_visibility.IsBlocked(_cell, CoverEvaluator.ChestHeight));
        }

        [Test]
        public void CellOutsideTheGridIsNotBlocked()
        {
            SpawnBox(height: 3f);

            Assert.IsFalse(_visibility.IsBlocked(new Vector2Int(-5, -5), CoverEvaluator.ChestHeight));
        }
    }
}
