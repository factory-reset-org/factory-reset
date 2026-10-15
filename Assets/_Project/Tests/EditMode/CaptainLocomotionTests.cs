using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The Captain's built clips, played on the real model: its boots stay on the floor without
    /// sliding while it walks and runs (the legs are solved, so a planted boot moves back at
    /// exactly the clip's speed), the swinging boot clears the floor, and the Aim pose holds the
    /// arm cannon level and pointing ahead.
    /// </summary>
    public sealed class CaptainLocomotionTests
    {
        const string Folder = "Assets/_Project/Animations/CaptainBot/CaptainBot_";
        const int Samples = 192;
        const float Touching = 0.006f;   // a vertex this close to the floor is standing on it

        GameObject _model;
        readonly Dictionary<string, Transform> _parts = new Dictionary<string, Transform>();

        [SetUp]
        public void SetUp()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Characters/CaptainBot.prefab");
            _model = Object.Instantiate(prefab);
            _model.hideFlags = HideFlags.HideAndDontSave;
            foreach (Transform part in _model.GetComponentsInChildren<Transform>(true))
                _parts[part.name] = part;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_model);

        static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Folder + name + ".anim");

        // Every vertex of both boots (all meshes under the ankles), in world space with the
        // body's forward travel added, so a planted vertex keeps the same position.
        IEnumerable<(string key, Vector3 at)> BootVertices(float travel)
        {
            foreach (string ankle in new[] { "Ankle_L_Pivot", "Ankle_R_Pivot" })
                foreach (MeshFilter part in _parts[ankle].GetComponentsInChildren<MeshFilter>())
                {
                    Vector3[] vertices = part.sharedMesh.vertices;
                    for (int i = 0; i < vertices.Length; i++)
                    {
                        Vector3 at = part.transform.TransformPoint(vertices[i]);
                        at.z += travel;
                        yield return (part.name + i, at);
                    }
                }
        }

        static readonly object[] Gaits =
        {
            new object[] { "Idle", 0f }, new object[] { "Walk", 2.5f }, new object[] { "Run", 4.6f },
            new object[] { "LeanLeft", 4.6f }, new object[] { "LeanRight", 4.6f },
        };

        [TestCaseSource(nameof(Gaits))]
        public void TheBootsStayOnTheFloorWithoutSliding(string name, float speed)
        {
            AnimationClip clip = Clip(name);
            float lowest = float.MaxValue, worstSlide = 0f;
            var before = new Dictionary<string, Vector3>();
            for (int i = 0; i <= Samples; i++)
            {
                float t = clip.length * i / Samples;
                clip.SampleAnimation(_model, t);
                var now = new Dictionary<string, Vector3>();
                foreach ((string key, Vector3 at) in BootVertices(speed * t))
                {
                    lowest = Mathf.Min(lowest, at.y);
                    if (at.y > Touching)
                        continue;
                    now[key] = at;
                    if (before.TryGetValue(key, out Vector3 was))
                        worstSlide = Mathf.Max(worstSlide, Vector2.Distance(new Vector2(at.x, at.z), new Vector2(was.x, was.z)));
                }
                before = now;
            }

            Assert.Greater(lowest, -0.005f, "No boot goes through the floor.");
            Assert.Less(lowest, 0.005f, "And they stand on it.");
            // A vertex on the floor may only roll (the heel landing, the boot peeling over its
            // tip), never slide: at most a few millimetres between samples, against 6 to 12 mm
            // of body travel per sample.
            Assert.Less(worstSlide, 0.005f, $"A planted boot slid {worstSlide * 1000f:F1} mm in 1/{Samples} of the clip.");
        }

        [TestCase("Walk", 0.15f)]
        [TestCase("Run", 0.3f)]
        public void EachSwingingBootLiftsClearOfTheFloor(string name, float lift)
        {
            AnimationClip clip = Clip(name);
            float left = 0f, right = 0f;
            for (int i = 0; i < Samples; i++)
            {
                clip.SampleAnimation(_model, clip.length * i / Samples);
                left = Mathf.Max(left, Lowest("Ankle_L_Pivot"));
                right = Mathf.Max(right, Lowest("Ankle_R_Pivot"));
            }
            Assert.Greater(left, lift);
            Assert.Greater(right, lift);
        }

        float Lowest(string ankle)
        {
            float lowest = float.MaxValue;
            foreach (MeshFilter part in _parts[ankle].GetComponentsInChildren<MeshFilter>())
                foreach (Vector3 vertex in part.sharedMesh.vertices)
                    lowest = Mathf.Min(lowest, part.transform.TransformPoint(vertex).y);
            return lowest;
        }

        [Test]
        public void TheAimPoseHoldsTheCannonLevelAndAhead()
        {
            Clip("Idle").SampleAnimation(_model, 0f);
            Clip("Aim").SampleAnimation(_model, 0f);   // the override layer on top
            Vector3 muzzle = _parts["Muzzle_Point"].position;
            Vector3 along = (muzzle - _parts["Cannon_Barrel"].position).normalized;

            Assert.Greater(along.z, 0.97f, $"The cannon points ahead: {along}.");
            Assert.Less(Mathf.Abs(along.y), 0.1f, "Level: AgentCannonAim makes the last few degrees to the target.");
            Assert.That(muzzle.y, Is.InRange(1.7f, 2.2f), "At the height of the Captain's chest.");
            Assert.Greater(muzzle.z, 1.2f, "Held out in front, not against the body.");
        }
    }
}
