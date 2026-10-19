using System;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ToyFactory.Interfaces;
using ToyFactory.Journey.Cutscenes;

namespace ToyFactory.Tests.EditMode
{
    /// <summary>
    /// The music director's choices (the track for each part of a run and each room, the
    /// volume for each state) and its setup in the game: every track has a clip in the Agents
    /// scene, at a sensible gain, imported to load in the background without a hitch.
    /// </summary>
    public class MusicDirectorChoiceTests
    {
        const string AgentsScene = "Assets/_Project/Scenes/Agents.unity";

        static readonly MusicTrack[] Every =
        {
            MusicTrack.Title, MusicTrack.AssemblyFloor, MusicTrack.PaintingRoom, MusicTrack.StorageArea,
            MusicTrack.ControlRoom, MusicTrack.WinJingle, MusicTrack.LoseJingle,
        };

        [Test]
        public void EachRoomHasItsOwnTrack()
        {
            Assert.AreEqual(MusicTrack.AssemblyFloor, MusicDirector.RoomTrack(0), "Before the first chapter starts.");
            Assert.AreEqual(MusicTrack.AssemblyFloor, MusicDirector.RoomTrack(1));
            Assert.AreEqual(MusicTrack.PaintingRoom, MusicDirector.RoomTrack(2));
            Assert.AreEqual(MusicTrack.StorageArea, MusicDirector.RoomTrack(3));
            Assert.AreEqual(MusicTrack.ControlRoom, MusicDirector.RoomTrack(4));
        }

        [Test]
        public void TheTrackFitsThePartOfTheRun()
        {
            Assert.AreEqual(MusicTrack.Title, MusicDirector.Choose(GameState.Title, 0, false, false));
            Assert.AreEqual(MusicTrack.AssemblyFloor, MusicDirector.Choose(GameState.Playing, 1, false, false));
            Assert.AreEqual(MusicTrack.PaintingRoom, MusicDirector.Choose(GameState.Cutscene, 2, false, false), "A cutscene keeps the room's music.");
            Assert.AreEqual(MusicTrack.ControlRoom, MusicDirector.Choose(GameState.Paused, 4, false, false), "So does the pause menu.");
            Assert.AreEqual(MusicTrack.WinJingle, MusicDirector.Choose(GameState.Results, 4, true, false));
            Assert.AreEqual(MusicTrack.LoseJingle, MusicDirector.Choose(GameState.Results, 2, false, false));
            Assert.AreEqual(MusicTrack.Title, MusicDirector.Choose(GameState.Results, 4, true, true), "Then the title track again.");
        }

        [Test]
        public void OnlyTheStingsPlayOnce()
        {
            foreach (MusicTrack track in Every)
                Assert.AreEqual(track == MusicTrack.WinJingle || track == MusicTrack.LoseJingle, MusicDirector.IsJingle(track), track.ToString());
        }

        [Test]
        public void TheMusicDipsUnderCutscenesAndWhilePaused()
        {
            float playing = MusicDirector.VolumeFor(GameState.Playing, 0.22f, 0.5f, 0.5f, 1.5f);
            Assert.AreEqual(0.22f, playing, 1e-5f);
            Assert.Less(MusicDirector.VolumeFor(GameState.Cutscene, 0.22f, 0.5f, 0.5f, 1.5f), playing);
            Assert.Less(MusicDirector.VolumeFor(GameState.Paused, 0.22f, 0.5f, 0.5f, 1.5f), playing);
            Assert.Greater(MusicDirector.VolumeFor(GameState.Title, 0.22f, 0.5f, 0.5f, 1.5f), playing, "Louder where nothing else plays.");
        }

        [Test]
        public void EveryTrackHasAClipInTheGameImportedToLoadWithoutAHitch()
        {
            Scene scene = SceneManager.GetSceneByPath(AgentsScene);
            bool opened = !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(AgentsScene, OpenSceneMode.Additive);
            try
            {
                MusicDirector music = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                    if (music == null)
                        music = root.GetComponentInChildren<MusicDirector>(true);
                Assert.IsNotNull(music, "The Agents scene has a music director.");

                foreach (MusicTrack track in Every)
                {
                    Assert.IsTrue(music.TryGetCue(track, out MusicCue cue), track + " has a clip.");
                    Assert.That(cue.gain, Is.InRange(0.3f, 2f), track + " has a sensible gain.");
                    string path = AssetDatabase.GetAssetPath(cue.clip);
                    StringAssert.StartsWith("Assets/_ThirdParty/Music/", path, track + "'s clip is a credited third-party track.");
                    var importer = (AudioImporter)AssetImporter.GetAtPath(path);
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    Assert.AreEqual(AudioClipLoadType.CompressedInMemory, settings.loadType, track + " is kept compressed in memory.");
                    Assert.IsTrue(importer.loadInBackground, track + " loads in the background, so a change never stalls a frame.");
                    Assert.AreEqual(AudioCompressionFormat.Vorbis, settings.compressionFormat, track + " is Vorbis.");
                    Assert.Greater(cue.clip.length, MusicDirector.IsJingle(track) ? 5f : 25f, track + " is long enough not to repeat too soon.");
                }
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
