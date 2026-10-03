using NUnit.Framework;
using ToyFactory.Interfaces;

namespace ToyFactory.Tests.EditMode
{
    public class GameClockTests
    {
        class FakeClock : IGameClock
        {
            public GameState State { get; set; }
            public float GameTime { get; set; }
            public void AddListener(IGameStateListener listener) { }
            public void RemoveListener(IGameStateListener listener) { }
        }

        [TearDown]
        public void TearDown()
        {
            GameClock.Publish(null);
        }

        [Test]
        public void PublishSetsCurrent()
        {
            var clock = new FakeClock();

            GameClock.Publish(clock);

            Assert.AreSame(clock, GameClock.Current);
        }

        [Test]
        public void PublishingReplacesThePreviousClock()
        {
            GameClock.Publish(new FakeClock());
            var second = new FakeClock();

            GameClock.Publish(second);

            Assert.AreSame(second, GameClock.Current);
        }
    }
}
