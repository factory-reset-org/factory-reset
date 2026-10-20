using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Chapters;
using ToyFactory.Journey.Cutscenes;
using ToyFactory.Journey.Debugging;
using ToyFactory.Runtime.Agents;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace ToyFactory.Tests
{
    /// <summary>
    /// The four rooms for the README, taken from the finished game so they show it as it is: each room dressed and lit
    /// in its own chapter, with its props and the agents in it, from a corner under the ceiling. PNG files at
    /// 1280 x 720 in <c>Docs/Evidence/Gallery/</c>.
    /// </summary>
    /// <remarks>Needs a graphics device. The HUD is hidden; the player is kept alive and moved by the test.</remarks>
    [Category("Evidence"), Explicit("Evidence run: about 30 s, needs a graphics device. Writes PNG files to Docs/Evidence/Gallery.")]
    public sealed class ReadmeGalleryEvidenceTests
    {
        const string Folder = "Docs/Evidence/Gallery";
        const int Width = 1280;
        const int Height = 720;

        readonly List<Object> _created = new List<Object>();
        Component _health;
        System.Reflection.MethodInfo _setHealth;
        Camera _camera;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (Object created in _created)
                if (created != null)
                    Object.Destroy(created);
            _created.Clear();

            Scene empty = SceneManager.CreateScene("Empty after the gallery " + Guid.NewGuid());
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

        static IEnumerator Until(Func<bool> condition, float seconds, string what)
        {
            float until = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for " + what);
                yield return null;
            }
        }

        static bool IsCutscenePlaying => CutsceneDirector.Current != null && CutsceneDirector.Current.IsPlaying;

        static IEnumerator SkipCutscenes()
        {
            float until = Time.realtimeSinceStartup + 30f;
            while (GameClock.Current.State != GameState.Playing || IsCutscenePlaying)
            {
                if (IsCutscenePlaying)
                    CutsceneDirector.Current.Skip();
                if (Time.realtimeSinceStartup > until)
                    Assert.Fail("Timed out waiting for the cutscene to end");
                yield return null;
            }
        }

        static IEnumerator JumpTo(int chapter)
        {
            ChapterJump jump = Object.FindAnyObjectByType<ChapterJump>();
            jump.JumpTo(chapter);
            yield return Until(() => !jump.IsJumping, 30f, "the jump to Chapter " + chapter);
            yield return SkipCutscenes();
        }

        IEnumerator Wait(float seconds)
        {
            float until = Time.time + seconds;
            while (Time.time < until)
            {
                _setHealth.Invoke(_health, new object[] { 100f });
                yield return null;
            }
        }

        static void PlacePlayer(Vector3 feet, float yaw)
        {
            var body = ((Component)PlayerState.Current).GetComponent<CharacterController>();
            body.enabled = false;
            body.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, yaw, 0f));
            body.enabled = true;
            Physics.SyncTransforms();
        }

        // One room from a corner under the ceiling, through the gallery camera into a 1280 x 720 picture (no HUD).
        void Shoot(string name, Vector3 from, Vector3 at)
        {
            _camera.transform.position = from;
            _camera.transform.LookAt(at);
            var target = RenderTexture.GetTemporary(Width, Height, 24);
            _camera.targetTexture = target;
            _camera.Render();
            _camera.targetTexture = null;

            var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            picture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            picture.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            File.WriteAllBytes(Path.Combine(Folder, name + ".png"), picture.EncodeToPNG());
            Debug.Log("EVIDENCE_GALLERY " + name);
            Object.Destroy(picture);
        }

        [UnityTest]
        public IEnumerator Evidence_TheFourRoomsOfTheFinishedGame()
        {
            Assert.IsTrue(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null, "Run this without -nographics.");
            Directory.CreateDirectory(Folder);

            AgentSpawner previous = AgentSpawner.Instance;
            SceneManager.LoadScene("Bootstrap");
            yield return null;
            yield return Until(() => AgentSpawner.Instance != null && AgentSpawner.Instance != previous
                && AgentSpawner.Instance.SpawnedAgents.Count > 0 && ChapterManager.Current != null
                && ChapterManager.Current.Flow != null && ChapterManager.Current.Flow.HasBegun
                && PlayerState.Current != null && Object.FindAnyObjectByType<ChapterJump>() != null, 60f, "the level");
            _health = ((Component)PlayerState.Current).GetComponent(Type.GetType("ToyFactory.Player.PlayerHealth, Assembly-CSharp"));
            _setHealth = _health.GetType().GetProperty("Current").GetSetMethod(true);

            var go = new GameObject("Gallery Camera");
            _created.Add(go);
            _camera = go.AddComponent<Camera>();
            _camera.enabled = false;
            _camera.fieldOfView = 62f;
            _camera.nearClipPlane = 0.05f;
            _camera.useOcclusionCulling = false;   // it stands outside the baked occlusion volume's cells

            GameClock.Current.RequestState(GameState.Playing);
            yield return Until(() => CutsceneDirector.Current != null && CutsceneDirector.Current.IntroRequested, 20f, "the intro");
            yield return SkipCutscenes();

            // Each room in its own chapter, so its props, lighting and agents are as the player meets them.
            PlacePlayer(new Vector3(10.5f, 0.1f, 3f), 0f);
            yield return Wait(1.5f);
            Shoot("room_assembly", new Vector3(1.6f, 4.6f, 1.6f), new Vector3(12f, 0.6f, 12f));

            yield return JumpTo(2);
            yield return Wait(1f);
            Shoot("room_painting", new Vector3(40f, 4.6f, 1.6f), new Vector3(29f, 0.6f, 12f));

            yield return JumpTo(3);
            yield return Wait(1f);
            Shoot("room_storage", new Vector3(40f, 4.6f, 40f), new Vector3(29f, 0.6f, 29f));

            yield return JumpTo(4);
            yield return Wait(1.5f);
            Shoot("room_control", new Vector3(1.6f, 4.6f, 40f), new Vector3(12f, 0.6f, 29f));
        }
    }
}
