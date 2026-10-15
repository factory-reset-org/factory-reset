using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.Tests
{
    /// <summary>The blackboard's trap and battery facts: copied in, and a version that moves only on a real change.</summary>
    public class SabotageFactsTests
    {
        static SabotageFact Trap(int id, bool available = true) =>
            new SabotageFact(id, new Vector2Int(id, 2), SabotageFactKind.Trap, available);

        static SabotageFact Battery(int id) =>
            new SabotageFact(id, new Vector2Int(id, 5), SabotageFactKind.Battery, true);

        [Test]
        public void NewBlackboard_HasNoFacts()
        {
            var board = new WorldBlackboard();
            Assert.AreEqual(0, board.SabotageFacts.Count);
            Assert.AreEqual(0, board.SabotageFactsVersion);
        }

        [Test]
        public void SetSabotageFacts_CopiesTheList()
        {
            var board = new WorldBlackboard();
            var source = new List<SabotageFact> { Trap(11), Battery(21) };
            board.SetSabotageFacts(source);
            source.Clear();

            Assert.AreEqual(2, board.SabotageFacts.Count);
            Assert.AreEqual(11, board.SabotageFacts[0].Id);
            Assert.AreEqual(SabotageFactKind.Battery, board.SabotageFacts[1].Kind);
            Assert.AreEqual(new Vector2Int(21, 5), board.SabotageFacts[1].Cell);
        }

        [Test]
        public void SetSabotageFacts_SameFacts_DoNotMoveTheVersion()
        {
            var board = new WorldBlackboard();
            board.SetSabotageFacts(new[] { Trap(11), Battery(21) });
            int version = board.SabotageFactsVersion;

            board.SetSabotageFacts(new[] { Trap(11), Battery(21) });

            Assert.AreEqual(version, board.SabotageFactsVersion);
        }

        [Test]
        public void SetSabotageFacts_ATrapGetsArmed_MovesTheVersionAndKeepsTheFact()
        {
            var board = new WorldBlackboard();
            board.SetSabotageFacts(new[] { Trap(11) });
            int version = board.SabotageFactsVersion;

            board.SetSabotageFacts(new[] { Trap(11, available: false) });

            Assert.Greater(board.SabotageFactsVersion, version);
            Assert.AreEqual(1, board.SabotageFacts.Count);
            Assert.IsFalse(board.SabotageFacts[0].Available);
        }

        [Test]
        public void SetSabotageFacts_ABatteryIsTaken_DropsItFromTheList()
        {
            var board = new WorldBlackboard();
            board.SetSabotageFacts(new[] { Battery(21), Battery(22) });

            board.SetSabotageFacts(new[] { Battery(22) });

            Assert.AreEqual(1, board.SabotageFacts.Count);
            Assert.AreEqual(22, board.SabotageFacts[0].Id);
        }

        [Test]
        public void SetSabotageFacts_Null_Throws() =>
            Assert.Throws<System.ArgumentNullException>(() => new WorldBlackboard().SetSabotageFacts(null));
    }
}
