using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.UI;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// Evidence for the OptimisationLog: what the HUD's split into a static canvas and nested dynamic canvases
    /// (bars, score panel, current-task marker) saves. The HUD is measured in the running game while the player
    /// walks a circle, so the arrow, the distance to the task and the score text change every frame; then the
    /// nested canvases are removed (their children fall back onto the one HUD canvas) and the same walk is measured
    /// again. The cost is Unity's own <c>Canvas.BuildBatch</c> marker, which rebuilds the mesh of a canvas that has
    /// a changed child.
    /// </summary>
    /// <remarks>Needs a graphics device (a canvas is not drawn, so not batched, under <c>-nographics</c>), so it is explicit.</remarks>
    [Explicit("Evidence run: about 40 s, needs a graphics device. Run it on its own for the optimisation log.")]
    [Category("Evidence")]
    public sealed class HudCanvasEvidenceTests
    {
        const int WarmUpFrames = 120;
        const int SampleFrames = 900;

        // Every Canvas.* marker Unity has registered by the end of the warm-up is sampled.
        static string[] Markers = { "Canvas.BuildBatch" };

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene empty = SceneManager.CreateScene("Empty after HUD canvas evidence");
            SceneManager.SetActiveScene(empty);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != empty && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            if (GameClock.Current is Object clock && clock == null)
                GameClock.Publish(null);
            if (PlayerState.Current is Object player && player == null)
                PlayerState.Publish(null);
        }

        static IEnumerator Until(Func<bool> condition, float timeout, string what)
        {
            float until = Time.realtimeSinceStartup + timeout;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static IEnumerator StartPlaying()
        {
            SceneManager.LoadScene("Bootstrap");
            yield return Until(() => HudPresenter.Current != null && ChapterManager.Current != null && ChapterManager.Current.Flow != null
                && ChapterManager.Current.Flow.HasBegun && CutsceneDirector.Current != null && PlayerState.Current != null,
                60f, "Bootstrap to load the scenes and the HUD");
            GameClock.Current?.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current.IsPlaying, 10f, "the intro");
            CutsceneDirector.Current.Skip();
            yield return Until(() => GameClock.Current != null && GameClock.Current.State == GameState.Playing, 10f, "Playing");
            yield return Until(() => ChapterManager.Current.Flow.CurrentChapter == 1, 10f, "Chapter 1");
        }

        // The marker is found by name among the ones Unity has registered, and recorded in the category it was
        // registered under, so it does not matter which one that is.
        static ProfilerRecorder? Start(string name)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (ProfilerRecorderHandle handle in handles)
            {
                ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.Name == name)
                    return ProfilerRecorder.StartNew(description.Category, description.Name, 1);
            }

            return null;
        }

        static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0)
                return 0;
            List<double> sorted = values.OrderBy(v => v).ToList();
            return sorted[Mathf.Clamp((int)Math.Ceiling(p * sorted.Count) - 1, 0, sorted.Count - 1)];
        }

        // Walks the player in a circle for the warm-up and the sample, reading the markers once per frame.
        static IEnumerator Sample(CharacterController body, Dictionary<string, List<double>> into)
        {
            Vector3 centre = body.transform.position;
            float angle = 0f;
            for (int i = 0; i < WarmUpFrames; i++)
            {
                angle += 0.03f;
                body.enabled = false;
                body.transform.position = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f;
                body.enabled = true;
                yield return null;
            }

            var known = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(known);
            Markers = known.Select(h => ProfilerRecorderHandle.GetDescription(h).Name)
                .Where(n => n.StartsWith("Canvas.") && n != "Canvas.OnAllAssetImport").Distinct().OrderBy(n => n).ToArray();

            var recorders = new Dictionary<string, ProfilerRecorder>();
            foreach (string marker in Markers)
            {
                ProfilerRecorder? recorder = Start(marker);
                if (recorder.HasValue)
                    recorders[marker] = recorder.Value;
                into[marker] = new List<double>();
            }

            for (int i = 0; i < SampleFrames; i++)
            {
                angle += 0.03f;
                body.enabled = false;
                body.transform.position = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 2f;
                body.enabled = true;
                yield return null;
                foreach (KeyValuePair<string, ProfilerRecorder> pair in recorders)
                    into[pair.Key].Add(pair.Value.LastValue / 1000.0);   // ns -> microseconds
            }

            foreach (ProfilerRecorder recorder in recorders.Values)
                recorder.Dispose();
        }

        // The overlay canvases are never drawn (so never batched) when nothing displays them, as in a batch-mode run, so
        // they are sent to a camera that renders into a texture: the same canvases, batched the same way.
        static GameObject SendCanvasesToACamera(Transform hud, RenderTexture target)
        {
            var cameraObject = new GameObject("HUD evidence camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.targetTexture = target;
            // The canvases sit on the default layer, so the camera sees it; parked far above the level, looking up at nothing.
            cameraObject.transform.position = new Vector3(0f, 5000f, 0f);
            cameraObject.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            foreach (Canvas canvas in hud.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas)
                    continue;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
            }

            return cameraObject;
        }

        static string Line(string name, string mode, int canvases, List<double> values) =>
            "EVIDENCE_CANVAS marker=" + name + " mode=\"" + mode + "\" canvases=" + canvases + " frames=" + values.Count +
            " mean_us=" + (values.Count == 0 ? 0 : values.Average()).ToString("0.0") +
            " p50_us=" + Percentile(values, 0.5).ToString("0.0") + " p95_us=" + Percentile(values, 0.95).ToString("0.0") +
            " max_us=" + (values.Count == 0 ? 0 : values.Max()).ToString("0.0");

        [UnityTest]
        public IEnumerator Evidence_CanvasSplitAgainstOneCanvas()
        {
            Assert.IsTrue(SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null, "Run this without -nographics.");
            yield return StartPlaying();

            HudPresenter hud = HudPresenter.Current;
            Transform hudCanvas = hud.transform.Find("HUD Canvas");
            Assert.IsNotNull(hudCanvas, "The HUD's main canvas.");
            var body = ((Component)PlayerState.Current).GetComponent<CharacterController>();
            Assert.IsNotNull(body);

            var target = new RenderTexture(1920, 1080, 16);
            GameObject evidenceCamera = SendCanvasesToACamera(hud.transform, target);
            yield return null;

            // Chapter 1 has the current-task marker up; make sure the HUD is visible and not folded.
            Canvas[] nested = hudCanvas.GetComponentsInChildren<Canvas>(true).Where(c => c.transform != hudCanvas).ToArray();
            int splitCanvases = nested.Length + 1;
            Assert.Greater(nested.Length, 3, "The HUD has its nested dynamic canvases.");

            var split = new Dictionary<string, List<double>>();
            yield return Sample(body, split);

            // Deepest first, so a canvas inside another is removed before the one around it.
            foreach (Canvas canvas in nested.OrderByDescending(c => c.transform.GetComponentsInParent<Transform>().Length))
                Object.DestroyImmediate(canvas);
            yield return null;

            var single = new Dictionary<string, List<double>>();
            yield return Sample(body, single);

            var text = new StringBuilder();
            text.AppendLine("EVIDENCE_CANVAS device=" + SystemInfo.graphicsDeviceName + " unity=" + Application.unityVersion +
                            " warmup=" + WarmUpFrames + " frames=" + SampleFrames + " nestedCanvasesRemoved=" + nested.Length +
                            " hudVisible=" + hud.IsVisible + " distance=\"" + hud.CurrentDistanceShown + "\"");
            foreach (string marker in Markers)
            {
                if (split.TryGetValue(marker, out List<double> a) && a.Count > 0)
                    text.AppendLine(Line(marker, "split (as built)", splitCanvases, a));
                if (single.TryGetValue(marker, out List<double> b) && b.Count > 0)
                    text.AppendLine(Line(marker, "one canvas", 1, b));
            }

            var found = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(found);
            text.AppendLine("EVIDENCE_CANVAS markers: " + string.Join(", ", found.Select(h => ProfilerRecorderHandle.GetDescription(h).Name)
                .Where(n => n.StartsWith("Canvas.")).Distinct().OrderBy(n => n)));
            Debug.Log(text.ToString());
            Object.Destroy(evidenceCamera);
            target.Release();
            Assert.IsTrue(split.ContainsKey("Canvas.BuildBatch") && split["Canvas.BuildBatch"].Count == SampleFrames,
                "The Canvas.BuildBatch marker was found and sampled.");
        }
    }
}
