using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ToyFactory.AI.Agents.Captain;
using ToyFactory.AI.Core;
using ToyFactory.AI.Core.Blackboard;
using ToyFactory.AI.Core.Grid;
using ToyFactory.AI.Core.Search;
using ToyFactory.Interfaces;
using ToyFactory.Runtime.Agents;
using ToyFactory.Runtime.Animation;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The Captain's body shows its call-outs: when it commits to a goal it flashes the red
    /// marker on that goal, and says a line above its head only if the camera can see it.
    /// </summary>
    public sealed class CaptainCalloutTests
    {
        readonly List<Object> _created = new List<Object>();

        static readonly Vector2Int CaptainCell = new Vector2Int(10, 10);
        static readonly Vector2Int GoalCell = new Vector2Int(55, 10);
        static readonly Vector2Int PlayerCell = new Vector2Int(47, 10);   // 4 m short of the goal, 18 m from the Captain

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();
        }

        T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }

        static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);

        // An open 30 x 10 m room with one goal, so the Captain is sure of it at once and commits.
        CaptainCallout Captain(GridGraph grid)
        {
            GameObject floor = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
            floor.transform.position = new Vector3(15f, 0f, 5f);
            floor.transform.localScale = new Vector3(4f, 1f, 2f);

            var world = new WorldBlackboard();
            world.SetObjectiveTargets(new[] { new ObjectiveTarget(1, GoalCell, ObjectiveTargetKind.Task) });
            world.SetPlayer(new PlayerSnapshot(true, PlayerCell, grid.CellToWorld(PlayerCell), Vector3.zero, Vector3.right,
                5f, true, 1f, 1f, false, 0f, -1f));

            var go = Track(new GameObject("Captain"));
            go.SetActive(false);
            go.transform.position = grid.CellToWorld(CaptainCell) + Vector3.up * 0.1f;
            CharacterController body = go.AddComponent<CharacterController>();
            body.center = new Vector3(0f, 1.6f, 0f);
            body.height = 3.2f;
            body.radius = 0.55f;
            AgentController agent = go.AddComponent<AgentController>();
            CaptainCallout callout = go.AddComponent<CaptainCallout>();
            // Stand-ins for S1's beacon parts, set before Awake builds the marker.
            GameObject cube = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            cube.SetActive(false);
            var material = Track(new Material(Shader.Find("Sprites/Default")));
            SetField(callout, "ringMesh", cube.GetComponent<MeshFilter>().sharedMesh);
            SetField(callout, "ringMaterial", material);
            // Stand-ins for the comic bubbles: one small sprite per line, named like the real ones.
            var bubbles = new List<Sprite>();
            var texture = Track(new Texture2D(8, 4));
            foreach (string line in CaptainCallouts.AllLines())
            {
                Sprite bubble = Track(Sprite.Create(texture, new Rect(0f, 0f, 8f, 4f), new Vector2(0.5f, 0f), 4f));
                bubble.name = CaptainCallouts.BubbleName(line);
                bubbles.Add(bubble);
            }
            SetField(callout, "bubbles", bubbles.ToArray());
            go.SetActive(true);
            agent.Initialise(new AgentIdentity(AgentType.Captain, 6), new CaptainBrain(grid, new AStarSearch(grid), world, startAwake: true), world, grid);
            return callout;
        }

        void CameraLookingAt(Vector3 target)
        {
            var go = Track(new GameObject("Main Camera"));
            go.tag = "MainCamera";
            go.AddComponent<Camera>();
            go.transform.position = target + new Vector3(0f, 3f, -10f);
            go.transform.LookAt(target + Vector3.up * 2f);
        }

        IEnumerator Watch(CaptainCallout callout, float seconds, List<string> lines, List<Vector3> marks)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                if (callout.Line != null && !lines.Contains(callout.Line))
                    lines.Add(callout.Line);
                if (callout.IsMarking)
                    marks.Add(callout.MarkPoint);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator InViewItSaysALineAndFlashesTheGoal()
        {
            var grid = new GridGraph(60, 20, Vector3.zero);
            CaptainCallout callout = Captain(grid);
            CameraLookingAt(grid.CellToWorld(CaptainCell));

            var lines = new List<string>();
            var marks = new List<Vector3>();
            yield return Watch(callout, 2f, lines, marks);

            Assert.AreEqual(1, lines.Count, "One line: " + string.Join(" | ", lines));
            Assert.Greater(marks.Count, 0, "The goal flashed.");
            Vector3 goal = grid.CellToWorld(GoalCell);
            Assert.Less(Vector3.Distance(new Vector3(marks[0].x, 0f, marks[0].z), new Vector3(goal.x, 0f, goal.z)), 0.01f, "On the goal, not on the Captain.");
        }

        [UnityTest]
        public IEnumerator BehindAWallItOnlyFlashesTheGoal()
        {
            var grid = new GridGraph(60, 20, Vector3.zero);
            CaptainCallout callout = Captain(grid);
            Vector3 captain = grid.CellToWorld(CaptainCell);
            CameraLookingAt(captain);
            GameObject wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = captain + new Vector3(0f, 3f, -5f);
            wall.transform.localScale = new Vector3(12f, 8f, 0.5f);

            var lines = new List<string>();
            var marks = new List<Vector3>();
            yield return Watch(callout, 2f, lines, marks);

            Assert.AreEqual(0, lines.Count, "No line nobody can see it say.");
            Assert.Greater(marks.Count, 0, "The flash still tells the player.");
        }
    }
}
