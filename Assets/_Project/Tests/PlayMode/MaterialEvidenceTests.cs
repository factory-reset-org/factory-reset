using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Evidence for the OptimisationLog: how many SetPass calls S3's final models cost, with the shared
    /// <c>M_Char_*</c> plastics they use and with the same models given a material of their own in every slot (what
    /// the greybox models and the per-prop materials did). The models are laid out in one fixed top-down view, so
    /// every one is on screen; only the materials differ between the two runs.
    /// </summary>
    /// <remarks>
    /// Needs a graphics device (the SetPass counter is zero under <c>-nographics</c>), so it is explicit. The
    /// counters are read one frame later, once the camera has rendered into its texture (WaitForEndOfFrame did not return in a batch-mode editor run). Editor numbers.
    /// </remarks>
    [Explicit("Evidence run: about 30 s, needs a graphics device. Run it on its own for the optimisation log.")]
    [Category("Evidence")]
    public sealed class MaterialEvidenceTests
    {
        const int WarmUpFrames = 10;
        const int SampleFrames = 30;
        const string PropFolder = "Assets/_Project/Prefabs/Props";

        static readonly string[] Characters =
        {
            "Assets/_Project/Prefabs/Characters/TrackerToy.prefab",
            "Assets/_Project/Prefabs/Characters/SaboteurBot.prefab",
            "Assets/_Project/Prefabs/Characters/GuardBot.prefab",
            "Assets/_Project/Prefabs/Characters/CaptainBot.prefab",
            "Assets/_Project/Prefabs/Characters/Unit047.prefab",
            "Assets/_Project/Prefabs/Props/Keycard_Root.prefab"
        };

        readonly List<Object> _made = new List<Object>();

        struct Row
        {
            public string View;
            public string Mode;
            public int Renderers;
            public int Slots;
            public int Materials;
            public int Triangles;
            public double SetPass;
            public double Batches;
            public double DrawCalls;
        }

        [TearDown]
        public void TearDown() => DestroyMade();

        void DestroyMade()
        {
            foreach (Object made in _made)
            {
                if (made != null)
                    Object.Destroy(made);
            }

            _made.Clear();
        }

#if UNITY_EDITOR
        static GameObject Load(string path)
        {
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, path);
            return prefab;
        }

        static string[] Props() =>
            Directory.GetFiles(PropFolder, "*_Root.prefab").Select(f => f.Replace('\\', '/')).OrderBy(f => f).ToArray();
#endif

        static Vector3 Size(GameObject root)
        {
            var bounds = new Bounds(root.transform.position, Vector3.zero);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(renderer.bounds);
            return bounds.size;
        }

        // The models in a grid with 1 m between them, a top-down orthographic camera that sees all of them, and a
        // shadowed directional light, rendering into a 1920 x 1080 texture.
        void BuildView(IEnumerable<GameObject> prefabs, bool uniqueMaterials, out int renderers, out int slots, out int distinctMaterials, out int triangles)
        {
            var roots = prefabs.Select(p => Object.Instantiate(p)).ToList();
            _made.AddRange(roots);

            float cell = roots.Max(r => Mathf.Max(Size(r).x, Size(r).z)) + 1f;
            int columns = Mathf.CeilToInt(Mathf.Sqrt(roots.Count * 16f / 9f));
            for (int i = 0; i < roots.Count; i++)
                roots[i].transform.position = new Vector3((i % columns) * cell, 0f, -(i / columns) * cell);

            renderers = 0;
            slots = 0;
            triangles = 0;
            var materials = new HashSet<Material>();
            foreach (GameObject root in roots)
            {
                foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh != null && filter.GetComponent<Renderer>() != null)
                        triangles += (int)(filter.sharedMesh.GetIndexCount(0) / 3);
                }

                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                {
                    renderers++;
                    Material[] shared = renderer.sharedMaterials;
                    slots += shared.Length;
                    if (uniqueMaterials)
                    {
                        for (int i = 0; i < shared.Length; i++)
                        {
                            if (shared[i] == null)
                                continue;
                            shared[i] = new Material(shared[i]);
                            _made.Add(shared[i]);
                        }

                        renderer.sharedMaterials = shared;
                    }

                    foreach (Material material in shared)
                    {
                        if (material != null)
                            materials.Add(material);
                    }
                }
            }

            distinctMaterials = materials.Count;

            var bounds = new Bounds(roots[0].transform.position, Vector3.zero);
            foreach (GameObject root in roots)
            {
                foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
                    bounds.Encapsulate(renderer.bounds);
            }

            var lightObject = new GameObject("Evidence light");
            _made.Add(lightObject);
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.shadows = LightShadows.Soft;

            var cameraObject = new GameObject("Evidence camera");
            _made.Add(cameraObject);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = Mathf.Max(bounds.extents.z, bounds.extents.x * 9f / 16f) + 1f;
            camera.transform.position = bounds.center + Vector3.up * 30f;
            camera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
            var texture = new RenderTexture(1920, 1080, 24);
            _made.Add(texture);
            camera.targetTexture = texture;
        }

        IEnumerator Measure(string view, IEnumerable<GameObject> prefabs, bool unique, List<Row> rows)
        {
            BuildView(prefabs, unique, out int renderers, out int slots, out int materials, out int triangles);
            using (ProfilerRecorder setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1))
            using (ProfilerRecorder batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1))
            using (ProfilerRecorder draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1))
            {
                for (int i = 0; i < WarmUpFrames; i++)
                    yield return null;

                double setPassSum = 0, batchSum = 0, drawSum = 0;
                for (int i = 0; i < SampleFrames; i++)
                {
                    yield return null;
                    setPassSum += setPass.LastValue;
                    batchSum += batches.LastValue;
                    drawSum += draws.LastValue;
                }

                rows.Add(new Row
                {
                    View = view,
                    Mode = unique ? "unique material in every slot" : "shared plastics (as built)",
                    Renderers = renderers,
                    Slots = slots,
                    Materials = materials,
                    Triangles = triangles,
                    SetPass = setPassSum / SampleFrames,
                    Batches = batchSum / SampleFrames,
                    DrawCalls = drawSum / SampleFrames
                });
            }

            DestroyMade();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Evidence_SetPassWithSharedAndUniqueMaterials()
        {
#if UNITY_EDITOR
            Assert.IsTrue(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Run this without -nographics.");

            GameObject[] characters = Characters.Select(Load).ToArray();
            GameObject[] props = Props().Select(Load).ToArray();
            var rows = new List<Row>();

            foreach (bool unique in new[] { false, true })
                yield return Measure("six characters and the keycard", characters, unique, rows);
            foreach (bool unique in new[] { false, true })
                yield return Measure(props.Length + " props", props, unique, rows);
            foreach (bool unique in new[] { false, true })
                yield return Measure("all models", characters.Concat(props), unique, rows);

            var text = new StringBuilder();
            text.AppendLine("EVIDENCE_MATERIALS device=" + SystemInfo.graphicsDeviceName + " api=" + SystemInfo.graphicsDeviceType +
                            " pipeline=" + (GraphicsSettings.currentRenderPipeline != null ? GraphicsSettings.currentRenderPipeline.name : "built-in") +
                            " srpBatcher=" + GraphicsSettings.useScriptableRenderPipelineBatching + " unity=" + Application.unityVersion + " frames=" + SampleFrames);
            foreach (Row row in rows)
                text.AppendLine("EVIDENCE_MATERIALS view=\"" + row.View + "\" mode=\"" + row.Mode + "\" renderers=" + row.Renderers + " slots=" + row.Slots +
                                " materials=" + row.Materials + " triangles=" + row.Triangles + " setPass=" + row.SetPass.ToString("0.0") +
                                " batches=" + row.Batches.ToString("0.0") + " draws=" + row.DrawCalls.ToString("0.0"));
            Debug.Log(text.ToString());
            Assert.AreEqual(6, rows.Count);
#else
            Assert.Ignore("Editor only.");
            yield break;
#endif
        }
    }
}
