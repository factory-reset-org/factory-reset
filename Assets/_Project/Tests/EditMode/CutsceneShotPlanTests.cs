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

        // Obstacles from Docs/LevelLayout.md: centre (x, z), footprint (width x, depth z), top height.
        static readonly (Vector2 centre, Vector2 size, float top, string name)[] Obstacles =
        {
            (new Vector2(7f, 9f), new Vector2(2f, 2f), 3.1f, "Assembly press 1"),
            (new Vector2(14f, 10f), new Vector2(2f, 2f), 3.1f, "Assembly press 2"),
            (new Vector2(26f, 6f), new Vector2(2f, 1f), 1.2f, "Painting cover 1"),
            (new Vector2(35f, 6f), new Vector2(2f, 1f), 1.2f, "Painting cover 2"),
            (new Vector2(27f, 15f), new Vector2(2f, 1f), 1.2f, "Painting cover 3"),
            (new Vector2(36f, 15f), new Vector2(2f, 1f), 1.2f, "Painting cover 4"),
            (new Vector2(28.5f, 25.5f), new Vector2(9f, 1f), 3.2f, "Storage shelf 1"),
            (new Vector2(29.5f, 31f), new Vector2(9f, 1f), 3.2f, "Storage shelf 2"),
            (new Vector2(28.5f, 36.5f), new Vector2(9f, 1f), 3.2f, "Storage shelf 3"),
            (new Vector2(5f, 35f), new Vector2(1.5f, 1.5f), 3.8f, "Control pillar 1 and its core"),
            (new Vector2(16f, 35f), new Vector2(1.5f, 1.5f), 3.8f, "Control pillar 2 and its core"),
            (new Vector2(10.5f, 28.5f), new Vector2(1.5f, 1.5f), 3.8f, "Control pillar 3 and its core"),
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
        public void FollowCamerasStayCloseToUnit047()
        {
            foreach (var (shot, camera, pose) in Cameras(ShotFraming.FollowActor))
                Assert.LessOrEqual(new Vector2(camera.x, camera.z).magnitude, 9.5f, Name(shot, pose));
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
