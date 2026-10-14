using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The presentation scene stays out of builds and lists every S3 model, and the Charger's LOD group does
    /// what the optimisation log says it does. The scene is read through its serialized data, so these tests
    /// need no reference to the runtime assembly.
    /// </summary>
    public class ModelShowcaseSceneTests
    {
        const string ScenePath = "Assets/_Project/Scenes/ModelShowcase.unity";
        const string PropFolder = "Assets/_Project/Prefabs/Props";

        static readonly string[] Characters = { "TrackerToy", "SaboteurBot", "GuardBot", "CaptainBot", "Unit047" };

        static SerializedObject OpenController(out Scene scene)
        {
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MonoBehaviour behaviour = root.GetComponents<MonoBehaviour>().FirstOrDefault(b => b != null && b.GetType().Name == "ModelShowcase");
                if (behaviour != null)
                    return new SerializedObject(behaviour);
            }

            Assert.Fail("No ModelShowcase controller in the scene.");
            return null;
        }

        [Test]
        public void TheShowcaseIsNotInTheBuild()
        {
            Assert.IsFalse(EditorBuildSettings.scenes.Any(s => s.path == ScenePath), "A presentation scene never ships.");
        }

        [Test]
        public void TheTurntableListsEveryCharacterAndEveryRootPropPrefab()
        {
            SerializedObject controller = OpenController(out Scene scene);
            try
            {
                SerializedProperty entries = controller.FindProperty("entries");
                var listed = Enumerable.Range(0, entries.arraySize)
                    .Select(i => AssetDatabase.GetAssetPath(entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue))
                    .ToArray();

                var expected = Characters.Select(c => "Assets/_Project/Prefabs/Characters/" + c + ".prefab")
                    .Concat(Directory.GetFiles(PropFolder, "*_Root.prefab").Select(f => f.Replace('\\', '/')))
                    .ToArray();

                CollectionAssert.AreEquivalent(expected, listed, "Add a new model's prefab to the Showcase object's entries.");
                Assert.AreEqual(listed.Length, listed.Distinct().Count(), "No model twice.");
                for (int i = 0; i < entries.arraySize; i++)
                    Assert.IsNotEmpty(entries.GetArrayElementAtIndex(i).FindPropertyRelative("label").stringValue);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void TheLineupKeepsTheDocumentedOrderAndTheCamerasAreSet()
        {
            SerializedObject controller = OpenController(out Scene scene);
            try
            {
                SerializedProperty lineup = controller.FindProperty("lineup");
                var names = Enumerable.Range(0, lineup.arraySize).Select(i => lineup.GetArrayElementAtIndex(i).objectReferenceValue.name).ToArray();
                CollectionAssert.AreEqual(new[] { "TrackerToy", "SaboteurBot", "GuardBot", "Keycard_Root", "CaptainBot", "Unit047" }, names,
                    "Left to right as in the design document: the keycard hangs in the gap between the Guard and the Captain.");

                var lineupCamera = (Camera)controller.FindProperty("lineupCamera").objectReferenceValue;
                var turntableCamera = (Camera)controller.FindProperty("turntableCamera").objectReferenceValue;
                Assert.IsNotNull(lineupCamera);
                Assert.IsTrue(lineupCamera.orthographic);
                Assert.AreEqual(3.3f, lineupCamera.orthographicSize, 0.0001f, "The framing in the design document.");
                Assert.IsNotNull(turntableCamera);
                Assert.IsFalse(turntableCamera.orthographic);
                Assert.IsNotNull(controller.FindProperty("pedestal").objectReferenceValue);

                var pedestal = (Transform)controller.FindProperty("pedestal").objectReferenceValue;
                Assert.Greater(Vector3.Distance(pedestal.position, Vector3.zero), 30f, "Far from the line-up, so the two never share a view.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // ---- The Charger's LOD group

        static int Triangles(Renderer[] renderers) =>
            renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3);

        [Test]
        public void TheChargerHasTwoLevelsAndTheFarOneIsAboutHalfTheTriangles()
        {
            var charger = AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder + "/Charger_Root.prefab");
            LODGroup group = charger.GetComponent<LODGroup>();
            Assert.IsNotNull(group);

            LOD[] levels = group.GetLODs();
            Assert.AreEqual(2, levels.Length);
            Assert.Greater(levels[0].screenRelativeTransitionHeight, levels[1].screenRelativeTransitionHeight);
            Assert.Greater(levels[1].screenRelativeTransitionHeight, 0f, "Below the last level the model is culled.");

            int near = Triangles(levels[0].renderers);
            int far = Triangles(levels[1].renderers);
            Assert.That(far / (float)near, Is.InRange(0.4f, 0.6f), $"{far} of {near} triangles.");
        }

        [Test]
        public void TheChargersLevelsShareTheSameMaterialsAndSitInTheSamePlace()
        {
            var charger = AssetDatabase.LoadAssetAtPath<GameObject>(PropFolder + "/Charger_Root.prefab");
            LOD[] levels = charger.GetComponent<LODGroup>().GetLODs();

            CollectionAssert.AreEqual(levels[0].renderers[0].sharedMaterials, levels[1].renderers[0].sharedMaterials);
            Bounds near = levels[0].renderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Bounds far = levels[1].renderers[0].GetComponent<MeshFilter>().sharedMesh.bounds;
            Assert.Less(Vector3.Distance(near.center, far.center), 0.01f);
            Assert.Less(Vector3.Distance(near.size, far.size), 0.02f, "The far level keeps the silhouette.");
        }

        [Test]
        public void NoModelFileIsNamedLikeAUnityLodLevel()
        {
            // Unity builds a LOD group by itself from a file whose nodes end in _LOD0, _LOD1 and so on.
            foreach (string path in AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Project/Models", "Assets/_ThirdParty" }).Select(AssetDatabase.GUIDToAssetPath))
                StringAssert.DoesNotContain("_LOD", Path.GetFileNameWithoutExtension(path), path);
        }
    }
}
