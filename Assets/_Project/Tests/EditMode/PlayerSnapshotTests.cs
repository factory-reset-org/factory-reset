using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.Tests.EditMode
{
    public class PlayerSnapshotTests
    {
        [Test]
        public void NewBlackboardReportsNoKnownPlayer()
        {
            var blackboard = new WorldBlackboard();

            Assert.IsFalse(blackboard.Player.IsKnown);
        }

        [Test]
        public void SetPlayerStoresTheSnapshot()
        {
            var blackboard = new WorldBlackboard();
            var snapshot = new PlayerSnapshot(
                isKnown: true,
                cell: new Vector2Int(3, 4),
                position: new Vector3(1.5f, 0f, 2f),
                velocity: Vector3.forward,
                forward: Vector3.forward,
                sprintSpeed: 7f,
                isAlive: true,
                healthFraction: 1f,
                ammoFraction: 0.25f,
                isReloading: false,
                overchargeTimeLeft: 0f,
                lastShotTime: -1f);

            blackboard.SetPlayer(snapshot);

            Assert.IsTrue(blackboard.Player.IsKnown);
            Assert.AreEqual(new Vector2Int(3, 4), blackboard.Player.Cell);
            Assert.AreEqual(0.25f, blackboard.Player.AmmoFraction);
            Assert.AreEqual(7f, blackboard.Player.SprintSpeed);
        }

        [Test]
        public void SetPlayerReplacesThePreviousSnapshot()
        {
            var blackboard = new WorldBlackboard();
            blackboard.SetPlayer(new PlayerSnapshot(
                true, new Vector2Int(1, 1), Vector3.zero, Vector3.zero, Vector3.forward,
                7f, true, 1f, 1f, false, 0f, -1f));

            blackboard.SetPlayer(default);

            Assert.IsFalse(blackboard.Player.IsKnown);
        }
    }
}
