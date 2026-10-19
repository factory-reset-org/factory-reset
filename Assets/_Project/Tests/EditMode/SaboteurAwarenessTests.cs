using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using ToyFactory.AI.Agents.Saboteur;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Perception;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// What a Saboteur can know about the player: the forward cone, the short all-round sense, line of sight,
    /// tracking a player already spotted, the memory of a sighting and the player noises it hears.
    /// </summary>
    public class SaboteurAwarenessTests
    {
        sealed class FakeSight : IPlayerSight
        {
            public bool Visible = true;
            public int Asked;

            public bool CanSeeCell(Vector2Int cell)
            {
                Asked++;
                return Visible;
            }
        }

        GridGraph _grid;
        FakeSight _sight;
        SaboteurAwareness _awareness;
        Vector2Int _home;

        [SetUp]
        public void SetUp()
        {
            _grid = new GridGraph(80, 80, Vector3.zero);
            _sight = new FakeSight();
            _awareness = new SaboteurAwareness(_sight);
            _home = new Vector2Int(40, 40);
        }

        Vector3 Home => _grid.CellToWorld(_home);

        WorldBlackboard PlayerAt(Vector3 offset, Vector3? velocity = null, bool alive = true)
        {
            var world = new WorldBlackboard();
            Vector3 position = Home + offset;
            world.SetPlayer(new PlayerSnapshot(true, _grid.WorldToCell(position), position, velocity ?? Vector3.zero, Vector3.forward,
                5f, alive, 1f, 1f, false, 0f, 0f));
            return world;
        }

        AgentContext Ctx(WorldBlackboard world, float time, Vector3? facing = null, SensorSnapshot senses = default) =>
            new AgentContext(_home, Home, facing ?? Vector3.forward, time, world, senses);

        // ---- Seeing

        [Test]
        public void APlayerAheadWithinTheConeAndInSightIsSeen()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 10f)), 0f));
            Assert.IsTrue(_awareness.Sees);
            Assert.IsTrue(_awareness.InCone);
            Assert.AreEqual(10f, _awareness.Distance, 1e-3f);
        }

        [Test]
        public void TheConeIsSixtyDegreesEachSideAndTwelveMetresDeep()
        {
            // 55 degrees off the facing: inside. 65 degrees: outside (and too far for the all-round sense).
            float r = 8f;
            _awareness.Update(Ctx(PlayerAt(new Vector3(Mathf.Sin(55f * Mathf.Deg2Rad), 0f, Mathf.Cos(55f * Mathf.Deg2Rad)) * r), 0f));
            Assert.IsTrue(_awareness.Sees, "55 degrees");
            var fresh = new SaboteurAwareness(_sight);
            fresh.Update(Ctx(PlayerAt(new Vector3(Mathf.Sin(65f * Mathf.Deg2Rad), 0f, Mathf.Cos(65f * Mathf.Deg2Rad)) * r), 0f));
            Assert.IsFalse(fresh.Sees, "65 degrees");

            var far = new SaboteurAwareness(_sight);
            far.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 12.5f)), 0f));
            Assert.IsFalse(far.Sees, "Straight ahead, but past 12 m.");
        }

        [Test]
        public void APlayerBehindIsOnlySensedUpClose()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -6f)), 0f));
            Assert.IsFalse(_awareness.Sees, "6 m behind: not seen.");

            var near = new SaboteurAwareness(_sight);
            near.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -3f)), 0f));
            Assert.IsTrue(near.Sees, "3 m behind: sensed.");
            Assert.IsTrue(near.InRear);
        }

        [Test]
        public void AWallBlocksEveryWayOfSeeing()
        {
            _sight.Visible = false;
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f)), 0f));
            Assert.IsFalse(_awareness.Sees, "In the cone, but behind a wall.");

            var near = new SaboteurAwareness(_sight);
            near.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -2f)), 0f));
            Assert.IsFalse(near.Sees, "Close behind, but behind a wall.");
        }

        [Test]
        public void WithNoFacingOnlyTheAllRoundSenseWorks()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 6f)), 0f, Vector3.zero));
            Assert.IsFalse(_awareness.Sees);
        }

        [Test]
        public void APlayerAlreadySpottedIsTrackedForAMomentWhateverTheFacing()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 8f)), 0f));
            Assert.IsTrue(_awareness.Sees);

            // Now behind the Saboteur (it has turned), 8 m off: still tracked within a second.
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -8f)), 0.5f));
            Assert.IsTrue(_awareness.Sees, "Tracked.");

            // Over a second later and still behind it: lost.
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -8f)), 0.5f + SaboteurAwareness.TrackGraceSeconds + 0.1f));
            Assert.IsFalse(_awareness.Sees, "Lost.");
        }

        [Test]
        public void TheLineOfSightIsNotAskedWhenNoRuleCouldSeeThePlayer()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -10f)), 0f));
            Assert.AreEqual(0, _sight.Asked, "Behind and out of the all-round range: no ray.");
        }

        [Test]
        public void UpdatingTwiceAtTheSameTimeDoesTheWorkOnce()
        {
            WorldBlackboard world = PlayerAt(new Vector3(0f, 0f, 5f));
            _awareness.Update(Ctx(world, 1f));
            _awareness.Update(Ctx(world, 1f));
            Assert.AreEqual(1, _sight.Asked);
        }

        // ---- Remembering

        [Test]
        public void ASightingIsRememberedForSixSeconds()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f), new Vector3(2f, 0f, 0f)), 0f));
            _sight.Visible = false;
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 9f)), 3f));

            Assert.IsFalse(_awareness.Sees);
            Assert.IsTrue(_awareness.HasLastSeen);
            Assert.AreEqual(Home + new Vector3(0f, 0f, 5f), _awareness.LastSeenPosition);
            Assert.AreEqual(new Vector3(2f, 0f, 0f), _awareness.LastSeenVelocity);
            Assert.IsTrue(_awareness.IsAlerted(3f));

            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 9f)), SaboteurAwareness.MemorySeconds + 0.5f));
            Assert.IsFalse(_awareness.HasLastSeen, "Forgotten.");
            Assert.IsFalse(_awareness.IsAlerted(SaboteurAwareness.MemorySeconds + 0.5f));
        }

        [Test]
        public void ADeadPlayerIsForgotten()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f)), 0f));
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f), alive: false), 1f));
            Assert.IsFalse(_awareness.Sees);
            Assert.IsFalse(_awareness.HasLastSeen);
            Assert.IsFalse(_awareness.IsAlerted(1f));
        }

        // ---- Hearing

        [Test]
        public void APlayerNoiseIsHeardAndRemembered()
        {
            var shot = new SensorSnapshot(Home + new Vector3(10f, 0f, 0f), 60f, SaboteurAwareness.PlayerNoiseSourceId, 1f);
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -20f)), 1f, senses: shot));

            Assert.IsTrue(_awareness.HasNoise);
            Assert.AreEqual(shot.NoisePosition, _awareness.NoisePosition);
            Assert.IsTrue(_awareness.IsAlerted(1f));

            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -20f)), 1f + SaboteurAwareness.NoiseMemorySeconds + 0.5f));
            Assert.IsFalse(_awareness.HasNoise, "Forgotten.");
        }

        [Test]
        public void NoisesFromAgentsAndThrownLuresAreNotThePlayer()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -20f)), 1f,
                senses: new SensorSnapshot(Home, 70f, 3, 1f)));
            Assert.IsFalse(_awareness.HasNoise, "Another agent.");

            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, -20f)), 2f,
                senses: new SensorSnapshot(Home, 70f, SaboteurAwareness.PlayerNoiseSourceId, 2f, noiseIsLure: true)));
            Assert.IsFalse(_awareness.HasNoise, "A wind-up toy, meant for the Tracker.");
        }

        [Test]
        public void ASearchedNoiseOrSightingIsNotOfferedAgain()
        {
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f)), 0f,
                senses: new SensorSnapshot(Home, 60f, SaboteurAwareness.PlayerNoiseSourceId, 0f)));
            _sight.Visible = false;
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f)), 1f));
            _awareness.MarkSearched(1f);

            Assert.IsFalse(_awareness.HasLastSeen);
            Assert.IsFalse(_awareness.HasNoise);

            // The same old noise arriving late is ignored; a new one is heard.
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f)), 2f,
                senses: new SensorSnapshot(Home, 60f, SaboteurAwareness.PlayerNoiseSourceId, 0.5f)));
            Assert.IsFalse(_awareness.HasNoise);
            _awareness.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 5f)), 3f,
                senses: new SensorSnapshot(Home, 60f, SaboteurAwareness.PlayerNoiseSourceId, 3f)));
            Assert.IsTrue(_awareness.HasNoise);
        }

        [Test]
        public void WithoutASightThePlayerIsNeverSeenButStillHeard()
        {
            var blind = new SaboteurAwareness(null);
            blind.Update(Ctx(PlayerAt(new Vector3(0f, 0f, 2f)), 0f,
                senses: new SensorSnapshot(Home, 40f, SaboteurAwareness.PlayerNoiseSourceId, 0f)));
            Assert.IsFalse(blind.Sees);
            Assert.IsTrue(blind.HasNoise);
        }
    }
}
