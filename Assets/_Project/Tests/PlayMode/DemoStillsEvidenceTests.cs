using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Runtime.Visuals;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Stills for the demo video (S4 assembles it): the Saboteur and Unit 047 from the ModelShowcase turntable, the
    /// Unity render on the left and the same model as a wireframe on the right, with the triangle count the showcase
    /// reads from the prefab. The files go to <c>Docs/Evidence/Demo/</c>, or to the folder named by the environment
    /// variable <c>FR_DEMO_SHOTS</c>.
    /// </summary>
    /// <remarks>
    /// Needs a graphics device (not <c>-nographics</c>), so it is explicit. The showcase scene is not in the build, so
    /// it is loaded by path, which only works in the Editor.
    /// </remarks>
    [Explicit("Evidence run: about 30 s, needs a graphics device. Writes PNG files to Docs/Evidence/Demo.")]
    [Category("Evidence")]
    public sealed class DemoStillsEvidenceTests
    {
        const string ScenePath = "Assets/_Project/Scenes/ModelShowcase.unity";
        const int Width = 1280;
        const int Height = 720;

        static readonly string[] Models = { "SaboteurBot", "Unit047" };

        string _folder;

        [SetUp]
        public void SetUp()
        {
            _folder = Environment.GetEnvironmentVariable("FR_DEMO_SHOTS");
            if (string.IsNullOrEmpty(_folder))
                _folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Docs", "Evidence", "Demo"));
            Directory.CreateDirectory(_folder);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene empty = SceneManager.CreateScene("Empty after demo stills");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        static T Field<T>(object target, string name) =>
            (T)typeof(ModelShowcase).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(target);

        // One frame of the turntable camera, read back from a render texture.
        static IEnumerator Capture(Camera camera, Action<Texture2D> done)
        {
            var target = new RenderTexture(Width, Height, 24);
            camera.targetTexture = target;
            for (int i = 0; i < 4; i++)
                yield return null;

            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;
            target.Release();
            Object.Destroy(target);
            done(picture);
        }

        [UnityTest]
        public IEnumerator Evidence_SaboteurAndUnit047ShadedBesideWireframe()
        {
#if UNITY_EDITOR
            Assert.IsTrue(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Run this without -nographics.");

            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;

            ModelShowcase showcase = Object.FindAnyObjectByType<ModelShowcase>();
            Assert.IsNotNull(showcase, "The ModelShowcase object in the scene.");
            ModelShowcase.Entry[] entries = Field<ModelShowcase.Entry[]>(showcase, "entries");
            Camera turntable = Field<Camera>(showcase, "turntableCamera");

            foreach (string model in Models)
            {
                int index = Array.FindIndex(entries, e => e.prefab != null && e.prefab.name == model);
                Assert.GreaterOrEqual(index, 0, model + " is a showcase entry.");

                showcase.ShowTurntable(index);
                showcase.SetWireframe(false);
                if (showcase.Spin)
                    showcase.ToggleSpin();
                yield return null;
                Texture2D shaded = null;
                yield return Capture(turntable, t => shaded = t);
                int triangles = showcase.CurrentTriangles;

                showcase.SetWireframe(true);
                yield return null;
                Texture2D wire = null;
                yield return Capture(turntable, t => wire = t);
                showcase.SetWireframe(false);

                var both = new Texture2D(Width * 2, Height, TextureFormat.RGB24, false);
                both.SetPixels(0, 0, Width, Height, shaded.GetPixels());
                both.SetPixels(Width, 0, Width, Height, wire.GetPixels());
                both.Apply();

                string path = Path.Combine(_folder, model + "_shaded_beside_wireframe.png");
                File.WriteAllBytes(path, both.EncodeToPNG());
                Debug.Log("EVIDENCE_DEMO " + model + " triangles=" + triangles + " file=" + path);
                Object.Destroy(shaded);
                Object.Destroy(wire);
                Object.Destroy(both);
            }
#else
            Assert.Ignore("Editor only.");
            yield break;
#endif
        }
    }
}
