using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;

namespace ToyFactory.Tests
{
    public sealed class RuntimeWriterTests
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

        [TearDown]
        public void ClearPlayer() => PlayerState.Publish(null);

        [Test]
        public void NoPlayerWritesAnUnknownSnapshot()
        {
            var blackboard = new WorldBlackboard();
            PlayerState.Publish(null);

            new PlayerStateWriter(blackboard).Write(new GridGraph(10, 10, Vector3.zero));

            Assert.IsFalse(blackboard.Player.IsKnown);
        }

        [Test]
        public void PlayerIsCopiedWithItsCellOnTheLevelGrid()
        {
            var blackboard = new WorldBlackboard();
            var grid = new GridGraph(40, 40, new Vector3(-10f, 0f, -10f));
            var player = new FakePlayer
            {
                Position = new Vector3(2.3f, 0f, 4.8f),
                Velocity = new Vector3(3f, 0f, 0f),
                AmmoFraction = 0.25f,
                IsReloading = true,
                OverchargeTimeLeft = 4f,
                LastShotTime = 12.5f,
            };
            PlayerState.Publish(player);

            new PlayerStateWriter(blackboard).Write(grid);

            PlayerSnapshot snapshot = blackboard.Player;
            Assert.IsTrue(snapshot.IsKnown);
            Assert.AreEqual(grid.WorldToCell(player.Position), snapshot.Cell);
            Assert.AreEqual(new Vector2Int(24, 29), snapshot.Cell, "floor((2.3 + 10) / 0.5), floor((4.8 + 10) / 0.5)");
            Assert.AreEqual(player.Position, snapshot.Position);
            Assert.AreEqual(player.Velocity, snapshot.Velocity);
            Assert.AreEqual(7f, snapshot.SprintSpeed);
            Assert.IsTrue(snapshot.IsAlive);
            Assert.AreEqual(0.25f, snapshot.AmmoFraction);
            Assert.IsTrue(snapshot.IsReloading);
            Assert.AreEqual(4f, snapshot.OverchargeTimeLeft);
            Assert.AreEqual(12.5f, snapshot.LastShotTime);
        }

        [Test]
        public void WithoutAGridThePlayerIsKnownButHasNoCell()
        {
            var blackboard = new WorldBlackboard();
            PlayerState.Publish(new FakePlayer { Position = new Vector3(3f, 0f, 3f) });

            new PlayerStateWriter(blackboard).Write(null);

            Assert.IsTrue(blackboard.Player.IsKnown);
            Assert.AreEqual(Vector2Int.zero, blackboard.Player.Cell);
        }

        [Test]
        public void EachWriteReflectsThePlayerNow()
        {
            var blackboard = new WorldBlackboard();
            var grid = new GridGraph(20, 20, Vector3.zero);
            var player = new FakePlayer { Position = new Vector3(1f, 0f, 1f) };
            PlayerState.Publish(player);
            var writer = new PlayerStateWriter(blackboard);

            writer.Write(grid);
            player.Position = new Vector3(5f, 0f, 1f);
            player.IsAlive = false;
            writer.Write(grid);

            Assert.AreEqual(new Vector2Int(10, 2), blackboard.Player.Cell);
            Assert.IsFalse(blackboard.Player.IsAlive);

            PlayerState.Publish(null);
            writer.Write(grid);
            Assert.IsFalse(blackboard.Player.IsKnown, "A player that has gone leaves the snapshot unknown.");
        }

        [Test]
        public void ChapterStartIsWrittenToTheBlackboardUntilDisposed()
        {
            var blackboard = new WorldBlackboard();
            var writer = new ChapterIndexWriter(blackboard);
            try
            {
                ChapterEvents.RaiseChapterStarted(3);
                Assert.AreEqual(3, blackboard.ChapterIndex);
            }
            finally
            {
                writer.Dispose();
            }

            ChapterEvents.RaiseChapterStarted(4);
            Assert.AreEqual(3, blackboard.ChapterIndex, "A disposed writer no longer listens.");
        }
    }
}
