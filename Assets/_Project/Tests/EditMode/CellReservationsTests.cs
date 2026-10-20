using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Core.Blackboard;

namespace ToyFactory.Tests.EditMode
{
    public class CellReservationsTests
    {
        static readonly Vector2Int CellA = new Vector2Int(1, 1);
        static readonly Vector2Int CellB = new Vector2Int(2, 2);

        [Test]
        public void FreeCellCanBeReserved()
        {
            var reservations = new CellReservations();

            Assert.IsTrue(reservations.TryReserve(CellA, 1));
            Assert.AreEqual(1, reservations.ReservedBy(CellA));
        }

        [Test]
        public void CellHeldByAnotherAgentCannotBeReserved()
        {
            var reservations = new CellReservations();
            reservations.TryReserve(CellA, 1);

            Assert.IsFalse(reservations.TryReserve(CellA, 2));
            Assert.AreEqual(1, reservations.ReservedBy(CellA));
        }

        [Test]
        public void ReservingANewCellGivesUpTheOldOne()
        {
            var reservations = new CellReservations();
            reservations.TryReserve(CellA, 1);

            reservations.TryReserve(CellB, 1);

            Assert.IsNull(reservations.ReservedBy(CellA));
            Assert.AreEqual(1, reservations.ReservedBy(CellB));
        }

        [Test]
        public void FailedReservationKeepsTheAgentsCurrentCell()
        {
            var reservations = new CellReservations();
            reservations.TryReserve(CellA, 1);
            reservations.TryReserve(CellB, 2);

            Assert.IsFalse(reservations.TryReserve(CellA, 2));
            Assert.AreEqual(2, reservations.ReservedBy(CellB));
        }

        [Test]
        public void ReleaseFreesTheCell()
        {
            var reservations = new CellReservations();
            reservations.TryReserve(CellA, 1);

            reservations.Release(1);

            Assert.IsNull(reservations.ReservedBy(CellA));
            Assert.IsTrue(reservations.TryReserve(CellA, 2));
        }

        [Test]
        public void ReleasingWithNothingHeldDoesNothing()
        {
            var reservations = new CellReservations();

            Assert.DoesNotThrow(() => reservations.Release(1));
        }
    }
}
