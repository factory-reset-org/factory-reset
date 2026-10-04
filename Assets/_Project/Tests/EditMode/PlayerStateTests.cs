using NUnit.Framework;
using UnityEngine;
using ToyFactory.Interfaces;

namespace ToyFactory.Tests.EditMode
{
    public class PlayerStateTests
    {
        class FakePlayer : IPlayerState
        {
            public Vector3 Position { get; set; }
            public Vector3 Velocity { get; set; }
            public Vector3 Forward { get; set; }
            public float SprintSpeed { get; set; }
            public bool IsAlive { get; set; } = true;
            public float HealthFraction { get; set; } = 1f;
            public float AmmoFraction { get; set; } = 1f;
            public bool IsReloading { get; set; }
            public float OverchargeTimeLeft { get; set; }
            public float LastShotTime { get; set; } = -1f;
            public void TakeDamage(float amount, int sourceAgentId) { }
        }

        [TearDown]
        public void TearDown()
        {
            PlayerState.Publish(null);
        }

        [Test]
        public void PublishSetsCurrent()
        {
            var player = new FakePlayer();

            PlayerState.Publish(player);

            Assert.AreSame(player, PlayerState.Current);
        }

        [Test]
        public void PublishingNullClearsCurrent()
        {
            PlayerState.Publish(new FakePlayer());

            PlayerState.Publish(null);

            Assert.IsNull(PlayerState.Current);
        }
    }
}
