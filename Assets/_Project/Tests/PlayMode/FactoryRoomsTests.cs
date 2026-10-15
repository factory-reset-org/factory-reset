using NUnit.Framework;
using ToyFactory.Runtime.World;

namespace ToyFactory.Tests
{
    /// <summary>The room name the HUD shows is a two-comparison lookup on the level's 2 x 2 layout.</summary>
    public class FactoryRoomsTests
    {
        [TestCase(5f, 5f, FactoryRooms.Assembly)]
        [TestCase(30f, 5f, FactoryRooms.Painting)]
        [TestCase(30f, 30f, FactoryRooms.Storage)]
        [TestCase(5f, 30f, FactoryRooms.Control)]
        public void Room_InsideEachQuadrant_IsThatRoom(float x, float z, string expected) =>
            Assert.AreEqual(expected, FactoryRooms.At(x, z, string.Empty));

        [Test]
        public void Room_StandingOnAWall_KeepsTheRoomItCameFrom()
        {
            // Door 1 sits on the line between Assembly and Painting: no flicker while in the doorway.
            Assert.AreEqual(FactoryRooms.Assembly, FactoryRooms.At(FactoryRooms.Split + 0.2f, 10f, FactoryRooms.Assembly));
            Assert.AreEqual(FactoryRooms.Painting, FactoryRooms.At(FactoryRooms.Split - 0.2f, 10f, FactoryRooms.Painting));
        }

        [Test]
        public void Room_PastTheDeadBand_ChangesRoom()
        {
            float past = FactoryRooms.Split + FactoryRooms.Hysteresis + 0.1f;
            Assert.AreEqual(FactoryRooms.Painting, FactoryRooms.At(past, 10f, FactoryRooms.Assembly));
        }

        [Test]
        public void Room_WalkingTheRing_NamesEachRoomInOrder()
        {
            string room = FactoryRooms.At(5f, 5f, string.Empty);
            room = FactoryRooms.At(30f, 5f, room);
            Assert.AreEqual(FactoryRooms.Painting, room);
            room = FactoryRooms.At(30f, 30f, room);
            Assert.AreEqual(FactoryRooms.Storage, room);
            room = FactoryRooms.At(5f, 30f, room);
            Assert.AreEqual(FactoryRooms.Control, room);
        }
    }
}
