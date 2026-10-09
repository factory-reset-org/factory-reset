using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// Checks every cutscene camera against the level: under the 6 m ceiling, inside its room,
    /// clear of the obstacles, and one shot per line group of the dialogue scripts.
    /// </summary>
    public sealed class CutsceneShotPlanTests
    {
        const float WallMargin = 0.5f;
        const float ObstacleMargin = 0.3f;

        // Obstacles as built in Env.unity (Docs/LevelLayout.md, rooms as in the prototype):
        // centre (x, z), footprint (width x, depth z), top height. Cores float above servers 1, 2 and 9.
        static readonly (Vector2 centre, Vector2 size, float top, string name)[] Obstacles =
        {
            (new Vector2(3.5f, 11.5f), new Vector2(2f, 2f), 2.8f, "Assembly press 1"),
            (new Vector2(5.6f, 11.5f), new Vector2(2f, 2f), 2.8f, "Assembly press 2"),
            (new Vector2(24.6f, 17.5f), new Vector2(2f, 2f), 3.8f, "Painting tank 1 and its target"),
            (new Vector2(37.4f, 17.5f), new Vector2(2f, 2f), 3.8f, "Painting tank 2 and its target"),
            (new Vector2(27.4f, 11.5f), new Vector2(2f, 2f), 3.8f, "Painting tank 3 and its target"),
            (new Vector2(38.9f, 5.5f), new Vector2(2f, 2f), 3.8f, "Painting tank 4 and its target"),
            (new Vector2(23.1f, 1.5f), new Vector2(2f, 2f), 2.4f, "Painting tank 5"),
            (new Vector2(31f, 10.5f), new Vector2(1.4f, 1.2f), 2.8f, "Colour terminal"),
            (new Vector2(32.42f, 37.5f), new Vector2(5.72f, 1f), 3.2f, "Storage shelf 1"),
            (new Vector2(24.56f, 37.5f), new Vector2(4.29f, 1f), 3.2f, "Storage shelf 2"),
            (new Vector2(36f, 33.5f), new Vector2(4.29f, 1f), 3.2f, "Storage shelf 3"),
            (new Vector2(24.56f, 33.5f), new Vector2(4.29f, 1f), 3.2f, "Storage shelf 4"),
            (new Vector2(37.43f, 29.5f), new Vector2(4.29f, 1f), 3.2f, "Storage shelf 5"),
            (new Vector2(29.56f, 29.5f), new Vector2(5.72f, 1f), 3.2f, "Storage shelf 6"),
            (new Vector2(36.71f, 25.5f), new Vector2(2.86f, 1f), 3.2f, "Storage shelf 7"),
            (new Vector2(25.27f, 25.5f), new Vector2(2.86f, 1f), 3.2f, "Storage shelf 8"),
            (new Vector2(16.93f, 38f), new Vector2(1.3f, 1.5f), 4.3f, "Control server 1 and core 1"),
            (new Vector2(4.06f, 38f), new Vector2(1.3f, 1.5f), 4.3f, "Control server 2 and core 2"),
            (new Vector2(14.07f, 34f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 3"),
            (new Vector2(12.64f, 34f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 4"),
            (new Vector2(8.35f, 34f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 5"),
            (new Vector2(6.92f, 34f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 6"),
            (new Vector2(14.07f, 28f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 7"),
            (new Vector2(12.64f, 28f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 8"),
            (new Vector2(8.35f, 28f), new Vector2(1.3f, 1.5f), 4.3f, "Control server 9 and core 3"),
            (new Vector2(6.92f, 28f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 10"),
            (new Vector2(16.93f, 24f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 11"),
            (new Vector2(4.06f, 24f), new Vector2(1.3f, 1.5f), 2.8f, "Control server 12"),
            (new Vector2(10.5f, 31f), new Vector2(2f, 1.6f), 2f, "Control console"),
        };

        static IEnumerable<(CutsceneShot shot, Vector3 camera, string pose)> Cameras(ShotFraming framing)
        {
            foreach (CutsceneShot shot in CutsceneShotPlan.All.Where(s => s.Framing == framing))
            {
                yield return (shot, shot.From, "start");
                yield return (shot, shot.To, "end");
            }
        }

        static string Name(CutsceneShot shot, string pose) => $"{shot.CutsceneId} shot {shot.Index + 1} ({shot.Subject}), {pose}";

        [Test]
        public void EveryCameraStaysAMetreUnderTheCeiling()
        {
            float highest = CutsceneShotPlan.CeilingHeight - CutsceneShotPlan.CeilingClearance;
            foreach (CutsceneShot shot in CutsceneShotPlan.All)
            {
                Assert.LessOrEqual(shot.From.y, highest, Name(shot, "start"));
                Assert.LessOrEqual(shot.To.y, highest, Name(shot, "end"));
                Assert.GreaterOrEqual(Mathf.Min(shot.From.y, shot.To.y), 0.5f, Name(shot, "above the floor"));
            }
        }

        [Test]
        public void FixedCamerasStayInsideTheirRoom()
        {
            foreach (var (shot, camera, pose) in Cameras(ShotFraming.World))
            {
                Rect room = CutsceneShotPlan.Interior(shot.Room);
                Assert.That(camera.x >= room.xMin + WallMargin && camera.x <= room.xMax - WallMargin
                    && camera.z >= room.yMin + WallMargin && camera.z <= room.yMax - WallMargin,
                    $"{Name(shot, pose)} at {camera} is not {WallMargin} m inside the {shot.Room} walls.");
            }
        }

        [Test]
        public void FixedCamerasStayClearOfTheObstacles()
        {
            foreach (var (shot, camera, pose) in Cameras(ShotFraming.World))
            foreach (var obstacle in Obstacles)
            {
                Vector2 half = obstacle.size * 0.5f + Vector2.one * ObstacleMargin;
                bool over = Mathf.Abs(camera.x - obstacle.centre.x) <= half.x && Mathf.Abs(camera.z - obstacle.centre.y) <= half.y;
                Assert.IsFalse(over && camera.y <= obstacle.top + ObstacleMargin,
                    $"{Name(shot, pose)} at {camera} is inside {obstacle.name}.");
            }
        }

        [Test]
        public void FollowCamerasStayCloseEnoughToUnit047ToStayInTheRoom()
        {
            foreach (var (shot, camera, pose) in Cameras(ShotFraming.FollowActor))
                Assert.LessOrEqual(new Vector2(camera.x, camera.z).magnitude, CutsceneShotPlan.MaxFollowDistance, Name(shot, pose));
        }

        [Test]
        public void EveryDialogueShotHasACameraShot()
        {
            foreach (CutsceneDefinition definition in CutsceneDefinition.JourneyDefaults())
            {
                var script = AssetDatabase.LoadAssetAtPath<DialogueScript>($"Assets/_Project/Data/Dialogue/{definition.Id}.asset");
                Assert.IsNotNull(script, definition.Id);
                List<CutsceneShot> shots = CutsceneShotPlan.For(definition.Id);
                CollectionAssert.AreEqual(Enumerable.Range(0, script.ShotCount), shots.Select(s => s.Index), definition.Id);
            }
        }

        [Test]
        public void ShotsFireExactlyTheCutscenesCriticalSignals()
        {
            foreach (CutsceneDefinition definition in CutsceneDefinition.JourneyDefaults())
            {
                IEnumerable<string> planned = CutsceneShotPlan.For(definition.Id).SelectMany(s => s.Signals);
                CollectionAssert.AreEquivalent(definition.CriticalSignals, planned, definition.Id);
            }
        }
    }
}
